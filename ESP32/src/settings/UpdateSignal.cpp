#include "settings/UpdateSignal.h"

#include "esp_log.h"

static const char* TAG = "UpdateSignal";

UpdateSignal::UpdateSignal() : events_(xEventGroupCreate()) {
  if (events_ == nullptr) {
    ESP_LOGE(TAG,
             "could not create the update event group - settings will be picked "
             "up at the next poll rather than immediately.");
  }
}

UpdateSignal::~UpdateSignal() {
  if (events_ != nullptr) {
    vEventGroupDelete(events_);
  }
}

void UpdateSignal::raise(EventBits_t bits) {
  if (events_ == nullptr) {
    return;
  }
  xEventGroupSetBits(events_, bits);
}

EventBits_t UpdateSignal::wait(EventBits_t bits, uint32_t timeoutMs) {
  if (events_ == nullptr) {
    return 0;
  }

  // pdTRUE clears the bits on exit; pdFALSE means "any of them", not "all".
  const EventBits_t fired = xEventGroupWaitBits(
      events_, bits, pdTRUE, pdFALSE, pdMS_TO_TICKS(timeoutMs));

  // xEventGroupWaitBits returns the whole group, including bits we were not
  // waiting on and did not clear. Mask so the caller only ever sees what it
  // asked about.
  return fired & bits;
}

EventBits_t UpdateSignal::takePending(EventBits_t bits) {
  if (events_ == nullptr) {
    return 0;
  }
  return xEventGroupClearBits(events_, bits) & bits;
}
