#include "mqtt/AccelDebugStream.h"

#include "esp_log.h"
#include "esp_timer.h"
#include "time/UtcClock.h"
#include "util/ScopedLock.h"

static const char* TAG = "AccelDebugStream";

namespace {

// Stack for the tick task. Generous because a tick runs the whole delivery
// path: RSA-OAEP sealing (PayloadCrypto notes that its one-time entropy poll and
// PEM parse are what pushed the main task over its own limit), cJSON, and FATFS
// writes when the queue is involved.
constexpr uint32_t kTaskStackBytes = 8192;

// Same priority as app_main, so this can never preempt the task driving the
// modem - it only runs when that one is blocked, which is nearly always.
constexpr UBaseType_t kTaskPriority = 1;

constexpr int64_t kMicrosPerSecond = 1000000;

}  // namespace

AccelDebugStream::AccelDebugStream(Adxl345& accel, FixForwarder& forwarder,
                                   uint32_t intervalMs)
    : accel_(accel),
      forwarder_(forwarder),
      // A zero interval would spin; one tick is the fastest that means anything.
      intervalMs_(intervalMs == 0 ? 1 : intervalMs),
      lock_(xSemaphoreCreateMutex()),
      snapshot_(),
      snapshotUs_(0),
      haveSnapshot_(false),
      task_(nullptr),
      warnedNoFix_(false) {}

bool AccelDebugStream::start() {
  if (task_ != nullptr) {
    return true;  // already running
  }
  if (lock_ == nullptr) {
    ESP_LOGE(TAG, "no snapshot lock - refusing to start");
    return false;
  }

  const BaseType_t created =
      xTaskCreate(&AccelDebugStream::taskEntry, "accel_debug", kTaskStackBytes,
                  this, kTaskPriority, &task_);
  if (created != pdPASS) {
    ESP_LOGE(TAG, "could not create the debug task (out of memory?)");
    task_ = nullptr;
    return false;
  }

  ESP_LOGW(TAG,
           "accelerometer debug stream ON - publishing every %ums. This stores "
           "one position per second server-side; do not leave it enabled.",
           (unsigned)intervalMs_);
  return true;
}

void AccelDebugStream::updateSnapshot(const TelemetrySample& sample) {
  ScopedLock guard(lock_);
  snapshot_     = sample;
  snapshotUs_   = esp_timer_get_time();
  haveSnapshot_ = true;
}

void AccelDebugStream::taskEntry(void* arg) {
  static_cast<AccelDebugStream*>(arg)->run();
}

void AccelDebugStream::run() {
  const TickType_t period = pdMS_TO_TICKS(intervalMs_);
  TickType_t       wake   = xTaskGetTickCount();

  while (true) {
    // Absolute pacing: a publish that took 300 ms leaves a 700 ms wait rather
    // than pushing every later tick 300 ms further out.
    vTaskDelayUntil(&wake, period);
    publishOnce();

    // If that publish overran its slot - a slow ack, a backlog drain - the next
    // wake time is already in the past, and vTaskDelayUntil would fire back to
    // back to "catch up". Re-anchor instead, so missed ticks are genuinely
    // skipped: a debug stream that falls behind should thin out, not deliver a
    // burst of samples that were already stale when they were built.
    //
    // The subtraction is done in the tick type and then read as signed, which is
    // the usual way to compare FreeRTOS tick stamps safely across the 32-bit
    // wraparound.
    const TickType_t now = xTaskGetTickCount();
    if (static_cast<int32_t>(now - wake) >= 0) {
      wake = now;
    }
  }
}

bool AccelDebugStream::publishOnce() {
  // Copy the snapshot and let the lock go straight away: the forward path below
  // can block for seconds waiting on acks, and updateSnapshot() must never be
  // stuck behind it.
  TelemetrySample sample;
  int64_t         takenUs = 0;
  {
    ScopedLock guard(lock_);
    if (!haveSnapshot_) {
      if (!warnedNoFix_) {
        ESP_LOGI(TAG, "waiting for the first fix before streaming");
        warnedNoFix_ = true;
      }
      return false;
    }
    sample  = snapshot_;
    takenUs = snapshotUs_;
  }

  // Fresh accelerometer reading - the one part of the report that is actually
  // new on this tick. A failed read leaves the sample invalid, which the
  // publisher renders as "accel fields absent" rather than as zeros; there is
  // still no point sending a debug report with nothing debug-worthy in it.
  if (!accel_.read(sample.accel)) {
    ESP_LOGW(TAG, "accelerometer read failed - skipping this tick");
    return false;
  }

  // Advance the clock by however long ago the snapshot was taken, so successive
  // reports carry distinct timestamps. Without a valid GNSS time there is
  // nothing to advance and the API would reject the report anyway, so skip it.
  if (!sample.gnss.time.valid) {
    return false;
  }
  const int64_t agedSeconds = (esp_timer_get_time() - takenUs) / kMicrosPerSecond;
  if (agedSeconds > 0) {
    UtcClock::fromEpoch(UtcClock::toEpoch(sample.gnss.time) + agedSeconds,
                        sample.gnss.time);
  }

  // Same delivery as a real fix: sealed, published, acked, or queued on the card
  // when the link is down.
  forwarder_.process(sample);
  return true;
}
