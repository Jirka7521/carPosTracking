#pragma once

// =============================================================================
//  AmbientWindowSampler  -  Collect DHT22 readings for the whole awake window.
// -----------------------------------------------------------------------------
//  Responsibility (single!): read the DHT22 on a fixed cadence from its own
//  task, hold the readings, and hand back the MEDIAN of them when the report is
//  assembled. It publishes nothing and decides nothing beyond "no readings ->
//  no value".
//
//  Modelled on BatteryWindowSampler, deliberately: same task shape, same
//  take-and-reset contract, same "an optional subsystem logs its failure and
//  the device carries on" style. Two things differ.
//
//  FIRST, THE CADENCE IS THE SENSOR'S, NOT OURS. The battery window samples
//  every 500 ms alongside the accelerometer; the DHT22 physically cannot be
//  read faster than once every 2 s, so this task runs four times slower and a
//  short awake window may collect only a handful of readings. That is a
//  property of the part, not something to tune around - see Dht22.h.
//
//  SECOND, THERE IS NO OUTLIER TRIM. The battery path trims because a SIM7000
//  transmit burst drags the pack rail down for tens of milliseconds and those
//  samples are measurement artefacts. Air temperature has no equivalent: a
//  reading that differs from its neighbours is usually the air actually
//  changing, and deleting it would be deleting the signal. The median alone is
//  enough to reject the occasional corrupt frame, and a corrupt frame does not
//  reach here anyway because the checksum already rejected it.
//
//      boot / deep-sleep wake                                  publish
//         |                                                       |
//         | *       *       *       *       *       *       *     |
//         | one reading every kDht22SampleIntervalMs               |
//         +-------------------------------------------------------+
//                                                        takeMedian()
//                                                        -> window reset
//
//  Bounded memory without decimation: the reservoir simply stops growing once
//  it is full. At 2 s a cadence, 128 entries is over four minutes - longer than
//  any awake window this device has - so the cap is a safety net rather than a
//  policy, and the simpler rule is the honest one to write.
//
//  Threading: the sampling task and the main loop's takeMedian() both touch the
//  reservoir, so it is mutex-guarded. Nothing else shares the sensor.
// =============================================================================

#include <cstddef>
#include <cstdint>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "sensors/AmbientData.h"
#include "sensors/Dht22.h"

class AmbientWindowSampler {
 public:
  // Upper bound on the readings one window keeps. See the banner: at the
  // sensor's 2 s floor this is four minutes of coverage.
  static constexpr std::size_t kMaxSamples = 128;

  // Borrows `sensor` (it must outlive this object).
  //   sampleIntervalMs : gap between reads (kDht22SampleIntervalMs)
  AmbientWindowSampler(Dht22& sensor, uint32_t sampleIntervalMs);

  // Create the sampling task, which opens the first window. Returns false if it
  // could not be created - logged and carried on from.
  bool start();

  // Hand back the median of the readings collected since the last call and
  // begin a fresh window.
  //
  // Returns false, leaving `out` invalid, when nothing has been collected yet -
  // the task never started, the sensor is absent, or the awake window was
  // shorter than the sensor's first conversion. The caller then publishes no
  // ambient fields at all, which is the honest answer.
  bool takeMedian(AmbientSample& out);

 private:
  // FreeRTOS entry point; forwards to run() on the instance in `arg`.
  static void taskEntry(void* arg);

  // Sampling loop, paced on absolute time so a slow read cannot make the
  // cadence drift later and later.
  void run();

  // Read once and store the result. A failed read is skipped rather than
  // stored - a zero would read as a freezing, bone-dry cabin and drag both
  // medians with it.
  void sampleOnce();

  Dht22&   sensor_;
  uint32_t sampleIntervalMs_;

  // The reservoir, guarded by `lock_`. Temperature and humidity are kept in
  // step but medianed INDEPENDENTLY: they are separate quantities and the
  // middle reading of one has no reason to be the middle reading of the other.
  SemaphoreHandle_t lock_;
  float             temperatures_[kMaxSamples];
  float             humidities_[kMaxSamples];
  std::size_t       count_;

  TaskHandle_t task_;
};
