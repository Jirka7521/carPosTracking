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
//  It borrows AdcSampler (for the calibration) and BatteryWindowSampler (for the
//  readings), owning neither, like every other collaborator in this firmware.
//
//  WHERE THE READINGS COME FROM, and why that is not this class's job: the
//  conversions are taken by BatteryWindowSampler on its own task, one every half
//  second, for the whole time the device is awake - boot, modem, MQTT connect
//  and the entire fix hunt. This class drains that window and scores it. It used
//  to fire the conversions itself, as a two-second burst just before the
//  publish, which is why the split reads like an extraction: the measuring
//  stayed here, the sampling moved out to the task that can afford to run for
//  minutes.
//
//  The spreading is the whole point either way. Under a SIM7000 transmit burst
//  (~2 A) or a WiFi publish the pack rail sags for a few tens of milliseconds;
//  conversions taken back to back finish in microseconds, so a sag that
//  coincides with them moves EVERY sample the same way and no amount of
//  medianing can reject it. Spread far enough apart, such a sag is a MINORITY of
//  the window - and the outlier trim below then deletes that minority outright,
//  so the median is computed from what the pack really sits at. Neither half is
//  useful without the other: spreading alone only spreads the damage, trimming
//  alone has nothing but noise to trim.
//
//  The median-of-a-trimmed-window and the piecewise curve were not picked by
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
#include "power/BatteryWindowSampler.h"

class BatteryMethods {
 public:
  // Borrows `adc` and `window` (both must outlive this object); everything else
  // is a tuning knob straight out of Config.h.
  //   vbatDivider : on-board divider ratio (the voltage is multiplied back up
  //                 by this)
  //   madFactor   : how many median absolute deviations from the window median a
  //                 sample may sit before it is discarded; 0 disables the trim
  //   noReadingMv : pack voltage below this = the ADC path has no battery in
  //                 front of it (see the USB caveat)
  BatteryMethods(AdcSampler& adc, BatteryWindowSampler& window,
                 float vbatDivider, uint32_t madFactor, uint32_t noReadingMv);

  // Take one measurement: drain the window that has been filling since the last
  // call and score it. Always fills `out` (an unreadable pack is marked absent,
  // never faked); returns out.valid.
  //
  // Does NOT block - the waiting was done by the sampling task while the device
  // was busy with something else. Call it once per reporting cycle, and BEFORE
  // the publish: the window closes here, so the publish's own transmit droop
  // lands in the next window rather than in the one being scored.
  bool sample(BatteryMethodsSample& out);

 private:
  // Median of `n` raw counts. Sorts `values` in place (insertion sort - the
  // window is a few hundred entries at most, already nearly sorted after a trim,
  // and it beats anything cleverer at this size).
  static uint32_t medianOf(uint32_t* values, std::size_t n);

  // Delete the transmit-droop samples from a window, in place.
  //
  // Keeps only the samples within madFactor_ median absolute deviations of the
  // window median, compacts the survivors to the front of `values` and returns
  // how many there are (never 0 - see the two fallbacks in the implementation,
  // which are what stop this filtering a legitimately collapsing pack down to
  // nothing). `scratch` must have room for `n` entries; it holds the deviations.
  //
  // Not static only because it reads madFactor_. Sorts `values` as a side effect,
  // which callers must not depend on - the compaction reorders it again.
  std::size_t rejectOutliers(uint32_t* values, std::size_t n, uint32_t* scratch);

  // The state-of-charge model: pack millivolts -> 0-100 %. Static and pure so a
  // logged voltage can be re-scored offline with the same arithmetic.
  static int8_t percentCurve(uint32_t mv);

  // As many counts as one window can hand over. Taken from the sampler so the
  // two cannot drift apart.
  static constexpr std::size_t kMaxSamples = BatteryWindowSampler::kMaxSamples;

  AdcSampler&           adc_;
  BatteryWindowSampler& window_;

  float    vbatDivider_;
  uint32_t madFactor_;
  uint32_t noReadingMv_;

  // The drained window and the trim's scratch. Members rather than locals: two
  // kilobytes is more than a task frame should carry, and sample() no longer
  // blocks, so there is nothing to be gained by making them transient.
  uint32_t values_[kMaxSamples];
  uint32_t work_[kMaxSamples];
};
