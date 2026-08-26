#pragma once

// =============================================================================
//  ChargerWatcher  -  Spot the moment the charger comes off.
// -----------------------------------------------------------------------------
//  Responsibility (single!): remember whether the charger was connected the last
//  time it was asked, and report the present -> absent EDGE. Nothing else in this
//  firmware remembers anything from one cycle to the next, which is exactly why
//  this is a class and not two lines in the loop.
//
//  It does no I/O of its own. BatteryMonitor already owns the charge-sense
//  question (GPIO35, see its banner), so this takes that verdict as a bool - one
//  detector, one place, one threshold to tune.
//
//  Why RTC memory: with the sleep_between setting on, the chip DEEP SLEEPS
//  between reports and app_main() runs again from the top on every wake. An
//  ordinary static would be reinitialised each cycle, the previous charger state
//  would be lost, and the edge could never be seen at all. RTC slow memory
//  survives deep sleep (and CPU resets), so the comparison still works - the same
//  trick, and the same magic-word guard, BootJournal uses for its boot history.
//
//  A state that did NOT survive - a genuine power cut, or the very first boot of
//  a device - is deliberately reported as "unknown" and yields no edge. A tracker
//  switched on already unplugged has not just been unplugged, and must not
//  behave as though it had.
// =============================================================================

class ChargerWatcher {
 public:
  // Adopts whatever state survived in RTC memory. Cheap: no I/O, no allocation.
  ChargerWatcher();

  // Record this cycle's charger reading and report the disconnect edge. Returns
  // true ONLY on a present -> absent transition, so it is false on the first
  // call after a power-on and false on every steady-state cycle.
  bool update(bool chargerPresent);

  // What the last update() recorded. False before the first one, so pair it with
  // known() to tell "not connected" apart from "not asked yet".
  bool present() const { return present_; }

  // False until the first update() on a device whose RTC memory was cleared.
  bool known() const { return known_; }

 private:
  bool known_;
  bool present_;
};
