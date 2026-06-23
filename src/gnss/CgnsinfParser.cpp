#include "CgnsinfParser.h"

#include <cstdlib>
#include <cstring>

namespace {

// Split `line` (modified in place) on commas. Stores a pointer to each field
// in `fields`. Empty fields (two commas in a row) become empty strings, which
// is important because CGNSINF leaves many fields blank when there is no fix.
// Returns the number of fields found.
int splitCsv(char* line, char** fields, int maxFields) {
  int count = 0;
  fields[count++] = line;
  for (char* c = line; *c != '\0' && count < maxFields; ++c) {
    if (*c == ',') {
      *c = '\0';
      fields[count++] = c + 1;
    }
  }
  return count;
}

// true if the field exists and is not empty.
bool present(char** fields, int count, int index) {
  return index < count && fields[index][0] != '\0';
}

// Decode "yyyyMMddhhmmss.sss" into a GnssTime.
void parseTime(const char* text, GnssTime& time) {
  // Need at least "yyyyMMddhhmmss" (14 chars) to be meaningful.
  if (strlen(text) < 14) {
    return;
  }
  char buf[5];

  auto take = [&](int start, int len) -> int {
    memcpy(buf, text + start, len);
    buf[len] = '\0';
    return atoi(buf);
  };

  time.year        = static_cast<uint16_t>(take(0, 4));
  time.month       = static_cast<uint8_t>(take(4, 2));
  time.day         = static_cast<uint8_t>(take(6, 2));
  time.hour        = static_cast<uint8_t>(take(8, 2));
  time.minute      = static_cast<uint8_t>(take(10, 2));
  time.second      = static_cast<uint8_t>(take(12, 2));
  time.millisecond = (strlen(text) >= 18) ? static_cast<uint16_t>(take(15, 3)) : 0;
  time.valid       = (time.year > 0);
}

}  // namespace

bool CgnsinfParser::parse(const char* payload, GnssFix& out) {
  // Work on a private, mutable copy because splitCsv writes '\0' separators.
  char buf[200];
  strncpy(buf, payload, sizeof(buf) - 1);
  buf[sizeof(buf) - 1] = '\0';

  // CGNSINF defines up to 21 fields; size the array generously.
  char* f[24];
  int   n = splitCsv(buf, f, 24);

  // We need at least the run-status and fix-status fields to call it a reading.
  if (n < 2) {
    return false;
  }

  out = GnssFix{};  // Start from clean defaults.

  // --- Field layout (1-based in the datasheet -> 0-based here) -------------
  // 1  run status      | 2  fix status   | 3  UTC time
  // 4  latitude        | 5  longitude    | 6  altitude (m)
  // 7  speed (km/h)    | 8  course       | 9  fix mode
  // 11 HDOP            | 12 PDOP         | 13 VDOP
  // 15 GPS sats in view| 16 GNSS sats used
  out.engineRunning = present(f, n, 0) && atoi(f[0]) != 0;
  out.fixStatus     = (present(f, n, 1) && atoi(f[1]) != 0) ? GnssFixStatus::Fix
                                                            : GnssFixStatus::NoFix;

  if (present(f, n, 2)) {
    parseTime(f[2], out.time);
  }

  const bool haveFix = out.hasFix();

  if (haveFix && present(f, n, 3) && present(f, n, 4)) {
    out.position.latitudeDeg    = atof(f[3]);
    out.position.longitudeDeg   = atof(f[4]);
    out.position.altitudeMeters = present(f, n, 5) ? atof(f[5]) : 0.0;
    out.position.valid          = true;
  }

  if (present(f, n, 6)) out.speedKmph = atof(f[6]);
  if (present(f, n, 7)) out.courseDeg = atof(f[7]);

  if (present(f, n, 10)) out.hdop = atof(f[10]);
  if (present(f, n, 11)) out.pdop = atof(f[11]);
  if (present(f, n, 12)) out.vdop = atof(f[12]);

  if (present(f, n, 14)) {
    out.satellitesInView = static_cast<uint8_t>(atoi(f[14]));
  }
  if (present(f, n, 15)) {
    out.satellitesUsed = static_cast<uint8_t>(atoi(f[15]));
  }

  return true;
}
