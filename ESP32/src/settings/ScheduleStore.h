#pragma once

// =============================================================================
//  ScheduleStore  -  The schedule bundle, cached in the clear on the microSD.
// -----------------------------------------------------------------------------
//  Responsibility (single!): persist one ScheduleBundle to a single JSON file on
//  the card and read it back. Format goes to ScheduleCodec, file IO to SdCard;
//  what is left is the policy binding them, exactly as SettingsStore does for
//  the configuration document:
//
//      load()  card unreadable / file missing / file corrupt  ->  invalid bundle
//      save()  rewrite the file whole (atomically, via SdCard::writeFile)
//
//  Why the card matters more here than it does for the settings. The retained
//  config document is replayed by the broker on every connect, so a device that
//  loses it is a few seconds from having it back. The schedule is the thing the
//  device needs precisely WHEN it cannot reach the broker - a car in a garage
//  overnight - so a bundle that lived only in RAM would be gone by the first
//  deep-sleep reboot and the feature would quietly stop working in exactly the
//  situation it exists for.
//
//  A failed load yields an INVALID bundle, not an empty one. The distinction
//  matters: an empty schedule would mean "no windows, use the fallback", which
//  is an answer; an invalid one means "unknown", and the caller must leave the
//  settings alone and defer to the config document.
//
//  Unencrypted, like settings.json beside it and for the same reason: profiles
//  and windows are a cadence, not a position. There is nothing here a stolen
//  card should not reveal.
// =============================================================================

#include "sdcard/SdCard.h"
#include "settings/ScheduleBundle.h"

class ScheduleStore {
 public:
  // Borrows `card` and `filePath` (both must outlive this object).
  ScheduleStore(SdCard& card, const char* filePath);

  // Read the cached bundle. Anything that goes wrong - no card, no file, a file
  // torn by a power cut mid-write - yields a bundle whose valid() is false.
  ScheduleBundle load() const;

  // Overwrite the cached bundle. Returns false if the card is unusable, which is
  // survivable: the device keeps the bundle it has in RAM and re-fetches it from
  // the broker after the next reboot.
  bool save(const ScheduleBundle& bundle);

 private:
  SdCard&     card_;
  const char* filePath_;
};
