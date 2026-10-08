#include "settings/SettingsApplier.h"

#include <algorithm>

SettingsApplier::SettingsApplier(FixQueue& queue, RetryQueue& retryQueue)
    : queue_(queue), retryQueue_(retryQueue) {}

void SettingsApplier::apply(const DeviceSettings& settings,
                            const ModeSettings& active) {
  // The two storage limits come from both sets whenever both can be in force -
  // see the banner for why they must never follow a mode switch downwards.
  const ModeSettings& standby   = settings.standby();
  uint32_t            queueMax  = standby.queueMaxFixes();
  uint32_t            maxAgeHrs = standby.retryMaxAgeHours();
  if (settings.motion().enabled()) {
    const ModeSettings& moving = settings.motion().moving();
    queueMax  = std::max(queueMax, moving.queueMaxFixes());
    maxAgeHrs = lenientMaxAgeHours(maxAgeHrs, moving.retryMaxAgeHours());
  }

  // A failed trim is not worth reporting upwards: the cap has still been
  // adopted, and the excess entries will be dropped by the next enqueue. The
  // queue logs the IO error itself.
  queue_.setMaxEntries(static_cast<std::size_t>(queueMax));

  retryQueue_.setRetryIntervalHours(active.retryIntervalHours());
  retryQueue_.setMaxAgeHours(maxAgeHrs);
}

uint32_t SettingsApplier::lenientMaxAgeHours(uint32_t a, uint32_t b) {
  if (a == 0 || b == 0) {
    return 0;  // "never give up" is the most lenient answer there is
  }
  return std::max(a, b);
}
