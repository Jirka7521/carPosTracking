#include "settings/ScheduleBundle.h"

ScheduleBundle::ScheduleBundle()
    : valid_(false),
      version_(0),
      enabled_(false),
      fallbackSlot_(kNoSlot),
      hasOverride_(false),
      overrideUntilEpoch_(0) {}

void ScheduleBundle::clear() {
  valid_        = false;
  version_      = 0;
  enabled_      = false;
  fallbackSlot_ = kNoSlot;

  // shrink_to_fit, not just clear(): a bundle is replaced wholesale on every
  // publish, and on a device whose internal heap is under 200 KB once mbedTLS is
  // up there is no reason to keep capacity for twelve profiles around when the
  // next document might carry two.
  profiles_.clear();
  profiles_.shrink_to_fit();
  rules_.clear();
  rules_.shrink_to_fit();

  hasOverride_        = false;
  overrideUntilEpoch_ = 0;
  overrideValues_     = DeviceSettings();
}

const ScheduleBundle::Profile* ScheduleBundle::findProfile(int slot) const {
  if (slot == kNoSlot) {
    return nullptr;
  }

  // A linear scan over at most twelve entries. An index would be faster in the
  // abstract and slower here, once building and keeping it is counted.
  for (const Profile& profile : profiles_) {
    if (profile.slot == slot) {
      return &profile;
    }
  }
  return nullptr;
}

void ScheduleBundle::setOverride(int64_t untilEpoch,
                                 const DeviceSettings& values) {
  hasOverride_        = true;
  overrideUntilEpoch_ = untilEpoch;
  overrideValues_     = values;
}
