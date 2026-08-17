#pragma once

// =============================================================================
//  AccelDebugStream  -  Publish the accelerometer at 1 Hz against the last fix.
// -----------------------------------------------------------------------------
//  Responsibility (single!): run a timer task that, every kAccelDebugIntervalMs,
//  takes a fresh accelerometer reading, attaches it to the most recent telemetry
//  sample the main loop handed over, and forwards that report exactly as a real
//  fix would be forwarded.
//
//  Why it is a task and not a step in the main loop: the main loop spends nearly
//  all of its time blocked - inside waitForFix() during an acquire that can run
//  for minutes, and inside the interruptible interval wait afterwards. A 1 Hz
//  sample cannot be driven from there without shortening both of those waits and
//  changing how the device behaves when the flag is off. A separate task leaves
//  the normal path untouched:
//
//      app_main task : [ acquire fix ..... ][ publish ][ wait interval ...... ]
//                                               |
//                                               v  updateSnapshot(sample)
//      this task     :  * * * * * * * * * * * * * * * * * * * * * *   (1 Hz)
//
//  What it does NOT touch: the modem. The main task owns that UART, and
//  interleaving AT commands from here would corrupt GNSS parsing. So the
//  battery percentage and modem temperature in each report are the ones carried
//  in the snapshot (at most one reporting interval old); only the accelerometer
//  is re-read. The accelerometer is I2C and Adxl345::read() is locked, so that
//  one is safe to share.
//
//  The timestamp is advanced, not repeated. Every published sample carries the
//  snapshot's position with its UTC time moved forward by the seconds since the
//  snapshot was taken (see UtcClock). This is not cosmetic: the API dedupes
//  stored positions on (device, fix time), so repeating the fix's own timestamp
//  would mean all but one sample per interval is silently discarded. The
//  position is honestly stale; the clock is honest about now, which is what
//  makes the accelerometer trace plottable.
//
//  Delivery is FixForwarder's, unchanged: same topic, same encrypted envelope,
//  same broker and API acks, and the same store-and-forward to the SD card when
//  the link is down. That is why the queue had to be sized for it - a week at
//  1 Hz is 604800 entries (see kSdMaxQueuedFixes).
//
//  This whole class is debug scaffolding, gated on config::kAccelDebugStream.
//  Leaving it on in the field means one stored position per second and a queue
//  that can evict real fixes during an outage - see the README.
// =============================================================================

#include <cstdint>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "mqtt/TelemetrySample.h"
#include "sdcard/FixForwarder.h"
#include "sensors/Adxl345.h"

class AccelDebugStream {
 public:
  // Borrows its collaborators (both must outlive this object).
  //   accel      : sampled fresh on every tick
  //   forwarder  : publishes or queues the report, exactly as for a real fix
  //   intervalMs : gap between reports (kAccelDebugIntervalMs)
  AccelDebugStream(Adxl345& accel, FixForwarder& forwarder,
                   uint32_t intervalMs);

  // Create the task and start ticking. Returns false if the task could not be
  // created, which - like every other optional subsystem here - is logged and
  // left to the caller to carry on without.
  bool start();

  // Hand over the newest complete telemetry sample. Called by the main loop for
  // every fix it publishes; the task copies from it until the next one arrives.
  // Cheap and non-blocking in practice: it holds the snapshot lock only for the
  // copy, never across a publish.
  void updateSnapshot(const TelemetrySample& sample);

 private:
  // FreeRTOS entry point; forwards to run() on the instance in `arg`.
  static void taskEntry(void* arg);

  // The tick loop. Paced on absolute time so the cost of a publish shortens the
  // next wait instead of pushing the cadence later every time, and re-anchored
  // when a publish overruns its slot so missed ticks are skipped rather than
  // fired back to back. A stream that falls behind should thin out, not deliver
  // a burst of samples that were already stale when they were built.
  void run();

  // Build and forward one report. Returns false when there is nothing to send
  // yet (no fix so far) or the accelerometer read failed.
  bool publishOnce();

  Adxl345&      accel_;
  FixForwarder& forwarder_;
  uint32_t      intervalMs_;

  // The snapshot and its capture time, guarded by `lock_`. `snapshotUs_` is an
  // esp_timer stamp, used only to work out how far to advance the clock.
  SemaphoreHandle_t lock_;
  TelemetrySample   snapshot_;
  int64_t           snapshotUs_;
  bool              haveSnapshot_;

  TaskHandle_t task_;
  bool         warnedNoFix_;  // so "waiting for a fix" is logged once, not 1 Hz
};
