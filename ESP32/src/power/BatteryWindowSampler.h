#pragma once

// =============================================================================
//  BatteryWindowSampler  -  Collect pack readings for the whole awake window.
// -----------------------------------------------------------------------------
//  Responsibility (single!): sample the VBAT sense pin on a fixed cadence from
//  its own task and hold the RAW COUNTS until someone takes them. It converts
//  nothing, decides nothing and publishes nothing - BatteryMethods scores the
//  window it hands over, and BatteryReporter decides what of that reaches the
//  wire.
//
//  Why a window instead of a burst: under a SIM7000 transmit burst (~2 A) or a
//  WiFi publish the pack rail sags for a few tens of milliseconds. A handful of
//  back-to-back conversions finishes in microseconds, so a sag that coincides
//  with them moves EVERY sample the same way and no amount of medianing can
//  reject it. Spreading the conversions is what demotes such a sag to a
//  minority the outlier trim can then delete outright. This class spreads them
//  as far as they will go: over the entire time the device is awake.
//
//      boot / deep-sleep wake                                  publish
//         │                                                       │
//         │ * * * * * * * * * * * * * * * * * * * * * * * * * * * │
//         │ one conversion every kBatteryWindowSampleMs           │
//         └───────────────────────────────────────────────────────┘
//              modem, WiFi, MQTT, config, GNSS acquire     takeWindow()
//                                                          -> window reset
//
//  That covers the modem power-on, the MQTT connect and the whole fix hunt - all
//  the moments the rail actually moves - rather than the 2 s the old burst had
//  to guess its way into. A cycle that hunts for a lock for three minutes puts
//  hundreds of samples on both sides of every droop in it.
//
//  DECIMATION, and why the reservoir cannot simply fill up: the fix budget is a
//  runtime setting and can be minutes, so the sample count is unbounded while
//  memory is not. Two obvious policies are both wrong. Keeping the FIRST N
//  samples describes the boot and ignores the acquire; keeping the LAST N (a
//  ring) describes the minutes nearest the publish, which is precisely where the
//  radio traffic is - it would weight the median toward the droops instead of
//  away from them. So when the reservoir fills, every second entry is dropped
//  and the sampling stride doubles: the window stays a UNIFORM sample of its
//  whole span at any duration, in O(1) memory, and the median of a uniformly
//  decimated set is the median of the set.
//
//  Threading: the sampling task and the main loop's takeWindow() both touch the
//  reservoir, so it is mutex-guarded. AdcSampler carries its own lock, so
//  sharing the ADC with BatteryMonitor's charge sense on the same pin is safe.
//
//  Modelled on AccelPeakTracker, deliberately: same task shape, same take-and-
//  reset contract, same "an optional subsystem logs its failure and the device
//  carries on" style. The difference is what is kept - the accelerometer folds
//  each sample into a running peak and needs O(1) memory by construction, while
//  a median has to see the samples.
// =============================================================================

#include <cstddef>
#include <cstdint>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "power/AdcSampler.h"

class BatteryWindowSampler {
 public:
  // Upper bound on the samples one window keeps. Public because BatteryMethods
  // sizes the buffer it drains into from it, and the two must agree.
  //
  // 256 raw counts is a kilobyte, and at the default half-second cadence it is
  // just over two minutes before the first decimation halves the rate. Past
  // that the window keeps growing in time while staying this size - see the
  // banner - so this caps memory, never coverage.
  static constexpr std::size_t kMaxSamples = 256;

  // Borrows `adc` (it must outlive this object).
  //   vbatPin          : ADC1 pin the pack sits behind (through the divider)
  //   sampleIntervalMs : gap between conversions (kBatteryWindowSampleMs)
  BatteryWindowSampler(AdcSampler& adc, int vbatPin, uint32_t sampleIntervalMs);

  // Claim the sense pin on the shared ADC. Returns true when it is usable.
  bool begin();

  // Create the sampling task, which opens the first window. Returns false if it
  // could not be created - logged and carried on from, like every other optional
  // subsystem here.
  bool start();

  // Hand back the counts collected since the last call and begin a fresh window.
  // Copies at most `cap` entries into `dest` and reports how many in `countOut`.
  //
  // Returns false, with countOut == 0, when nothing has been collected yet (the
  // task never started, or every conversion failed). Does NOT block on the ADC:
  // it is a memcpy under the accumulator lock, so it is safe to call from the
  // reporting path with the modem mid-cycle.
  bool takeWindow(uint32_t* dest, std::size_t cap, std::size_t& countOut);

 private:
  // FreeRTOS entry point; forwards to run() on the instance in `arg`.
  static void taskEntry(void* arg);

  // Sampling loop, paced on absolute time so a slow conversion cannot make the
  // cadence drift later and later.
  void run();

  // Read once and store the count. A failed conversion is skipped rather than
  // stored - a zero would read as a flat pack and drag the median with it.
  void sampleOnce();

  // Halve the reservoir in place, keeping every second entry, and double the
  // stride. Called with the lock held, from sampleOnce(), when the reservoir is
  // full. See the banner for why this rather than truncating or ringing.
  void decimate();

  AdcSampler& adc_;

  int      vbatPin_;
  uint32_t sampleIntervalMs_;

  // The reservoir, guarded by `lock_`. `stride_` is how many conversions one
  // stored count now stands for (1 until the first decimation, then 2, 4, ...);
  // `sinceStored_` counts conversions toward the next stored one.
  SemaphoreHandle_t lock_;
  uint32_t          counts_[kMaxSamples];
  std::size_t       count_;
  uint32_t          stride_;
  uint32_t          sinceStored_;

  TaskHandle_t task_;
};
