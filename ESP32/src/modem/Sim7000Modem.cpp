#include "Sim7000Modem.h"

#include <cstring>

#include "driver/gpio.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char* TAG = "Sim7000Modem";

// Small helper so the timing constants read clearly below.
static inline void delayMs(uint32_t ms) { vTaskDelay(pdMS_TO_TICKS(ms)); }

Sim7000Modem::Sim7000Modem(SerialPort& serial, int pwrKeyPin)
    : serial_(serial), pwrKeyPin_(pwrKeyPin) {}

bool Sim7000Modem::begin() {
  // PWRKEY is an output that idles HIGH on the T-SIM7000G.
  gpio_reset_pin(static_cast<gpio_num_t>(pwrKeyPin_));
  gpio_set_direction(static_cast<gpio_num_t>(pwrKeyPin_), GPIO_MODE_OUTPUT);
  gpio_set_level(static_cast<gpio_num_t>(pwrKeyPin_), 1);

  return serial_.begin();
}

void Sim7000Modem::pulsePwrKey(uint32_t lowMs) {
  // Toggle sequence per the SIM7000 datasheet: hold PWRKEY LOW, then release.
  gpio_set_level(static_cast<gpio_num_t>(pwrKeyPin_), 0);
  delayMs(lowMs);
  gpio_set_level(static_cast<gpio_num_t>(pwrKeyPin_), 1);
}

bool Sim7000Modem::powerOn() {
  ESP_LOGI(TAG, "Powering modem ON...");

  // If it is already awake, don't toggle (a pulse would turn it off).
  if (isResponsive()) {
    ESP_LOGI(TAG, "Modem already responsive.");
    return true;
  }

  // ~1 s LOW pulse boots the modem.
  pulsePwrKey(1000);

  // The modem needs a few seconds before the UART answers. Poll for "AT" OK.
  for (int attempt = 0; attempt < 20; ++attempt) {
    delayMs(500);
    if (isResponsive()) {
      ESP_LOGI(TAG, "Modem is up.");
      // Turn off command echo so replies are easier to parse.
      sendCommand("ATE0", 1000);
      return true;
    }
  }

  ESP_LOGE(TAG, "Modem did not respond after power-on.");
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
  return sendCommand("AT", 1000, "OK");
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
