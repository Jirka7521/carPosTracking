#include "settings/DeviceSettings.h"

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

DeviceSettings::DeviceSettings()
    : version_(0),  // 0 = no server revision yet; see the header
      intervalSeconds_(config::kDefaultSendIntervalSeconds),
      sleepBetweenSends_(config::kDefaultSleepBetweenSends),
      fixTimeoutSeconds_(config::kFixAcquireTimeoutSeconds),
      queueMaxFixes_(config::kSdMaxQueuedFixes),
      retryIntervalHours_(config::kRetryIntervalHours),
      retryMaxAgeHours_(config::kRetryMaxAgeHours),
      configCheckSeconds_(config::kDefaultConfigCheckSeconds) {}

void DeviceSettings::clampToLimits() {
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

bool DeviceSettings::operator==(const DeviceSettings& other) const {
  // version_ is deliberately excluded - see the banner in the header.
  return intervalSeconds_ == other.intervalSeconds_ &&
         sleepBetweenSends_ == other.sleepBetweenSends_ &&
         fixTimeoutSeconds_ == other.fixTimeoutSeconds_ &&
         queueMaxFixes_ == other.queueMaxFixes_ &&
         retryIntervalHours_ == other.retryIntervalHours_ &&
         retryMaxAgeHours_ == other.retryMaxAgeHours_ &&
         configCheckSeconds_ == other.configCheckSeconds_;
}
