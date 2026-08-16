#pragma once

// =============================================================================
//  AccelData.h  -  Plain data type describing one accelerometer reading.
// -----------------------------------------------------------------------------
//  A dependency-free value struct, shared by the Adxl345 driver and whoever
//  serialises the telemetry. The ADXL345 is a 3-axis accelerometer only (no
//  gyroscope), so a full reading is just the three axes plus a validity flag.
// =============================================================================

// One instantaneous acceleration sample, in units of standard gravity (g).
//
//   xG / yG / zG  signed acceleration per axis. At rest on a level surface one
//                 axis reads ~+1 g (gravity) and the other two ~0 g.
//   valid         true only when the sample was actually read from the sensor;
//                 false leaves the axes at 0 and marks the reading absent so the
//                 publisher can omit the fields rather than send bogus zeros.
struct AccelSample {
  double xG    = 0.0;
  double yG    = 0.0;
  double zG    = 0.0;
  bool   valid = false;
};
