#include "settings/DeviceSettings.h"

DeviceSettings::DeviceSettings()
    : version_(0),  // 0 = no server revision yet; see the header
      standby_(ModeSettings::standbyDefaults()),
      motion_() {}

void DeviceSettings::clampToLimits() {
  // Each half owns its own bounds; this only makes sure both are asked.
  standby_.clampToLimits();
  motion_.clampToLimits();
}

bool DeviceSettings::operator==(const DeviceSettings& other) const {
  // version_ is deliberately excluded - see the banner in the header.
  return standby_ == other.standby_ && motion_ == other.motion_;
}
