#include "sensors/AccelPeakTracker.h"

#include <cmath>

#include "esp_log.h"
#include "util/ScopedLock.h"

static const char* TAG = "AccelPeakTracker";

namespace {

// One I2C read and three compares per tick - no crypto, no files, no network.
// This is as small as a task stack usefully gets on ESP-IDF once the logging
// machinery is accounted for.
constexpr uint32_t kTaskStackBytes = 2048;

// Same priority as app_main, so sampling can never preempt the task driving the
// modem; it runs in the gaps, of which there are plenty.
constexpr UBaseType_t kTaskPriority = 1;

// Keep whichever reading is further from zero, sign intact.
inline void foldAxis(double sample, double& peak) {
  if (std::fabs(sample) > std::fabs(peak)) {
    peak = sample;
  }
}

}  // namespace

AccelPeakTracker::AccelPeakTracker(Adxl345& accel, uint32_t sampleIntervalMs)
    : accel_(accel),
      // A zero interval would spin the CPU; one millisecond is the floor that
      // still means something.
      sampleIntervalMs_(sampleIntervalMs == 0 ? 1 : sampleIntervalMs),
      lock_(xSemaphoreCreateMutex()),
      peak_(),
      havePeak_(false),
      task_(nullptr) {}

bool AccelPeakTracker::start() {
  if (task_ != nullptr) {
    return true;  // already running
  }
  if (lock_ == nullptr) {
    ESP_LOGE(TAG, "no accumulator lock - refusing to start");
    return false;
  }

  const BaseType_t created =
      xTaskCreate(&AccelPeakTracker::taskEntry, "accel_peak", kTaskStackBytes,
                  this, kTaskPriority, &task_);
  if (created != pdPASS) {
    ESP_LOGE(TAG, "could not create the sampling task (out of memory?)");
    task_ = nullptr;
    return false;
  }

  ESP_LOGI(TAG, "peak tracking on - sampling every %ums",
           (unsigned)sampleIntervalMs_);
  return true;
}

bool AccelPeakTracker::takePeak(AccelSample& out) {
  ScopedLock guard(lock_);
  if (!havePeak_) {
    out = AccelSample();  // invalid - the caller falls back to a live read
    return false;
  }

  out = peak_;
  out.valid = true;

  // Start a fresh window. Without this the peak would latch for the lifetime of
  // the device and every report after the first big bump would repeat it.
  peak_     = AccelSample();
  havePeak_ = false;
  return true;
}

void AccelPeakTracker::taskEntry(void* arg) {
  static_cast<AccelPeakTracker*>(arg)->run();
}

void AccelPeakTracker::run() {
  const TickType_t period = pdMS_TO_TICKS(sampleIntervalMs_);
  TickType_t       wake   = xTaskGetTickCount();

  while (true) {
    vTaskDelayUntil(&wake, period);
    sampleOnce();
  }
}

void AccelPeakTracker::sampleOnce() {
  AccelSample sample;
  if (!accel_.read(sample)) {
    return;  // see the header: a failed read is skipped, not folded in
  }

  ScopedLock guard(lock_);
  foldAxis(sample.xG, peak_.xG);
  foldAxis(sample.yG, peak_.yG);
  foldAxis(sample.zG, peak_.zG);
  havePeak_ = true;
}
