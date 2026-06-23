#pragma once

// =============================================================================
//  NmeaParser  -  Counts satellites per constellation from NMEA "GSV" lines.
// -----------------------------------------------------------------------------
//  AT+CGNSINF only tells us the *total* satellites used. To answer "how many
//  satellites of each technology are in view" (a debug feature) we briefly let
//  the modem stream raw NMEA (AT+CGNSTST=1) and inspect the "GSV" sentences.
//
//  A GSV sentence looks like:   $GPGSV,3,1,11,...*7A
//  where the talker prefix identifies the constellation and the 3rd numeric
//  field is the number of satellites that system currently sees:
//      GP = GPS      GL = GLONASS      GB/BD = BeiDou      GA = Galileo
//
//  This class is a small stateful accumulator: feed it lines, then read the
//  totals. It does no I/O itself, which keeps it easy to test and reuse.
// =============================================================================

#include "gnss/GnssData.h"

class NmeaParser {
 public:
  // Forget all previously fed data and start a fresh scan.
  void reset();

  // Feed one received line (with or without trailing CR/LF). Non-GSV lines are
  // ignored. Returns true if the line was a GSV sentence we understood.
  bool feedLine(const char* line);

  // The accumulated per-constellation "in view" counts. `valid` is true once
  // at least one GSV sentence has been parsed.
  const GnssSatelliteCounts& counts() const { return counts_; }

 private:
  GnssSatelliteCounts counts_{};
};
