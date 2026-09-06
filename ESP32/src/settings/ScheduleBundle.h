#pragma once

// =============================================================================
//  ScheduleBundle  -  The profiles and weekly windows this device switches between.
// -----------------------------------------------------------------------------
//  Responsibility (single!): hold one decoded schedule - the profiles, the rules
//  that select them, the fallback, and any override in force - and answer simple
//  questions about it. No parsing (ScheduleCodec), no arithmetic
//  (ScheduleEvaluator), no IO (ScheduleStore).
//
//  Where it comes from: the API publishes it retained to devices/<id>/schedule,
//  RemoteSchedule catches it, ScheduleStore caches it on the card. Until this
//  existed the device had no idea a schedule was involved at all - the server
//  evaluated the rules and simply republished a new settings document, which
//  meant a tracker could only change profile while the broker could reach it.
//
//  Profiles are addressed by SLOT, a small dense integer the server assigns and
//  keeps stable, not by the profile's database id. The slot travels back inside
//  every encrypted fix so the server can check the device is where it should be,
//  and a 36-character identifier in that position - on every report, for ever -
//  would be a poor trade for something one byte says as well.
//
//  The rules carry an `ordinal` for the same reason: the server has already
//  ranked them by age, so a priority tie is broken here with one integer
//  comparison rather than by shipping timestamps this device would have to parse
//  and compare identically to a .NET DateTime. Lower ordinal = older = wins.
//
//  Everything in here is UTC. There is no timezone anywhere on this device and
//  no zone data to consult; the dashboard converts for the human and stores UTC.
// =============================================================================

#include <cstdint>
#include <string>
#include <vector>

#include "settings/DeviceSettings.h"

class ScheduleBundle {
 public:
  // Returned wherever a slot is asked for and none applies.
  static constexpr int kNoSlot = -1;

  // One named set of the seven settings.
  struct Profile {
    int            slot = kNoSlot;
    std::string    name;    // for the serial log only; nothing keys off it
    DeviceSettings values;
  };

  // One weekly window. Minutes are UTC; the day mask has Sunday in bit 0, which
  // matches both .NET's DayOfWeek and CivilTime::weekdayFromEpoch.
  struct Rule {
    int      profileSlot     = kNoSlot;
    uint8_t  daysMask        = 0;  // 7 bits, bit 0 = Sunday
    uint16_t startMinute     = 0;  // 0-1439, minute of the UTC day
    uint16_t durationMinutes = 0;  // 1-1440, end-exclusive, may wrap midnight
    uint16_t priority        = 0;  // lower wins where windows overlap
    uint16_t ordinal         = 0;  // tie-break rank; lower is older, and wins
  };

  ScheduleBundle();

  // Forget everything, including the valid flag.
  void clear();

  // True once a bundle has actually been decoded into this object. A default-
  // constructed bundle is not merely empty, it is *unknown*, and the two must
  // not be confused: an empty schedule means "no windows", while an unknown one
  // means the device has never been told and must leave the settings alone.
  bool valid() const { return valid_; }

  uint32_t version() const { return version_; }
  bool     enabled() const { return enabled_; }
  int      fallbackSlot() const { return fallbackSlot_; }

  const std::vector<Profile>& profiles() const { return profiles_; }
  const std::vector<Rule>&    rules() const { return rules_; }

  // The profile occupying `slot`, or nullptr when nothing does. The pointer is
  // invalidated by any mutation, so callers use it and drop it.
  const Profile* findProfile(int slot) const;

  // An override beats the schedule until its instant passes - see the comment on
  // SettingsSelector for what produces one and why it self-expires.
  bool                  hasOverride() const { return hasOverride_; }
  int64_t               overrideUntilEpoch() const { return overrideUntilEpoch_; }
  const DeviceSettings& overrideValues() const { return overrideValues_; }

  // -- Construction, used by ScheduleCodec ------------------------------------
  // Deliberately plain setters rather than a builder: the codec fills one of
  // these in a single pass and there is nothing to validate here that the codec
  // has not already checked.
  void setVersion(uint32_t version) { version_ = version; }
  void setEnabled(bool enabled) { enabled_ = enabled; }
  void setFallbackSlot(int slot) { fallbackSlot_ = slot; }
  void addProfile(const Profile& profile) { profiles_.push_back(profile); }
  void addRule(const Rule& rule) { rules_.push_back(rule); }
  void setOverride(int64_t untilEpoch, const DeviceSettings& values);
  void markValid() { valid_ = true; }

 private:
  bool     valid_;
  uint32_t version_;
  bool     enabled_;
  int      fallbackSlot_;

  std::vector<Profile> profiles_;
  std::vector<Rule>    rules_;

  bool           hasOverride_;
  int64_t        overrideUntilEpoch_;
  DeviceSettings overrideValues_;
};
