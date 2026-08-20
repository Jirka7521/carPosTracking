#pragma once

// =============================================================================
//  ScopedLock  -  Hold a FreeRTOS mutex for the length of a scope.
// -----------------------------------------------------------------------------
//  Responsibility (single!): take a mutex on construction and give it back on
//  destruction, so a method with several early returns cannot leak the lock. It
//  is the standard RAII guard, written out here because FreeRTOS ships handles
//  rather than lockable types.
//
//  Used by the classes the accelerometer debug stream made concurrent - the
//  forwarder, both SD queues and the ADXL345 driver - where every public method
//  is a take/give pair around code full of `return false` branches. Doing that
//  by hand is exactly the kind of thing that works until the one error path
//  nobody tested.
//
//  A null handle is tolerated and does nothing. That matters: if a class could
//  not create its mutex at start-up it has already logged the failure, and the
//  honest fallback is to run unsynchronised rather than to deadlock or crash on
//  every call.
//
//  Not recursive - the underlying mutex is created with xSemaphoreCreateMutex(),
//  so a method holding the lock must never call another that takes it. Each user
//  keeps its locking to its public entry points for that reason.
// =============================================================================

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"

class ScopedLock {
 public:
  // Blocks until `mutex` is held (or returns immediately when it is null).
  explicit ScopedLock(SemaphoreHandle_t mutex);
  ~ScopedLock();

  // A lock guard is tied to one scope; copying or moving it would give the
  // mutex back at the wrong time.
  ScopedLock(const ScopedLock&)            = delete;
  ScopedLock& operator=(const ScopedLock&) = delete;

 private:
  SemaphoreHandle_t mutex_;
};
