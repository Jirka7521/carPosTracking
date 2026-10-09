#pragma once

// =============================================================================
//  DeviceSettings  -  The knobs the broker is allowed to turn at runtime.
// -----------------------------------------------------------------------------
//  Responsibility (single!): hold a *valid* set of runtime settings. It is a
//  small value object - copyable, comparable, no collaborators - so it can be
//  passed around freely between the store, the codec, the applier and main().
//
//  Everything else in Config.h is a compile-time constant. These are not:
//      standby()              the three per-mode knobs (interval, sleep, fix
//                             timeout) - the config document's top-level keys
//      queueMaxFixes() ...    the four shared knobs (queue cap, retry pacing,
//      configCheckSeconds()   retry give-up age, config re-check) - top-level
//                             keys too, and in force in EVERY mode
//      motion()               the motion-wake block: whether the accelerometer
//                             wakes the device, its thresholds, and a second set
//                             of the three per-mode knobs for while it is moving
//
//  The first three getters and setters below (intervalSeconds() and friends) are
//  the STANDBY set's, kept as forwards so everything that predates motion wake
//  still reads exactly what it always did. With motion wake off the standby set
//  is the only set, so for those callers nothing has changed. Code that must
//  honour the MOVING set asks MotionTracker::activeMode() which one is in force
//  instead. The four shared knobs have no second copy, so there is nothing to ask.
//
//  version() is not a setting but the server's revision number for this whole
//  document. It rides along so the device can echo it back in every report
//  (as `settings_version`), which is how the dashboard knows whether a change
//  it published has actually been picked up. It is NOT part of operator==:
//  two documents with identical values are the same settings, and re-saving
//  the card just because a number changed would be pointless IO.
//
//  Validity is the class's own business: clampToLimits() pins every field into
//  the range from Config.h, so a malformed broker message can never leave the
//  device spinning on a zero-second interval or asleep for a month. Construct,
//  then clamp anything that came from outside.
// =============================================================================

#include <cstdint>

#include "settings/ModeSettings.h"
#include "settings/MotionSettings.h"

class DeviceSettings {
 public:
  // The compile-time defaults from Config.h, for a device that has never had a
  // config message and has no cached settings on its card. version() is 0,
  // which is the sentinel for "no server revision yet" - the publisher omits
  // the field entirely in that case rather than claiming revision zero.
  DeviceSettings();

  uint32_t version() const { return version_; }
  void     setVersion(uint32_t version) { version_ = version; }

  // The two halves. See the banner for which one a caller wants.
  const ModeSettings&   standby() const { return standby_; }
  const MotionSettings& motion() const { return motion_; }
  void setStandby(const ModeSettings& standby) { standby_ = standby; }
  void setMotion(const MotionSettings& motion) { motion_ = motion; }

  // The STANDBY set, forwarded - see the banner.
  uint32_t intervalSeconds() const { return standby_.intervalSeconds(); }
  bool     sleepBetweenSends() const { return standby_.sleepBetweenSends(); }
  uint32_t fixTimeoutSeconds() const { return standby_.fixTimeoutSeconds(); }

  void setIntervalSeconds(uint32_t seconds) {
    standby_.setIntervalSeconds(seconds);
  }
  void setSleepBetweenSends(bool sleep) { standby_.setSleepBetweenSends(sleep); }
  void setFixTimeoutSeconds(uint32_t seconds) {
    standby_.setFixTimeoutSeconds(seconds);
  }

  // The shared knobs - one value, whichever set is in force.
  uint32_t queueMaxFixes() const { return queueMaxFixes_; }
  uint32_t retryIntervalHours() const { return retryIntervalHours_; }
  uint32_t retryMaxAgeHours() const { return retryMaxAgeHours_; }

  // Only meaningful while awake: a deep-sleeping device re-subscribes on every
  // wake anyway, so the periodic re-check has nothing left to do for it.
  uint32_t configCheckSeconds() const { return configCheckSeconds_; }

  void setQueueMaxFixes(uint32_t fixes) { queueMaxFixes_ = fixes; }
  void setRetryIntervalHours(uint32_t hours) { retryIntervalHours_ = hours; }
  void setRetryMaxAgeHours(uint32_t hours) { retryMaxAgeHours_ = hours; }
  void setConfigCheckSeconds(uint32_t seconds) { configCheckSeconds_ = seconds; }

  // Pin every field - both sets, the shared knobs and the motion thresholds -
  // into the range allowed by Config.h. Always call this on settings that
  // arrived from the broker or from the card. version() is left alone - it is
  // the server's number to choose, not ours to second-guess.
  void clampToLimits();

  // Value equality over the settings themselves, deliberately ignoring
  // version() - see the note in the banner above. Used to skip a needless card
  // write when the broker resends a config we already have, and by
  // SettingsSelector to notice that what is in force has changed.
  bool operator==(const DeviceSettings& other) const;
  bool operator!=(const DeviceSettings& other) const {
    return !(*this == other);
  }

 private:
  uint32_t       version_;
  ModeSettings   standby_;
  uint32_t       queueMaxFixes_;
  uint32_t       retryIntervalHours_;
  uint32_t       retryMaxAgeHours_;
  uint32_t       configCheckSeconds_;
  MotionSettings motion_;
};
