#pragma once

// =============================================================================
//  DeepSleepController  -  Shut the board down cleanly and deep-sleep it.
// -----------------------------------------------------------------------------
//  Responsibility (single!): perform the ordered shutdown that has to happen
//  before an ESP32 deep sleep, arm the wake sources, and enter sleep. It borrows
//  the subsystems it must quiesce but owns the *order*, which is where the real
//  knowledge lives:
//
//      0. Status - say WHY (PresenceReporter): "going to sleep" (on the
//                  interval, or because the car is parked), "switched off",
//                  "battery low", "error". It has to come first, while the
//                  link is still up - and it has to come before step 1, whose
//                  clean DISCONNECT is what makes the broker throw the Last
//                  Will away. Out of range it is kept on the SD card instead,
//                  which step 4 is still to unmount. Every sleep path goes
//                  through this class, so a new one cannot forget to report.
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
//  Wake sources depend on WHY the device is going to sleep:
//
//    sleepFor()                 the RTC timer, plus - when the caller passes a
//                               threshold - the accelerometer's activity
//                               interrupt on ext1 (the motion wake). Never ext0:
//                               this sleep happens with the power switch ON, so
//                               its pin already sits at the ext0 wake level, and
//                               ext0 is level-triggered - arming it would wake
//                               the chip the instant it went down.
//    sleepUntilExternalWake()   ext0 ALONE, which is what makes the power switch
//                               mean "off until you turn it back on" rather than
//                               "off until the next report". No accelerometer
//                               either: off means off.
//    sleepForLowBattery() /     the RTC timer ALONE - ext0 for the same reason as
//    sleepAfterError()          sleepFor(), and no motion wake: a flat pack or a
//                               fault is not fixed by the car moving.
//
//  The accelerometer is armed as the very LAST step, after the modem is off and
//  the card unmounted: its activity detection is AC-coupled against a reference
//  taken at the moment it is armed, and that reference has to be a quiet sample.
// =============================================================================

#include <cstdint>

#include "gnss/GnssModule.h"
#include "mqtt/MqttClient.h"
#include "mqtt/OfflineReason.h"
#include "mqtt/PresenceReporter.h"
#include "sdcard/SdCard.h"
#include "sensors/Adxl345.h"
#include "wifi/WifiManager.h"

class DeepSleepController {
 public:
  // Borrows every collaborator (all must outlive this object).
  //   mqtt          : disconnected before sleep
  //   wifi          : radio stopped before sleep
  //   gnss          : powered down before sleep (modem + engine + antenna)
  //   card          : unmounted before sleep
  //   modemPwrKeyPin: held HIGH through the sleep so the modem stays off
  //   wakeGpioPin   : the power switch's ext0 wake pin, or -1 for none
  //   wakeGpioLevel : the level on that pin which wakes us (0 or 1)
  //   accel         : armed for the motion wake, or nullptr for none
  //   motionWakePin : the accelerometer's INT1 GPIO (ext1), or -1 for none
  //   motionLowPower: put the accelerometer in its low-power rate for the sleep
  //   presence      : told why we are going down (step 0), or nullptr to sleep
  //                   without a status message
  DeepSleepController(MqttClient& mqtt, WifiManager& wifi, GnssModule& gnss,
                      SdCard& card, int modemPwrKeyPin, int wakeGpioPin,
                      int wakeGpioLevel, Adxl345* accel, int motionWakePin,
                      bool motionLowPower, PresenceReporter* presence);

  // Release the pin latches applied before the previous sleep. Call once at the
  // very start of app_main(), before any driver touches those pins - until this
  // runs, PWRKEY is frozen and the modem cannot be pulsed back on, and the wake
  // pin still reads through the RTC pull ext0 latched on it.
  // Harmless on a cold boot, where there is nothing latched.
  //
  //   wakeGpioPin   : the ext0 pin to hand back from the RTC mux, or -1
  //   motionWakePin : the ext1 pin to hand back likewise, or -1
  static void releasePinHolds(int modemPwrKeyPin, int wakeGpioPin,
                              int motionWakePin);

  // Human-readable reason this boot happened ("timer", "ext0", "power-on", ...).
  // Purely for logging, so a serial trace shows whether a wake was the scheduled
  // one or the external signal.
  static const char* wakeCauseName();

  // Quiesce everything, arm the wake sources and enter deep sleep for
  // `durationMs`. Never returns - the chip reboots when it wakes.
  //
  // `motionThresholdSteps` (THRESH_ACT, 62.5 mg per step) additionally arms the
  // accelerometer as an ext1 wake source; 0 - which is never a usable threshold -
  // means "timer only". If arming fails the device still sleeps on the timer,
  // which is always armed here, so it can never end up with no way back.
  [[noreturn]] void sleepFor(uint32_t durationMs,
                             uint8_t  motionThresholdSteps = 0);

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

  // The pack has fallen below the cut-off (see LowBatteryGuard): report
  // "battery low", shut down and sleep for `recheckMs` on the timer alone. The
  // next wake re-checks the pack before it powers anything up. Never returns.
  [[noreturn]] void sleepForLowBattery(uint32_t recheckMs);

  // A fault the firmware caught itself - `detail` is a short machine code such
  // as "gnss_init". Report it, shut down, and sleep `retryMs` on the timer
  // alone so the next boot tries again from scratch instead of the device
  // sitting awake doing nothing. Never returns.
  [[noreturn]] void sleepAfterError(const char* detail, uint32_t retryMs);

  // A timer-only sleep with nothing to quiesce, for the low-battery re-check
  // wakes that find the pack still too low: they stop before WiFi, MQTT, the
  // modem or the card are started. Like sleepUntilExternalWakeBare(), it does
  // not touch the modem - the caller has made sure it is off. Never returns.
  [[noreturn]] static void sleepForBare(uint32_t durationMs, int modemPwrKeyPin);

 private:
  // Step 0 above: tell the broker why, if there is anyone to tell.
  void sayGoodbye(OfflineReason reason, uint32_t sleepS,
                  const char* detail = nullptr);

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

  // Arm the accelerometer's activity interrupt and the ext1 wake on its INT1
  // pin. Returns false - and leaves ext1 disarmed - when there is no sensor, no
  // pin, or either step fails; the caller's timer is then the only wake source.
  bool armMotionWake(uint8_t thresholdSteps);

  // Log the last lines and enter deep sleep. Never returns.
  [[noreturn]] static void enterSleep();

  MqttClient&  mqtt_;
  WifiManager& wifi_;
  GnssModule&  gnss_;
  SdCard&      card_;
  int          modemPwrKeyPin_;
  int          wakeGpioPin_;
  int          wakeGpioLevel_;
  Adxl345*     accel_;
  int          motionWakePin_;
  bool         motionLowPower_;
  PresenceReporter* presence_;
};
