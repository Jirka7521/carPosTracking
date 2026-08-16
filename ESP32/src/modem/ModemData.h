#pragma once

// =============================================================================
//  ModemData.h  -  Plain data type describing the modem's own health reading.
// -----------------------------------------------------------------------------
//  A dependency-free value struct produced from Sim7000Modem's AT+CPMUTEMP and
//  consumed by the telemetry publisher, mirroring power/BatteryData.h and
//  sensors/AccelData.h. Kept separate from the battery reading on purpose: the
//  temperature is the *modem's* die sensor, not a property of the pack.
// =============================================================================

// One modem health reading.
//
//   temperatureC  the SIM7000's internal die temperature in degrees Celsius - a
//                 proxy for how hot the device is running, since the pack has no
//                 sensor of its own. Published as "temp_c".
//   valid         true only when AT+CPMUTEMP was read and parsed; false leaves
//                 temperatureC at its default and marks the reading absent so the
//                 publisher omits the field rather than sending a misleading 0.
struct ModemHealth {
  float temperatureC = 0.0f;
  bool  valid        = false;
};
