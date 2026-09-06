#include "settings/ScheduleStore.h"

#include <string>
#include <vector>

#include "esp_log.h"
#include "settings/ScheduleCodec.h"

static const char* TAG = "ScheduleStore";

ScheduleStore::ScheduleStore(SdCard& card, const char* filePath)
    : card_(card), filePath_(filePath) {}

ScheduleBundle ScheduleStore::load() const {
  ScheduleBundle bundle;  // invalid until something decodes into it

  // The document is written as a single compact line, so one line is the whole
  // file - the same shape as settings.json, and it reuses the same SdCard
  // primitive. A full bundle is under 4 KB, which is fine to hold once; it is
  // the queue files, thousands of lines long, that must be streamed.
  std::vector<std::string> lines;
  if (!card_.readLines(filePath_, 1, lines)) {
    ESP_LOGW(TAG, "card unreadable - no cached schedule.");
    return bundle;
  }
  if (lines.empty()) {
    ESP_LOGI(TAG, "no cached schedule on card.");
    return bundle;
  }

  if (!ScheduleCodec::decode(lines[0].data(), lines[0].size(), bundle)) {
    ESP_LOGW(TAG, "cached schedule is corrupt - ignoring it.");
    return ScheduleBundle();
  }

  ESP_LOGI(TAG, "loaded schedule v%u: %u profile(s), %u rule(s), %s",
           (unsigned)bundle.version(), (unsigned)bundle.profiles().size(),
           (unsigned)bundle.rules().size(),
           bundle.enabled() ? "enabled" : "disabled");
  return bundle;
}

bool ScheduleStore::save(const ScheduleBundle& bundle) {
  const std::string json = ScheduleCodec::encode(bundle);
  if (json.empty()) {
    return false;
  }
  if (!card_.writeFile(filePath_, json)) {
    ESP_LOGW(TAG, "could not cache the schedule to %s", filePath_);
    return false;
  }

  // The bundle itself is not logged: a dozen profiles is several kilobytes of
  // serial output every time one changes, and the summary is what anyone
  // watching actually needs.
  ESP_LOGI(TAG, "cached schedule v%u to %s (%u bytes)",
           (unsigned)bundle.version(), filePath_, (unsigned)json.size());
  return true;
}
