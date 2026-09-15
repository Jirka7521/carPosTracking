#include "sensors/AmbientWindowSampler.h"

#include <cstring>

#include "esp_log.h"
#include "util/Statistics.h"

static const char* TAG = "AmbientWindow";

namespace {

// The task does one sensor read and a couple of float stores; 3 kB covers the
// RMT driver's own frame on top of that with room to spare.
constexpr uint32_t   kTaskStackBytes = 3072;
constexpr UBaseType_t kTaskPriority  = 1;  // same as app_main

// Small RAII lock so an early return cannot leak the mutex.
class ScopedLock {
 public:
  explicit ScopedLock(SemaphoreHandle_t handle) : handle_(handle) {
    if (handle_ != nullptr) {
      xSemaphoreTake(handle_, portMAX_DELAY);
    }
  }
  ~ScopedLock() {
    if (handle_ != nullptr) {
      xSemaphoreGive(handle_);
    }
  }
  ScopedLock(const ScopedLock&)            = delete;
  ScopedLock& operator=(const ScopedLock&) = delete;

 private:
  SemaphoreHandle_t handle_;
};

}  // namespace

AmbientWindowSampler::AmbientWindowSampler(Dht22& sensor,
                                           uint32_t sampleIntervalMs)
    : sensor_(sensor),
      // A zero interval would spin the task; the sensor's own floor is enforced
      // inside Dht22::read() regardless, so this only stops the busy loop.
      sampleIntervalMs_(sampleIntervalMs == 0 ? 1 : sampleIntervalMs),
      lock_(xSemaphoreCreateMutex()),
      temperatures_(),
      humidities_(),
      count_(0),
      task_(nullptr) {}

bool AmbientWindowSampler::start() {
  if (task_ != nullptr) {
    return true;  // already running
  }
  if (lock_ == nullptr) {
    ESP_LOGE(TAG, "no accumulator lock - refusing to start");
    return false;
  }

  const BaseType_t created =
      xTaskCreate(&AmbientWindowSampler::taskEntry, "ambient_window",
                  kTaskStackBytes, this, kTaskPriority, &task_);
  if (created != pdPASS) {
    ESP_LOGE(TAG, "could not create the sampling task (out of memory?)");
    task_ = nullptr;
    return false;
  }

  ESP_LOGI(TAG, "ambient sampling on - reading every %ums",
           (unsigned)sampleIntervalMs_);
  return true;
}

void AmbientWindowSampler::taskEntry(void* arg) {
  static_cast<AmbientWindowSampler*>(arg)->run();
}

void AmbientWindowSampler::run() {
  TickType_t lastWake = xTaskGetTickCount();
  while (true) {
    sampleOnce();
    vTaskDelayUntil(&lastWake, pdMS_TO_TICKS(sampleIntervalMs_));
  }
}

void AmbientWindowSampler::sampleOnce() {
  AmbientSample reading;
  if (!sensor_.read(reading) || !reading.valid) {
    // Skipped, not stored. A failed frame contributes nothing rather than a
    // zero - see the header.
    return;
  }

  ScopedLock guard(lock_);
  if (count_ >= kMaxSamples) {
    // Four minutes of readings already in hand. Keeping the earliest ones is
    // arbitrary but harmless: at this point the window is far longer than any
    // real awake period, so this branch means something has gone wrong with the
    // cycle, not that the data needs thinning.
    return;
  }
  temperatures_[count_] = reading.temperatureC;
  humidities_[count_]   = reading.humidityPct;
  ++count_;
}

bool AmbientWindowSampler::takeMedian(AmbientSample& out) {
  out = AmbientSample();

  float       temperatures[kMaxSamples];
  float       humidities[kMaxSamples];
  std::size_t count = 0;

  {
    ScopedLock guard(lock_);
    count = count_;
    if (count > 0) {
      std::memcpy(temperatures, temperatures_, count * sizeof(float));
      std::memcpy(humidities, humidities_, count * sizeof(float));
    }
    // Reset under the same lock, so a reading taken between the copy and the
    // reset cannot be silently dropped.
    count_ = 0;
  }

  if (count == 0) {
    return false;
  }

  out.temperatureC = statistics::medianOf(temperatures, count);
  out.humidityPct  = statistics::medianOf(humidities, count);
  out.valid        = true;
  return true;
}
