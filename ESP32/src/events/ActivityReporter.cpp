#include "events/ActivityReporter.h"

#include <string>

#include "esp_log.h"
#include "esp_sleep.h"

static const char* TAG = "ActivityReporter";

ActivityReporter::ActivityReporter(StatusPublisher& publisher, EventLog& log)
    : publisher_(publisher), log_(log) {}

void ActivityReporter::recordWake() {
  WakeCause cause;
  switch (esp_sleep_get_wakeup_cause()) {
    case ESP_SLEEP_WAKEUP_TIMER:
      cause = WakeCause::Timer;
      break;
    case ESP_SLEEP_WAKEUP_EXT1:
      cause = WakeCause::Accelerometer;  // ext1 is only ever the ADXL345's INT1
      break;
    case ESP_SLEEP_WAKEUP_EXT0:
      cause = WakeCause::PowerSwitch;  // ext0 is only ever the power switch
      break;
    default:
      return;  // not a wake from deep sleep - see the header
  }

  std::string envelope;
  if (!publisher_.sealWake(cause, envelope)) {
    ESP_LOGW(TAG, "wake event could not be sealed - not recorded");
    return;
  }
  log_.submit(envelope);
}

void ActivityReporter::recordMotion(MotionChange change) {
  std::string envelope;
  if (!publisher_.sealMotion(change, envelope)) {
    ESP_LOGW(TAG, "motion event could not be sealed - not recorded");
    return;
  }
  log_.submit(envelope);
}
