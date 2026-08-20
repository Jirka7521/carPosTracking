#include "util/ScopedLock.h"

ScopedLock::ScopedLock(SemaphoreHandle_t mutex) : mutex_(mutex) {
  if (mutex_ != nullptr) {
    // portMAX_DELAY: these locks guard short critical sections plus, in the
    // forwarder's case, one publish. Waiting is always the right answer - the
    // alternative, skipping the work because the lock was busy, would silently
    // drop a fix.
    xSemaphoreTake(mutex_, portMAX_DELAY);
  }
}

ScopedLock::~ScopedLock() {
  if (mutex_ != nullptr) {
    xSemaphoreGive(mutex_);
  }
}
