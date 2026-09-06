#pragma once

// =============================================================================
//  UpdateSignal  -  One wake-up shared by everything that can change the
//                   settings in force.
// -----------------------------------------------------------------------------
//  Responsibility (single!): let the application task sleep until *something*
//  worth re-resolving has arrived, and say which. It is a FreeRTOS event group
//  with named bits and a deadline-based wait, and nothing more.
//
//  Why it exists. The application task spends nearly all its life blocked,
//  waiting out the reporting interval. Blocking on an event group instead of on
//  a plain delay costs exactly the same energy - a blocked task is a blocked
//  task - but it also wakes the instant a document lands, which is what turns
//  "applied within one reporting interval" into "applied within a second".
//  RemoteSettings has worked that way since remote config existed.
//
//  The catch: a task can only wait on ONE event group. Once RemoteSchedule
//  arrived there were two sources of change, and a bundle published to a device
//  whose reporting interval is an hour would have sat unnoticed for up to an
//  hour - the schedule would still be right, just late, which is the one thing a
//  schedule must not be. Giving both watchers the same event group, with a bit
//  each, is what lets one wait cover both.
//
//  Owned by main() and borrowed by both watchers, following this codebase's rule
//  that classes borrow their collaborators rather than owning them.
// =============================================================================

#include <cstdint>

#include "freertos/FreeRTOS.h"
#include "freertos/event_groups.h"

class UpdateSignal {
 public:
  // A retained configuration document is waiting in RemoteSettings.
  static constexpr EventBits_t kConfigArrived = BIT0;

  // A retained schedule bundle is waiting in RemoteSchedule.
  static constexpr EventBits_t kScheduleArrived = BIT1;

  // Either of the above - what the interval wait blocks on.
  static constexpr EventBits_t kAnyArrived = kConfigArrived | kScheduleArrived;

  UpdateSignal();
  ~UpdateSignal();

  // A lock guard is tied to one owner; copying would double-free the group.
  UpdateSignal(const UpdateSignal&)            = delete;
  UpdateSignal& operator=(const UpdateSignal&) = delete;

  // False when the event group could not be created. The callers treat that as
  // "no interruptible waiting", not as a fatal error: settings still arrive and
  // are still applied, just at the next poll rather than immediately.
  bool valid() const { return events_ != nullptr; }

  // Raise one or more bits. Safe to call from an ISR-free task context - in
  // practice the esp-mqtt event task, which is why the callers do this last,
  // after releasing their own mutex, so the woken task never immediately blocks
  // on a lock they still hold.
  void raise(EventBits_t bits);

  // Block for up to `timeoutMs` until any of `bits` is set, then clear those
  // bits and return which of them fired. Returns 0 on timeout.
  //
  // The bits are cleared on exit so the next wait starts clean, and the caller
  // is told which fired so it can poll only the watcher that has something.
  EventBits_t wait(EventBits_t bits, uint32_t timeoutMs);

  // Take whichever of `bits` are already set, without blocking at all. Used
  // before a wait, for a document that arrived while the caller was busy
  // elsewhere.
  EventBits_t takePending(EventBits_t bits);

 private:
  EventGroupHandle_t events_;
};
