#pragma once

// =============================================================================
//  SettingsSelector  -  Decide which DeviceSettings is actually in force.
// -----------------------------------------------------------------------------
//  Responsibility (single!): own the precedence rule, and be the only place that
//  knows it. Three things can have an opinion about this device's settings - the
//  retained config document, the schedule it evaluates itself, and an override
//  the server has stamped - and before this class existed main() would have had
//  to arbitrate between them at each of its five adoption points. Five copies of
//  a precedence rule is five chances for them to disagree.
//
//  The rule, highest priority first:
//
//    1. THE BUNDLE'S OVERRIDE, while the clock is trusted and its instant has not
//       passed. Somebody saved settings by hand on a scheduled device (the
//       dashboard told them it holds until the next switch), or the server
//       noticed this device running the wrong profile and is correcting it.
//       Either way it must beat the device's own evaluation, or the device would
//       switch straight back at the next boundary it computes.
//
//    2. THE SCHEDULE, while the clock is trusted and a profile resolves. This is
//       the normal case on a scheduled device, and the whole point of the
//       feature: it works with no broker in reach.
//
//    3. THE RETAINED CONFIG DOCUMENT. Exactly what this firmware did before
//       schedules existed. Used when the schedule is disabled, absent,
//       unresolvable, or - critically - when the clock is not trustworthy.
//
//  Rule 3 being the fallthrough is what makes the whole feature safe to ship.
//  Every way the schedule can fail lands back on the mechanism that has always
//  worked, and the server can see from the reports that it has happened.
//
//  version() is always the CONFIG DOCUMENT's revision, whichever branch supplied
//  the values. That number means "which config document do I hold", it is what
//  the dashboard's in-sync badge reads, and pinning it to a profile would break
//  a display that has nothing to do with schedules.
//
//  activeSlot() is always the SCHEDULE's answer, even while an override is
//  supplying the values. It means "where in its schedule does the device think it
//  is", which is what the server needs to check the device's clock and rules -
//  and the server skips a device with a live override anyway, so reporting the
//  override instead would just blind it for the stretch either side.
// =============================================================================

#include <cstdint>

#include "settings/DeviceSettings.h"
#include "settings/RemoteSchedule.h"
#include "settings/RemoteSettings.h"
#include "settings/ScheduleBundle.h"
#include "settings/UpdateSignal.h"
#include "util/DeviceClock.h"

class SettingsSelector {
 public:
  // Borrows all collaborators (all must outlive this object).
  SettingsSelector(RemoteSettings& settings, RemoteSchedule& schedule,
                   DeviceClock& clock, UpdateSignal& signal);

  // Poll both watchers, re-apply the precedence rule, and return the settings
  // now in force. Cheap enough to call on every GNSS poll: with nothing pending
  // it is two mutex takes and, on a scheduled device, one pass of the evaluator
  // over at most 32 rules.
  const DeviceSettings& resolve();

  // The settings resolve() last returned, without re-polling.
  const DeviceSettings& current() const { return current_; }

  // The profile slot the schedule puts the device in, or ScheduleBundle::kNoSlot
  // when the schedule is not in force. Sealed into every position report.
  int activeSlot() const { return activeSlot_; }

  // The bundle revision behind activeSlot(), or 0 when there is none. Reported
  // alongside the slot so the server can tell a device that disagrees from one
  // that simply has not received the current bundle.
  uint32_t scheduleVersion() const { return scheduleVersion_; }

  // Seconds until the schedule next switches profiles. Returns false when
  // nothing is scheduled to change - no bundle, no trusted clock, or a schedule
  // that resolves the same way all week.
  //
  // This is what shortens the interval wait and caps the deep sleep, so that a
  // profile due at 22:00 takes effect at 22:00 rather than whenever the device
  // next happens to wake.
  bool secondsUntilNextChange(int64_t& secondsOut) const;

  // Block for up to `timeoutMs` for either watcher to deliver something, then
  // resolve. Returns true when the settings in force changed as a result.
  //
  // The counterpart of RemoteSettings::waitForUpdate, but watching both topics -
  // which is the reason UpdateSignal exists.
  bool waitForChange(uint32_t timeoutMs);

  // Ask the broker to replay both retained documents, at most once every
  // `intervalSeconds`. Each watcher self-paces independently.
  void resyncIfDue(uint32_t intervalSeconds);

 private:
  // Re-apply the precedence rule to whatever the watchers currently hold.
  // Returns true when the resulting settings, slot or next change differ from
  // what was in force before.
  bool reselect();

  RemoteSettings& settings_;
  RemoteSchedule& schedule_;
  DeviceClock&    clock_;
  UpdateSignal&   signal_;

  DeviceSettings current_;
  int            activeSlot_;
  uint32_t       scheduleVersion_;

  // Absolute Unix time of the next profile switch, valid only while
  // hasNextChange_ is set.
  int64_t nextChangeEpoch_;
  bool    hasNextChange_;
};
