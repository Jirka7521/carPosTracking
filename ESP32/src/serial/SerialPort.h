#pragma once

// =============================================================================
//  SerialPort  -  Thin, friendly C++ wrapper around the ESP-IDF UART driver.
// -----------------------------------------------------------------------------
//  Responsibility (single!): move bytes to/from a UART peripheral and offer a
//  convenient "read one line" helper. It knows nothing about modems, AT
//  commands or GNSS - that separation keeps every layer easy to test and read.
// =============================================================================

#include <cstddef>
#include <cstdint>

#include "driver/uart.h"

class SerialPort {
 public:
  // Construct (does not touch hardware yet - call begin()).
  //   port  : which UART peripheral to use (e.g. UART_NUM_1)
  //   txPin : ESP32 GPIO wired to the modem's RX
  //   rxPin : ESP32 GPIO wired to the modem's TX
  //   baud  : bit rate (SIM7000G defaults to 9600)
  SerialPort(uart_port_t port, int txPin, int rxPin, int baud);

  // Install and configure the UART driver. Returns true on success.
  bool begin();

  // Uninstall the driver and release the pins.
  void end();

  // Throw away any bytes currently sitting in the receive buffer. Useful right
  // before sending a command so we only read its fresh reply.
  void flushInput();

  // Send raw bytes. Returns the number of bytes actually written.
  size_t write(const char* data, size_t length);

  // Send a text line followed by a carriage return ('\r'), which is what the
  // SIM7000G expects to terminate an AT command.
  void writeLine(const char* line);

  // Read a single line (terminated by '\n') into `buffer`. The trailing CR/LF
  // is stripped and the result is null-terminated.
  //   Returns: number of characters stored (>= 0), or -1 if nothing arrived
  //            before `timeoutMs` elapsed.
  int readLine(char* buffer, size_t bufferSize, uint32_t timeoutMs);

  // Retune the live port to a different bit rate. Needed because the modem's
  // configured baud is not knowable up front (see Sim7000Modem::detectBaudRate),
  // so we have to try several against the running driver. Pending input is
  // dropped, since bytes clocked in at the old rate would decode as garbage.
  // Returns false if the port is not open or the driver rejects the rate.
  bool setBaudRate(int baud);

  // The rate currently in use - handy for logging which probe succeeded.
  int baudRate() const { return baud_; }

 private:
  uart_port_t port_;
  int         txPin_;
  int         rxPin_;
  int         baud_;
  bool        installed_;  // Guards against double install / use-before-begin
};
