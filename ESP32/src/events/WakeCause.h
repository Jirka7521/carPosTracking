#pragma once

// =============================================================================
//  WakeCause.h  -  What brought the device back out of deep sleep.
// -----------------------------------------------------------------------------
//  A dependency-free value type in the same spirit as OfflineReason.h:
//  ActivityReporter reads it off the chip, StatusPublisher turns it into the
//  wire word, and the API decides what it means for the history.
//
//      Timer          the RTC timer - the regular wake for the next report
//      Accelerometer  the ADXL345's activity interrupt on ext1 - the car moved
//      PowerSwitch    the power switch on ext0 - the operator turned it back on
//
//  A power-on, a brown-out or a crash is not a wake: the chip did not sleep. The
//  online message's reset reason is what reports those.
// =============================================================================

enum class WakeCause {
  Timer,
  Accelerometer,
  PowerSwitch,
};
