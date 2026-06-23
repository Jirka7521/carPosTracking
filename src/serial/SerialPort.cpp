#include "SerialPort.h"

#include <cstring>

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

// Size of the driver-managed receive ring buffer.
static constexpr int kRxBufferSize = 1024;

SerialPort::SerialPort(uart_port_t port, int txPin, int rxPin, int baud)
    : port_(port), txPin_(txPin), rxPin_(rxPin), baud_(baud), installed_(false) {}

bool SerialPort::begin() {
  if (installed_) {
    return true;  // Already up - nothing to do.
  }

  // Standard 8N1, no flow control 
  uart_config_t cfg = {};
  cfg.baud_rate  = baud_;
  cfg.data_bits  = UART_DATA_8_BITS;
  cfg.parity     = UART_PARITY_DISABLE;
  cfg.stop_bits  = UART_STOP_BITS_1;
  cfg.flow_ctrl  = UART_HW_FLOWCTRL_DISABLE;
  cfg.source_clk = UART_SCLK_DEFAULT;

  // Install driver (RX buffer only; no TX buffer => writes are blocking).
  if (uart_driver_install(port_, kRxBufferSize, 0, 0, nullptr, 0) != ESP_OK) {
    return false;
  }
  if (uart_param_config(port_, &cfg) != ESP_OK) {
    uart_driver_delete(port_);
    return false;
  }
  if (uart_set_pin(port_, txPin_, rxPin_, UART_PIN_NO_CHANGE,
                   UART_PIN_NO_CHANGE) != ESP_OK) {
    uart_driver_delete(port_);
    return false;
  }

  installed_ = true;
  return true;
}

void SerialPort::end() {
  if (installed_) {
    uart_driver_delete(port_);
    installed_ = false;
  }
}

void SerialPort::flushInput() {
  if (installed_) {
    uart_flush_input(port_);
  }
}

size_t SerialPort::write(const char* data, size_t length) {
  if (!installed_) {
    return 0;
  }
  int written = uart_write_bytes(port_, data, length);
  return written < 0 ? 0 : static_cast<size_t>(written);
}

void SerialPort::writeLine(const char* line) {
  write(line, strlen(line));
  write("\r", 1);  // AT commands are terminated by a carriage return.
}

int SerialPort::readLine(char* buffer, size_t bufferSize, uint32_t timeoutMs) {
  if (!installed_ || bufferSize == 0) {
    return -1;
  }

  size_t      index = 0;
  TickType_t  start = xTaskGetTickCount();
  bool        gotAny = false;

  while ((xTaskGetTickCount() - start) < pdMS_TO_TICKS(timeoutMs)) {
    uint8_t c;
    // Poll one byte at a time with a short timeout so we stay responsive.
    int n = uart_read_bytes(port_, &c, 1, pdMS_TO_TICKS(20));
    if (n != 1) {
      continue;  // No byte this round - keep waiting until the global timeout.
    }
    gotAny = true;

    if (c == '\n') {
      // Strip any trailing carriage returns, terminate, and return the line.
      while (index > 0 && buffer[index - 1] == '\r') {
        --index;
      }
      buffer[index] = '\0';
      return static_cast<int>(index);
    }

    if (index < bufferSize - 1) {
      buffer[index++] = static_cast<char>(c);
    }
  }

  // Timed out. Return whatever partial data we have (or -1 if truly nothing).
  buffer[index] = '\0';
  return gotAny ? static_cast<int>(index) : -1;
}
