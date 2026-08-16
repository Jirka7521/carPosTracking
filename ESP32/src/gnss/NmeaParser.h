#pragma once

// =============================================================================
//  NmeaParser  -  Per-constellation satellite stats from NMEA "GSV" lines.
// -----------------------------------------------------------------------------
//  AT+CGNSINF only tells us the *total* satellites used. To answer "how many
//  satellites of each technology can we see, and how strong are they" (a debug
//  feature) we briefly let the modem stream raw NMEA (AT+CGNSTST=1) and inspect
//  the "GSV" sentences.
//
//  A GSV sentence carries a header plus up to four satellites, each described
//  by four fields - PRN, elevation, azimuth and SNR:
//
//      $GPGSV,3,1,11,05,42,171,38,12,18,304,00,...*7A
//       |     | | |  \__________/ \__________/
//       |     | | |   sat 1        sat 2 (SNR 00 = not tracked)
//       |     | | satellites in view
//       |     | message number within the burst (1 = first)
//       |     number of messages in this burst
//       talker: GP = GPS, GL = GLONASS, GB/BD = BeiDou, GA = Galileo
//
//  Both numbers we extract matter, and they mean different things - see the
//  note on GnssSatelliteCounts. "In view" comes from the almanac and is
//  reported even with no antenna attached; the SNR fields are what prove signal
//  is actually arriving.
//
//  Because a burst is split across several messages and the receiver repeats
//  bursts continuously, the parser accumulates the tracked count per burst
//  (restarting whenever message 1 arrives) so satellites are never counted
//  twice.
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
  // The four constellations we recognise, in a fixed order so the per-burst
  // working state below can be indexed by it.
  enum Constellation { kGps = 0, kGlonass, kBeidou, kGalileo, kConstellationCount };

  // Map a GSV talker prefix to a Constellation, or -1 for a talker we ignore
  // (e.g. the combined "GN" sentences, which would double-count).
  static int constellationIndex(char t0, char t1);

  // Store a finished burst's numbers into the matching `counts_` members.
  void storeCounts(int constellation, uint8_t inView, uint8_t tracked);

  GnssSatelliteCounts counts_{};

  // Tracked satellites counted so far in the burst currently being received,
  // one slot per constellation. Reset when that constellation's message 1
  // arrives, which is what makes repeated bursts idempotent.
  uint8_t burstTracked_[kConstellationCount] = {};
};
