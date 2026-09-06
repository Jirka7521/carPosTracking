#include "power/BatteryWindowSampler.h"

#include <cstring>

#include "esp_log.h"
#include "util/ScopedLock.h"

static const char* TAG = "BatteryWindowSampler";

namespace {

// One ADC conversion and a store per tick - no crypto, no files, no network.
// The same figure AccelPeakTracker's task uses, for the same reason: this is as
// small as a task stack usefully gets on ESP-IDF once the logging machinery is
// accounted for.
constexpr uint32_t kTaskStackBytes = 2048;

// Same priority as app_main, so sampling can never preempt the task driving the
// modem; it runs in the gaps, of which there are plenty.
constexpr UBaseType_t kTaskPriority = 1;

}  // namespace

BatteryWindowSampler::BatteryWindowSampler(AdcSampler& adc, int vbatPin,
                                          uint32_t sampleIntervalMs)
    : adc_(adc),
      vbatPin_(vbatPin),
      // A zero interval would spin the CPU; one millisecond is the floor that
      // still means something.
      sampleIntervalMs_(sampleIntervalMs == 0 ? 1 : sampleIntervalMs),
      lock_(xSemaphoreCreateMutex()),
      counts_{},
      count_(0),
      stride_(1),
      sinceStored_(0),
      task_(nullptr) {}

bool BatteryWindowSampler::begin() {
  if (!adc_.addPin(vbatPin_)) {
    ESP_LOGW(TAG, "pack sense GPIO%d unavailable - no battery readings",
             vbatPin_);
    return false;
  }
  return true;
}

bool BatteryWindowSampler::start() {
  if (task_ != nullptr) {
    return true;  // already running
  }
  if (lock_ == nullptr) {
    ESP_LOGE(TAG, "no reservoir lock - refusing to start");
    return false;
  }

  const BaseType_t created =
      xTaskCreate(&BatteryWindowSampler::taskEntry, "vbat_window",
                  kTaskStackBytes, this, kTaskPriority, &task_);
  if (created != pdPASS) {
    ESP_LOGE(TAG, "could not create the sampling task (out of memory?)");
    task_ = nullptr;
    return false;
  }

  ESP_LOGI(TAG,
           "pack window open (GPIO%d, sampling every %ums, up to %u kept, "
           "calibration %s)",
           vbatPin_, (unsigned)sampleIntervalMs_, (unsigned)kMaxSamples,
           adc_.hasCalibration() ? "on" : "off");
  return true;
}

bool BatteryWindowSampler::takeWindow(uint32_t* dest, std::size_t cap,
                                      std::size_t& countOut) {
  countOut = 0;
  if (dest == nullptr || cap == 0) {
    return false;
  }

  ScopedLock guard(lock_);

  // A short copy would silently describe only the front of the window, so cap
  // it honestly and say so - the caller and this class disagreeing about
  // kMaxSamples is a programming error, not a runtime condition.
  std::size_t taken = count_;
  if (taken > cap) {
    ESP_LOGW(TAG, "window holds %u samples but only %u fit - truncating",
             (unsigned)taken, (unsigned)cap);
    taken = cap;
  }
  if (taken > 0) {
    std::memcpy(dest, counts_, taken * sizeof(counts_[0]));
  }
  countOut = taken;

  // Start a fresh window, at the full rate again. Without this the counts would
  // accumulate for the lifetime of the device and every report would be the
  // median of everything that ever happened rather than of this cycle. The
  // stride resets with it: the next window is short again until it earns a
  // decimation of its own.
  count_       = 0;
  stride_      = 1;
  sinceStored_ = 0;
  return taken > 0;
}

void BatteryWindowSampler::taskEntry(void* arg) {
  static_cast<BatteryWindowSampler*>(arg)->run();
}

void BatteryWindowSampler::run() {
  const TickType_t period = pdMS_TO_TICKS(sampleIntervalMs_);
  TickType_t       wake   = xTaskGetTickCount();

  while (true) {
    vTaskDelayUntil(&wake, period);
    sampleOnce();
  }
}

void BatteryWindowSampler::sampleOnce() {
  // Outside the lock: AdcSampler carries its own, and holding the reservoir for
  // the length of a conversion would block takeWindow() for no reason.
  int value = 0;
  if (!adc_.readRaw(vbatPin_, value)) {
    return;  // see the header: a failed conversion is skipped, not stored
  }

  ScopedLock guard(lock_);

  // The stride: once the window has been decimated, only every stride-th
  // conversion is kept, so the stored samples stay evenly spaced in time.
  ++sinceStored_;
  if (sinceStored_ < stride_) {
    return;
  }
  sinceStored_ = 0;

  if (count_ >= kMaxSamples) {
    decimate();
  }
  counts_[count_++] = static_cast<uint32_t>(value);
}

void BatteryWindowSampler::decimate() {
  // Keep the even indices: they are the samples that were already spaced two
  // strides apart, so what is left is the same window at half the resolution
  // rather than half the window at full resolution.
  const std::size_t kept = count_ / 2;
  for (std::size_t i = 0; i < kept; ++i) {
    counts_[i] = counts_[i * 2];
  }
  count_ = kept;
  stride_ *= 2;

  ESP_LOGD(TAG, "window full - now keeping one conversion in %u",
           (unsigned)stride_);
}
