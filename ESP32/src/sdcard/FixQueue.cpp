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
