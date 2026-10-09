#pragma once

// =============================================================================
//  SettingsCodec  -  DeviceSettings <-> the agreed JSON document.
// -----------------------------------------------------------------------------
//  Responsibility (single!): own the on-the-wire / on-the-card representation of
//  the runtime settings, and nothing else. The MQTT config message and the
//  cached file on the SD card are the *same* document:
//
//      { "version": 7, "interval_s": 1200, "sleep_between": false,
//        "fix_timeout_s": 180, "queue_max_fixes": 20000,
//        "retry_interval_h": 24, "retry_max_age_h": 168,
//        "config_check_s": 3600,
//        "motion": { "enabled": true, "threshold_mg": 188, "speed_kmph": 3,
//                    "wake_wait_s": 600, "stop_wait_s": 900,
//                    "moving": { "interval_s": 30, "sleep_between": false,
//                                "fix_timeout_s": 180 } } }
//
//  interval_s, sleep_between and fix_timeout_s at the top level are the STANDBY
//  set; "motion.moving" is the same three again for while the car is driving.
//  The other four top-level keys (queue_max_fixes, the two retry_* and
//  config_check_s) are shared by both modes and have no moving copy. Nesting the
//  moving set rather than prefixing more keys is deliberate: one pair of helpers
//  (encodeMode / decodeMode) handles both, so the two sets cannot disagree about
//  a key name or a type. An older publisher that knows nothing of "motion" simply
//  leaves it at its defaults, and older firmware ignores the key outright. A
//  "moving" object that still carries the four shared keys (an older dashboard)
//  is fine too: decodeMode() never looks them up there, so they are ignored.
//
//  Keeping both sides of that format in one class is the point: the file we
//  write and the message we accept can never drift apart, because there is only
//  one encoder and one decoder. Adding a knob is a field on ModeSettings,
//  DeviceSettings or MotionSettings plus a key here - nothing else in the
//  settings pipeline needs to know.
//
//  Unlike the telemetry payload this document is plaintext - it carries no
//  position data, so there is nothing to encrypt end-to-end.
//
//  The object-level pair below exists because the schedule bundle embeds this
//  very document: each profile in it carries the same keys, "motion" included,
//  so ScheduleCodec hands the already-parsed object straight here rather than
//  keeping a second copy of the key names and the range checks. That is the whole
//  reason a profile and a config document can never disagree about what
//  "interval_s" means.
//
//  Stateless, so every method is static: there is nothing to construct.
// =============================================================================

#include <cstddef>
#include <cstdint>
#include <string>

#include "settings/DeviceSettings.h"

// Only the .cpp needs the full cJSON definition; a forward declaration keeps
// that dependency out of every file that merely encodes or decodes settings.
struct cJSON;

class SettingsCodec {
 public:
  // Serialise `settings` to the compact one-line JSON above. `version` is
  // omitted when it is 0, so a device that has never heard from the server
  // writes a document that claims no revision rather than revision zero.
  static std::string encode(const DeviceSettings& settings);

  // Parse `json` (`length` bytes, not necessarily NUL-terminated - MQTT payloads
  // are not) into `settings`.
  //
  // `settings` is used as the *starting point*, so a document that carries only
  // some of the keys updates just those and leaves the rest alone - and the same
  // holds inside "motion" and "motion.moving". That merge behaviour is what keeps
  // this firmware compatible with an older publisher that only knows about
  // interval_s and sleep_between. The result is NOT clamped; the caller decides
  // when to do that.
  //
  // Returns false if the document does not parse, is not an object, or contains
  // no known key at all - in which case `settings` is left untouched. A key of
  // the wrong type is ignored (and logged) rather than failing the whole
  // document: one bad field should not cost us the good ones beside it.
  static bool decode(const char* json, std::size_t length,
                     DeviceSettings& settings);

  // Add the value keys - the standby three, the shared four and the "motion"
  // object - to an existing cJSON object, plus "version" when `includeVersion`
  // is set and the version is non-zero. Used by encode() above and by
  // ScheduleCodec for each profile in a bundle.
  static void encodeInto(cJSON* object, const DeviceSettings& settings,
                         bool includeVersion);

  // Decode from an already-parsed object, with the same merge semantics and the
  // same return contract as decode(). Split out so a caller that has already
  // parsed a larger document does not have to re-print and re-parse a fragment
  // of it just to reach this code.
  static bool decodeObject(const cJSON* object, DeviceSettings& settings);

 private:
  // The three per-mode keys, into or out of `object`. Used for the top level
  // (standby) and for "motion.moving" alike. decodeMode() returns true when it
  // found at least one key it could use.
  static void encodeMode(cJSON* object, const ModeSettings& mode);
  static bool decodeMode(const cJSON* object, ModeSettings& mode);

  // The four keys shared by both modes, which only ever appear at the top level.
  // Same return contract as decodeMode().
  static void encodeShared(cJSON* root, const DeviceSettings& settings);
  static bool decodeShared(const cJSON* root, DeviceSettings& settings);

  // The "motion" object of `root`. decodeMotion() returns true when "motion" was
  // present and carried at least one usable key; an absent key is simply false,
  // a "motion" that is not an object is logged and ignored.
  static void encodeMotion(cJSON* root, const MotionSettings& motion);
  static bool decodeMotion(const cJSON* root, MotionSettings& motion);

  // Read one non-negative integer field into `out`. Returns true when the key
  // was present AND usable, which is what the caller counts to decide whether
  // the document said anything it understood. Factored out because the numeric
  // fields differ only in their name and destination.
  static bool readUint(const cJSON* root, const char* key, uint32_t& out);

  // The boolean counterpart of readUint(), for sleep_between and enabled.
  static bool readBool(const cJSON* root, const char* key, bool& out);

  // The field names, defined once. Both the encoder and the decoder use these,
  // so renaming a field is a one-line change here.
  static const char* const kVersionKey;
  static const char* const kIntervalKey;
  static const char* const kSleepKey;
  static const char* const kFixTimeoutKey;
  static const char* const kQueueMaxFixesKey;
  static const char* const kRetryIntervalKey;
  static const char* const kRetryMaxAgeKey;
  static const char* const kConfigCheckKey;
  static const char* const kMotionKey;
  static const char* const kEnabledKey;
  static const char* const kThresholdKey;
  static const char* const kSpeedKey;
  static const char* const kWakeWaitKey;
  static const char* const kStopWaitKey;
  static const char* const kMovingKey;
};
