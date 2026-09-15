#pragma once

// =============================================================================
//  StatusLed  -  One indicator LED, and nothing else.
// -----------------------------------------------------------------------------
//  Responsibility (single!): own one GPIO and translate a MODE into a pin level.
//  It knows a pin number and a polarity. It does not know what it is indicating,
//  it owns no task and it keeps no timer - StatusLeds drives the blink phase and
//  decides what each LED means.
//
//  The three modes are deliberately the whole vocabulary:
//
//      Off    dark. The subsystem is not running at all.
//      Blink  working on it - searching, associating, retrying.
//      On     solid. Done: locked, or connected.
//
//  WIRING, and why it is active high: the pin drives the anode through a series
//  resistor, with the cathode at ground. During deep sleep the digital pads go
//  high-impedance, and a high-Z source cannot light an LED - so the indicators
//  go genuinely dark on sleep with no hold logic, no RTC pad and nothing to undo
//  on the next boot. Wiring them the other way (LED from 3V3 down to the pin)
//  would work electrically but would need all of that, for nothing.
// =============================================================================

#include <cstdint>

class StatusLed {
 public:
  enum class Mode : uint8_t {
    Off   = 0,  // dark
    Blink = 1,  // alternating, driven by the phase StatusLeds passes in
    On    = 2,  // solid
  };

  //   pin        : GPIO driving the LED, or -1 to disable this indicator
  //                entirely (every method then does nothing)
  //   activeHigh : true when a HIGH level lights the LED (see the header)
  StatusLed(int pin, bool activeHigh);

  // Configure the pin as an output and start dark. Returns false when the pin
  // could not be configured; a disabled LED (pin < 0) returns true and does
  // nothing, so a caller never has to special-case it.
  bool begin();

  // Change what this LED is indicating. Cheap enough to call every cycle - it
  // only stores the mode; apply() is what touches the hardware.
  void setMode(Mode mode);

  // Drive the pin to match the current mode. `blinkPhase` is the shared on/off
  // phase and is used only in Blink mode, so every LED in the system flashes in
  // step rather than each drifting on its own timer.
  void apply(bool blinkPhase) const;

  // Force the LED dark immediately, without disturbing its mode. Used on the
  // way into deep sleep, where the mode no longer matters but a lit LED during
  // the shutdown sequence looks like a fault.
  void off() const;

 private:
  // Drive the pin, translating `lit` through the configured polarity.
  void write(bool lit) const;

  int       pin_;
  bool      activeHigh_;
  Mode      mode_;
};
