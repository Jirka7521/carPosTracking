#pragma once

// =============================================================================
//  DeviceSettings  -  The knobs the broker is allowed to turn at runtime.
// -----------------------------------------------------------------------------
//  Responsibility (single!): hold a *valid* set of runtime settings. It is a
//  small value object - copyable, comparable, no collaborators - so it can be
//  passed around freely between the store, the codec, the applier and main().
//
//  Everything else in Config.h is a compile-time constant. These are not:
//      intervalSeconds()      seconds between position reports
//      sleepBetweenSends()    power the modem down and deep-sleep in between
//      fixTimeoutSeconds()    how long to chase a GNSS lock before giving up
//      queueMaxFixes()        how many undelivered fixes the SD queue may hold
//      retryIntervalHours()   how long to wait between attempts on a rejected fix
//      retryMaxAgeHours()     when to abandon a fix the API keeps refusing
//      configCheckSeconds()   how often to ask the broker to re-send this document
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

class DeviceSettings {
 public:
  // The compile-time defaults from Config.h, for a device that has never had a
  // config message and has no cached settings on its card. version() is 0,
  // which is the sentinel for "no server revision yet" - the publisher omits
  // the field entirely in that case rather than claiming revision zero.
  DeviceSettings();

  uint32_t version() const { return version_; }
  uint32_t intervalSeconds() const { return intervalSeconds_; }
  bool     sleepBetweenSends() const { return sleepBetweenSends_; }
  uint32_t fixTimeoutSeconds() const { return fixTimeoutSeconds_; }
  uint32_t queueMaxFixes() const { return queueMaxFixes_; }
  uint32_t retryIntervalHours() const { return retryIntervalHours_; }
  uint32_t retryMaxAgeHours() const { return retryMaxAgeHours_; }

  // Only meaningful while awake: a deep-sleeping device re-subscribes on every
  // wake anyway, so the periodic re-check has nothing left to do for it.
  uint32_t configCheckSeconds() const { return configCheckSeconds_; }

  void setVersion(uint32_t version) { version_ = version; }
  void setIntervalSeconds(uint32_t seconds) { intervalSeconds_ = seconds; }
  void setSleepBetweenSends(bool sleep) { sleepBetweenSends_ = sleep; }
  void setFixTimeoutSeconds(uint32_t seconds) { fixTimeoutSeconds_ = seconds; }
  void setQueueMaxFixes(uint32_t fixes) { queueMaxFixes_ = fixes; }
  void setRetryIntervalHours(uint32_t hours) { retryIntervalHours_ = hours; }
  void setRetryMaxAgeHours(uint32_t hours) { retryMaxAgeHours_ = hours; }
  void setConfigCheckSeconds(uint32_t seconds) { configCheckSeconds_ = seconds; }

  // Pin every field into the range allowed by Config.h. Always call this on
  // settings that arrived from the broker or from the card. version() is left
  // alone - it is the server's number to choose, not ours to second-guess.
  void clampToLimits();

  // Value equality over the settings themselves, deliberately ignoring
  // version() - see the note in the banner above. Used to skip a needless card
  // write when the broker resends a config we already have.
  bool operator==(const DeviceSettings& other) const;
  bool operator!=(const DeviceSettings& other) const {
    return !(*this == other);
  }

 private:
  uint32_t version_;
  uint32_t intervalSeconds_;
  bool     sleepBetweenSends_;
  uint32_t fixTimeoutSeconds_;
  uint32_t queueMaxFixes_;
  uint32_t retryIntervalHours_;
  uint32_t retryMaxAgeHours_;
  uint32_t configCheckSeconds_;
};
