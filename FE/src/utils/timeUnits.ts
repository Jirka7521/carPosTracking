// ---------------------------------------------------------------------------
// Converting a duration between the unit it is STORED in and the unit a person
// wants to TYPE it in.
//
// Every duration in this app has exactly one canonical unit on the wire: the
// reporting interval, the GNSS lock timeout and the settings re-check are whole
// seconds; the two retry knobs are whole hours. That is fixed by the API and by
// the firmware, and nothing here changes it.
//
// What a person wants is different. "Report every six hours" is 21600 seconds,
// and nobody should have to do that multiplication — or the division back —
// just to read the form. So DurationField lets them pick a unit, and these
// helpers do the arithmetic. The unit is a display choice held in component
// state: it is never saved, never sent, and never part of the config document.
//
// Everything here is a pure function of its arguments — no state, no I/O — for
// the same reason utils/deviceConfig.ts is: the same conversion has to agree
// with itself in the input, in its min/max, and in its step.
// ---------------------------------------------------------------------------

// The units a duration field may offer. Deliberately no "weeks" or "months":
// neither is a fixed number of seconds anyone agrees on, and the firmware
// clamps everything in seconds.
export type TimeUnit = 'seconds' | 'minutes' | 'hours' | 'days'

// How many seconds one of each unit is. This is the whole conversion table —
// every function below is a multiply or a divide by one of these.
export const UNIT_SECONDS: Record<TimeUnit, number> = {
  seconds: 1,
  minutes: 60,
  hours: 3600,
  days: 86400,
}

// What the combobox shows — as translation keys, not as text, because this
// file stays free of any particular language for the same reason it stays free
// of state: it is arithmetic, and DurationField is what renders.
//
// Plural throughout: the value beside it is usually not 1, and a dropdown that
// flips between "hour" and "hours" as you type draws the eye to the wrong half
// of the control. Czech has no single plural form, so these are the "many"
// form its grammar uses after a bare numeral — see common:units.
//
// `as const` matters: it keeps the values literal types, which is what lets
// t() type-check them against the catalogue.
export const UNIT_LABEL_KEYS = {
  seconds: 'common:units.seconds',
  minutes: 'common:units.minutes',
  hours: 'common:units.hours',
  days: 'common:units.days',
} as const satisfies Record<TimeUnit, string>

// The unit a value reads most naturally in: the largest offered unit it divides
// into evenly. 3600 s is "1 hour", 300 s is "5 minutes", and 90 s stays "90
// seconds" because "1.5 minutes" is not an improvement on it.
//
// `fallback` is what to use when nothing divides evenly — and, importantly, for
// zero. Zero divides by everything, so without the fallback the field for
// "give up after (0 = never)" would open in days, which reads as a duration
// when it is really a sentinel. Callers pass the field's own storage unit.
export function bestUnit(
  seconds: number,
  allowed: readonly TimeUnit[],
  fallback: TimeUnit,
): TimeUnit {
  if (!Number.isFinite(seconds) || seconds <= 0) {
    return fallback
  }

  // Largest first, so the first match is also the tersest rendering.
  const descending: TimeUnit[] = [...allowed].sort(
    (left, right) => UNIT_SECONDS[right] - UNIT_SECONDS[left],
  )

  for (const unit of descending) {
    const size: number = UNIT_SECONDS[unit]
    // `seconds >= size` matters as well as the remainder: 30 s is divisible by
    // nothing bigger than itself, but 0.5 minutes would pass a remainder test
    // on a fractional value and read worse than "30 seconds".
    if (seconds >= size && seconds % size === 0) {
      return unit
    }
  }

  return fallback
}

// Seconds → the number shown in the input. Rounded to six decimals because the
// division is floating point: 15 s in hours is 0.004166666666666667, and the
// input should not offer that many digits to a person editing it.
export function toUnit(seconds: number, unit: TimeUnit): number {
  if (!Number.isFinite(seconds)) {
    return 0
  }
  return Math.round((seconds / UNIT_SECONDS[unit]) * 1e6) / 1e6
}

// The number typed into the input → seconds. Not rounded here: the caller knows
// which unit it has to land on and rounds to a whole one of those.
export function fromUnit(value: number, unit: TimeUnit): number {
  if (!Number.isFinite(value)) {
    return 0
  }
  return value * UNIT_SECONDS[unit]
}
