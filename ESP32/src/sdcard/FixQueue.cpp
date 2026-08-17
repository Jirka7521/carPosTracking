#include "sdcard/FixQueue.h"

#include "esp_log.h"
#include "util/ScopedLock.h"

static const char* TAG = "FixQueue";

namespace {
// How much dead prefix has to accumulate before compaction is worth its copy.
// Below this the reclaimed space simply does not matter on a card sized for a
// week of backlog, and the copy would cost more than it saves.
constexpr long kCompactMinDeadBytes = 32L * 1024 * 1024;
}  // namespace

FixQueue::FixQueue(SdCard& card, const char* filePath, std::size_t maxEntries)
    : card_(card),
      filePath_(filePath),
      indexPath_(std::string(filePath) + ".idx"),
      index_(card, indexPath_.c_str()),
      maxEntries_(maxEntries),
      count_(0),
      head_(0),
      lock_(xSemaphoreCreateMutex()) {}

bool FixQueue::begin() {
  if (!card_.isMounted()) {
    return false;
  }

  const long size = card_.fileSize(filePath_);

  // A power loss may have left fixes queued from a previous run - adopt them,
  // resuming at the offset the sidecar recorded.
  long        head  = 0;
  std::size_t count = 0;
  // The record has to agree with the file it describes: the head cannot sit
  // past the end, and a non-zero count cannot be satisfied by bytes that are
  // not there. Either mismatch means the two got separated - a card moved
  // between devices, a truncated write - and the scan below is the honest way
  // back.
  const bool indexUsable = index_.load(head, count) && head <= size &&
                           (count == 0 || head < size);
  if (indexUsable) {
    head_  = head;
    count_ = count;
  } else {
    // No usable sidecar, or one pointing past the end of the file it describes
    // (a card swapped between devices, a truncation). Fall back to reading the
    // file from the top: slow, but correct, and the API's dedupe absorbs the
    // entries this re-offers.
    head_  = 0;
    count_ = card_.countLines(filePath_);
    if (count_ > 0) {
      ESP_LOGW(TAG,
               "no usable index for %s - re-scanned %u fix(es) from the top; "
               "some may be delivered twice",
               filePath_, (unsigned)count_);
    }
    index_.store(head_, count_);
  }

  // Nothing live, but bytes still on the card: the last drain never got to
  // reclaim the file. Do it now rather than carrying a dead prefix forever.
  if (count_ == 0 && size > 0) {
    return reset();
  }

  if (count_ > 0) {
    ESP_LOGI(TAG, "recovered %u queued fix(es) from %s (head %ld)",
             (unsigned)count_, filePath_, head_);
  }
  return true;
}

bool FixQueue::setMaxEntries(std::size_t maxEntries) {
  ScopedLock guard(lock_);
  if (maxEntries == maxEntries_) {
    return true;
  }
  ESP_LOGI(TAG, "queue cap %u -> %u", (unsigned)maxEntries_,
           (unsigned)maxEntries);
  maxEntries_ = maxEntries;

  // Nothing to do when the cap grew, was removed, or we are still under it.
  if (maxEntries_ == 0 || count_ <= maxEntries_) {
    return true;
  }

  // Shrunk below what we are holding: trim now. Doing this here rather than
  // leaving it to the next enqueue matters because a device that has just been
  // told to keep less may not enqueue again for a whole interval - or at all,
  // if the link is healthy - and the point of lowering the cap is to reclaim
  // the card straight away.
  const std::size_t toDrop = count_ - maxEntries_;
  if (!advanceHead(toDrop)) {
    ESP_LOGW(TAG, "could not trim %u fix(es) to the new cap", (unsigned)toDrop);
    return false;
  }
  ESP_LOGW(TAG, "new cap (%u) - dropped %u oldest fix(es)",
           (unsigned)maxEntries_, (unsigned)toDrop);
  return count_ == 0 ? reset() : compactIfNeeded();
}

bool FixQueue::enqueue(const std::string& envelope) {
  ScopedLock guard(lock_);
  // Enforce the cap first: if we are at (or somehow over) the limit, drop enough
  // of the oldest entries to leave room for this one. Keeps the newest, most
  // relevant positions when an outage runs very long.
  if (maxEntries_ != 0 && count_ >= maxEntries_) {
    const std::size_t toDrop = count_ - maxEntries_ + 1;
    if (advanceHead(toDrop)) {
      ESP_LOGW(TAG, "queue full (%u) - dropped %u oldest fix(es)",
               (unsigned)maxEntries_, (unsigned)toDrop);
    }
  }

  if (!card_.appendLine(filePath_, envelope)) {
    return false;
  }
  ++count_;
  index_.store(head_, count_);

  // A queue held at its cap advances the head on every append, so the dead
  // prefix grows without bound unless it is reclaimed from here too - the drain
  // path may not run for days.
  return compactIfNeeded();
}

bool FixQueue::peekBatch(std::size_t maxCount,
                         std::vector<std::string>& out) const {
  ScopedLock guard(lock_);
  return card_.readLinesFrom(filePath_, head_, maxCount, out);
}

bool FixQueue::popFront(std::size_t count) {
  ScopedLock guard(lock_);
  if (count == 0) {
    return true;
  }
  if (!advanceHead(count)) {
    return false;
  }
  // The queue draining is the cheapest compaction there is: drop the file.
  if (count_ == 0) {
    return reset();
  }
  return compactIfNeeded();
}

bool FixQueue::clear() {
  ScopedLock guard(lock_);
  return reset();
}

bool FixQueue::advanceHead(std::size_t n) {
  long        end   = head_;
  std::size_t found = 0;
  if (!card_.measureLines(filePath_, head_, n, end, found)) {
    return false;
  }
  head_  = end;
  count_ = (found >= count_) ? 0 : (count_ - found);
  index_.store(head_, count_);
  return true;
}

bool FixQueue::compactIfNeeded() {
  if (head_ < kCompactMinDeadBytes) {
    return true;
  }
  const long size = card_.fileSize(filePath_);
  // Compact only once the dead prefix outweighs what is still live. Phrased as
  // a subtraction rather than `head_ * 2 > size` because the doubling would
  // overflow a 32-bit long on a file this large.
  if (head_ <= size - head_) {
    return true;
  }
  if (!card_.compactFrom(filePath_, head_)) {
    return false;
  }
  head_ = 0;
  return index_.store(head_, count_);
}

bool FixQueue::reset() {
  const bool fileGone  = card_.removeFile(filePath_);
  const bool indexGone = index_.clear();
  head_                = 0;
  count_               = 0;
  return fileGone && indexGone;
}
