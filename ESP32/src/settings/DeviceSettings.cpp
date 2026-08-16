#include "settings/DeviceSettings.h"

#include "config/Config.h"

DeviceSettings::DeviceSettings()
    : intervalSeconds_(config::kDefaultSendIntervalSeconds),
      sleepBetweenSends_(config::kDefaultSleepBetweenSends) {}

DeviceSettings::DeviceSettings(uint32_t intervalSeconds, bool sleepBetweenSends)
    : intervalSeconds_(intervalSeconds),
      sleepBetweenSends_(sleepBetweenSends) {}

void DeviceSettings::clampToLimits() {
  // Clamp rather than reject: a typo in the broker's config should degrade to
  // the nearest sane cadence, not leave the device unable to report at all.
  if (intervalSeconds_ < config::kMinSendIntervalSeconds) {
    intervalSeconds_ = config::kMinSendIntervalSeconds;
  } else if (intervalSeconds_ > config::kMaxSendIntervalSeconds) {
    intervalSeconds_ = config::kMaxSendIntervalSeconds;
  }
}

bool DeviceSettings::operator==(const DeviceSettings& other) const {
  return intervalSeconds_ == other.intervalSeconds_ &&
         sleepBetweenSends_ == other.sleepBetweenSends_;
}
