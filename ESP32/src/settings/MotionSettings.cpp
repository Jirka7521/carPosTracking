#include "settings/MotionSettings.h"

#include "config/Config.h"

namespace {

// Same helper as ModeSettings.cpp, kept file-local for the same reason: the
// call sites read better with it than with std::clamp and a cast.
uint32_t clampRange(uint32_t value, uint32_t low, uint32_t high) {
  if (value < low) {
    return low;
  }
  if (value > high) {
    return high;
  }
  return value;
}

// The ADXL345's activity threshold resolution: THRESH_ACT is 62.5 mg per LSB,
// written as the exact fraction 125/2 mg so the rounding below stays in integers.
constexpr uint32_t kStepNumeratorMg   = 125;  // 62.5 mg = 125 / 2
constexpr uint32_t kStepDenominator   = 2;
constexpr uint32_t kMinThresholdSteps = 1;    // 0 misbehaves per the datasheet
constexpr uint32_t kMaxThresholdSteps = 255;  // the register is 8 bits

}  // namespace

MotionSettings::MotionSettings()
    : enabled_(config::kDefaultMotionWakeEnabled),
      thresholdMg_(config::kDefaultMotionThresholdMg),
      speedKmph_(config::kDefaultMotionSpeedKmph),
      wakeWaitSeconds_(config::kDefaultMotionWakeWaitSeconds),
      stopWaitSeconds_(config::kDefaultMotionStopWaitSeconds),
      moving_(ModeSettings::movingDefaults()) {}

uint8_t MotionSettings::thresholdSteps() const {
  // round(mg / 62.5) == round(mg * 2 / 125), done as add-half-then-divide so the
  // whole calculation stays in integers.
  const uint32_t steps =
      (thresholdMg_ * kStepDenominator + kStepNumeratorMg / 2) /
      kStepNumeratorMg;
  return static_cast<uint8_t>(
      clampRange(steps, kMinThresholdSteps, kMaxThresholdSteps));
}

void MotionSettings::clampToLimits() {
  // Clamp rather than reject, exactly like the reporting knobs: a typo should
  // degrade to the nearest sane value, never leave a sleeping device deaf.
  thresholdMg_     = clampRange(thresholdMg_, config::kMinMotionThresholdMg,
                                config::kMaxMotionThresholdMg);
  speedKmph_       = clampRange(speedKmph_, config::kMinMotionSpeedKmph,
                                config::kMaxMotionSpeedKmph);
  wakeWaitSeconds_ = clampRange(wakeWaitSeconds_,
                                config::kMinMotionWakeWaitSeconds,
                                config::kMaxMotionWakeWaitSeconds);
  stopWaitSeconds_ = clampRange(stopWaitSeconds_,
                                config::kMinMotionStopWaitSeconds,
                                config::kMaxMotionStopWaitSeconds);
  moving_.clampToLimits();
}

bool MotionSettings::operator==(const MotionSettings& other) const {
  return enabled_ == other.enabled_ && thresholdMg_ == other.thresholdMg_ &&
         speedKmph_ == other.speedKmph_ &&
         wakeWaitSeconds_ == other.wakeWaitSeconds_ &&
         stopWaitSeconds_ == other.stopWaitSeconds_ && moving_ == other.moving_;
}
