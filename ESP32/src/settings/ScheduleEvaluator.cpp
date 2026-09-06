#include "settings/ScheduleEvaluator.h"

#include <algorithm>
#include <vector>

#include "util/CivilTime.h"

bool ScheduleEvaluator::evaluate(const ScheduleBundle& bundle,
                                 int64_t epochSeconds, Result& out) {
  if (!bundle.valid() || !bundle.enabled()) {
    return false;
  }

  const int nowMinute = minuteOfWeek(epochSeconds);

  const ScheduleBundle::Rule* activeRule = winnerAt(bundle, nowMinute);
  const int                   activeSlot = profileOf(bundle, activeRule);

  if (activeSlot == ScheduleBundle::kNoSlot ||
      bundle.findProfile(activeSlot) == nullptr) {
    // No window covers now and there is no fallback, or the fallback names a
    // profile this bundle does not carry. Either way there is no answer, and
    // guessing one would be worse than deferring to the config document.
    return false;
  }

  Result result;
  result.slot = activeSlot;

  // Every minute at which some window opens or closes. At most 2 x 7 x 32 of
  // them, and usually a handful; collected once and reused by the walk below so
  // "the next boundary after m" is a scan from the right place rather than a
  // re-sort per step.
  std::vector<int> boundaries;
  boundaries.reserve(bundle.rules().size() * 14);
  for (const ScheduleBundle::Rule& rule : bundle.rules()) {
    for (int day = 0; day < 7; day++) {
      if ((rule.daysMask & (1 << day)) == 0) {
        continue;
      }
      const int windowStart = (day * kMinutesPerDay) + rule.startMinute;
      boundaries.push_back(modulo(windowStart, kMinutesPerWeek));
      boundaries.push_back(
          modulo(windowStart + rule.durationMinutes, kMinutesPerWeek));
    }
  }

  std::sort(boundaries.begin(), boundaries.end());
  boundaries.erase(std::unique(boundaries.begin(), boundaries.end()),
                   boundaries.end());

  if (!boundaries.empty()) {
    // Index of the first boundary strictly after now, wrapping into next week
    // when there is none later in this one.
    std::size_t firstAfter = 0;
    while (firstAfter < boundaries.size() &&
           boundaries[firstAfter] <= nowMinute) {
      firstAfter++;
    }

    // The first boundary at which the winner is a DIFFERENT PROFILE. Comparing
    // profiles rather than rules is deliberate and matches the server: two rules
    // naming the same profile back to back are not a change the device would
    // notice, and waking for one would be a wake for nothing.
    for (std::size_t step = 0; step < boundaries.size(); step++) {
      const int candidate = boundaries[(firstAfter + step) % boundaries.size()];
      if (profileOf(bundle, winnerAt(bundle, candidate)) == activeSlot) {
        continue;
      }

      int deltaMinutes = candidate - nowMinute;
      if (deltaMinutes <= 0) {
        // The boundary is earlier in the week's numbering than we are, so its
        // next occurrence is in the week ahead.
        deltaMinutes += kMinutesPerWeek;
      }

      // Truncated to the minute the same way the server truncates, so the two
      // agree on when the switch is due rather than differing by the seconds
      // that happen to be on this clock.
      const int64_t nowAtMinute =
          epochSeconds - (epochSeconds % CivilTime::kSecondsPerMinute);
      result.nextChangeEpoch =
          nowAtMinute +
          static_cast<int64_t>(deltaMinutes) * CivilTime::kSecondsPerMinute;
      result.hasNextChange = true;
      break;
    }
  }

  out = result;
  return true;
}

int ScheduleEvaluator::minuteOfWeek(int64_t epochSeconds) {
  const int weekday = CivilTime::weekdayFromEpoch(epochSeconds);  // 0 = Sunday

  // Minute of the day, floored so a pre-epoch instant still lands inside its own
  // day rather than one minute into the next.
  int64_t secondsIntoDay = epochSeconds % CivilTime::kSecondsPerDay;
  if (secondsIntoDay < 0) {
    secondsIntoDay += CivilTime::kSecondsPerDay;
  }

  const int minuteOfDay =
      static_cast<int>(secondsIntoDay / CivilTime::kSecondsPerMinute);
  return (weekday * kMinutesPerDay) + minuteOfDay;
}

const ScheduleBundle::Rule* ScheduleEvaluator::winnerAt(
    const ScheduleBundle& bundle, int minute) {
  const ScheduleBundle::Rule* best = nullptr;

  for (const ScheduleBundle::Rule& rule : bundle.rules()) {
    if (!covers(rule, minute)) {
      continue;
    }
    if (best == nullptr || beats(rule, *best)) {
      best = &rule;
    }
  }

  return best;
}

bool ScheduleEvaluator::covers(const ScheduleBundle::Rule& rule, int minute) {
  for (int day = 0; day < 7; day++) {
    if ((rule.daysMask & (1 << day)) == 0) {
      continue;
    }

    const int windowStart = (day * kMinutesPerDay) + rule.startMinute;

    // Modulo the week, so a Saturday-evening window that runs into Sunday
    // morning is one window rather than two - including across minute 0, which
    // is the case a naive "start <= m && m < end" comparison gets wrong.
    const int offset = modulo(minute - windowStart, kMinutesPerWeek);
    if (offset < static_cast<int>(rule.durationMinutes)) {
      return true;
    }
  }

  return false;
}

bool ScheduleEvaluator::beats(const ScheduleBundle::Rule& candidate,
                              const ScheduleBundle::Rule& incumbent) {
  if (candidate.priority != incumbent.priority) {
    return candidate.priority < incumbent.priority;
  }

  // Older wins. The server ranked the rules by creation time (falling back to
  // their ids) before sending them, so this one comparison stands in for the
  // timestamp-then-guid tie-break in ScheduleEvaluator.Beats.
  return candidate.ordinal < incumbent.ordinal;
}

int ScheduleEvaluator::profileOf(const ScheduleBundle&       bundle,
                                 const ScheduleBundle::Rule* rule) {
  return rule != nullptr ? rule->profileSlot : bundle.fallbackSlot();
}

int ScheduleEvaluator::modulo(int value, int modulus) {
  const int remainder = value % modulus;
  return remainder < 0 ? remainder + modulus : remainder;
}
