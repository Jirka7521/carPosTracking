#pragma once

// =============================================================================
//  BootJournal  -  One line per boot, on the card and on the serial console.
// -----------------------------------------------------------------------------
//  Responsibility (single!): answer the question "why did this device restart?"
//  after the fact, when nobody was watching the serial port.
//
//  Nothing else in the firmware records it. DeepSleepController::wakeCauseName()
//  reports the deep-sleep *wake* cause, which cannot tell a brownout from a
//  panic from a clean power-on - so a tracker found dark leaves no trace of
//  whether it crashed, reboot-looped, or simply lost its supply. This class adds
//  that trace and prints the recent history at start-up, so plugging in USB
//  shows "the last six boots were BROWNOUT" straight away.
//
//  The discriminator is RTC memory. RTC_DATA_ATTR variables survive deep sleep
//  *and* CPU resets (panic, watchdog, software reset); they are lost only when
//  the rail actually drops. So "was the magic word still there?" answers *did
//  this device lose power, or did it reboot?* directly - which is precisely the
//  distinction that was missing. A cleared domain is reported as
//  "(RTC CLEARED)" and restarts the boot counter.
//
//  Deliberately NOT this class's job: the battery voltage at the moment of
//  death. That already arrives through the encrypted position backlog, which
//  survives a flat pack on the card. What only a boot log can show is the reset
//  reason, and that is what the line format is built around - the `bat=` field
//  is a convenience carried in RTC memory, and honestly reads "?" after the very
//  power loss it would be most interesting for.
//
//  Optional, like every other SD-backed subsystem: with no card it still prints
//  this boot's line, it just cannot persist it or show any history.
// =============================================================================

#include <cstddef>
#include <cstdint>

#include "sdcard/SdCard.h"

class BootJournal {
 public:
  // Borrows `card` and `filePath` (both must outlive this object). `maxLines`
  // caps the file: once reached, the oldest lines are dropped to make room
  // (0 means "no cap"). `printLines` is how many previous boots begin() shows
  // on the console.
  BootJournal(SdCard& card, const char* filePath, std::size_t maxLines,
              std::size_t printLines);

  // Bump the boot counter, compose this boot's line, print the recent history
  // plus that line to the serial console, then append it to the card and trim to
  // the cap. Call once, as early as possible after the card is mounted - the
  // failures most worth recording are the ones that end the run seconds later.
  //
  // Returns true when the line reached the card; false means console only (no
  // card, or an IO error), which is not fatal and never stops the caller.
  bool begin();

  // Checkpoint the running device in RTC memory, so the NEXT boot's line can
  // report where this run got to. RAM only - no card write, no flash wear, no
  // measurable power - so it is safe to call every reporting cycle.
  //
  // noteBattery() also stamps the uptime, so the ordinary path needs one call;
  // noteUptime() covers the cycles that have no usable voltage (charging, or a
  // failed read), where stamping a bogus 0 mV would be worse than saying nothing.
  void noteBattery(uint16_t millivolts);
  void noteUptime();

  // Human-readable esp_reset_reason() - "POWERON", "BROWNOUT", "PANIC",
  // "TASK_WDT", "DEEPSLEEP", ... Static because it reads chip state, not ours.
  static const char* resetCauseName();

 private:
  // Stream the file and print its last `n` lines. Only `n` lines are ever
  // resident: the log is capped small, but the streaming primitive is already
  // there and a ring buffer costs nothing.
  void printRecent(std::size_t n) const;

  // Drop the oldest lines once the file exceeds the cap. Same rule as FixQueue.
  bool trimToCap() const;

  SdCard&     card_;
  const char* filePath_;
  std::size_t maxLines_;
  std::size_t printLines_;
};
