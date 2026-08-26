#pragma once

// =============================================================================
//  BatteryMonitor  -  State of charge + charging detection for the 18650 pack.
// -----------------------------------------------------------------------------
//  Responsibility (single!): produce one BatteryStatus on request. It combines
//  two hardware sources, mirroring how GnssModule sits on top of Sim7000Modem:
//
//    * Charging  -> an ADC read of the charge-sense pin (GPIO35). The board's
//                   charger pulls that pin to ~0 while charging, so a raw ADC
//                   reading below the threshold means "on charge" and the pack
//                   percent is reported as the sentinel 0.
//    * Percent   -> when NOT charging, the modem's AT+CBC gives the pack voltage,
//                   which is mapped onto a 1-100 % window. (AT+CBC is used rather
//                   than a second ADC divider so no extra analog wiring is
//                   needed; the modem is already powered when we read.)
//
//  It borrows the AdcSampler and the Sim7000Modem (owning neither) exactly like
//  the other modem users. The ADC unit deliberately lives in AdcSampler rather
//  than here: the IDF allows only one owner per unit, and BatteryMethods needs
//  the same unit for its own pins - see AdcSampler.h.
// =============================================================================

#include <cstdint>

#include "modem/Sim7000Modem.h"
#include "power/AdcSampler.h"
#include "power/BatteryData.h"

class BatteryMonitor {
 public:
  // Borrows `adc` and `modem` (both must outlive this object); stores the tuning
  // knobs.
  //   chargeSensePin   : GPIO wired to the charger's sense line (GPIO35)
  //   chargeAdcThreshold : raw ADC counts below which we treat as "charging"
  //   emptyMv / fullMv : voltage window mapped onto 1-100 % (single Li-ion cell)
  BatteryMonitor(AdcSampler& adc, Sim7000Modem& modem, int chargeSensePin,
                 int chargeAdcThreshold, uint32_t emptyMv, uint32_t fullMv);

  // Claim the charge-sense pin on the shared ADC. Returns true when ready.
  // Optional subsystem: on failure it logs and returns false, and read() then
  // reports an invalid status.
  bool begin();

  // Take one reading (see the class banner for the two-source logic). On success
  // fills `out` (valid = true) and returns true.
  bool read(BatteryStatus& out);

 private:
  // Parse the pack voltage in millivolts out of an AT+CBC reply. Tolerates both
  // the "+CBC: <volts>" (e.g. 3.987) and "+CBC: <bcs>,<bcl>,<mV>" reply shapes
  // seen across SIMCom firmware revisions. Returns false if no number is found.
  static bool parseCbcMillivolts(const char* response, uint32_t& mvOut);

  // Map a pack voltage (mV) onto 1-100 %, clamped so it is never 0 (0 is the
  // charging sentinel and must stay unambiguous).
  uint8_t voltageToPercent(uint32_t mv) const;

  AdcSampler&   adc_;
  Sim7000Modem& modem_;
  int           chargeSensePin_;
  int           chargeAdcThreshold_;
  uint32_t      emptyMv_;
  uint32_t      fullMv_;

  bool ready_ = false;
};
