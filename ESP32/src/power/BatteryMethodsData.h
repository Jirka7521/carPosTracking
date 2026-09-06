#pragma once

// =============================================================================
//  BatteryMethodsData.h  -  Plain data type describing ONE battery measurement.
// -----------------------------------------------------------------------------
//  A dependency-free value struct produced by BatteryMethods and consumed by
//  BatteryReporter, in the same spirit as BatteryData.h and AccelData.h.
//
//  It carries a measurement, not a verdict: `percent` is what the curve says
//  about the pack, and NOT what the payload will report. The charging sentinel,
//  the 1..100 floor and the "leave the field out" case all belong to
//  BatteryReporter - see its banner for why those rules live there.
//
//  Units: the voltage is an integer in MILLIVOLTS at the battery (the board's
//  divider is already undone) and the state of charge an integer PERCENT.
//  Nothing here is a float, which steers clear of the reduced float support in
//  the nano printf this firmware builds with.
// =============================================================================

#include <cstdint>

// One measurement of the pack.
//
//   millivolts  pack voltage, calibrated, from the MEDIAN of the trimmed ADC
//               burst. 0 when the ADC path had nothing to say (see `valid`).
//   percent     that voltage scored with the piecewise Li-ion curve, or -1 for
//               "absent" - which must never be read as a flat pack, and must
//               never reach the wire (see BatteryReporter, rule 3).
//   valid       true when the pack was actually measured this sweep. False on
//               USB power, where the sense pin is cut off from the cell.
struct BatteryMethodsSample {
  uint16_t millivolts = 0;
  int8_t   percent    = -1;
  bool     valid      = false;
};
