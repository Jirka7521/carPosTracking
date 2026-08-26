#pragma once

// =============================================================================
//  BatteryMethodsData.h  -  Plain data type describing ONE multi-method battery
//                           measurement.
// -----------------------------------------------------------------------------
//  A dependency-free value struct produced by BatteryMethods and consumed by
//  BatteryCsvLogger, in the same spirit as BatteryData.h and AccelData.h.
//
//  Why an array of sources instead of five named fields: the whole point of the
//  exercise is that no single method is authoritative, so the code that formats,
//  spreads and compares them wants to LOOP. Naming them individually would push
//  a five-way copy-paste into every one of those places.
//
//  Units: every voltage is an integer in MILLIVOLTS at the battery (the board's
//  divider is already undone), every state of charge an integer PERCENT. Nothing
//  here is a float, which keeps the CSV exact and steers clear of the reduced
//  float support in the nano printf this firmware builds with.
// =============================================================================

#include <cstdint>

// The five ways this board can arrive at a pack voltage. The first four are all
// the SAME burst of ADC samples read four different ways, which is exactly what
// makes their disagreement interesting: it is conversion maths, not noise.
enum BatterySource : uint8_t {
  kSourceNaive       = 0,  // raw counts * 3.3 V / 4095, no calibration
  kSourceCalPerSample,     // each sample calibrated, then averaged
  kSourceCalMean,          // calibration applied to the mean raw count
  kSourceCalMedian,        // calibration applied to the median raw count
  kSourceModem,            // the modem's own VBAT measurement (AT+CBC)
  kBatterySourceCount
};

// The three state-of-charge models applied to every source above.
enum BatteryModel : uint8_t {
  kModelLinear  = 0,  // straight line across the usable window
  kModelCurve,        // piecewise-linear Li-ion discharge curve
  kModelSigmoid,      // the standard LiPo sigmoid approximation
  kBatteryModelCount
};

// One sweep of every method.
//
//   rawMean/rawMedian  the ADC burst behind sources 0-3, kept because a count
//                      near 0 is the fingerprint of the GPIO35-on-USB cut-off
//                      and tells you instantly why those sources read nothing.
//   mv / mvValid       per-source pack voltage; an invalid source leaves 0 and
//                      must be read as "absent", never as "0 mV".
//   percent            [source][model], or -1 where the source had no reading.
//   modemPercent       the modem's own <bcl> figure, -1 when unavailable.
//   modemStatus        the modem's <bcs>: 0 not charging, 1 charging,
//                      2 charge complete, -1 unavailable.
//   solarRaw/solarMv   the charge-input (solar/VIN) sense pin, GPIO36.
//   inputPresent       true when that pin says a charge source is connected.
//   trendCharging      true when the pack voltage has risen across the trend
//                      window; trendUsable is false until it has filled.
//   spreadMv/spreadPct how far apart the methods landed - the deliverable.
//   valid              true when at least one source produced a reading.
struct BatteryMethodsSample {
  uint16_t rawMean   = 0;
  uint16_t rawMedian = 0;

  uint16_t mv[kBatterySourceCount]      = {};
  bool     mvValid[kBatterySourceCount] = {};

  int8_t percent[kBatterySourceCount][kBatteryModelCount] = {};

  int8_t modemPercent = -1;
  int8_t modemStatus  = -1;

  uint16_t solarRaw     = 0;
  uint16_t solarMv      = 0;
  bool     inputPresent = false;

  bool trendCharging = false;
  bool trendUsable   = false;

  uint16_t spreadMv  = 0;
  int8_t   spreadPct = -1;

  bool valid = false;
};
