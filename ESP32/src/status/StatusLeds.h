#pragma once

// =============================================================================
//  StatusLeds  -  Drive the two front-panel indicators from one small task.
// -----------------------------------------------------------------------------
//  Responsibility (single!): own the GNSS and WiFi LEDs, run the blink phase,
//  and keep both in step with what the device is actually doing.
//
//      YELLOW (GNSS)   blinking : hunting a fix
//                      solid    : locked
//                      dark     : not searching - asleep, or the acquire gave up
//
//      GREEN (WiFi)    blinking : associating, or retrying in the background
//                      solid    : connected, holding an IP
//                      dark     : radio stopped (kWifiEnabled false, or shut
//                                 down on the way into sleep)
//
//  WHY THE TWO LEDS ARE DRIVEN DIFFERENTLY. The GNSS state is PUSHED: the main
//  loop knows exactly when it starts an acquire and exactly when one succeeds,
//  so it calls setGnss() at those two points. The WiFi state is POLLED: the
//  connection comes and goes on the WiFi driver's own event task, at moments the
//  main loop knows nothing about - and the main loop is blocked inside
//  averager.acquire() for minutes at a time, so it could not forward those
//  events even if it saw them. This task therefore reads WifiManager::linkState()
//  on every tick.
//
//  That is also why the dependency points this way round. WifiManager stays
//  ignorant of LEDs - it is an independent subsystem that has to keep working in
//  a build with no indicators at all - so the presentation layer reaches into
//  it, never the reverse.
//
//  Modelled on AccelPeakTracker: same task shape, same "an optional subsystem
//  logs its failure and the device carries on" style.
// =============================================================================

#include <cstdint>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "status/StatusLed.h"
#include "wifi/WifiManager.h"

class StatusLeds {
 public:
  // Borrows `wifi` (it must outlive this object). Pass nullptr in a build with
  // no WiFi and the green LED simply stays dark.
  //   gnssPin / wifiPin : GPIOs for the yellow and green LEDs, -1 to disable
  //   activeHigh        : true when a HIGH level lights an LED
  //   tickMs            : how often the task refreshes the pins
  //   blinkHalfPeriodMs : how long each half of a blink lasts
  StatusLeds(const WifiManager* wifi, int gnssPin, int wifiPin, bool activeHigh,
             uint32_t tickMs, uint32_t blinkHalfPeriodMs);

  // Configure both pins. Returns false only if a pin could not be claimed; the
  // device carries on regardless.
  bool begin();

  // Create the driving task. Returns false if it could not be created.
  bool start();

  // What the GNSS receiver is doing. Called from the main loop either side of
  // an acquire.
  void setGnss(StatusLed::Mode mode);

  // Both LEDs dark, immediately. Called on the way into deep sleep, before the
  // shutdown sequence, so the indicators do not sit lit through it.
  void allOff() const;

 private:
  // FreeRTOS entry point; forwards to run() on the instance in `arg`.
  static void taskEntry(void* arg);

  // Refresh loop, paced on absolute time.
  void run();

  // Map the WiFi link state onto a LED mode. Free function in spirit - it reads
  // only its argument - but a member so it can see the Mode enum plainly.
  static StatusLed::Mode modeFor(WifiManager::LinkState state);

  const WifiManager* wifi_;

  StatusLed gnssLed_;
  StatusLed wifiLed_;

  uint32_t tickMs_;
  uint32_t blinkHalfPeriodMs_;

  // The GNSS mode is written by the main task and read by this one, so it is
  // mutex-guarded. The WiFi mode needs no such protection: it is derived inside
  // the task from a single aligned read of WifiManager's own state.
  SemaphoreHandle_t lock_;
  StatusLed::Mode   gnssMode_;

  TaskHandle_t task_;
};
