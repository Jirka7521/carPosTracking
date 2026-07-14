#pragma once

// =============================================================================
//  DeepSleepController  -  Shut the board down cleanly and deep-sleep it.
// -----------------------------------------------------------------------------
//  Responsibility (single!): perform the ordered shutdown that has to happen
//  before an ESP32 deep sleep, arm the wake sources, and enter sleep. It borrows
//  the subsystems it must quiesce but owns the *order*, which is where the real
//  knowledge lives:
//
//      1. MQTT   - disconnect so the broker sees a DISCONNECT rather than
//                  waiting out the keep-alive on a session that is already gone.
//      2. WiFi   - stop the driver. ESP-IDF requires the radio be stopped before
//                  deep sleep, not merely idle.
//      3. Modem  - powerOffModule(): stops the GNSS engine, cuts power to the
//                  active antenna's amplifier (modem GPIO4) and drops the LTE PA.
//                  This is the single biggest current saving on the board.
//      4. SD     - unmount, so the FAT is clean and the SPI pins stop driving.
//      5. PWRKEY - latch the pin HIGH for the duration of the sleep. During deep
//                  sleep the digital IO matrix is powered down and pins float;
//                  a floating PWRKEY reads as a pulse and would switch the modem
//                  straight back on, which is exactly the current draw we just
//                  spent four steps eliminating.
//
//  Deep sleep does not return. The chip REBOOTS on wake and app_main() starts
//  over: nothing on the stack or the heap survives, only RTC memory does. Call
//  releasePinHolds() at the top of app_main() to undo step 5, before anything
//  tries to drive those pins again.
//
//  Wake sources: the RTC timer always, plus an optional ext0 GPIO when
//  config::kWakeGpioPin is not -1 (see Config.h).
// =============================================================================

#include <cstdint>

#include "gnss/GnssModule.h"
#include "mqtt/MqttClient.h"
#include "sdcard/SdCard.h"
#include "wifi/WifiManager.h"

class DeepSleepController {
 public:
  // Borrows every collaborator (all must outlive this object).
  //   mqtt          : disconnected before sleep
  //   wifi          : radio stopped before sleep
  //   gnss          : powered down before sleep (modem + engine + antenna)
  //   card          : unmounted before sleep
  //   modemPwrKeyPin: held HIGH through the sleep so the modem stays off
  //   wakeGpioPin   : extra ext0 wake pin, or -1 for "timer only"
  //   wakeGpioLevel : the level on that pin which wakes us (0 or 1)
  DeepSleepController(MqttClient& mqtt, WifiManager& wifi, GnssModule& gnss,
                      SdCard& card, int modemPwrKeyPin, int wakeGpioPin,
                      int wakeGpioLevel);

  // Release the pin latches applied before the previous sleep. Call once at the
  // very start of app_main(), before any driver touches those pins - until this
  // runs, PWRKEY is frozen and the modem cannot be pulsed back on.
  // Harmless on a cold boot, where there is nothing latched.
  static void releasePinHolds(int modemPwrKeyPin);

  // Human-readable reason this boot happened ("timer", "ext0", "power-on", ...).
  // Purely for logging, so a serial trace shows whether a wake was the scheduled
  // one or the external signal.
  static const char* wakeCauseName();

  // Quiesce everything, arm the wake sources and enter deep sleep for
  // `durationMs`. Never returns - the chip reboots when it wakes.
  [[noreturn]] void sleepFor(uint32_t durationMs);

 private:
  // Steps 1-4 above: stop the network, the modem and the card.
  void shutdownPeripherals();

  // Step 5 plus the wake sources.
  void holdModemOff();
  void armWakeSources(uint32_t durationMs);

  MqttClient&  mqtt_;
  WifiManager& wifi_;
  GnssModule&  gnss_;
  SdCard&      card_;
  int          modemPwrKeyPin_;
  int          wakeGpioPin_;
  int          wakeGpioLevel_;
};
