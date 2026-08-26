#pragma once

// =============================================================================
//  BatteryReporter  -  Pick the ONE battery percent that goes on the wire.
// -----------------------------------------------------------------------------
//  Responsibility (single!): turn one BatteryMethodsSample into the BatteryStatus
//  the telemetry payload carries. It measures nothing and stores nothing - it
//  applies the *publishing* rules to a measurement somebody else took, which is
//  why those rules live here rather than in BatteryMethods (a neutral measurer,
//  deliberately opinion-free) or in main.cpp (a wiring layer).
//
//  Which method it publishes is a constructor argument, named in Config.h as a
//  (source, model) pair. The shipped choice is kSourceCalMedian + kModelCurve -
//  the "p4_curve" column of the diagnostic capture (see BatteryCsvLogger), the
//  method that tracked the real pack best across the captures on the card.
//
//  Three rules, in order, and each exists to protect a downstream contract:
//
//    1. Charging  -> percent 0, the agreed SENTINEL. The API accepts it and the
//       front end renders "charging" rather than a flat pack. It is also the only
//       honest answer here: on the T-SIM7000G the pack sense pin is cut off from
//       the cell while USB power is present (LilyGO issue #128), so the ADC
//       sources this class reads have nothing to say.
//    2. A reading -> that percent, floored at 1, so a genuinely empty pack can
//       never be mistaken for the charging sentinel. That is the same 1..100
//       guarantee BatteryMonitor::voltageToPercent() has always made.
//    3. Neither   -> valid = false, and TelemetryPublisher leaves battery_pct out
//       of the JSON entirely. NEVER -1: BatteryMethods uses -1 for "absent", but
//       the API validates battery_pct to 0..100 and rejects the WHOLE fix on
//       anything outside it - the position with it.
// =============================================================================

#include <cstdint>

#include "power/BatteryData.h"
#include "power/BatteryMethodsData.h"

class BatteryReporter {
 public:
  // `source` and `model` name one cell of BatteryMethodsSample::percent - i.e.
  // one column of battery.csv. An out-of-range value falls back to the shipped
  // choice rather than indexing off the end of the array.
  BatteryReporter(BatterySource source, BatteryModel model);

  // Apply the three rules above to this cycle's sweep.
  //   methods  : the sweep BatteryMethods has just taken
  //   charging : the shipped charge detector's verdict (BatteryMonitor), which
  //              owns that question - see rule 1.
  // Fills `out` either way; returns out.valid.
  bool toStatus(const BatteryMethodsSample& methods, bool charging,
                BatteryStatus& out) const;

  // The published method's CSV column name ("p4_curve"), so a log line and a
  // capture can be lined up without a translation table.
  const char* methodName() const { return name_; }

 private:
  BatterySource source_;
  BatteryModel  model_;
  char          name_[12];  // "p<n>_<model>", composed once in the constructor
};
