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
                                         int wakeGpioLevel, Adxl345* accel,
                                         int motionWakePin, bool motionLowPower,
                                         PresenceReporter* presence)
    : mqtt_(mqtt),
      wifi_(wifi),
      gnss_(gnss),
      card_(card),
      modemPwrKeyPin_(modemPwrKeyPin),
      wakeGpioPin_(wakeGpioPin),
      wakeGpioLevel_(wakeGpioLevel),
      accel_(accel),
      motionWakePin_(motionWakePin),
      motionLowPower_(motionLowPower),
      presence_(presence) {}

void DeepSleepController::releasePinHolds(int modemPwrKeyPin, int wakeGpioPin,
                                          int motionWakePin) {
  // Undo the latch applied before the previous sleep. Until this runs the pad
  // ignores the GPIO driver entirely, so Sim7000Modem::powerOn() would drive
  // PWRKEY into a pin that refuses to move and the modem would never start.
  const gpio_num_t pwrKey = static_cast<gpio_num_t>(modemPwrKeyPin);
  gpio_hold_dis(pwrKey);
  gpio_deep_sleep_hold_dis();

  // And hand the ext0 pad back from the RTC mux, or the first read of the power
  // switch comes through a latched pull and means nothing.
  PowerSwitch::releaseRtcHold(wakeGpioPin);

  // Likewise the ext1 pad, which a motion sleep left in the RTC mux with its
  // pulldown on. Nothing reads it digitally while awake - the activity latch is
  // polled over I2C - but a pad left half-owned by the RTC is a trap for whoever
  // does one day. Harmless when nothing was armed.
  if (motionWakePin >= 0) {
    const gpio_num_t motionPin = static_cast<gpio_num_t>(motionWakePin);
    if (rtc_gpio_is_valid_gpio(motionPin)) {
      rtc_gpio_deinit(motionPin);
    }
  }
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

void DeepSleepController::sayGoodbye(OfflineReason reason, uint32_t sleepS,
                                     const char* detail) {
  // Bounded by the status publish timeout and a no-op while disconnected, so a
  // dead link cannot hold the device awake on its way down.
  if (presence_ != nullptr) {
    presence_->reportOffline(reason, sleepS, detail);
  }
}

void DeepSleepController::shutdownPeripherals() {
  // 1. Say goodbye properly, while the radio is still up. The clean DISCONNECT
  //    is also what makes the broker discard our Last Will.
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

void DeepSleepController::sleepFor(uint32_t durationMs,
                                   uint8_t  motionThresholdSteps) {
  ESP_LOGI(TAG, "Sleeping for %us%s; modem and card going down first.",
           (unsigned)(durationMs / 1000),
           motionThresholdSteps > 0 ? " or until motion" : "");

  // sleep_s lets the dashboard say when to expect the device back - and notice
  // when it does not come back. A motion wake can only make that earlier.
  sayGoodbye(OfflineReason::Sleep, durationMs / 1000);
  shutdownPeripherals();
  holdModemOff(modemPwrKeyPin_);

  // Last of all, with the modem off and the card unmounted: the accelerometer
  // takes its AC reference at this moment, so it should be the quietest one.
  if (motionThresholdSteps > 0) {
    armMotionWake(motionThresholdSteps);
  }

  // The timer only - never ext0. This sleep happens with the power switch ON,
  // i.e. with its pin already at the ext0 wake level, and ext0 is level-
  // triggered: arming it here used to wake the chip the instant it went down,
  // which turned every sleep_between cycle into an immediate reboot. A switch
  // turned off mid-sleep is caught by checkpoint 0 on the next wake instead.
  armWakeSources(durationMs, -1, wakeGpioLevel_);
  enterSleep();
}

bool DeepSleepController::armMotionWake(uint8_t thresholdSteps) {
  if (accel_ == nullptr || motionWakePin_ < 0) {
    return false;
  }
  const gpio_num_t pin = static_cast<gpio_num_t>(motionWakePin_);
  if (!rtc_gpio_is_valid_gpio(pin)) {
    ESP_LOGE(TAG, "GPIO %d is not RTC-capable - motion wake NOT armed.",
             motionWakePin_);
    return false;
  }

  // Sensor first: if it cannot be armed, INT1 will never rise, and arming ext1
  // on a dead line would only advertise a wake source that does not exist.
  if (!accel_->armActivity(thresholdSteps, motionLowPower_)) {
    ESP_LOGW(TAG, "accelerometer not armed - sleeping on the timer alone.");
    return false;
  }

  const esp_err_t err =
      esp_sleep_enable_ext1_wakeup_io(1ULL << motionWakePin_,
                                      ESP_EXT1_WAKEUP_ANY_HIGH);
  if (err != ESP_OK) {
    ESP_LOGE(TAG, "ext1 wake on GPIO %d failed: %s", motionWakePin_,
             esp_err_to_name(err));
    return false;
  }

  // INT1 is push-pull and active high, so it needs no pull while the sensor is
  // there. The pulldown is for the day it is not: a floating ext1 pin would wake
  // the device at random. Internal pulls only survive deep sleep while the RTC
  // peripheral domain is powered, hence the explicit request.
  esp_sleep_pd_config(ESP_PD_DOMAIN_RTC_PERIPH, ESP_PD_OPTION_ON);
  rtc_gpio_pullup_dis(pin);
  rtc_gpio_pulldown_en(pin);

  ESP_LOGI(TAG, "Motion wake armed on GPIO %d (ext1, any high).",
           motionWakePin_);
  return true;
}

void DeepSleepController::sleepUntilExternalWake() {
  ESP_LOGI(TAG,
           "Power switch off - sleeping until it is switched back on; modem "
           "and card going down first.");

  // No sleep_s: off is off, there is nothing to expect back.
  sayGoodbye(OfflineReason::PowerOff, 0);
  shutdownPeripherals();
  holdModemOff(modemPwrKeyPin_);
  armWakeSources(0, wakeGpioPin_, wakeGpioLevel_);
  enterSleep();
}

void DeepSleepController::sleepForLowBattery(uint32_t recheckMs) {
  ESP_LOGW(TAG,
           "Battery below the cut-off - shutting down; re-checking the pack "
           "every %us.", (unsigned)(recheckMs / 1000));

  // No sleep_s either: the re-check wake stays dark unless the pack has
  // recovered, so there is no time at which the dashboard should expect it.
  sayGoodbye(OfflineReason::BatteryLow, 0);
  shutdownPeripherals();
  holdModemOff(modemPwrKeyPin_);
  armWakeSources(recheckMs, -1, wakeGpioLevel_);  // timer only - see banner
  enterSleep();
}

void DeepSleepController::sleepAfterError(const char* detail,
                                          uint32_t retryMs) {
  ESP_LOGE(TAG, "Unrecoverable this boot (%s) - sleeping %us, then retrying.",
           detail != nullptr ? detail : "error", (unsigned)(retryMs / 1000));

  sayGoodbye(OfflineReason::Error, retryMs / 1000, detail);
  shutdownPeripherals();
  holdModemOff(modemPwrKeyPin_);
  armWakeSources(retryMs, -1, wakeGpioLevel_);  // timer only - see banner
  enterSleep();
}

void DeepSleepController::sleepForBare(uint32_t durationMs,
                                       int modemPwrKeyPin) {
  ESP_LOGI(TAG, "Battery still low - back to sleep for %us without starting up.",
           (unsigned)(durationMs / 1000));

  holdModemOff(modemPwrKeyPin);
  armWakeSources(durationMs, -1, 0);  // timer only; the level is unused
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
