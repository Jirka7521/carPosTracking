#include "sdcard/QueueIndex.h"

#include <cstdio>
#include <vector>

#include "esp_log.h"

static const char* TAG = "QueueIndex";

QueueIndex::QueueIndex(SdCard& card, const char* filePath)
    : card_(card), filePath_(filePath) {}

bool QueueIndex::load(long& headOut, std::size_t& countOut) const {
  headOut  = 0;
  countOut = 0;

  std::vector<std::string> lines;
  if (!card_.readLines(filePath_, 1, lines) || lines.empty()) {
    return false;  // no sidecar (fresh card, or it was just cleared)
  }

  long          head  = 0;
  unsigned long count = 0;
  if (std::sscanf(lines[0].c_str(), "head=%ld count=%lu", &head, &count) != 2) {
    ESP_LOGW(TAG, "%s is not a valid index record - starting from the top",
             filePath_);
    return false;
  }
  if (head < 0) {
    ESP_LOGW(TAG, "%s has a negative head (%ld) - starting from the top",
             filePath_, head);
    return false;
  }

  headOut  = head;
  countOut = static_cast<std::size_t>(count);
  return true;
}

bool QueueIndex::store(long head, std::size_t count) {
  char record[64];
  std::snprintf(record, sizeof(record), "head=%ld count=%lu", head,
                static_cast<unsigned long>(count));
  if (!card_.writeFileDirect(filePath_, record)) {
    ESP_LOGW(TAG, "could not persist %s - the next boot will re-scan", filePath_);
    return false;
  }
  return true;
}

bool QueueIndex::clear() { return card_.removeFile(filePath_); }
