#pragma once

// =============================================================================
//  BatteryMethods  -  Measure the pack every way this board allows, at once.
// -----------------------------------------------------------------------------
//  Responsibility (single!): produce one BatteryMethodsSample - five voltage
//  sources, three state-of-charge models each, the modem's own percentage, the
//  charge-input sense and the charging detectors - and say how far apart they
//  landed. It stores nothing and prints nothing; BatteryCsvLogger does that.
//
//  It measures; it does not choose. ONE cell of the grid it produces becomes the
//  published battery_pct - BatteryReporter picks which (kBatteryReportSourceIndex
//  / kBatteryReportModelIndex in Config.h, "p4_curve" by default) - and the rest
//  go to BatteryCsvLogger, where they keep that choice honest by making the
//  disagreement between methods visible instead of assumed away. Deciding what
//  to publish is deliberately NOT this class's job: it has to stay the neutral
//  measurer that the chosen method is judged against.
//
//  Charge DETECTION stays with BatteryMonitor. The detectors here (the modem's
//  <bcs>, the charge-input pin, the voltage trend) are corroborating columns for
//  a capture, not the verdict the firmware acts on.
//
//  It borrows AdcSampler and Sim7000Modem (owning neither), like every other
//  user of those two.
//
//  Ported from the Arduino comparison rig (the standalone BatteryTest sketch,
//  outside this repo), with two deliberate differences:
//
//    * ONE burst of samples feeds sources 0-3. The rig interleaved analogRead()
//      and analogReadMilliVolts(), so its V2/V3 gap mixed "different maths" with
//      "different samples"; converting one burst four ways isolates the maths,
//      which is the thing actually under test.
//    * The trend detector's window is measured in CALLS, not seconds. At one
//      call per reporting cycle it spans five cycles - minutes, not the rig's
//      ten seconds - so it reacts far more slowly. It is a corroborating signal
//      next to the modem's <bcs> and the charge-input pin, not the primary one.
//
//  Hardware caveat worth knowing before reading any capture: on the T-SIM7000G
//  the VBAT sense pin is cut off from the cell whenever USB power is connected
//  (LilyGO issue #128), so sources 0-3 read ~0 there while the modem's AT+CBC
//  keeps working - and then reports the charger rail rather than the cell.
// =============================================================================

#include <cstddef>
#include <cstdint>

#include "modem/Sim7000Modem.h"
#include "power/AdcSampler.h"
#include "power/BatteryMethodsData.h"

class BatteryMethods {
 public:
  // Borrows `adc` and `modem` (both must outlive this object); everything else
  // is a tuning knob straight out of Config.h.
  //   vbatPin / solarPin       : ADC1 pins for the pack and the charge input
  //   vbatDivider/solarDivider : on-board divider ratios (voltage is multiplied
  //                              back up by these)
  //   samples                  : ADC conversions per burst, averaged + medianed
  //   emptyMv / fullMv         : the linear model's 0 % and 100 % ends
  //   inputThresholdMv         : charge input above this = a source is present
  //   noReadingMv              : pack voltage below this = the ADC path has no
  //                              battery in front of it (see the USB caveat)
  BatteryMethods(AdcSampler& adc, Sim7000Modem& modem, int vbatPin,
                 int solarPin, float vbatDivider, float solarDivider,
                 std::size_t samples, uint32_t emptyMv, uint32_t fullMv,
                 uint32_t inputThresholdMv, uint32_t noReadingMv);

  // Claim both sense pins on the shared ADC. Returns true when at least the pack
  // pin is usable; a missing charge-input pin only costs the solar columns, so
  // it is a warning rather than a failure.
  bool begin();

  // Take one sweep. Always fills `out` (invalid sources are marked absent, never
  // faked); returns out.valid, i.e. whether anything answered at all. Costs one
  // ADC burst plus a single AT+CBC round trip.
  bool sample(BatteryMethodsSample& out);

 private:
  // Parse an AT+CBC reply into all three of its fields. A superset of
  // BatteryMonitor::parseCbcMillivolts(): both must tolerate the two reply
  // shapes SIMCom firmware revisions use ("<bcs>,<bcl>,<mV>" and a bare
  // "<volts>"), but only this one keeps the charge status and the modem's own
  // percentage, which the published payload has no field for.
  //   statusOut/percentOut are set to -1 on the bare-volts shape.
  static bool parseCbc(const char* response, uint32_t& mvOut, int8_t& statusOut,
                       int8_t& percentOut);

  // Median of `n` raw counts. Sorts `values` in place (insertion sort - n is a
  // handful, and it beats anything cleverer at this size).
  static uint32_t medianOf(uint32_t* values, std::size_t n);

  // The three state-of-charge models. Each maps a pack voltage in millivolts to
  // 0-100 %. Kept static and pure so a capture can be re-scored offline with the
  // same arithmetic.
  int8_t        percentLinear(uint32_t mv) const;
  static int8_t percentCurve(uint32_t mv);
  static int8_t percentSigmoid(uint32_t mv);

  // Push one pack voltage into the trend ring and report whether the window has
  // risen by more than the charge threshold since its oldest entry.
  bool noteTrend(uint32_t mv, bool& usableOut);

  // How many past voltages the trend detector keeps. Five, as in the rig - but
  // see the banner: here they are five CALLS apart, not five seconds.
  static constexpr std::size_t kTrendWindow = 5;

  // Upper bound on one ADC burst. The burst lives on the stack, so this caps
  // that too; `samples_` is clamped to it in the constructor.
  static constexpr std::size_t kMaxSamples = 32;

  AdcSampler&   adc_;
  Sim7000Modem& modem_;

  int         vbatPin_;
  int         solarPin_;
  float       vbatDivider_;
  float       solarDivider_;
  std::size_t samples_;
  uint32_t    emptyMv_;
  uint32_t    fullMv_;
  uint32_t    inputThresholdMv_;
  uint32_t    noReadingMv_;

  bool solarReady_ = false;  // false when GPIO36 could not be claimed

  uint32_t    trend_[kTrendWindow] = {};
  std::size_t trendCount_          = 0;  // entries filled so far (caps at window)
  std::size_t trendIndex_          = 0;  // next slot to overwrite = oldest entry
};
