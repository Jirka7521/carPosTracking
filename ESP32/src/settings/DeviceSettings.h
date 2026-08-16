#pragma once

// =============================================================================
//  DeviceSettings  -  The two knobs the broker is allowed to turn at runtime.
// -----------------------------------------------------------------------------
//  Responsibility (single!): hold a *valid* pair of runtime settings. It is a
//  small value object - copyable, comparable, no collaborators - so it can be
//  passed around freely between the store, the codec and main().
//
//  Everything else in Config.h is a compile-time constant. These two are not:
//      intervalSeconds()    seconds between position reports
//      sleepBetweenSends()  power the modem down and deep-sleep in between
//
//  Validity is the class's own business: clampToLimits() pins the interval into
//  the [kMinSendIntervalSeconds, kMaxSendIntervalSeconds] window from Config.h,
//  so a malformed broker message can never leave the device spinning on a
//  zero-second interval or asleep for a month. Construct, then clamp anything
//  that came from outside.
// =============================================================================

#include <cstdint>

class DeviceSettings {
 public:
  // The compile-time defaults from Config.h, for a device that has never had a
  // config message and has no cached settings on its card.
  DeviceSettings();

  DeviceSettings(uint32_t intervalSeconds, bool sleepBetweenSends);

  uint32_t intervalSeconds() const { return intervalSeconds_; }
  bool     sleepBetweenSends() const { return sleepBetweenSends_; }

  void setIntervalSeconds(uint32_t seconds) { intervalSeconds_ = seconds; }
  void setSleepBetweenSends(bool sleep) { sleepBetweenSends_ = sleep; }

  // Pin the interval into the range allowed by Config.h. Always call this on
  // settings that arrived from the broker or from the card.
  void clampToLimits();

  // Value equality - used to skip a needless card write when the broker resends
  // a config we already have.
  bool operator==(const DeviceSettings& other) const;
  bool operator!=(const DeviceSettings& other) const {
    return !(*this == other);
  }

 private:
  uint32_t intervalSeconds_;
  bool     sleepBetweenSends_;
};
