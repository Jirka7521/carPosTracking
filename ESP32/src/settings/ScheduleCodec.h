#pragma once

// =============================================================================
//  ScheduleCodec  -  ScheduleBundle <-> the agreed JSON document.
// -----------------------------------------------------------------------------
//  Responsibility (single!): own the representation of the schedule bundle, for
//  both the MQTT message and the copy cached on the SD card - the same document
//  in both places, exactly as SettingsCodec does for the config:
//
//    { "sched_v": 7, "enabled": true, "fallback": 0,
//      "profiles": [ { "slot": 0, "name": "Day", "interval_s": 60,
//                      "sleep_between": false, "fix_timeout_s": 180,
//                      "queue_max_fixes": 20000, "retry_interval_h": 24,
//                      "retry_max_age_h": 168, "config_check_s": 3600 } ],
//      "rules":    [ { "slot": 1, "days": 62, "start_m": 1320, "dur_m": 480,
//                      "prio": 100, "ord": 3 } ],
//      "override": { "until": "2026-09-06T22:00:00Z", ...the seven values... } }
//
//  The seven value keys inside a profile are DELIBERATELY the same as the config
//  document's, so each profile object is handed straight to SettingsCodec:
//  one decoder, one set of bounds, no second place for the two to drift.
//
//  Two differences from SettingsCodec worth knowing:
//
//    1. Decoding is ALL-OR-NOTHING. A partial config document is a legitimate
//       partial update - the server may only be changing the interval. A partial
//       bundle is corruption: half a rule set does not describe a schedule, it
//       describes a different one, and applying it would put the device on the
//       wrong profile with complete confidence.
//
//    2. "override" is absent, not null, when there is none. The API serialises
//       with WhenWritingNull for exactly this reason, so the test here is for
//       the key's presence.
//
//  Mirrored by API/CarPosAPI/Dtos/DeviceScheduleBundleDto.cs and its three
//  companions - they must match this character for character.
//
//  Stateless, so the methods are static: there is nothing to construct.
// =============================================================================

#include <cstddef>
#include <cstdint>
#include <string>

#include "settings/ScheduleBundle.h"

// Only the .cpp needs the full cJSON definition.
struct cJSON;

class ScheduleCodec {
 public:
  // Serialise `bundle` to the compact one-line JSON above, for the card. Returns
  // an empty string if it could not be built.
  static std::string encode(const ScheduleBundle& bundle);

  // Parse `json` (`length` bytes, not necessarily NUL-terminated - MQTT payloads
  // are not) into `bundle`, replacing whatever it held.
  //
  // Returns false, leaving `bundle` untouched, when the document does not parse,
  // is not an object, carries no version, or exceeds the caps in Config.h. A
  // single malformed profile or rule fails the whole document - see the
  // all-or-nothing note above.
  static bool decode(const char* json, std::size_t length,
                     ScheduleBundle& bundle);

 private:
  // Read one integer field into `out`, bounded to [min, max]. Returns false when
  // the key is absent, is not a finite number, or falls outside the range - all
  // of which fail the document rather than being quietly clamped, because a slot
  // or a day mask outside its range is not a value to correct, it is evidence
  // the document is not what this firmware expects.
  static bool readBoundedInt(const cJSON* object, const char* key, int min,
                             int max, int& out);

  // Decode one entry of "profiles" / "rules". Both return false on anything they
  // do not fully understand.
  static bool decodeProfile(const cJSON* item, ScheduleBundle::Profile& out);
  static bool decodeRule(const cJSON* item, ScheduleBundle::Rule& out);

  // Decode the optional "override" object. Returns false only when the key is
  // present but unusable; an absent key is success with `applied` left false.
  static bool decodeOverride(const cJSON* root, ScheduleBundle& bundle,
                             bool& applied);

  // The field names, defined once, so renaming one is a single-line change.
  static const char* const kVersionKey;
  static const char* const kEnabledKey;
  static const char* const kFallbackKey;
  static const char* const kProfilesKey;
  static const char* const kRulesKey;
  static const char* const kOverrideKey;
  static const char* const kSlotKey;
  static const char* const kNameKey;
  static const char* const kDaysKey;
  static const char* const kStartMinuteKey;
  static const char* const kDurationKey;
  static const char* const kPriorityKey;
  static const char* const kOrdinalKey;
  static const char* const kUntilKey;
};
