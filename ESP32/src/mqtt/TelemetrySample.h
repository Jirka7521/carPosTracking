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

#include <cstdint>

#include "gnss/GnssData.h"
#include "modem/ModemData.h"
#include "power/BatteryData.h"
#include "sensors/AccelData.h"

struct TelemetrySample {
  GnssFix       gnss;     // position / speed / time (always present)
  BatteryStatus battery;  // pack state of charge (valid when read succeeded)
  AccelSample   accel;    // X/Y/Z acceleration in g (valid when read succeeded)
  ModemHealth   modem;    // modem die temperature (valid when read succeeded)

  // Revision of the settings document in force when this sample was taken; 0
  // when the device has never received one. Sealed into the payload so the
  // server can tell which configuration the device is actually running.
  //
  // It is captured per sample rather than per publish on purpose: a fix drained
  // from the SD card days later reports the settings it was TAKEN under, which
  // is the honest answer. The API only advances a device's applied version from
  // the newest fix in a batch, so an old backlog cannot walk it backwards.
  uint32_t settingsVersion = 0;
};
