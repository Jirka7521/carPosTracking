#pragma once

// =============================================================================
//  Sim7000Modem  -  Owns the *modem itself*: power state and the AT protocol.
// -----------------------------------------------------------------------------
//  Responsibilities:
//    * Switch the whole modem on/off via the PWRKEY pin (for low-power sleep).
//    * Send AT commands and collect their replies over a SerialPort.
//
//  It deliberately knows nothing about GNSS - GnssModule sits on top of this
//  and speaks the GNSS-specific AT commands. This keeps the modem transport
//  reusable (e.g. if you later add cellular / MQTT on the same modem).
// =============================================================================

#include <cstddef>
#include <cstdint>

#include "serial/SerialPort.h"

class Sim7000Modem {
 public:
  // The modem borrows (does not own) the SerialPort and uses `pwrKeyPin` to
  // toggle hardware power.
  Sim7000Modem(SerialPort& serial, int pwrKeyPin);

  // Bring the modem up: configure the PWRKEY GPIO and open the UART.
  // Call once before powerOn(). Returns true on success.
  bool begin();

  // Power the whole modem ON (PWRKEY pulse) and wait until it answers "AT".
  // Returns true once the modem is responsive. This is the normal start path
  // after a deep low-power period.
  bool powerOn();

  // Gracefully power the whole modem OFF (AT+CPOWD=1) to minimise current draw.
  bool powerOff();

  // Quick liveness check: send a bare "AT" and expect "OK".
  bool isResponsive();

  // Same check, but retried up to `attempts` times. A single "AT" is not proof
  // of anything: a modem that is up but still emitting its boot URCs will miss
  // the first one or two, and an autobaud modem needs a few before it locks on.
  bool isResponsive(int attempts);

  // Hunt for the modem's actual bit rate: try the configured kModemBaudRate
  // first, then each of kModemBaudCandidates, leaving the port on whichever one
  // answered. Returns false if none did. Safe to call repeatedly - it starts
  // from the rate currently set, so a second call after a power-on pulse is
  // cheap when nothing changed.
  bool detectBaudRate();

  // The level PWRKEY sits at when idle, derived from kModemPwrKeyActiveLow.
  // Exposed so the deep-sleep path can latch the pin at the same idle level
  // this class uses, instead of hard-coding a polarity of its own.
  static int pwrKeyIdleLevel();

  // Send an AT command and capture the full multi-line reply.
  //   cmd          : command WITHOUT trailing CR (e.g. "AT+CGNSINF")
  //   response     : buffer that receives every reply line, '\n'-separated
  //   responseSize : size of that buffer
  //   timeoutMs    : how long to wait for the terminator
  //   terminator   : line we wait for to consider the command finished ("OK")
  //   Returns: true if `terminator` was seen, false on "ERROR"/timeout.
  bool sendCommand(const char* cmd, char* response, size_t responseSize,
                   uint32_t timeoutMs, const char* terminator = "OK");

  // Convenience overload when you don't care about the reply text.
  bool sendCommand(const char* cmd, uint32_t timeoutMs = 1000,
                   const char* terminator = "OK");

  // Direct access for components (like the NMEA scanner) that need to read raw
  // lines straight from the UART.
  SerialPort& serial() { return serial_; }

 private:
  // Drive PWRKEY LOW for `lowMs`, then release it HIGH again.
  void pulsePwrKey(uint32_t lowMs);

  SerialPort& serial_;
  int         pwrKeyPin_;
};
