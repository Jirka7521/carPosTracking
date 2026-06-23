#pragma once

// =============================================================================
//  GnssData.h  -  Plain data types describing a GNSS reading.
// -----------------------------------------------------------------------------
//  These are simple "value" structs with no behaviour, shared by the parsers
//  and the high-level GnssModule. Keeping the data definitions in one small,
//  dependency-free header makes the whole subsystem easy to follow.
// =============================================================================

#include <cstdint>

// Whether the modem currently has a valid position fix.
enum class GnssFixStatus : uint8_t {
  NoFix = 0,  // Searching - position is not yet trustworthy.
  Fix   = 1,  // Locked - position/speed/time are valid.
};

// UTC date and time reported alongside the fix.
struct GnssTime {
  uint16_t year         = 0;
  uint8_t  month        = 0;
  uint8_t  day          = 0;
  uint8_t  hour         = 0;
  uint8_t  minute       = 0;
  uint8_t  second       = 0;
  uint16_t millisecond  = 0;
  bool     valid        = false;  // true once a real timestamp was decoded
};

// Geographic position.
struct GnssPosition {
  double latitudeDeg    = 0.0;   // +north / -south, decimal degrees
  double longitudeDeg   = 0.0;   // +east  / -west,  decimal degrees
  double altitudeMeters = 0.0;   // metres above mean sea level
  bool   valid          = false; // true only when the modem reports a fix
};

// Per-constellation count of satellites currently *in view*.
// Populated from NMEA "GSV" sentences (see NmeaParser). Each member is the
// number of satellites the modem can see for that system - more systems and
// more satellites generally mean a faster, more accurate fix.
struct GnssSatelliteCounts {
  uint8_t gps     = 0;   // USA
  uint8_t glonass = 0;   // Russia
  uint8_t beidou  = 0;   // China
  uint8_t galileo = 0;   // Europe
  bool    valid   = false;

  uint8_t total() const { return gps + glonass + beidou + galileo; }
};

// The full result of a single GNSS read.
struct GnssFix {
  bool          engineRunning = false;            // GNSS power state per modem
  GnssFixStatus fixStatus     = GnssFixStatus::NoFix;

  GnssTime      time;
  GnssPosition  position;

  double speedKmph     = 0.0;   // ground speed
  double courseDeg     = 0.0;   // direction of travel (0 = north, 90 = east)

  double hdop          = 0.0;   // horizontal dilution of precision
  double pdop          = 0.0;   // positional dilution of precision
  double vdop          = 0.0;   // vertical dilution of precision

  // Satellite usage as reported directly by AT+CGNSINF.
  uint8_t satellitesInView = 0; // GPS satellites visible
  uint8_t satellitesUsed   = 0; // total GNSS satellites used in the solution

  // Convenience helper: do we have a usable position?
  bool hasFix() const { return fixStatus == GnssFixStatus::Fix; }
};
