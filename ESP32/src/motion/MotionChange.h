#pragma once

// =============================================================================
//  MotionChange.h  -  One step of the motion-wake state machine, and why.
// -----------------------------------------------------------------------------
//  A dependency-free value type in the same spirit as OfflineReason.h:
//  MotionTracker says which transition it has just made, StatusPublisher turns
//  that into the wire word, and the API decides what it means for the history.
//  The target state is implied by the change, so the change alone is the event.
//
//      Checking   -> CHECKING: the boot began a check (any wake, a power-on)
//      Activity   -> CHECKING: the accelerometer tripped while awake in standby
//      MotionOn   -> CHECKING: a config switched motion wake on
//      Moving     -> MOVING:   a fix faster than speed_kmph
//      NoMotion   CHECKING -> STANDBY: the wake window passed with no fast fix
//      Stopped    MOVING   -> STANDBY: stationary for the whole stop window
//      MotionOff  -> STANDBY: a config switched motion wake off
//
//  MOVING resumed after a timed sleep is deliberately absent: the car was moving
//  before the sleep and still is, so nothing changed - the wake is its own event.
// =============================================================================

enum class MotionChange {
  Checking,
  Activity,
  MotionOn,
  Moving,
  NoMotion,
  Stopped,
  MotionOff,
};
