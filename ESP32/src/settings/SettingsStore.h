#pragma once

// =============================================================================
//  SettingsStore  -  The runtime settings, cached in the clear on the microSD.
// -----------------------------------------------------------------------------
//  Responsibility (single!): persist one DeviceSettings to a single JSON file on
//  the card and read it back. It delegates the format to SettingsCodec and the
//  file IO to SdCard, so it is little more than the policy that binds them:
//
//      load()  card unreadable / file missing / file corrupt  ->  defaults
//      save()  rewrite the file whole (atomically, via SdCard::writeFile)
//
//  Why the card at all? The broker's config is only replayed when we are online.
//  A device that boots in a tunnel must still know its interval and, crucially,
//  whether it is supposed to deep-sleep - otherwise a flat battery is one bad
//  reboot away. The card is that memory.
//
//  Why unencrypted, when the queue beside it is ciphertext? Because this file
//  holds no position data - only a cadence and a flag. There is nothing here
//  that a stolen card should not reveal.
// =============================================================================

#include "sdcard/SdCard.h"
#include "settings/DeviceSettings.h"

class SettingsStore {
 public:
  // Borrows `card` and `filePath` (both must outlive this object).
  SettingsStore(SdCard& card, const char* filePath);

  // Read the cached settings. Anything that goes wrong - no card, no file, a
  // file torn by a power cut mid-write - yields `defaults` rather than an error:
  // there is always a usable answer, which is what the caller needs.
  // The result is clamped to the limits in Config.h.
  DeviceSettings load(const DeviceSettings& defaults) const;

  // Overwrite the cached settings. Returns false if the card is unusable, which
  // is survivable: the device keeps running on the settings it has in RAM and
  // simply re-fetches them from the broker after the next reboot.
  bool save(const DeviceSettings& settings);

 private:
  SdCard&     card_;
  const char* filePath_;
};
