#pragma once

// =============================================================================
//  AmbientData.h  -  Plain data types describing one ambient climate reading.
// -----------------------------------------------------------------------------
//  A dependency-free value struct, shared by the Dht22 driver, the window
//  sampler that aggregates it and whoever serialises the telemetry.
//
//  "Ambient" rather than "temperature" on purpose: the firmware already reports
//  a temperature - the SIM7000's own die temperature from AT+CPMUTEMP, which is
//  what explains a hot-car cut-off. This is a different quantity measured by a
//  different part, so it carries a different name all the way to the database.
// =============================================================================

// One instantaneous reading from the DHT22.
//
//   temperatureC  ambient air temperature in degrees Celsius (-40..80 on this
//                 part). Signed: the DHT22 encodes negatives in the top bit of
//                 its temperature word, not as two's complement.
//   humidityPct   relative humidity, 0..100 %.
//   valid         true only when a checksummed frame was actually decoded;
//                 false leaves both fields at 0 and marks the reading absent so
//                 the publisher omits them rather than sending bogus zeros.
struct AmbientSample {
  float temperatureC = 0.0f;
  float humidityPct  = 0.0f;
  bool  valid        = false;
};
