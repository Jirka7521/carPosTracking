#pragma once

// =============================================================================
//  DeviceClock  -  A UTC wall clock, seeded from GNSS, that survives deep sleep.
// -----------------------------------------------------------------------------
//  Responsibility (single!): answer "what time is it, and can I believe that?".
//  Nothing else on the device could answer either half before this existed.
//
//  Why this is harder here than it sounds:
//    * There is no RTC crystal. sdkconfig selects CONFIG_RTC_CLK_SRC_INT_RC - the
//      internal 150 kHz RC oscillator - which is calibrated against the main
//      crystal before each sleep but is still temperature-dependent and drifts.
//    * There is no NTP. WiFi is optional and usually absent; the modem link is
//      an MQTT session, not a time source.
//    * esp_timer_get_time() restarts at zero on the deep-sleep reboot, so every
//      other deadline in this firmware is measured in "microseconds since this
//      wake" and cannot be turned into a date.
//  That leaves the GNSS receiver as the only wall clock on the board, and it
//  only speaks when it has a lock.
//
//  So the design is: seed from a fix, coast on the ESP32's own RTC timekeeping
//  between fixes, and STOP TRUSTING the result once the last seed is old enough
//  that drift could have moved it past a schedule boundary.
//
//  Coasting is free. ESP-IDF restores the system time base across deep sleep, so
//  settimeofday() once and gettimeofday() keeps working through any number of
//  sleep/wake cycles. What does NOT survive a power-on reset is the RTC slow
//  memory below, which is exactly the behaviour wanted: after a battery pull the
//  clock is honestly unknown rather than confidently wrong.
//
//  The trust window is the whole safety story. A device that stops seeing the
//  sky keeps reporting its position from the SD queue but stops evaluating its
//  schedule, falling back to the retained config document - and the server, which
//  can see how stale the reports are, takes over. Failing closed like that is
//  what makes an on-device schedule safe to ship on hardware with no crystal.
// =============================================================================

#include <cstdint>

#include "gnss/GnssData.h"

class DeviceClock {
 public:
  // How old a seed may be before isTrusted() gives up on it, in seconds.
  explicit DeviceClock(uint32_t trustWindowSeconds);

  // Restore what the RTC slow memory remembers. Call once, early, before
  // anything asks the time. Safe to call on every boot: a power-on reset finds
  // the magic word absent and starts unseeded.
  void begin();

  // Set the clock from a GNSS fix. Ignores an invalid time and one whose year is
  // implausible - a modem that has powered up but not yet locked happily reports
  // 1980, and seeding from that would be worse than having no clock at all.
  //
  // Returns true when the clock was actually set.
  bool seedFromGnss(const GnssTime& time);

  // Seconds since the Unix epoch, UTC. Returns false when the clock has never
  // been seeded, in which case `epochOut` is untouched.
  bool nowUtc(int64_t& epochOut) const;

  // Whether the clock is seeded AND the seed is recent enough to act on.
  //
  // Callers that only want to stamp a log line should use nowUtc(); this is for
  // the decisions where being an hour out would change what the device does.
  bool isTrusted() const;

  // Seconds since the last seed, or -1 when never seeded. Diagnostic only.
  int64_t secondsSinceSeed() const;

 private:
  // Reject anything before this. 2024-01-01T00:00:00Z, comfortably after this
  // project existed and comfortably before any fix it will ever see.
  static constexpr int64_t kMinPlausibleEpoch = 1704067200;

  uint32_t trustWindowSeconds_;
  bool     seeded_;  // mirrors the RTC-memory state, cached for cheap reads
};
