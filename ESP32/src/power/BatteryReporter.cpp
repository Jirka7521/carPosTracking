#include "power/BatteryReporter.h"

#include <cstdio>

#include "esp_log.h"

static const char* TAG = "BatteryReporter";

// Short names for the three state-of-charge models, in BatteryModel order. They
// are the CSV's own column suffixes on purpose - see methodName().
static const char* kModelNames[kBatteryModelCount] = {"lin", "curve", "sig"};

BatteryReporter::BatteryReporter(BatterySource source, BatteryModel model)
    : source_(source < kBatterySourceCount ? source : kSourceCalMedian),
      model_(model < kBatteryModelCount ? model : kModelCurve) {
  // "p4_curve". The CSV numbers its sources from 1 while the enum starts at 0,
  // so the shift happens here and nowhere else.
  std::snprintf(name_, sizeof(name_), "p%u_%s",
                static_cast<unsigned>(source_) + 1, kModelNames[model_]);
}

bool BatteryReporter::toStatus(const BatteryMethodsSample& methods,
                               bool charging, BatteryStatus& out) const {
  out = BatteryStatus();

  // Rule 1: on charge, the sentinel - and the ADC sources have nothing to say
  // here anyway (the sense pin is cut off from the cell on USB power).
  if (charging) {
    out.charging = true;
    out.percent  = 0;
    out.valid    = true;
    return true;
  }

  // Rule 2: the configured method, when it answered this cycle.
  //
  // methods.valid is tested FIRST and is not redundant. BatteryMethods fills the
  // percent grid with -1 before it measures, but a sweep that never ran at all -
  // the sampler failed to come up, or the whole path is disabled - leaves a
  // default-constructed struct whose grid is ZERO. Reading that as a percent
  // would publish a fabricated 1 % for a pack nobody looked at.
  const int8_t percent = methods.percent[source_][model_];
  if (methods.valid && percent >= 0) {
    // Floor at 1 - 0 is spoken for. A pack this flat is about to cut out anyway,
    // so the one-percent lie is cheaper than an ambiguous sentinel. The ceiling
    // is defensive: every model already clamps to 100, and a value above it
    // would cost us the whole fix at the API's validator, not just the field.
    int clamped = percent;
    if (clamped < 1) clamped = 1;
    if (clamped > 100) clamped = 100;

    out.percent = static_cast<uint8_t>(clamped);
    // The voltage this percent was actually derived from, for the serial console
    // (BatteryData.h: millivolts are deliberately never published).
    out.millivolts = methods.mv[source_];
    out.charging   = false;
    out.valid      = true;
    return true;
  }

  // Rule 3: no reading and no charger. Leave the field out rather than invent
  // one - see the banner on why -1 must never reach the wire.
  ESP_LOGW(TAG, "%s had no reading and no charger - battery_pct omitted", name_);
  return false;
}
