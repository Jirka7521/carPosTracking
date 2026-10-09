#pragma once

// =============================================================================
//  OfflineReason.h  -  Why the device is about to drop off the broker.
// -----------------------------------------------------------------------------
//  A dependency-free value type in the same spirit as TelemetrySample.h: the
//  sleep paths say WHY they are going down, StatusPublisher turns that into the
//  wire word, and the API decides how worrying it is. Keeping the severity on
//  the server is deliberate - one mapping table there instead of two that can
//  drift apart.
//
//      Sleep           a planned deep sleep between reports (sleep_between)
//      SleepNoMotion   the same, but in STANDBY with motion wake on: the car is
//                      parked, and the accelerometer is armed to say otherwise
//      PowerOff        the operator switched the unit off
//      BatteryLow      the pack fell below the cut-off - see LowBatteryGuard
//      Error           a fault the firmware caught itself (e.g. GNSS init failed)
//      ConnectionLost  never sent by the device in person: it is the LAST WILL
//                      the broker publishes on the device's behalf when the
//                      session dies without a clean DISCONNECT
// =============================================================================

enum class OfflineReason {
  Sleep,
  SleepNoMotion,
  PowerOff,
  BatteryLow,
  Error,
  ConnectionLost,
};
