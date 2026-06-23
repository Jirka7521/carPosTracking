#pragma once

// =============================================================================
//  GnssModule  -  The friendly, high-level GNSS API you actually use.
// -----------------------------------------------------------------------------
//  This is the class application code talks to. It hides the AT commands and
//  delegates the low-level work to its collaborators:
//      Sim7000Modem    -> power + AT transport
//      CgnsinfParser   -> turns the position reply into a GnssFix
//      NmeaParser      -> counts satellites per constellation (debug)
//
//  Typical lifecycle:
//      GnssModule gnss(modem);
//      gnss.begin();              // power up + configure constellations
//      GnssFix fix;
//      if (gnss.readFix(fix) && fix.hasFix()) { ...use position... }
//      gnss.powerOffModule();     // sleep to save battery
// =============================================================================

#include "gnss/GnssData.h"
#include "gnss/NmeaParser.h"
#include "modem/Sim7000Modem.h"

class GnssModule {
 public:
  // Borrows (does not own) a Sim7000Modem.
  explicit GnssModule(Sim7000Modem& modem);

  // One-shot setup: power the modem on, switch the GNSS engine on, and select
  // the constellations from Config.h. Returns true when ready to read.
  bool begin();

  // --- Power management ----------------------------------------------------
  // Power the WHOLE modem on/off. Off => lowest power draw (use between reads
  // if you only need an occasional position and want maximum battery life).
  bool powerOnModule();
  bool powerOffModule();

  // Power just the GNSS engine on/off, leaving the modem itself running.
  // Cheaper to toggle than the whole modem, but draws more than a full off.
  bool enableGnss();
  bool disableGnss();

  // Choose which satellite systems to use. GPS is forced on (the modem
  // requires it). Pass-through to AT+CGNSMOD.
  bool setConstellations(bool gps, bool glonass, bool beidou, bool galileo);

  // --- Reading -------------------------------------------------------------
  // Read the current position/speed/time via AT+CGNSINF. Returns true if a
  // reply was decoded (check fix.hasFix() to know if it is a real position).
  // When debugging is enabled in Config.h this also prints a full report.
  bool readFix(GnssFix& fix);

  // Count satellites in view per constellation by listening to NMEA output for
  // `scanMs` milliseconds. Heavier than readFix(); mainly used for debugging.
  bool readSatelliteCounts(GnssSatelliteCounts& counts, uint32_t scanMs);

 private:
  // Pretty-prints a full decoded fix to the serial console (debug builds).
  void debugPrintFix(const GnssFix& fix);
  // Pretty-prints per-constellation satellite counts (debug builds).
  void debugPrintSatellites(const GnssSatelliteCounts& counts);

  Sim7000Modem& modem_;
  NmeaParser    nmea_;
};
