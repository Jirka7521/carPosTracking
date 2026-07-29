#pragma once

// =============================================================================
//  BatteryData.h  -  Plain data type describing one battery reading.
// -----------------------------------------------------------------------------
//  A dependency-free value struct produced by BatteryMonitor and consumed by the
//  telemetry publisher.
// =============================================================================

#include <cstdint>

// One battery reading for the 18650 pack.
//
//   percent     state of charge, 0-100. The value 0 is a SENTINEL meaning "the
//               charger is connected" (see BatteryMonitor); a genuinely low but
//               discharging pack is clamped to 1 so 0 is never ambiguous.
//   millivolts  the raw pack voltage the percent was derived from (AT+CBC), or 0
//               on the charging path where no voltage was read. Carried for the
//               serial debug print only - it is deliberately NOT published, so
//               the telemetry/DB contract stays "percent + temperature" (see
//               TelemetryPublisher). Handy for calibrating voltageToPercent().
//   charging    true when the charger was detected (percent is then 0).
//   valid       true only when the reading actually succeeded; false leaves the
//               fields at their defaults and marks the reading absent so the
//               publisher can omit it rather than send a misleading 0.
struct BatteryStatus {
  uint8_t  percent    = 0;
  uint16_t millivolts = 0;
  bool     charging   = false;
  bool     valid      = false;
};
