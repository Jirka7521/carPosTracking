#pragma once

// =============================================================================
//  TelemetrySample.h  -  One full telemetry reading: position + battery + accel.
// -----------------------------------------------------------------------------
//  A small aggregate that bundles everything one report carries, so the GNSS,
//  battery, accelerometer and modem-health subsystems each stay independent and
//  GnssFix stays a pure GNSS type. TelemetryPublisher serialises this whole
//  struct; the main loop fills it in each cycle.
//
//  The battery/accel/modem members carry their own `valid` flags: when a sensor
//  is disabled or a read failed, the publisher simply omits those fields rather
//  than emitting misleading zeros.
// =============================================================================

#include "gnss/GnssData.h"
#include "modem/ModemData.h"
#include "power/BatteryData.h"
#include "sensors/AccelData.h"

struct TelemetrySample {
  GnssFix       gnss;     // position / speed / time (always present)
  BatteryStatus battery;  // pack state of charge (valid when read succeeded)
  AccelSample   accel;    // X/Y/Z acceleration in g (valid when read succeeded)
  ModemHealth   modem;    // modem die temperature (valid when read succeeded)
};
