#pragma once

// =============================================================================
//  MotionTracker  -  Is the car parked, possibly moving, or driving?
// -----------------------------------------------------------------------------
//  Responsibility (single!): own the motion-wake state machine, and with it the
//  answer to "which set of the three per-mode reporting knobs is in force right
//  now".
//  It decides; it does not act. main() asks it what to do with each fix and how
//  to sleep, Adxl345 and DeepSleepController do the hardware.
//
//      STANDBY   the car is parked. The STANDBY set is in force. Asleep, the
//                device wakes on its timer or on the accelerometer; awake, main()
//                polls the accelerometer and calls onActivity().
//      CHECKING  something woke us - accelerometer, timer, power-on, the switch.
//                The MOVING set is in force but the device never sleeps: it
//                acquires every kMotionCheckPollMs for up to wake_wait_s, looking
//                for a fix faster than speed_kmph. Only the FIRST fix of a check
//                is published - one report per wake, not one per poll.
//      MOVING    a fast fix was seen. The MOVING set is in force and every fix is
//                published. Each fast fix pushes the deadline to now +
//                stop_wait_s; once it passes, back to STANDBY.
//
//  CHECKING and MOVING share one mechanism - a deadline - and differ only in what
//  sets it: a check's is fixed when it starts, moving's is renewed by every fast
//  fix. A fix with no position (tunnel, garage) renews nothing, so a device that
//  loses the sky eventually sleeps; the accelerometer wakes it again if the car
//  is in fact still moving.
//
//  Surviving deep sleep. CHECKING never sleeps and STANDBY needs no memory (every
//  wake from it starts a check), so only MOVING with moving.sleep_between has
//  anything to carry across: the time left on its deadline, kept in RTC memory.
//  It is stored as a REMAINDER with the sleep already subtracted, not as a
//  timestamp - esp_timer restarts at zero on wake, and the wall clock jumps when
//  DeviceClock is first seeded from GNSS, so neither is a usable time base across
//  a reboot. Only a TIMER wake resumes MOVING; any other cause means something
//  else happened (switch, reset, power loss) and starts a fresh check.
//
//  With motion wake OFF this class is inert: STANDBY for ever, every fix
//  published, the standby set in force - i.e. exactly the device as it was before
//  motion wake existed.
//
//  Every transition is also REPORTED, as a MotionChange, to the handler set
//  with setChangeHandler() - that is how the dashboard's event history learns
//  when the car started moving and when it parked. Reporting is not acting:
//  the handler only records, it never feeds back into the state machine.
// =============================================================================

#include <cstdint>
#include <functional>

#include "esp_sleep.h"
#include "motion/MotionChange.h"
#include "settings/DeviceSettings.h"
#include "settings/ModeSettings.h"

class MotionTracker {
 public:
  enum class State : uint8_t {
    Standby  = 0,
    Checking = 1,
    Moving   = 2,
  };

  // Called with every transition, on the caller's (main) task.
  using ChangeHandler = std::function<void(MotionChange change)>;

  MotionTracker();

  // Read the wake cause and what RTC memory remembers, and pick the starting
  // state for `settings`. Call once, early: after the cached settings are loaded
  // and before anything asks activeMode().
  void begin(const DeviceSettings& settings);

  // Start reporting transitions to `handler` (an empty one stops it). begin()
  // runs before anyone can listen, so the check a boot starts with is NOT
  // reported - the caller records that one itself (see isChecking()).
  void setChangeHandler(ChangeHandler handler);

  // Adopt settings that may have changed - a config arrived, the schedule moved
  // to another profile. Turning motion wake ON starts a check at once; turning it
  // OFF drops straight back to plain standby. Cheap when nothing changed.
  void update(const DeviceSettings& settings);

  bool  enabled() const { return enabled_; }
  State state() const { return state_; }
  bool  isChecking() const { return enabled_ && state_ == State::Checking; }
  bool  isMoving() const { return enabled_ && state_ == State::Moving; }

  // The reporting set in force: MOVING while checking or moving, STANDBY
  // otherwise - and always STANDBY while motion wake is off. The reference points
  // into `settings`, so re-ask after `settings` is reassigned.
  const ModeSettings& activeMode(const DeviceSettings& settings) const;

  // An acquire has just returned. Moves the state machine on, and returns
  // whether this fix should be published. Never true without a fix, so the
  // caller can use it alone as its "publish" condition.
  bool onFix(const DeviceSettings& settings, bool haveFix, double speedKmph);

  // The accelerometer saw activity while the device was awake in STANDBY: start
  // a check. Ignored in any other state.
  void onActivity(const DeviceSettings& settings);

  // Close a window whose deadline has passed. Returns true when this call
  // dropped the device back to STANDBY - the caller then re-reads activeMode().
  bool evaluate();

  // Milliseconds until the current window closes (0 once it has), or -1 when
  // there is no window - STANDBY, or motion wake off.
  int64_t msUntilDeadline() const;

  // After a check ends in STANDBY, the standby interval is measured from the
  // moment the check BEGAN - i.e. from the wake - so a parked device keeps a
  // steady cadence however long each check took. One-shot: true (and `anchorUs`
  // overwritten, in esp_timer microseconds) only on the first call after such an
  // ending.
  bool takeCheckAnchor(int64_t& anchorUs);

  // THRESH_ACT for the deep sleep about to happen: the configured step while in
  // STANDBY, 0 ("timer only") otherwise. MOVING sleeps on the timer alone,
  // because a moving car would trip the accelerometer the moment it was armed.
  uint8_t sleepWakeSteps(const DeviceSettings& settings) const;

  // Record in RTC memory what the next TIMER wake needs to resume: the time left
  // on a MOVING deadline, minus the `sleepMs` about to be slept. Call right
  // before any timed deep sleep.
  void prepareForSleep(uint32_t sleepMs);

  // "STANDBY" / "CHECKING" / "MOVING", for logs.
  static const char* stateName(State state);

 private:
  // Enter CHECKING for wake_wait_s. `reason` is for the log line, `change` is
  // what gets reported.
  void startChecking(const DeviceSettings& settings, const char* reason,
                     MotionChange change);

  // Hand `change` to the handler, if there is one.
  void report(MotionChange change);

  // Why this boot happened, phrased for the motion log.
  static const char* wakeReason(esp_sleep_wakeup_cause_t cause);

  ChangeHandler onChange_;

  bool    enabled_;
  State   state_;
  int64_t deadlineUs_;          // esp_timer clock; meaningful outside STANDBY
  int64_t checkStartedUs_;      // when the current or last check began
  bool    checkPublished_;      // this check's one report has gone out
  bool    checkAnchorPending_;  // a check just ended in STANDBY; see above
};
