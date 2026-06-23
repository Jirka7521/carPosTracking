#include "NmeaParser.h"

#include <cstdlib>
#include <cstring>

void NmeaParser::reset() {
  counts_ = GnssSatelliteCounts{};
}

bool NmeaParser::feedLine(const char* line) {
  // A valid sentence starts with '$' and is at least "$ttGSV," long.
  if (line == nullptr || line[0] != '$' || strlen(line) < 7) {
    return false;
  }

  // Characters 3..5 are the sentence type; we only care about "GSV".
  if (strncmp(line + 3, "GSV", 3) != 0) {
    return false;
  }

  // The two-letter talker ID (chars 1..2) tells us the constellation.
  const char t0 = line[1];
  const char t1 = line[2];

  // The satellite-in-view count is the 3rd comma-separated field. Walk to it.
  //   $GPGSV , numMsgs , msgNum , numSatsInView , ...
  const char* p     = line;
  int         commas = 0;
  while (*p != '\0' && commas < 3) {
    if (*p == ',') {
      ++commas;
    }
    ++p;
  }
  if (commas < 3) {
    return false;  // Malformed - not enough fields.
  }

  const int satsInView = atoi(p);  // atoi stops at the next comma/non-digit.

  // Store the count for the matching constellation. The value is identical in
  // every GSV message of a burst, so a plain assignment is correct.
  if (t0 == 'G' && t1 == 'P') {
    counts_.gps = static_cast<uint8_t>(satsInView);
  } else if (t0 == 'G' && t1 == 'L') {
    counts_.glonass = static_cast<uint8_t>(satsInView);
  } else if ((t0 == 'G' && t1 == 'B') || (t0 == 'B' && t1 == 'D')) {
    counts_.beidou = static_cast<uint8_t>(satsInView);
  } else if (t0 == 'G' && t1 == 'A') {
    counts_.galileo = static_cast<uint8_t>(satsInView);
  } else {
    return false;  // Some other talker (e.g. GN/GQ) - ignore.
  }

  counts_.valid = true;
  return true;
}
