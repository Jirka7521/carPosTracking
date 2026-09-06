#pragma once

// =============================================================================
//  ScheduleEvaluator  -  Which profile a set of windows puts in force, and when
//                        that next changes.
// -----------------------------------------------------------------------------
//  Responsibility (single!): the arithmetic, and nothing else. No clock (the
//  instant is a parameter), no storage, no MQTT, no logging of consequence.
//  That is what makes a window wrapping past midnight on the Sunday of a week
//  that also wraps something you can reason about at a desk instead of something
//  discovered in a car park at 02:00.
//
//  *** PARITY WARNING ***
//  This is a port of API/CarPosAPI/Services/Scheduling/ScheduleEvaluator.cs and
//  must produce identical answers for identical inputs. The server evaluates the
//  same rules to CHECK what this device reports, so a disagreement here does not
//  show up as a wrong answer - it shows up as the server correcting a device
//  that was, by its own lights, right. Any change to the semantics below has to
//  be made on both sides in the same commit. The four cases most worth testing
//  together are a window wrapping midnight, one wrapping the Saturday/Sunday
//  week edge, two adjacent windows meeting exactly, and a priority tie.
//
//  The semantics, stated once:
//    * Everything is minute-of-week, UTC. Minute 0 is Sunday 00:00, matching
//      .NET's DayOfWeek numbering and CivilTime::weekdayFromEpoch.
//    * A window is the half-open range [start, start + duration) modulo the
//      week. Half-open is what lets two windows meet at 06:00 with neither a
//      one-minute gap nor a one-minute overlap.
//    * Lower priority wins an overlap; a tie goes to the lower ordinal, which
//      the server has already computed from rule age.
//    * The active profile can only change where some window opens or closes, so
//      the next change is found by walking that small sorted set of boundaries
//      rather than by scanning the 10 080 minutes of the week.
//
//  Stateless, so both methods are static: there is nothing to construct.
// =============================================================================

#include <cstdint>

#include "settings/ScheduleBundle.h"

class ScheduleEvaluator {
 public:
  // Minutes in a day and in a week. Mirrors ScheduleRules.MinutesPerDay/Week.
  static constexpr int kMinutesPerDay  = 1440;
  static constexpr int kMinutesPerWeek = kMinutesPerDay * 7;

  // What the schedule resolves to at one instant.
  struct Result {
    // The profile slot in force, or ScheduleBundle::kNoSlot when no window
    // covers the instant and there is no usable fallback.
    int slot = ScheduleBundle::kNoSlot;

    // When `slot` next becomes something else, as Unix epoch seconds. Only
    // meaningful when `hasNextChange` is true - a schedule whose rules resolve
    // the same way all week genuinely never changes, and inventing a boundary
    // for it would make the device wake for nothing once a week for ever.
    int64_t nextChangeEpoch = 0;
    bool    hasNextChange   = false;
  };

  // Resolve `bundle` at `epochSeconds`. Returns false when the bundle cannot be
  // evaluated at all - not valid, not enabled, or it resolves to no profile - in
  // which case `out` is left alone and the caller should fall back to the
  // retained configuration document.
  static bool evaluate(const ScheduleBundle& bundle, int64_t epochSeconds,
                       Result& out);

 private:
  // Minute of the UTC week for an instant: 0 = Sunday 00:00, 10079 = Saturday
  // 23:59.
  static int minuteOfWeek(int64_t epochSeconds);

  // The rule whose window covers `minute` and beats every other that does, or
  // nullptr when none covers it.
  static const ScheduleBundle::Rule* winnerAt(const ScheduleBundle& bundle,
                                              int                   minute);

  // Whether a rule's window contains a minute of the week.
  static bool covers(const ScheduleBundle::Rule& rule, int minute);

  // Whether `candidate` outranks `incumbent`.
  static bool beats(const ScheduleBundle::Rule& candidate,
                    const ScheduleBundle::Rule& incumbent);

  // The profile a winning rule selects, or the bundle's fallback when none won.
  static int profileOf(const ScheduleBundle&       bundle,
                       const ScheduleBundle::Rule* rule);

  // A modulo that returns a non-negative result, which C's % does not for a
  // negative left operand - and every wrap here has one.
  static int modulo(int value, int modulus);
};
