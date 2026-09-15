#include "power/PowerSwitch.h"

#include "driver/gpio.h"
#include "driver/rtc_io.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char* TAG = "PowerSwitch";

PowerSwitch::PowerSwitch(int pin, int runLevel, uint32_t debounceMs)
    : pin_(pin),
      runLevel_(runLevel),
      debounceMs_(debounceMs),
      // Seeded to "run": a device that has not yet managed a stable reading
      // must keep running. The failure mode to avoid is sleeping by accident.
      lastStable_(true) {}

void PowerSwitch::releaseRtcHold(int pin) {
  if (pin < 0) {
    return;
  }
  const gpio_num_t num = static_cast<gpio_num_t>(pin);
  if (!rtc_gpio_is_valid_gpio(num)) {
    return;
  }
  // Undo the pull the ext0 arming latched on this pad. Harmless on a cold boot,
  // where there is nothing latched.
  rtc_gpio_deinit(num);
}

bool PowerSwitch::begin() {
  if (pin_ < 0) {
    ESP_LOGI(TAG, "power switch disabled in Config.h - always running.");
    return true;
  }

  // Hand the pad back from the RTC mux before configuring it. After an ext0
  // wake the latch is still in place and every read through it is nonsense.
  releaseRtcHold(pin_);

  gpio_config_t cfg = {};
  cfg.pin_bit_mask  = 1ULL << static_cast<uint32_t>(pin_);
  cfg.mode          = GPIO_MODE_INPUT;
  // The open state comes from the internal pull, opposite the run level: a
  // switch to ground needs a pull-up, a switch to 3V3 a pull-down.
  cfg.pull_up_en   = (runLevel_ == 0) ? GPIO_PULLUP_ENABLE : GPIO_PULLUP_DISABLE;
  cfg.pull_down_en = (runLevel_ == 0) ? GPIO_PULLDOWN_DISABLE
                                      : GPIO_PULLDOWN_ENABLE;
  cfg.intr_type = GPIO_INTR_DISABLE;

  const esp_err_t err = gpio_config(&cfg);
  if (err != ESP_OK) {
    ESP_LOGE(TAG,
             "GPIO %d could not be configured (%s) - switch DISABLED, the "
             "device will keep running.",
             pin_, esp_err_to_name(err));
    pin_ = -1;
    return false;
  }

  // 34-39 have no pull resistors in silicon, so gpio_config() quietly does
  // nothing for them and the board must supply one. Say so rather than let it
  // present later as a switch that reads randomly.
  if (!GPIO_IS_VALID_OUTPUT_GPIO(static_cast<gpio_num_t>(pin_))) {
    ESP_LOGW(TAG,
             "GPIO %d is input-only and has NO internal pull - an external "
             "resistor is required or the reading will float.",
             pin_);
  }

  ESP_LOGI(TAG, "Power switch on GPIO %d (run when level %d, debounce %ums).",
           pin_, runLevel_, (unsigned)debounceMs_);
  return true;
}

bool PowerSwitch::isRunRequested() {
  if (pin_ < 0) {
    return true;  // disabled, or failed to configure: always run
  }

  const gpio_num_t num = static_cast<gpio_num_t>(pin_);

  // One sample is meaningless on a mechanical contact, so require a whole run
  // of them to agree. The steps are what make this a debounce rather than a
  // delay: a bouncing switch disagrees somewhere in the run and we keep the
  // previous answer, which reads as "nothing has changed yet".
  const uint32_t steps =
      (debounceMs_ < kSampleStepMs) ? 1 : (debounceMs_ / kSampleStepMs);

  const bool first = (gpio_get_level(num) == runLevel_);
  for (uint32_t i = 1; i < steps; ++i) {
    vTaskDelay(pdMS_TO_TICKS(kSampleStepMs));
    if ((gpio_get_level(num) == runLevel_) != first) {
      return lastStable_;  // still bouncing - no new answer
    }
  }

  lastStable_ = first;
  return lastStable_;
}
