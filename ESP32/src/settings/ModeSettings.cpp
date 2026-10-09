#include "settings/ModeSettings.h"

#include "config/Config.h"

namespace {

// Pin `value` into [low, high]. A free helper in an anonymous namespace rather
// than std::clamp so the file stays dependency-free and the intent is obvious
// at the call sites below.
uint32_t clampRange(uint32_t value, uint32_t low, uint32_t high) {
  if (value < low) {
    return low;
  }
  if (value > high) {
    return high;
  }
  return value;
}

}  // namespace

ModeSettings ModeSettings::standbyDefaults() {
  // The fix timeout's default is the constant it replaces at runtime, and lives
  // in Config.h with the timing it belongs to.
  return ModeSettings(config::kDefaultSendIntervalSeconds,
                      config::kDefaultSleepBetweenSends,
                      config::kFixAcquireTimeoutSeconds);
}

ModeSettings ModeSettings::movingDefaults() {
  return ModeSettings(config::kDefaultMovingSendIntervalSeconds,
                      config::kDefaultMovingSleepBetweenSends,
                      config::kDefaultMovingFixTimeoutSeconds);
}

ModeSettings::ModeSettings() : ModeSettings(standbyDefaults()) {}

ModeSettings::ModeSettings(uint32_t intervalSeconds, bool sleepBetweenSends,
                           uint32_t fixTimeoutSeconds)
    : intervalSeconds_(intervalSeconds),
      sleepBetweenSends_(sleepBetweenSends),
      fixTimeoutSeconds_(fixTimeoutSeconds) {}

void ModeSettings::clampToLimits() {
  // Clamp rather than reject: a typo in the broker's config should degrade to
  // the nearest sane value, not leave the device unable to report at all.
  intervalSeconds_   = clampRange(intervalSeconds_,
                                  config::kMinSendIntervalSeconds,
                                  config::kMaxSendIntervalSeconds);
  fixTimeoutSeconds_ = clampRange(fixTimeoutSeconds_,
                                  config::kMinFixTimeoutSeconds,
                                  config::kMaxFixTimeoutSeconds);
}

bool ModeSettings::operator==(const ModeSettings& other) const {
  return intervalSeconds_ == other.intervalSeconds_ &&
         sleepBetweenSends_ == other.sleepBetweenSends_ &&
         fixTimeoutSeconds_ == other.fixTimeoutSeconds_;
}
