#include "settings/SettingsApplier.h"

SettingsApplier::SettingsApplier(FixQueue& queue, RetryQueue& retryQueue)
    : queue_(queue), retryQueue_(retryQueue) {}

void SettingsApplier::apply(const DeviceSettings& settings) {
  // A failed trim is not worth reporting upwards: the cap has still been
  // adopted, and the excess entries will be dropped by the next enqueue. The
  // queue logs the IO error itself.
  queue_.setMaxEntries(static_cast<std::size_t>(settings.queueMaxFixes()));

  retryQueue_.setRetryIntervalHours(settings.retryIntervalHours());
  retryQueue_.setMaxAgeHours(settings.retryMaxAgeHours());
}
