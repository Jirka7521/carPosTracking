#pragma once

// =============================================================================
//  BatteryMethods  -  Measure the pack, once per reporting cycle.
// -----------------------------------------------------------------------------
//  Responsibility (single!): produce one BatteryMethodsSample - the calibrated
//  pack voltage and the state of charge the Li-ion curve reads off it. It stores
//  nothing and publishes nothing.
//
//  It measures; it does not choose. BatteryReporter turns this sample into the
//  published battery_pct, and the publishing rules (the charging sentinel, the
//  1..100 floor, the "omit the field" case) live there rather than here, so this
//  class can stay a neutral measurer.
//
//  Charge DETECTION stays with BatteryMonitor, which owns GPIO35's other meaning
//  and the modem's AT+CBC. Nothing here answers that question.
//
//  It borrows AdcSampler (owning it not), like every other user of the ADC.
//
//  The burst is SPREAD IN TIME and TRIMMED, and both halves of that matter for
//  the same reason. Under a SIM7000 transmit burst (~2 A) or a WiFi publish the
//  pack rail sags for a few tens of milliseconds; a burst of back-to-back
//  conversions finishes in microseconds, so a sag that coincides with it moves
//  EVERY sample the same way and no amount of medianing can reject it. Spacing
//  the conversions (kBatteryAdcSampleGapMs) puts them on both sides of such a sag
//  rather than inside it, which demotes the sag to a minority - and the outlier
//  trim then deletes that minority outright, so the median is computed from what
//  the pack really sits at. Neither step is useful without the other: spacing
//  alone only spreads the damage, trimming alone has nothing but noise to trim.
//
//  The median-of-a-trimmed-burst and the piecewise curve were not picked by
//  taste: they are the pair that tracked the real pack best in the multi-method
//  captures this class used to write to the card, against four other voltage
//  sources and two other state-of-charge models.
//
//  Hardware caveat worth knowing before reading any measurement: on the
//  T-SIM7000G the VBAT sense pin is cut off from the cell whenever USB power is
//  connected (LilyGO issue #128), so the ADC reads ~0 there - which this class
//  reports as ABSENT (see noReadingMv), never as a flat pack.
// =============================================================================

#include <cstddef>
#include <cstdint>

#include "power/AdcSampler.h"
#include "power/BatteryMethodsData.h"

class BatteryMethods {
 public:
  // Borrows `adc` (it must outlive this object); everything else is a tuning
  // knob straight out of Config.h.
  //   vbatPin     : ADC1 pin the pack sits behind (through the divider)
  //   vbatDivider : on-board divider ratio (the voltage is multiplied back up
  //                 by this)
  //   samples     : ADC conversions per burst
  //   sampleGapMs : delay between those conversions, which is what spreads the
  //                 burst across a transmit burst rather than into one (see the
  //                 banner)
  //   madFactor   : how many median absolute deviations from the burst median a
  //                 sample may sit before it is discarded; 0 disables the trim
  //   noReadingMv : pack voltage below this = the ADC path has no battery in
  //                 front of it (see the USB caveat)
  BatteryMethods(AdcSampler& adc, int vbatPin, float vbatDivider,
                 std::size_t samples, uint32_t sampleGapMs, uint32_t madFactor,
                 uint32_t noReadingMv);

  // Claim the sense pin on the shared ADC. Returns true when it is usable.
  bool begin();

  // Take one measurement. Always fills `out` (an unreadable pack is marked
  // absent, never faked); returns out.valid.
  //
  // BLOCKS for roughly samples * sampleGapMs (~2 s at the defaults). That is
  // deliberate - see the banner - and it is spent on vTaskDelay, so the CPU is
  // idle rather than busy for it. Call it once per reporting cycle and OUTSIDE
  // the fix window; the cadence is unaffected because the reporting interval is
  // anchored at fix capture, so the time comes out of the idle wait rather than
  // out of the rhythm.
  bool sample(BatteryMethodsSample& out);

 private:
  // Median of `n` raw counts. Sorts `values` in place (insertion sort - n is a
  // handful, and it beats anything cleverer at this size).
  static uint32_t medianOf(uint32_t* values, std::size_t n);

  // Delete the transmit-droop samples from a burst, in place.
  //
  // Keeps only the samples within madFactor_ median absolute deviations of the
  // burst median, compacts the survivors to the front of `values` and returns how
  // many there are (never 0 - see the two fallbacks in the implementation, which
  // are what stop this filtering a legitimately collapsing pack down to nothing).
  // `scratch` must have room for `n` entries; it holds the deviations.
  //
  // Not static only because it reads madFactor_. Sorts `values` as a side effect,
  // which callers must not depend on - the compaction reorders it again.
  std::size_t rejectOutliers(uint32_t* values, std::size_t n, uint32_t* scratch);

  // The state-of-charge model: pack millivolts -> 0-100 %. Static and pure so a
  // logged voltage can be re-scored offline with the same arithmetic.
  static int8_t percentCurve(uint32_t mv);

  // Upper bound on one ADC burst. The burst lives on the stack, so this caps
  // that too; `samples_` is clamped to it in the constructor. Raised from 32
  // when the burst was spread in time: the window's span is set by the gap, but
  // a longer span wants more samples in it to keep the median sharp. Two arrays
  // of this size are on the stack (the counts and the deviations the trim works
  // from), which is still only half a kilobyte.
  static constexpr std::size_t kMaxSamples = 64;

  AdcSampler& adc_;

  int         vbatPin_;
  float       vbatDivider_;
  std::size_t samples_;
  uint32_t    sampleGapMs_;
  uint32_t    madFactor_;
  uint32_t    noReadingMv_;
};
