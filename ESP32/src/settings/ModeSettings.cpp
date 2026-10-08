#include "settings/ModeSettings.h"

#include "config/Config.h"

namespace {

// Pin `value` into [low, high]. A free helper in an anonymous namespace rather
// than std::clamp so the file stays dependency-free and the intent is obvious
// at the call sites below, which are otherwise six near-identical lines.
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
  // The remaining four defaults are the constants they replace at runtime, and
  // live in Config.h with the subsystem they belong to.
  return ModeSettings(config::kDefaultSendIntervalSeconds,
                      config::kDefaultSleepBetweenSends,
                      config::kFixAcquireTimeoutSeconds,
                      config::kSdMaxQueuedFixes, config::kRetryIntervalHours,
                      config::kRetryMaxAgeHours,
                      config::kDefaultConfigCheckSeconds);
}

ModeSettings ModeSettings::movingDefaults() {
  return ModeSettings(config::kDefaultMovingSendIntervalSeconds,
                      config::kDefaultMovingSleepBetweenSends,
                      config::kDefaultMovingFixTimeoutSeconds,
                      config::kDefaultMovingQueueMaxFixes,
                      config::kDefaultMovingRetryIntervalHours,
                      config::kDefaultMovingRetryMaxAgeHours,
                      config::kDefaultMovingConfigCheckSeconds);
}

ModeSettings::ModeSettings() : ModeSettings(standbyDefaults()) {}

ModeSettings::ModeSettings(uint32_t intervalSeconds, bool sleepBetweenSends,
                           uint32_t fixTimeoutSeconds, uint32_t queueMaxFixes,
                           uint32_t retryIntervalHours,
                           uint32_t retryMaxAgeHours,
                           uint32_t configCheckSeconds)
    : intervalSeconds_(intervalSeconds),
      sleepBetweenSends_(sleepBetweenSends),
      fixTimeoutSeconds_(fixTimeoutSeconds),
      queueMaxFixes_(queueMaxFixes),
      retryIntervalHours_(retryIntervalHours),
      retryMaxAgeHours_(retryMaxAgeHours),
      configCheckSeconds_(configCheckSeconds) {}

void ModeSettings::clampToLimits() {
  // Clamp rather than reject: a typo in the broker's config should degrade to
  // the nearest sane value, not leave the device unable to report at all.
  intervalSeconds_   = clampRange(intervalSeconds_,
                                  config::kMinSendIntervalSeconds,
                                  config::kMaxSendIntervalSeconds);
  fixTimeoutSeconds_ = clampRange(fixTimeoutSeconds_,
                                  config::kMinFixTimeoutSeconds,
                                  config::kMaxFixTimeoutSeconds);
  queueMaxFixes_     = clampRange(queueMaxFixes_, config::kMinQueueMaxFixes,
                                  config::kMaxQueueMaxFixes);
  retryIntervalHours_ = clampRange(retryIntervalHours_,
                                   config::kMinRetryIntervalHours,
                                   config::kMaxRetryIntervalHours);
  configCheckSeconds_ = clampRange(configCheckSeconds_,
                                   config::kMinConfigCheckSeconds,
                                   config::kMaxConfigCheckSeconds);

  // The odd one out: 0 is not "too small", it is the deliberate "never give up
  // on a rejected fix" value, so only the ceiling is enforced.
  if (retryMaxAgeHours_ > config::kMaxRetryMaxAgeHours) {
    retryMaxAgeHours_ = config::kMaxRetryMaxAgeHours;
  }
}

bool ModeSettings::operator==(const ModeSettings& other) const {
  return intervalSeconds_ == other.intervalSeconds_ &&
         sleepBetweenSends_ == other.sleepBetweenSends_ &&
         fixTimeoutSeconds_ == other.fixTimeoutSeconds_ &&
         queueMaxFixes_ == other.queueMaxFixes_ &&
         retryIntervalHours_ == other.retryIntervalHours_ &&
         retryMaxAgeHours_ == other.retryMaxAgeHours_ &&
         configCheckSeconds_ == other.configCheckSeconds_;
}
