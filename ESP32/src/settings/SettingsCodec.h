#pragma once

// =============================================================================
//  SettingsCodec  -  DeviceSettings <-> the agreed JSON document.
// -----------------------------------------------------------------------------
//  Responsibility (single!): own the on-the-wire / on-the-card representation of
//  the runtime settings, and nothing else. The MQTT config message and the
//  cached file on the SD card are the *same* document:
//
//      { "interval_s": 60, "sleep_between": true }
//
//  Keeping both sides of that format in one class is the point: the file we
//  write and the message we accept can never drift apart, because there is only
//  one encoder and one decoder.
//
//  Unlike the telemetry payload this document is plaintext - it carries no
//  position data, so there is nothing to encrypt end-to-end.
//
//  Stateless, so the two methods are static: there is nothing to construct.
// =============================================================================

#include <cstddef>
#include <string>

#include "settings/DeviceSettings.h"

class SettingsCodec {
 public:
  // Serialise `settings` to the compact one-line JSON above.
  static std::string encode(const DeviceSettings& settings);

  // Parse `json` (`length` bytes, not necessarily NUL-terminated - MQTT payloads
  // are not) into `settings`.
  //
  // `settings` is used as the *starting point*, so a document that carries only
  // one of the two keys updates just that one and leaves the other alone. The
  // result is NOT clamped; the caller decides when to do that.
  //
  // Returns false if the document does not parse, is not an object, or contains
  // neither known key - in which case `settings` is left untouched. A key of the
  // wrong type is ignored (and logged) rather than failing the whole document.
  static bool decode(const char* json, std::size_t length,
                     DeviceSettings& settings);

 private:
  // The two field names, defined once. Both the encoder and the decoder use
  // these, so renaming a field is a one-line change here.
  static const char* const kIntervalKey;
  static const char* const kSleepKey;
};
