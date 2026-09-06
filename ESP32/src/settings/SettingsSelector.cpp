#include "settings/SettingsSelector.h"

#include "config/Config.h"
#include "esp_log.h"
#include "settings/ScheduleEvaluator.h"

static const char* TAG = "SettingsSelector";

SettingsSelector::SettingsSelector(RemoteSettings& settings,
                                   RemoteSchedule& schedule, DeviceClock& clock,
                                   UpdateSignal& signal)
    : settings_(settings),
      schedule_(schedule),
      clock_(clock),
      signal_(signal),
      activeSlot_(ScheduleBundle::kNoSlot),
      scheduleVersion_(0),
      nextChangeEpoch_(0),
      hasNextChange_(false) {}

const DeviceSettings& SettingsSelector::resolve() {
  // Both are cheap no-ops when nothing is pending. Polled unconditionally rather
  // than only when the signal fired, because a retained replay can land while
  // this task is busy elsewhere and the bits would already have been consumed.
  settings_.poll();
  schedule_.poll();

  reselect();
  return current_;
}

bool SettingsSelector::reselect() {
  const DeviceSettings previous          = current_;
  const int            previousSlot      = activeSlot_;
  const bool           previousHasChange = hasNextChange_;
  const int64_t        previousChange    = nextChangeEpoch_;

  // Rule 3, the fallthrough, applied first so every early return below lands on
  // something usable. This is the behaviour the firmware had before schedules
  // existed, and every failure path returns to it.
  DeviceSettings resolved = settings_.current();

  activeSlot_      = ScheduleBundle::kNoSlot;
  scheduleVersion_ = 0;
  hasNextChange_   = false;
  nextChangeEpoch_ = 0;

  const ScheduleBundle& bundle = schedule_.current();

  int64_t nowEpoch = 0;
  const bool haveClock = clock_.isTrusted() && clock_.nowUtc(nowEpoch);

  if (config::kScheduleEnabled && bundle.valid() && haveClock) {
    scheduleVersion_ = bundle.version();

    // Rule 2 first, because rule 1 needs its answer: activeSlot_ reports where
    // the schedule puts the device even while an override supplies the values.
    ScheduleEvaluator::Result evaluation;
    if (ScheduleEvaluator::evaluate(bundle, nowEpoch, evaluation)) {
      const ScheduleBundle::Profile* profile =
          bundle.findProfile(evaluation.slot);
      if (profile != nullptr) {
        activeSlot_      = evaluation.slot;
        hasNextChange_   = evaluation.hasNextChange;
        nextChangeEpoch_ = evaluation.nextChangeEpoch;

        resolved = profile->values;
      }
    }

    // Rule 1. Checked after the evaluation so the slot above is still the
    // schedule's, and applied last so it wins.
    if (bundle.hasOverride() && nowEpoch < bundle.overrideUntilEpoch()) {
      resolved = bundle.overrideValues();

      // The override lapsing is itself a change worth waking for: at that
      // instant the schedule takes back over, and the device must re-resolve
      // rather than carry the override values into the next stretch.
      if (!hasNextChange_ || bundle.overrideUntilEpoch() < nextChangeEpoch_) {
        nextChangeEpoch_ = bundle.overrideUntilEpoch();
        hasNextChange_   = true;
      }
    }
  }

  // Whichever branch supplied the values, the revision stays the config
  // document's - see the note in the header.
  resolved.setVersion(settings_.current().version());
  resolved.clampToLimits();

  const bool changed = resolved != current_ ||
                       resolved.version() != previous.version() ||
                       activeSlot_ != previousSlot ||
                       hasNextChange_ != previousHasChange ||
                       nextChangeEpoch_ != previousChange;

  current_ = resolved;

  if (changed && activeSlot_ != previousSlot) {
    const ScheduleBundle::Profile* profile = bundle.findProfile(activeSlot_);
    ESP_LOGI(TAG, "schedule now selects slot %d (%s), interval=%us sleep=%s",
             activeSlot_,
             profile != nullptr && !profile->name.empty()
                 ? profile->name.c_str()
                 : "unnamed",
             (unsigned)current_.intervalSeconds(),
             current_.sleepBetweenSends() ? "yes" : "no");
  }

  return changed;
}

bool SettingsSelector::secondsUntilNextChange(int64_t& secondsOut) const {
  if (!hasNextChange_) {
    return false;
  }

  int64_t nowEpoch = 0;
  if (!clock_.isTrusted() || !clock_.nowUtc(nowEpoch)) {
    return false;
  }

  const int64_t remaining = nextChangeEpoch_ - nowEpoch;
  if (remaining <= 0) {
    // The boundary is already behind us - the caller polled a moment too late,
    // or the clock jumped forward on a fresh seed. Report one second so the
    // caller takes a short wait and re-resolves, rather than treating a negative
    // remainder as "no change due".
    secondsOut = 1;
    return true;
  }

  secondsOut = remaining;
  return true;
}

bool SettingsSelector::waitForChange(uint32_t timeoutMs) {
  // Something may already be pending from before this call - it arrived during
  // the acquire, say. Take it without blocking at all.
  if (signal_.takePending(UpdateSignal::kAnyArrived) != 0) {
    settings_.poll();
    schedule_.poll();
    if (reselect()) {
      return true;
    }
  }

  if (signal_.wait(UpdateSignal::kAnyArrived, timeoutMs) == 0) {
    return false;  // timed out with nothing delivered
  }

  // Both are polled regardless of which bit fired: the other document may have
  // landed in the microseconds between the wait returning and this line, and
  // polling it costs a mutex take.
  settings_.poll();
  schedule_.poll();
  return reselect();
}

void SettingsSelector::resyncIfDue(uint32_t intervalSeconds) {
  settings_.resyncIfDue(intervalSeconds);
  schedule_.resyncIfDue(intervalSeconds);
}
