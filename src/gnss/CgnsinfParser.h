#pragma once

// =============================================================================
//  CgnsinfParser  -  Decodes the SIM7000G "AT+CGNSINF" response line.
// -----------------------------------------------------------------------------
//  The modem answers a position request with one comma-separated line, e.g.
//
//    +CGNSINF: 1,1,20240623120000.000,48.123456,17.123456,210.5,0.42,0.0,
//              1,,1.1,1.4,0.9,,12,9,4,,30,,
//
//  This class turns that text into a tidy GnssFix struct. It is pure parsing
//  logic with no hardware dependency, so it is trivial to reason about.
// =============================================================================

#include "gnss/GnssData.h"

class CgnsinfParser {
 public:
  // Parse a CGNSINF payload (the text AFTER "+CGNSINF:") into `out`.
  // Returns true if at least the mandatory leading fields were present.
  static bool parse(const char* payload, GnssFix& out);
};
