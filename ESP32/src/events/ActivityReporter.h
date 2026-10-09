#pragma once

// =============================================================================
//  ActivityReporter  -  Decide when the device records a wake or a motion step.
// -----------------------------------------------------------------------------
//  Responsibility (single!): turn the moments the device's state changes into
//  status events - the wake that started this boot, and every step of the
//  motion-wake state machine. StatusPublisher formats and seals them, EventLog
//  delivers them (now, or later from the card); this class only knows WHEN.
//  It is PresenceReporter's sibling: that one covers the connection ("online")
//  and the goodbye before a sleep, this one everything in between.
//
//  Together they give the dashboard the device's whole day, for a parked car
//  with motion wake on typically:
//
//      wake (timer) -> motion (checking) -> motion (no_motion)
//                   -> offline (sleep_no_motion)
//
//  and for a trip:
//
//      wake (accelerometer) -> motion (checking) -> motion (moving)
//                           -> ... -> motion (stopped) -> offline (...)
//
//  Every call runs on the MAIN task - MotionTracker reports from there, and
//  sealing must stay there (PayloadCrypto's generator is not thread-safe).
//  Best effort: an event that cannot be sealed is logged and dropped.
// =============================================================================

#include "events/EventLog.h"
#include "events/WakeCause.h"
#include "motion/MotionChange.h"
#include "mqtt/StatusPublisher.h"

class ActivityReporter {
 public:
  // Borrows both collaborators (they must outlive this object).
  ActivityReporter(StatusPublisher& publisher, EventLog& log);

  // Record how this boot began, if it was a wake from deep sleep. A power-on, a
  // reset or a crash records nothing here - that is the online message's reset
  // reason. Call once per boot.
  void recordWake();

  // Record one step of the motion-wake state machine. Wired to
  // MotionTracker::setChangeHandler().
  void recordMotion(MotionChange change);

 private:
  StatusPublisher& publisher_;
  EventLog&        log_;
};
