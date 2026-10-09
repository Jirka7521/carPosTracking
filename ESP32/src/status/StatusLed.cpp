#include "status/StatusLed.h"

#include "driver/gpio.h"
#include "esp_log.h"

static const char* TAG = "StatusLed";

StatusLed::StatusLed(int pin, bool activeHigh)
    : pin_(pin), activeHigh_(activeHigh), mode_(Mode::Off) {}

bool StatusLed::begin() {
  if (pin_ < 0) {
    return true;  // disabled; every other method is a no-op too
  }

  gpio_config_t cfg = {};
  cfg.pin_bit_mask  = 1ULL << static_cast<uint32_t>(pin_);
  cfg.mode          = GPIO_MODE_OUTPUT;
  cfg.pull_up_en    = GPIO_PULLUP_DISABLE;
  cfg.pull_down_en  = GPIO_PULLDOWN_DISABLE;
  cfg.intr_type     = GPIO_INTR_DISABLE;

  const esp_err_t err = gpio_config(&cfg);
  if (err != ESP_OK) {
    ESP_LOGE(TAG, "GPIO %d could not be configured: %s", pin_,
             esp_err_to_name(err));
    pin_ = -1;  // disable rather than keep poking a pin we do not own
    return false;
  }

  write(false);
  return true;
}

void StatusLed::setMode(Mode mode) { mode_ = mode; }

void StatusLed::write(bool lit) const {
  if (pin_ < 0) {
    return;
  }
  const int level = (lit == activeHigh_) ? 1 : 0;
  gpio_set_level(static_cast<gpio_num_t>(pin_), level);
}

void StatusLed::apply(bool blinkPhase) const {
  switch (mode_) {
    case Mode::Off:
      write(false);
      break;
    case Mode::Blink:
      write(blinkPhase);
      break;
    case Mode::On:
      write(true);
      break;
  }
}

void StatusLed::off() const { write(false); }
