#include "sdcard/FixQueue.h"

#include "esp_log.h"

static const char* TAG = "FixQueue";

FixQueue::FixQueue(SdCard& card, const char* filePath, std::size_t maxEntries)
    : card_(card), filePath_(filePath), maxEntries_(maxEntries), count_(0) {}

bool FixQueue::begin() {
  if (!card_.isMounted()) {
    return false;
  }
  // A power loss may have left fixes queued from a previous run - adopt them.
  count_ = card_.countLines(filePath_);
  if (count_ > 0) {
    ESP_LOGI(TAG, "recovered %u queued fix(es) from %s", (unsigned)count_,
             filePath_);
  }
  return true;
}

bool FixQueue::setMaxEntries(std::size_t maxEntries) {
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
  if (!card_.dropFirstLines(filePath_, toDrop)) {
    ESP_LOGW(TAG, "could not trim %u fix(es) to the new cap",
             (unsigned)toDrop);
    return false;
  }
  count_ -= toDrop;
  ESP_LOGW(TAG, "new cap (%u) - dropped %u oldest fix(es)",
           (unsigned)maxEntries_, (unsigned)toDrop);
  return true;
}

bool FixQueue::enqueue(const std::string& envelope) {
  // Enforce the cap first: if we are at (or somehow over) the limit, drop enough
  // of the oldest entries to leave room for this one. Keeps the newest, most
  // relevant positions when an outage runs very long.
  if (maxEntries_ != 0 && count_ >= maxEntries_) {
    const std::size_t toDrop = count_ - maxEntries_ + 1;
    if (card_.dropFirstLines(filePath_, toDrop)) {
      count_ -= toDrop;
      ESP_LOGW(TAG, "queue full (%u) - dropped %u oldest fix(es)",
               (unsigned)maxEntries_, (unsigned)toDrop);
    }
  }

  if (!card_.appendLine(filePath_, envelope)) {
    return false;
  }
  ++count_;
  return true;
}

bool FixQueue::peekBatch(std::size_t maxCount,
                         std::vector<std::string>& out) const {
  return card_.readLines(filePath_, maxCount, out);
}

bool FixQueue::popFront(std::size_t count) {
  if (count == 0) {
    return true;
  }
  if (!card_.dropFirstLines(filePath_, count)) {
    return false;
  }
  count_ = (count >= count_) ? 0 : (count_ - count);
  return true;
}

bool FixQueue::clear() {
  if (!card_.removeFile(filePath_)) {
    return false;
  }
  count_ = 0;
  return true;
}
