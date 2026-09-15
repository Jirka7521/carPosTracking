#include "power/DeepSleepController.h"

#include "driver/gpio.h"
#include "driver/rtc_io.h"
#include "esp_log.h"
#include "esp_sleep.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "modem/Sim7000Modem.h"
#include "power/PowerSwitch.h"

static const char* TAG = "DeepSleep";

// Give the UART time to drain the last log lines before the clocks stop -
// otherwise the most interesting message (how long we are sleeping for) is the
// one that never makes it out.
static constexpr uint32_t kLogFlushMs = 50;

// Last-resort wake for a sleep that was meant to have no timer but whose ext0
// arming failed. Without it, a bad pin configuration would put the device to
// sleep with NO way back short of pulling the pack - so the one thing this must
// never do is nothing. A minute is short enough to notice and long enough not to
// drain anything while you do.
static constexpr uint32_t kFallbackWakeMs = 60000;

DeepSleepController::DeepSleepController(MqttClient& mqtt, WifiManager& wifi,
                                         GnssModule& gnss, SdCard& card,
                                         int modemPwrKeyPin, int wakeGpioPin,
                                         int wakeGpioLevel)
    : mqtt_(mqtt),
      wifi_(wifi),
      gnss_(gnss),
      card_(card),
      modemPwrKeyPin_(modemPwrKeyPin),
      wakeGpioPin_(wakeGpioPin),
      wakeGpioLevel_(wakeGpioLevel) {}

void DeepSleepController::releasePinHolds(int modemPwrKeyPin, int wakeGpioPin) {
  // Undo the latch applied before the previous sleep. Until this runs the pad
  // ignores the GPIO driver entirely, so Sim7000Modem::powerOn() would drive
  // PWRKEY into a pin that refuses to move and the modem would never start.
  const gpio_num_t pwrKey = static_cast<gpio_num_t>(modemPwrKeyPin);
  gpio_hold_dis(pwrKey);
  gpio_deep_sleep_hold_dis();

  // And hand the ext0 pad back from the RTC mux, or the first read of the power
  // switch comes through a latched pull and means nothing.
  PowerSwitch::releaseRtcHold(wakeGpioPin);
}

const char* DeepSleepController::wakeCauseName() {
  switch (esp_sleep_get_wakeup_cause()) {
    case ESP_SLEEP_WAKEUP_TIMER:
      return "timer";
    case ESP_SLEEP_WAKEUP_EXT0:
      return "ext0 GPIO";
    case ESP_SLEEP_WAKEUP_EXT1:
      return "ext1 GPIO";
    case ESP_SLEEP_WAKEUP_UNDEFINED:
      // Not a wake at all: power-on, brown-out, reset button, flash.
      return "power-on / reset";
    default:
      return "other";
  }
}

void DeepSleepController::shutdownPeripherals() {
  // 1. Say goodbye properly, while the radio is still up.
  mqtt_.stop();

  // 2. Stop the WiFi driver. ESP-IDF expects the radio stopped, not just idle,
  //    before deep sleep.
  wifi_.disconnect();

  // 3. The big one: modem off. This also stops the GNSS engine and cuts the
  //    active antenna amplifier - see GnssModule::disableGnss().
  gnss_.powerOffModule();

  // 4. Leave the filesystem consistent and release the SPI pins.
  card_.end();
}

void DeepSleepController::holdModemOff(int modemPwrKeyPin) {
  const gpio_num_t pwrKey = static_cast<gpio_num_t>(modemPwrKeyPin);

  // Park PWRKEY at its idle level before latching it, so we cannot freeze the
  // pin mid-pulse. The level comes from Sim7000Modem rather than being repeated
  // here, so the two agree even on an inverted board (kModemPwrKeyActiveLow).
  gpio_set_level(pwrKey, Sim7000Modem::pwrKeyIdleLevel());
  gpio_hold_en(pwrKey);

  // gpio_hold_en() alone stops holding once the chip powers down the digital
  // domain; this keeps the latch alive for the whole sleep.
  gpio_deep_sleep_hold_en();
}

void DeepSleepController::armWakeSources(uint32_t durationMs, int wakeGpioPin,
                                         int wakeGpioLevel) {
  // A zero duration means "no timer": ext0 is then the only way back, which is
  // exactly what the power switch wants. Everything below makes sure we do not
  // end up with no way back at all.
  bool haveWakeSource = false;

  if (durationMs > 0) {
    esp_sleep_enable_timer_wakeup(static_cast<uint64_t>(durationMs) * 1000ULL);
    haveWakeSource = true;
  }

  if (wakeGpioPin >= 0) {
    const gpio_num_t wakePin = static_cast<gpio_num_t>(wakeGpioPin);
    if (!rtc_gpio_is_valid_gpio(wakePin)) {
      ESP_LOGE(TAG, "GPIO %d is not RTC-capable - external wake NOT armed.",
               wakeGpioPin);
    } else {
      const esp_err_t err = esp_sleep_enable_ext0_wakeup(wakePin, wakeGpioLevel);
      if (err != ESP_OK) {
        ESP_LOGE(TAG, "ext0 wake on GPIO %d failed: %s", wakeGpioPin,
                 esp_err_to_name(err));
      } else {
        // Hold the pin at its idle level through the sleep, otherwise a
        // floating input wakes us at random. Pins 34-39 are input-only with no
        // internal pulls, so these calls do nothing there and the board must
        // provide the resistor.
        rtc_gpio_pullup_dis(wakePin);
        rtc_gpio_pulldown_dis(wakePin);
        if (wakeGpioLevel == 1) {
          rtc_gpio_pulldown_en(wakePin);  // idle LOW, wake on the rising signal
        } else {
          rtc_gpio_pullup_en(wakePin);  // idle HIGH, wake when pulled to ground
        }

        ESP_LOGI(TAG, "External wake armed on GPIO %d (level %d).", wakeGpioPin,
                 wakeGpioLevel);
        haveWakeSource = true;
      }
    }
  }

  if (!haveWakeSource) {
    // Everything asked for failed, and sleeping now would need a pack pull to
    // undo. Arm a timer instead and say so loudly.
    ESP_LOGE(TAG,
             "NO wake source could be armed - falling back to a %us timer so "
             "the device can recover.",
             (unsigned)(kFallbackWakeMs / 1000));
    esp_sleep_enable_timer_wakeup(static_cast<uint64_t>(kFallbackWakeMs) *
                                  1000ULL);
  }
}

void DeepSleepController::enterSleep() {
  ESP_LOGI(TAG, "Entering deep sleep. Next boot restarts app_main().");
  vTaskDelay(pdMS_TO_TICKS(kLogFlushMs));

  esp_deep_sleep_start();  // never returns; the chip reboots on wake
}

void DeepSleepController::sleepFor(uint32_t durationMs) {
  ESP_LOGI(TAG, "Sleeping for %us; modem and card going down first.",
           (unsigned)(durationMs / 1000));

  shutdownPeripherals();
  holdModemOff(modemPwrKeyPin_);
  armWakeSources(durationMs, wakeGpioPin_, wakeGpioLevel_);
  enterSleep();
}

void DeepSleepController::sleepUntilExternalWake() {
  ESP_LOGI(TAG,
           "Power switch off - sleeping until it is switched back on; modem "
           "and card going down first.");

  shutdownPeripherals();
  holdModemOff(modemPwrKeyPin_);
  armWakeSources(0, wakeGpioPin_, wakeGpioLevel_);
  enterSleep();
}

void DeepSleepController::sleepUntilExternalWakeBare(int modemPwrKeyPin,
                                                     int wakeGpioPin,
                                                     int wakeGpioLevel) {
  // No collaborators to quiesce: this runs before WiFi, MQTT, GNSS and the card
  // exist. The caller has already dealt with the modem - see the header.
  ESP_LOGI(TAG,
           "Power switch off at boot - sleeping until it is switched back on.");

  holdModemOff(modemPwrKeyPin);
  armWakeSources(0, wakeGpioPin, wakeGpioLevel);
  enterSleep();
}
