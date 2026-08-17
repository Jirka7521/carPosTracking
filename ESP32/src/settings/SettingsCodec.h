#pragma once

// =============================================================================
//  SettingsCodec  -  DeviceSettings <-> the agreed JSON document.
// -----------------------------------------------------------------------------
//  Responsibility (single!): own the on-the-wire / on-the-card representation of
//  the runtime settings, and nothing else. The MQTT config message and the
//  cached file on the SD card are the *same* document:
//
//      { "version": 7, "interval_s": 60, "sleep_between": true,
//        "fix_timeout_s": 180, "queue_max_fixes": 20000,
//        "retry_interval_h": 24, "retry_max_age_h": 168,
//        "config_check_s": 900 }
//
//  Keeping both sides of that format in one class is the point: the file we
//  write and the message we accept can never drift apart, because there is only
//  one encoder and one decoder. Adding a knob is a field on DeviceSettings plus
//  a key here - nothing else in the settings pipeline needs to know.
//
//  Unlike the telemetry payload this document is plaintext - it carries no
//  position data, so there is nothing to encrypt end-to-end.
//
//  Stateless, so the two methods are static: there is nothing to construct.
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
  // some of the keys updates just those and leaves the rest alone. That merge
  // behaviour is what keeps this firmware compatible with an older publisher
  // that only knows about interval_s and sleep_between. The result is NOT
  // clamped; the caller decides when to do that.
  //
  // Returns false if the document does not parse, is not an object, or contains
  // no known key at all - in which case `settings` is left untouched. A key of
  // the wrong type is ignored (and logged) rather than failing the whole
  // document: one bad field should not cost us the five good ones beside it.
  static bool decode(const char* json, std::size_t length,
                     DeviceSettings& settings);

 private:
  // Read one non-negative integer field into `out`. Returns true when the key
  // was present AND usable, which is what the caller counts to decide whether
  // the document said anything it understood. Factored out because the five
  // numeric fields differ only in their name and destination.
  static bool readUint(const cJSON* root, const char* key, uint32_t& out);

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
};
