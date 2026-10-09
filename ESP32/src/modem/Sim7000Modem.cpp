#include "Sim7000Modem.h"

#include <cstring>

#include "config/Config.h"
#include "driver/gpio.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char* TAG = "Sim7000Modem";

// Small helper so the timing constants read clearly below.
static inline void delayMs(uint32_t ms) { vTaskDelay(pdMS_TO_TICKS(ms)); }

// How long to keep polling for "OK" after the PWRKEY pulse. The SIM7000
// datasheet quotes ~4.5 s from pulse to a usable UART, but a cold module with a
// discharged supply rail can take noticeably longer, and giving up early looks
// exactly like a dead modem. 30 s of patience once at boot is cheap insurance.
static constexpr int kPostPulseAttempts = 30;
static constexpr int kPostPulseStepMs   = 1000;

// Tries given to a bare "AT" before concluding the modem is genuinely off. One
// is not enough - see isResponsive(int).
static constexpr int kLivenessAttempts = 5;

Sim7000Modem::Sim7000Modem(SerialPort& serial, int pwrKeyPin)
    : serial_(serial), pwrKeyPin_(pwrKeyPin) {}

int Sim7000Modem::pwrKeyIdleLevel() {
  // Active-low wiring (the common case) idles HIGH and pulses LOW; the inverted
  // board revision does the opposite.
  return config::kModemPwrKeyActiveLow ? 1 : 0;
}

bool Sim7000Modem::begin() {
  // Park PWRKEY at its idle level before anything else, so the modem is not
  // accidentally held in the "pressed" state while the UART comes up.
  gpio_reset_pin(static_cast<gpio_num_t>(pwrKeyPin_));
  gpio_set_direction(static_cast<gpio_num_t>(pwrKeyPin_), GPIO_MODE_OUTPUT);
  gpio_set_level(static_cast<gpio_num_t>(pwrKeyPin_), pwrKeyIdleLevel());

  return serial_.begin();
}

void Sim7000Modem::pulsePwrKey(uint32_t lowMs) {
  // Toggle sequence per the SIM7000 datasheet: drive PWRKEY to its *active*
  // level for `lowMs`, then release it back to idle. Which electrical level is
  // "active" depends on the board revision - see kModemPwrKeyActiveLow.
  const int idle   = pwrKeyIdleLevel();
  const int active = idle == 1 ? 0 : 1;

  gpio_set_level(static_cast<gpio_num_t>(pwrKeyPin_), active);
  delayMs(lowMs);
  gpio_set_level(static_cast<gpio_num_t>(pwrKeyPin_), idle);
}

bool Sim7000Modem::powerOn() {
  ESP_LOGI(TAG, "Powering modem ON (PWRKEY GPIO %d, idle level %d)...",
           pwrKeyPin_, pwrKeyIdleLevel());

  // Is it already awake? The modem keeps its power state across an ESP32 reset,
  // so after a re-flash or a watchdog reboot it very often is. Probing costs a
  // few seconds; getting this wrong costs far more, because the pulse below
  // would *switch a running modem off* and every later poll would then fail.
  // detectBaudRate() doubles as that probe - it only reports success when a
  // rate actually answered "OK".
  if (detectBaudRate()) {
    ESP_LOGI(TAG, "Modem already responsive at %d baud.", serial_.baudRate());
    sendCommand("ATE0", 1000);
    return true;
  }

  // Genuinely silent: pulse PWRKEY to boot it. ~1 s per the datasheet.
  ESP_LOGI(TAG, "No answer - pulsing PWRKEY to boot the modem.");
  pulsePwrKey(1000);

  // The modem needs several seconds before its UART answers, and we still do
  // not know its bit rate, so each attempt re-probes every candidate.
  for (int attempt = 1; attempt <= kPostPulseAttempts; ++attempt) {
    delayMs(kPostPulseStepMs);
    if (detectBaudRate()) {
      ESP_LOGI(TAG, "Modem is up at %d baud (after %d s).", serial_.baudRate(),
               attempt);
      // Turn off command echo so replies are easier to parse.
      sendCommand("ATE0", 1000);
      return true;
    }
    ESP_LOGW(TAG, "Still no answer from the modem (%d/%d)...", attempt,
             kPostPulseAttempts);
  }

  // Everything below this line is a hardware-side fault: the firmware has done
  // all it can, so spell out what to check rather than just reporting failure.
  ESP_LOGE(TAG, "Modem did not respond after power-on.");
  ESP_LOGE(TAG, "Checklist: (1) supply - the SIM7000G needs ~2 A peaks, USB "
                "alone is often not enough, try a LiPo on the JST connector; "
                "(2) PWRKEY polarity - flip kModemPwrKeyActiveLow in Config.h; "
                "(3) TX/RX pins %d/%d may be swapped.",
           config::kModemTxPin, config::kModemRxPin);
  return false;
}

bool Sim7000Modem::detectBaudRate() {
  // Try whatever the port is on right now first: on the happy path (and on the
  // repeat calls made while polling after a PWRKEY pulse) this hits immediately
  // and the loop below never runs.
  if (isResponsive(2)) {
    return true;
  }

  for (size_t i = 0; i < config::kModemBaudCandidateCount; ++i) {
    const int candidate = config::kModemBaudCandidates[i];
    if (candidate == serial_.baudRate()) {
      continue;  // Just tried this one.
    }
    if (!serial_.setBaudRate(candidate)) {
      continue;
    }
    // Two tries per rate: an autobaud modem discards the first "AT" while it
    // measures the bit timing, and answers the second.
    if (isResponsive(2)) {
      ESP_LOGI(TAG, "Modem answered at %d baud.", candidate);
      return true;
    }
  }

  // Nothing answered. Restore the configured rate so the port is left in a
  // predictable state for the next attempt (and for the logs).
  serial_.setBaudRate(config::kModemBaudRate);
  return false;
}

bool Sim7000Modem::powerOff() {
  ESP_LOGI(TAG, "Powering modem OFF (low-power)...");

  // Preferred: graceful software shutdown. The modem replies "NORMAL POWER
  // DOWN" and then stops drawing meaningful current.
  if (sendCommand("AT+CPOWD=1", 10000, "NORMAL POWER DOWN")) {
    ESP_LOGI(TAG, "Modem powered down cleanly.");
    return true;
  }

  // Fallback: force it off with a long PWRKEY pulse if the command failed
  // (e.g. the modem was in a wedged state).
  ESP_LOGW(TAG, "Graceful power-down failed; forcing PWRKEY off.");
  pulsePwrKey(1500);
  delayMs(2000);
  return true;
}

bool Sim7000Modem::isResponsive() {
  return isResponsive(kLivenessAttempts);
}

bool Sim7000Modem::isResponsive(int attempts) {
  for (int i = 0; i < attempts; ++i) {
    if (sendCommand("AT", 1000, "OK")) {
      return true;
    }
  }
  return false;
}

bool Sim7000Modem::sendCommand(const char* cmd, char* response,
                               size_t responseSize, uint32_t timeoutMs,
                               const char* terminator) {
  serial_.flushInput();   // Drop stale bytes so we only read this reply.
  serial_.writeLine(cmd); // Appends the required '\r'.

  if (response != nullptr && responseSize > 0) {
    response[0] = '\0';
  }

  char       line[192];
  TickType_t start = xTaskGetTickCount();

  while ((xTaskGetTickCount() - start) < pdMS_TO_TICKS(timeoutMs)) {
    int len = serial_.readLine(line, sizeof(line), 200);
    if (len <= 0) {
      continue;  // Nothing yet - keep waiting until the overall timeout.
    }

    // Skip the command echo (present if ATE0 hasn't been applied yet).
    if (strcmp(line, cmd) == 0) {
      continue;
    }

    // Append this line to the caller's response buffer (if provided).
    if (response != nullptr && responseSize > 0) {
      size_t used = strlen(response);
      size_t room = responseSize - used;
      if (room > 2) {
        strncat(response, line, room - 2);
        strcat(response, "\n");
      }
    }

    // Success terminator?
    if (strstr(line, terminator) != nullptr) {
      return true;
    }
    // Hard error from the modem?
    if (strstr(line, "ERROR") != nullptr) {
      return false;
    }
  }

  return false;  // Timed out without seeing the terminator.
}

bool Sim7000Modem::sendCommand(const char* cmd, uint32_t timeoutMs,
                               const char* terminator) {
  return sendCommand(cmd, nullptr, 0, timeoutMs, terminator);
}
