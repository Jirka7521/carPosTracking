#include "settings/DeviceSettings.h"

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

}  // namespace

DeviceSettings::DeviceSettings()
    : version_(0),  // 0 = no server revision yet; see the header
      standby_(ModeSettings::standbyDefaults()),
      // The shared knobs' defaults are the constants they replace at runtime,
      // and live in Config.h with the subsystem they belong to.
      queueMaxFixes_(config::kSdMaxQueuedFixes),
      retryIntervalHours_(config::kRetryIntervalHours),
      retryMaxAgeHours_(config::kRetryMaxAgeHours),
      configCheckSeconds_(config::kDefaultConfigCheckSeconds),
      motion_() {}

void DeviceSettings::clampToLimits() {
  // Each set owns its own bounds; the shared knobs are this class's own.
  standby_.clampToLimits();
  motion_.clampToLimits();

  // Clamp rather than reject, exactly like the per-mode knobs.
  queueMaxFixes_      = clampRange(queueMaxFixes_, config::kMinQueueMaxFixes,
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
  return standby_ == other.standby_ &&
         queueMaxFixes_ == other.queueMaxFixes_ &&
         retryIntervalHours_ == other.retryIntervalHours_ &&
         retryMaxAgeHours_ == other.retryMaxAgeHours_ &&
         configCheckSeconds_ == other.configCheckSeconds_ &&
         motion_ == other.motion_;
}
