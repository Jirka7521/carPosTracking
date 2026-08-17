#pragma once

// =============================================================================
//  UtcClock  -  Convert a GNSS UTC date/time to seconds and back.
// -----------------------------------------------------------------------------
//  Responsibility (single!): move between GnssTime (a broken-down civil UTC
//  date) and a count of seconds since the Unix epoch, so callers can do
//  arithmetic on a timestamp - "this reading is 37 seconds after that fix" -
//  without hand-rolling calendar maths.
//
//  Its one user today is AccelDebugStream, which repeats the last known fix at
//  1 Hz and has to move the clock forward on each copy. It cannot leave the
//  timestamp alone: the API dedupes stored positions on (device, fix time), so
//  identical timestamps would mean all but one sample of every interval is
//  silently discarded.
//
//  Hand-rolled (Howard Hinnant's civil-date algorithm) rather than using
//  timegm(): that function's availability varies across newlib configurations,
//  and mktime() would drag in the local timezone, which on a device with no zone
//  data is a trap. This is branch-free, exact for every date we will ever see,
//  and has no libc dependency at all.
//
//  UTC only. There is no leap-second table and none is wanted: GNSS UTC is
//  already leap-corrected by the receiver, and the API stores what it is given.
// =============================================================================

#include <cstdint>

#include "gnss/GnssData.h"

class UtcClock {
 public:
  // Seconds since 1970-01-01T00:00:00Z for `time`. The sub-second field is
  // ignored - every consumer of this works in whole seconds.
  static int64_t toEpoch(const GnssTime& time);

  // Inverse of toEpoch(). Fills the date/time fields of `timeOut` and marks it
  // valid; `millisecond` is zeroed, since an epoch second carries no fraction.
  static void fromEpoch(int64_t epochSeconds, GnssTime& timeOut);
};
