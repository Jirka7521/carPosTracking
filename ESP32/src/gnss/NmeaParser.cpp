#include "NmeaParser.h"

#include <cstdlib>
#include <cstring>

void NmeaParser::reset() {
  counts_ = GnssSatelliteCounts{};
  memset(burstTracked_, 0, sizeof(burstTracked_));
}

int NmeaParser::constellationIndex(char t0, char t1) {
  if (t0 == 'G' && t1 == 'P') {
    return kGps;
  }
  if (t0 == 'G' && t1 == 'L') {
    return kGlonass;
  }
  if ((t0 == 'G' && t1 == 'B') || (t0 == 'B' && t1 == 'D')) {
    return kBeidou;
  }
  if (t0 == 'G' && t1 == 'A') {
    return kGalileo;
  }
  return -1;  // Some other talker (e.g. the combined "GN") - ignore.
}

void NmeaParser::storeCounts(int constellation, uint8_t inView,
                             uint8_t tracked) {
  switch (constellation) {
    case kGps:
      counts_.gps        = inView;
      counts_.gpsTracked = tracked;
      break;
    case kGlonass:
      counts_.glonass        = inView;
      counts_.glonassTracked = tracked;
      break;
    case kBeidou:
      counts_.beidou        = inView;
      counts_.beidouTracked = tracked;
      break;
    case kGalileo:
      counts_.galileo        = inView;
      counts_.galileoTracked = tracked;
      break;
    default:
      break;
  }
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
  const int constellation = constellationIndex(line[1], line[2]);
  if (constellation < 0) {
    return false;
  }

  // Walk the comma-separated fields once, picking out the three things we
  // need. Field numbering (counting commas from the start of the sentence):
  //     1 = messages in burst   2 = this message's number   3 = sats in view
  //     4,5,6,7 = PRN,elevation,azimuth,SNR of the first satellite
  //     8,9,10,11 = ... the second, and so on in groups of four.
  // So every field where (index - 3) is a positive multiple of 4 is an SNR.
  int messageNumber = 0;
  int satsInView    = 0;
  int trackedHere   = 0;  // satellites with real signal in *this* message
  int fieldIndex    = 0;

  for (const char* p = line; *p != '\0'; ++p) {
    if (*p != ',') {
      continue;
    }
    ++fieldIndex;
    const char* value = p + 1;  // The field's text starts after the comma.

    if (fieldIndex == 2) {
      messageNumber = atoi(value);
    } else if (fieldIndex == 3) {
      satsInView = atoi(value);
    } else if (fieldIndex > 3 && ((fieldIndex - 3) % 4) == 0) {
      // An SNR slot. atoi() stops cleanly at the next comma or at the '*'
      // before the checksum, and yields 0 for the empty field the receiver
      // sends for a satellite it knows about but is not tracking.
      const int snr = atoi(value);
      if (snr > 0) {
        ++trackedHere;
        if (snr > counts_.maxSnr) {
          counts_.maxSnr = static_cast<uint8_t>(snr);
        }
      }
    }
  }

  if (fieldIndex < 3) {
    return false;  // Malformed - not enough fields.
  }

  // Message 1 starts a fresh burst for this constellation. Without this the
  // count would grow without bound, since the receiver repeats bursts for as
  // long as we keep listening.
  if (messageNumber <= 1) {
    burstTracked_[constellation] = 0;
  }
  burstTracked_[constellation] =
      static_cast<uint8_t>(burstTracked_[constellation] + trackedHere);

  // The in-view figure is identical in every message of a burst, so a plain
  // assignment is correct; the tracked figure is the burst total so far.
  storeCounts(constellation, static_cast<uint8_t>(satsInView),
              burstTracked_[constellation]);

  counts_.valid = true;
  return true;
}
