#pragma once

// =============================================================================
//  ModeSettings  -  The seven reporting knobs, for one operating mode.
// -----------------------------------------------------------------------------
//  Responsibility (single!): hold one *valid* set of the seven runtime values
//  that govern how the device reports. It is a small value object - copyable,
//  comparable, no collaborators - and it knows nothing about WHEN it is in force.
//
//      intervalSeconds()      seconds between position reports
//      sleepBetweenSends()    power the modem down and deep-sleep in between
//      fixTimeoutSeconds()    how long to chase a GNSS lock before giving up
//      queueMaxFixes()        how many undelivered fixes the SD queue may hold
//      retryIntervalHours()   how long to wait between attempts on a rejected fix
//      retryMaxAgeHours()     when to abandon a fix the API keeps refusing
//      configCheckSeconds()   how often to ask the broker to re-send the config
//
//  Why it exists apart from DeviceSettings: since motion wake, a device carries
//  TWO of these - the STANDBY set (the config document's top-level keys, in force
//  while the car is parked) and the MOVING set ("motion.moving", in force while it
//  is driving). Same seven values, same bounds, same meaning; only which one is in
//  force differs, and MotionTracker decides that. One class for both is what
//  guarantees a setting cannot quietly mean one thing parked and another moving.
//
//  Validity is the class's own business: clampToLimits() pins every field into
//  the range from Config.h - the SAME range in both modes - so a malformed broker
//  message can never leave the device spinning on a zero-second interval or
//  asleep for a month.
// =============================================================================

#include <cstdint>

class ModeSettings {
 public:
  // The STANDBY defaults from Config.h: what a device that has never had a
  // config message reports with. The default constructor produces the same.
  static ModeSettings standbyDefaults();

  // The MOVING defaults from Config.h (the kDefaultMoving* constants).
  static ModeSettings movingDefaults();

  ModeSettings();

  uint32_t intervalSeconds() const { return intervalSeconds_; }
  bool     sleepBetweenSends() const { return sleepBetweenSends_; }
  uint32_t fixTimeoutSeconds() const { return fixTimeoutSeconds_; }
  uint32_t queueMaxFixes() const { return queueMaxFixes_; }
  uint32_t retryIntervalHours() const { return retryIntervalHours_; }
  uint32_t retryMaxAgeHours() const { return retryMaxAgeHours_; }

  // Only meaningful while awake: a deep-sleeping device re-subscribes on every
  // wake anyway, so the periodic re-check has nothing left to do for it.
  uint32_t configCheckSeconds() const { return configCheckSeconds_; }

  void setIntervalSeconds(uint32_t seconds) { intervalSeconds_ = seconds; }
  void setSleepBetweenSends(bool sleep) { sleepBetweenSends_ = sleep; }
  void setFixTimeoutSeconds(uint32_t seconds) { fixTimeoutSeconds_ = seconds; }
  void setQueueMaxFixes(uint32_t fixes) { queueMaxFixes_ = fixes; }
  void setRetryIntervalHours(uint32_t hours) { retryIntervalHours_ = hours; }
  void setRetryMaxAgeHours(uint32_t hours) { retryMaxAgeHours_ = hours; }
  void setConfigCheckSeconds(uint32_t seconds) { configCheckSeconds_ = seconds; }

  // Pin every field into the range allowed by Config.h. Always call this on
  // values that arrived from the broker or from the card.
  void clampToLimits();

  bool operator==(const ModeSettings& other) const;
  bool operator!=(const ModeSettings& other) const { return !(*this == other); }

 private:
  // Every field spelled out, so the two factories above read as a table of
  // Config.h constants rather than a chain of setters.
  ModeSettings(uint32_t intervalSeconds, bool sleepBetweenSends,
               uint32_t fixTimeoutSeconds, uint32_t queueMaxFixes,
               uint32_t retryIntervalHours, uint32_t retryMaxAgeHours,
               uint32_t configCheckSeconds);

  uint32_t intervalSeconds_;
  bool     sleepBetweenSends_;
  uint32_t fixTimeoutSeconds_;
  uint32_t queueMaxFixes_;
  uint32_t retryIntervalHours_;
  uint32_t retryMaxAgeHours_;
  uint32_t configCheckSeconds_;
};
