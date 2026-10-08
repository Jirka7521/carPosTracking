#pragma once

// =============================================================================
//  MotionSettings  -  The motion-wake knobs, and the set used while moving.
// -----------------------------------------------------------------------------
//  Responsibility (single!): hold one *valid* "motion" block of the runtime
//  settings - a small value object, copyable and comparable, with no
//  collaborators:
//
//      enabled()          motion wake on at all. Off means the device behaves
//                         exactly as it did before this block existed.
//      thresholdMg()      accelerometer wake threshold, in milli-g
//      speedKmph()        a fix strictly faster than this counts as MOVING
//      wakeWaitSeconds()  after a wake, how long to look for a moving fix
//      stopWaitSeconds()  how long after the last moving fix to stay MOVING
//      moving()           the full set of the seven reporting knobs in force
//                         while moving (the standby set lives on DeviceSettings)
//
//  WHAT it means lives in MotionTracker; this class only keeps the numbers
//  valid. The bounds and defaults are the "Motion wake" block of Config.h, which
//  took them from docs/MOTION-WAKE-THRESHOLDS.md.
// =============================================================================

#include <cstdint>

#include "settings/ModeSettings.h"

class MotionSettings {
 public:
  // The Config.h defaults: OFF, with the documented thresholds ready for the
  // moment somebody switches it on.
  MotionSettings();

  bool                enabled() const { return enabled_; }
  uint32_t            thresholdMg() const { return thresholdMg_; }
  uint32_t            speedKmph() const { return speedKmph_; }
  uint32_t            wakeWaitSeconds() const { return wakeWaitSeconds_; }
  uint32_t            stopWaitSeconds() const { return stopWaitSeconds_; }
  const ModeSettings& moving() const { return moving_; }

  void setEnabled(bool enabled) { enabled_ = enabled; }
  void setThresholdMg(uint32_t mg) { thresholdMg_ = mg; }
  void setSpeedKmph(uint32_t kmph) { speedKmph_ = kmph; }
  void setWakeWaitSeconds(uint32_t seconds) { wakeWaitSeconds_ = seconds; }
  void setStopWaitSeconds(uint32_t seconds) { stopWaitSeconds_ = seconds; }
  void setMoving(const ModeSettings& moving) { moving_ = moving; }

  // thresholdMg() as the ADXL345's THRESH_ACT register value: the nearest
  // 62.5 mg step, never below 1 - the datasheet warns that 0 misbehaves - and
  // never above the register's 255. The dashboard takes milli-g because that is
  // what a person reasons in; the sensor only understands steps.
  uint8_t thresholdSteps() const;

  // Pin every field, and the moving set, into the ranges from Config.h.
  void clampToLimits();

  bool operator==(const MotionSettings& other) const;
  bool operator!=(const MotionSettings& other) const {
    return !(*this == other);
  }

 private:
  bool         enabled_;
  uint32_t     thresholdMg_;
  uint32_t     speedKmph_;
  uint32_t     wakeWaitSeconds_;
  uint32_t     stopWaitSeconds_;
  ModeSettings moving_;
};
