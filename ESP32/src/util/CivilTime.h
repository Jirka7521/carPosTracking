#pragma once

// =============================================================================
//  CivilTime  -  Convert between civil dates, Unix epoch seconds and ISO-8601.
// -----------------------------------------------------------------------------
//  Responsibility (single!): the calendar arithmetic this device needs, and
//  nothing else. No clock, no state, no IO - every method is a pure function of
//  its arguments, which is what lets the schedule evaluator and the retry queue
//  share it without sharing anything else.
//
//  Hand-rolled rather than using timegm(): that function's availability varies
//  across newlib configurations, and mktime() would drag in the local timezone,
//  which on a device with no zone data is a trap. Howard Hinnant's algorithms
//  below are branch-free, exact for every date we will ever see, and have no
//  libc dependency at all.
//
//  Everything here is UTC. There is no timezone parameter anywhere on purpose:
//  the GNSS receiver reports UTC, the API's schedules are defined in UTC, and
//  the dashboard is the only place a local time is ever shown.
//
//  This code was extracted verbatim from RetryQueue.cpp, which had it in an
//  anonymous namespace, when DeviceClock and ScheduleEvaluator needed the same
//  arithmetic. The behaviour is unchanged - only the address is.
// =============================================================================

#include <cstdint>
#include <string>

class CivilTime {
 public:
  // Seconds in each unit, named so the call sites below read as arithmetic
  // rather than as magic numbers.
  static constexpr int64_t kSecondsPerMinute = 60;
  static constexpr int64_t kSecondsPerHour   = 3600;
  static constexpr int64_t kSecondsPerDay    = 86400;

  // Days since 1970-01-01 for a civil date. `month` is 1-12, `day` is 1-31.
  static int64_t daysFromCivil(int64_t year, unsigned month, unsigned day);

  // Inverse of daysFromCivil.
  static void civilFromDays(int64_t days, int& yearOut, unsigned& monthOut,
                            unsigned& dayOut);

  // Parse "YYYY-MM-DDTHH:MM:SSZ" into seconds since the Unix epoch. Returns
  // false for anything that is not exactly that shape - the format is produced
  // by us and by the API, so a deviation is corruption, not a variant.
  static bool parseIso(const std::string& text, int64_t& epochOut);

  // Render seconds since the Unix epoch back to "YYYY-MM-DDTHH:MM:SSZ".
  static std::string formatIso(int64_t epoch);

  // Day of the week for an instant, as 0 = Sunday .. 6 = Saturday.
  //
  // Numbered to match .NET's DayOfWeek because that is what the API's schedule
  // rules are built on: bit 0 of a rule's day mask is Sunday, and the schedule
  // evaluator's minute-of-week has minute 0 at Sunday 00:00. A different
  // numbering here would silently rotate every schedule by some number of days.
  //
  // 1970-01-01 was a Thursday, hence the +4 before the modulo.
  static int weekdayFromEpoch(int64_t epoch);

 private:
  // A modulo that returns a non-negative result, which C's % does not for a
  // negative left operand - and both users of it here have one (a pre-epoch
  // timestamp, and the day-of-week wrap).
  static int64_t floorMod(int64_t value, int64_t modulus);
};
