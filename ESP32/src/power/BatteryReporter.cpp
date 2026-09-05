#include "power/BatteryReporter.h"

#include "esp_log.h"

static const char* TAG = "BatteryReporter";

bool BatteryReporter::toStatus(const BatteryMethodsSample& methods,
                               bool charging, BatteryStatus& out) const {
  out = BatteryStatus();

  // Rule 1: on charge, the sentinel - and the ADC has nothing to say here
  // anyway (the sense pin is cut off from the cell on USB power).
  if (charging) {
    out.charging = true;
    out.percent  = 0;
    out.valid    = true;
    return true;
  }

  // Rule 2: the measurement, when there was one.
  //
  // methods.valid is tested FIRST and is not redundant. A sweep that never ran
  // at all - the sampler failed to come up, or the whole path is disabled -
  // leaves a default-constructed struct, and only this flag tells that apart
  // from a pack that was genuinely looked at.
  if (methods.valid && methods.percent >= 0) {
    // Floor at 1 - 0 is spoken for. A pack this flat is about to cut out anyway,
    // so the one-percent lie is cheaper than an ambiguous sentinel. The ceiling
    // is defensive: the model already clamps to 100, and a value above it would
    // cost us the whole fix at the API's validator, not just the field.
    int clamped = methods.percent;
    if (clamped < 1) clamped = 1;
    if (clamped > 100) clamped = 100;

    out.percent = static_cast<uint8_t>(clamped);
    // The voltage this percent was actually derived from, for the serial console
    // (BatteryData.h: millivolts are deliberately never published).
    out.millivolts = methods.millivolts;
    out.charging   = false;
    out.valid      = true;
    return true;
  }

  // Rule 3: no reading and no charger. Leave the field out rather than invent
  // one - see the banner on why -1 must never reach the wire.
  ESP_LOGW(TAG, "%s had no reading and no charger - battery_pct omitted",
           methodName());
  return false;
}
