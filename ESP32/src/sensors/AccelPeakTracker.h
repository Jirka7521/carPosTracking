#pragma once

// =============================================================================
//  AccelPeakTracker  -  Remember the strongest acceleration between two reports.
// -----------------------------------------------------------------------------
//  Responsibility (single!): sample the ADXL345 on a fixed cadence and keep a
//  running PER-AXIS maximum, so the ordinary position report can carry the
//  strongest reading of the interval instead of one arbitrary instant.
//
//  The problem it solves: a report normally carries a single live sample. At the
//  default 60 s interval that is one reading a minute, and braking, cornering and
//  potholes all happen in between - they are simply never seen. Sampling every
//  second and keeping the peak costs one I2C read per second and nothing else:
//
//      report                                                  report
//         │                                                       │
//         │ * * * * * * * * * * * * * * * * * * * * * * * * * * * │
//         │ each sample folded into the running max               │
//         └───────────────────────────────────────────────────────┘
//                        takePeak() -> the report, window reset
//
//  Memory is O(1) - ONE sample is kept, never a list - so the reporting interval
//  can be arbitrarily long without this growing.
//
//  Per-axis, and what that costs: the three axes are tracked independently, so
//  the triple handed back may be assembled from three different moments and is
//  not a reading that ever actually occurred. That is the deliberate choice here
//  (each axis shows its own worst case), but it means anything deriving a
//  magnitude from the triple - the dashboard does - reads higher than any real
//  sample. Tracking the largest |a| and keeping that whole sample would be the
//  alternative; it was considered and not chosen.
//
//  The value kept per axis is SIGNED - the reading whose absolute value was the
//  largest. Braking and accelerating are not the same event, and dropping the
//  sign would make them indistinguishable.
//
//  Clipping: the driver runs the sensor in its +/-2 g range, so a peak saturates
//  there. Fine for braking (~0.8 g) and cornering (~0.5 g); a sharp pothole will
//  clip.
//
//  Threading: the sampling task and the main loop's takePeak() both touch the
//  accumulator, so it is mutex-guarded. Adxl345::read() carries its own lock, so
//  sharing the sensor with the main loop's debug print is safe.
// =============================================================================

#include <cstdint>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "sensors/AccelData.h"
#include "sensors/Adxl345.h"

class AccelPeakTracker {
 public:
  // Borrows `accel` (must outlive this object).
  //   sampleIntervalMs : gap between samples (kAccelSampleIntervalMs)
  AccelPeakTracker(Adxl345& accel, uint32_t sampleIntervalMs);

  // Create the sampling task. Returns false if it could not be created, which -
  // like every other optional subsystem here - is logged and carried on from.
  bool start();

  // Hand back the per-axis peak since the last call and begin a fresh window.
  // Returns false, leaving `out` invalid, when no sample has been folded in yet
  // (the very first report, or a sensor that is failing every read) so the
  // caller can fall back to a live reading rather than publish nothing.
  bool takePeak(AccelSample& out);

 private:
  // FreeRTOS entry point; forwards to run() on the instance in `arg`.
  static void taskEntry(void* arg);

  // Sampling loop, paced on absolute time so a slow read cannot make the cadence
  // drift later and later.
  void run();

  // Read once and fold the result into `peak_`. A failed read is skipped rather
  // than folded in - it would otherwise contribute a zero and, for the Z axis
  // that normally sits at 1 g, look like free fall.
  void sampleOnce();

  Adxl345& accel_;
  uint32_t sampleIntervalMs_;

  // The accumulator, guarded by `lock_`. `peak_` holds, per axis, the signed
  // reading with the largest absolute value seen this window.
  SemaphoreHandle_t lock_;
  AccelSample       peak_;
  bool              havePeak_;

  TaskHandle_t task_;
};
