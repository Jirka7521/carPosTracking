#include "status/StatusLeds.h"

#include "esp_log.h"

static const char* TAG = "StatusLeds";

namespace {

// The task does two GPIO writes and an enum compare; 2 kB is the same frame
// AccelPeakTracker uses and is more than enough.
constexpr uint32_t    kTaskStackBytes = 2048;
constexpr UBaseType_t kTaskPriority   = 1;  // same as app_main

}  // namespace

StatusLeds::StatusLeds(const WifiManager* wifi, int gnssPin, int wifiPin,
                       bool activeHigh, uint32_t tickMs,
                       uint32_t blinkHalfPeriodMs)
    : wifi_(wifi),
      gnssLed_(gnssPin, activeHigh),
      wifiLed_(wifiPin, activeHigh),
      // A zero tick would spin the CPU; one millisecond is the floor that still
      // means something.
      tickMs_(tickMs == 0 ? 1 : tickMs),
      blinkHalfPeriodMs_(blinkHalfPeriodMs == 0 ? 1 : blinkHalfPeriodMs),
      lock_(xSemaphoreCreateMutex()),
      gnssMode_(StatusLed::Mode::Off),
      task_(nullptr) {}

bool StatusLeds::begin() {
  const bool gnssOk = gnssLed_.begin();
  const bool wifiOk = wifiLed_.begin();
  return gnssOk && wifiOk;
}

bool StatusLeds::start() {
  if (task_ != nullptr) {
    return true;  // already running
  }
  if (lock_ == nullptr) {
    ESP_LOGE(TAG, "no mode lock - refusing to start");
    return false;
  }

  const BaseType_t created =
      xTaskCreate(&StatusLeds::taskEntry, "status_leds", kTaskStackBytes, this,
                  kTaskPriority, &task_);
  if (created != pdPASS) {
    ESP_LOGE(TAG, "could not create the indicator task (out of memory?)");
    task_ = nullptr;
    return false;
  }

  ESP_LOGI(TAG, "status LEDs on - refreshing every %ums, blinking every %ums",
           (unsigned)tickMs_, (unsigned)blinkHalfPeriodMs_);
  return true;
}

void StatusLeds::setGnss(StatusLed::Mode mode) {
  if (lock_ == nullptr) {
    return;
  }
  xSemaphoreTake(lock_, portMAX_DELAY);
  gnssMode_ = mode;
  xSemaphoreGive(lock_);
}

StatusLed::Mode StatusLeds::modeFor(WifiManager::LinkState state) {
  switch (state) {
    case WifiManager::LinkState::Connected:
      return StatusLed::Mode::On;
    case WifiManager::LinkState::Connecting:
      return StatusLed::Mode::Blink;
    case WifiManager::LinkState::Off:
    default:
      return StatusLed::Mode::Off;
  }
}

void StatusLeds::allOff() const {
  gnssLed_.off();
  wifiLed_.off();
}

void StatusLeds::taskEntry(void* arg) {
  static_cast<StatusLeds*>(arg)->run();
}

void StatusLeds::run() {
  TickType_t lastWake = xTaskGetTickCount();

  // Elapsed milliseconds, folded into the blink phase. Tracked rather than
  // counting ticks so the phase stays right if tickMs_ is changed.
  uint32_t elapsedMs = 0;

  while (true) {
    const bool blinkPhase =
        ((elapsedMs / blinkHalfPeriodMs_) % 2) == 0;

    // The GNSS mode is pushed in from the main loop; take a copy rather than
    // holding the lock across the GPIO writes.
    StatusLed::Mode gnssMode = StatusLed::Mode::Off;
    if (lock_ != nullptr) {
      xSemaphoreTake(lock_, portMAX_DELAY);
      gnssMode = gnssMode_;
      xSemaphoreGive(lock_);
    }
    gnssLed_.setMode(gnssMode);

    // The WiFi mode is polled straight off the manager - see the header for why
    // it cannot be pushed.
    wifiLed_.setMode(wifi_ != nullptr ? modeFor(wifi_->linkState())
                                      : StatusLed::Mode::Off);

    gnssLed_.apply(blinkPhase);
    wifiLed_.apply(blinkPhase);

    elapsedMs += tickMs_;
    vTaskDelayUntil(&lastWake, pdMS_TO_TICKS(tickMs_));
  }
}
