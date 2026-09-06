#pragma once

// =============================================================================
//  BatteryReporter  -  Turn one measurement into the percent that goes on the
//                      wire.
// -----------------------------------------------------------------------------
//  Responsibility (single!): turn one BatteryMethodsSample into the BatteryStatus
//  the telemetry payload carries. It measures nothing and stores nothing - it
//  applies the *publishing* rules to a measurement somebody else took, which is
//  why those rules live here rather than in BatteryMethods (a neutral measurer,
//  deliberately opinion-free) or in main.cpp (a wiring layer).
//
//  Three rules, in order, and each exists to protect a downstream contract:
//
//    1. Charging  -> percent 0, the agreed SENTINEL. The API accepts it and the
//       front end renders "charging" rather than a flat pack. It is also the only
//       honest answer here: on the T-SIM7000G the pack sense pin is cut off from
//       the cell while USB power is present (LilyGO issue #128), so the ADC
//       measurement this class reads has nothing to say.
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
  // Apply the three rules above to this cycle's measurement.
  //   methods  : the measurement BatteryMethods has just taken
  //   charging : the shipped charge detector's verdict (BatteryMonitor), which
  //              owns that question - see rule 1.
  // Fills `out` either way; returns out.valid.
  bool toStatus(const BatteryMethodsSample& methods, bool charging,
                BatteryStatus& out) const;

  // How the published percent was arrived at, for the serial console - so a log
  // line says which of the two sources (this or the modem's AT+CBC) produced it.
  const char* methodName() const { return "cal-median + Li-ion curve"; }
};
