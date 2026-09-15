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
//  Wake sources: an optional ext0 GPIO when config::kWakeGpioPin is not -1, and
//  the RTC timer for every sleep that has a duration. sleepUntilExternalWake()
//  is the exception - it arms ext0 ALONE, which is what makes the power switch
//  mean "off until you turn it back on" rather than "off until the next report".
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
  // runs, PWRKEY is frozen and the modem cannot be pulsed back on, and the wake
  // pin still reads through the RTC pull ext0 latched on it.
  // Harmless on a cold boot, where there is nothing latched.
  //
  //   wakeGpioPin : the ext0 pin to hand back from the RTC mux, or -1
  static void releasePinHolds(int modemPwrKeyPin, int wakeGpioPin);

  // Human-readable reason this boot happened ("timer", "ext0", "power-on", ...).
  // Purely for logging, so a serial trace shows whether a wake was the scheduled
  // one or the external signal.
  static const char* wakeCauseName();

  // Quiesce everything, arm the wake sources and enter deep sleep for
  // `durationMs`. Never returns - the chip reboots when it wakes.
  [[noreturn]] void sleepFor(uint32_t durationMs);

  // The same, but with NO timer: the device sleeps until the external signal
  // arrives. This is what the power switch uses - "off" means off until somebody
  // turns it back on, not until the next report was due.
  //
  // Never returns. If ext0 cannot be armed a timer is armed instead, because a
  // device asleep with no wake source at all is bricked until its pack is
  // pulled; see kFallbackWakeMs in the implementation.
  [[noreturn]] void sleepUntilExternalWake();

  // The same again, for the one caller that has no collaborators to quiesce:
  // the switch check at the very top of app_main(), which runs before WiFi,
  // MQTT, GNSS and the card exist. It cannot call the member version because
  // there is nothing yet to shut down.
  //
  // It is the caller's job to have established that the modem is already off -
  // the modem keeps its power state across an ESP32 reset, so this deliberately
  // does NOT pulse PWRKEY, which on an already-off modem would switch it ON.
  [[noreturn]] static void sleepUntilExternalWakeBare(int modemPwrKeyPin,
                                                      int wakeGpioPin,
                                                      int wakeGpioLevel);

 private:
  // Steps 1-4 above: stop the network, the modem and the card.
  void shutdownPeripherals();

  // Step 5: latch PWRKEY at its idle level for the duration of the sleep.
  //
  // Static, with the pin passed in rather than read off the instance, so
  // sleepUntilExternalWakeBare() can share it - that path runs before any
  // instance exists.
  static void holdModemOff(int modemPwrKeyPin);

  // Arm the wake sources. `durationMs` of 0 means "no timer" - ext0 alone - so
  // the device stays asleep until the external signal arrives. Static for the
  // same reason as holdModemOff().
  static void armWakeSources(uint32_t durationMs, int wakeGpioPin,
                             int wakeGpioLevel);

  // Log the last lines and enter deep sleep. Never returns.
  [[noreturn]] static void enterSleep();

  MqttClient&  mqtt_;
  WifiManager& wifi_;
  GnssModule&  gnss_;
  SdCard&      card_;
  int          modemPwrKeyPin_;
  int          wakeGpioPin_;
  int          wakeGpioLevel_;
};
