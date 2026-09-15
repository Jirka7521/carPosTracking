#pragma once

// =============================================================================
//  PowerSwitch  -  Read the physical run/sleep switch, debounced.
// -----------------------------------------------------------------------------
//  Responsibility (single!): answer one question - "does the operator want this
//  device running?" - from one GPIO, without ever answering it from a contact
//  bounce. It does not sleep anything; DeepSleepController does that, and
//  main.cpp decides when.
//
//  WIRING: the switch shorts the pin to ground and nothing else. The pin's
//  INTERNAL pull-up supplies the open state, which is why it must live on a pin
//  that has one - GPIOs 34-39 have no pull resistors in silicon at all, and a
//  floating input would wake the device at random. See Config.h for the pin
//  choice and what had to move to free it.
//
//      closed (shorted to GND)  ->  pin reads LOW   ->  run
//      open                     ->  pin reads HIGH  ->  sleep
//
//  WHY DEBOUNCING IS NOT OPTIONAL HERE. A wake from deep sleep is a REBOOT: the
//  chip restarts app_main() and pays a fresh TLS handshake and a cold GNSS
//  acquire. A mechanical switch bouncing for a few milliseconds would otherwise
//  turn one flick into a burst of full boots. Every answer this class gives is
//  therefore backed by a run of agreeing samples, and a disagreeing run returns
//  the LAST STABLE answer rather than a fresh guess - so a bounce is read as
//  "no change yet", never as a toggle.
//
//  The RTC HOLD, and why begin() starts by undoing something: the pin doubles as
//  the ext0 wake source, and arming ext0 hands the pad to the RTC mux with a
//  pull latched on it. After a wake that latch is still in place and the normal
//  GPIO driver reads nonsense through it, so the pad has to be handed back
//  before the first read. Getting this wrong looks exactly like a switch that
//  is stuck on.
// =============================================================================

#include <cstdint>

class PowerSwitch {
 public:
  //   pin        : GPIO the switch shorts to ground. Must be RTC-capable (it is
  //                the ext0 wake source) and must have an internal pull-up.
  //                -1 disables the feature: isRunRequested() is then always
  //                true and the device behaves exactly as it did before.
  //   runLevel   : the level that means "run" (0 for a switch to ground)
  //   debounceMs : how long the reading must hold steady to be believed
  PowerSwitch(int pin, int runLevel, uint32_t debounceMs);

  // Hand the pad back from the RTC mux and configure it as a pulled-up input.
  // Returns false when the pin could not be configured, in which case the
  // switch is disabled rather than half-working - a device that cannot read its
  // switch must keep running, never sleep forever.
  bool begin();

  // True when the operator wants the device running. Blocks for up to
  // debounceMs while it samples; call it from the main task, not an ISR.
  //
  // Always true when the feature is disabled or the pin failed to configure.
  bool isRunRequested();

  // Release the RTC latch left on `pin` by a previous ext0 sleep. Static, and
  // separate from begin(), because it has to run at the very top of app_main()
  // alongside the PWRKEY release - before any instance exists.
  static void releaseRtcHold(int pin);

 private:
  // How often the pin is sampled inside one debounce window. Ten milliseconds
  // is far longer than the switch-bounce it is rejecting and short enough that
  // a whole window is still imperceptible.
  static constexpr uint32_t kSampleStepMs = 10;

  int      pin_;
  int      runLevel_;
  uint32_t debounceMs_;

  // The last reading a full run of samples agreed on. Seeded to "run" so the
  // very first call cannot report sleep from a half-formed reading.
  bool lastStable_;
};
