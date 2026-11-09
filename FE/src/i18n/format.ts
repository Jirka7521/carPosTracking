// ============================================================
// The one place Intl is configured. Everything user-visible that is a number
// or a date goes through here, so the app never mixes conventions.
//
// Why this file exists at all: `toFixed()` always emits a "." decimal
// separator, while `toLocaleString()` groups thousands the reader's way. Used
// side by side — which is what the code did before — a Czech reader saw
// "1 234" and "12.3" in the same table row. Every DISPLAY site now uses these
// helpers instead.
//
// Two things deliberately do NOT come through here:
//   • <input type="datetime-local"> values, whose format is fixed by the HTML
//     spec — see formatDateTimeLocal() in utils/dates.ts.
//   • The CSV export, which writes ISO-8601 UTC and "." decimals on purpose
//     because it is read back by a machine — see utils/csv.ts.
// ============================================================

import i18n from 'i18next'

// The BCP 47 tag Intl should use. resolvedLanguage is the one i18next actually
// settled on after the fallback chain, which is what the reader is seeing.
function activeLocale(): string {
  return i18n.resolvedLanguage ?? i18n.language ?? 'en'
}

// Intl formatters are expensive to construct and the position table builds
// thousands of cells per render, so they are made once per (locale, shape).
const cache = new Map<string, Intl.NumberFormat | Intl.DateTimeFormat>()

function numberFormat(options: Intl.NumberFormatOptions): Intl.NumberFormat {
  const locale: string = activeLocale()
  const key: string = `n:${locale}:${JSON.stringify(options)}`

  let formatter = cache.get(key) as Intl.NumberFormat | undefined
  if (formatter === undefined) {
    formatter = new Intl.NumberFormat(locale, options)
    cache.set(key, formatter)
  }
  return formatter
}

function dateFormat(options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const locale: string = activeLocale()
  const key: string = `d:${locale}:${JSON.stringify(options)}`

  let formatter = cache.get(key) as Intl.DateTimeFormat | undefined
  if (formatter === undefined) {
    formatter = new Intl.DateTimeFormat(locale, options)
    cache.set(key, formatter)
  }
  return formatter
}

// Switching language must not leave the old locale's formatters in play.
i18n.on('languageChanged', () => {
  cache.clear()
})

// ---------------------------------------------------------------------------
// Numbers
// ---------------------------------------------------------------------------

// A measurement shown to a fixed number of decimals — the locale-aware
// replacement for `value.toFixed(decimals)`.
export function formatNumber(value: number, decimals: number): string {
  return numberFormat({
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value)
}

// A count, grouped the reader's way — the replacement for
// `value.toLocaleString()`.
export function formatInteger(value: number): string {
  return numberFormat({ maximumFractionDigits: 0 }).format(value)
}

// A coordinate. Six decimals is roughly 10 cm, which is far finer than the
// receiver, and is what the position table has always shown.
export function formatCoordinate(value: number): string {
  return formatNumber(value, 6)
}

// A unit Intl knows by name ("kilometer", "minute" …), in the locale's own short
// spelling — "km", "min", "km/h", with "h" or "hr" as the language writes it. The
// unit words come from the browser, not the catalogue, so they are never out of
// step with the digits beside them.
function formatUnit(value: number, unit: string, decimals: number): string {
  return numberFormat({
    style: 'unit',
    unit,
    unitDisplay: 'short',
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value)
}

// A trip's or a journey's distance. Metres below one kilometre — a short hop
// shown as "0.4 km" reads as a rounding error — one decimal of a kilometre up to
// a hundred, and whole kilometres beyond, where a decimal is noise.
export function formatDistance(meters: number): string {
  if (meters < 1000) {
    return formatUnit(Math.round(meters), 'meter', 0)
  }
  const kilometers: number = meters / 1000
  return formatUnit(kilometers, 'kilometer', kilometers < 100 ? 1 : 0)
}

// How long something took, to the minute: "45 s" under a minute, "18 min", and
// "1 h 12 min" from an hour up. Hours are not rolled into days — a journey's
// "41 h 10 min" of driving is a quantity of hours, not a span of the calendar.
export function formatDuration(seconds: number): string {
  if (seconds < 60) {
    return formatUnit(Math.max(0, Math.round(seconds)), 'second', 0)
  }
  const totalMinutes: number = Math.round(seconds / 60)
  const hours: number = Math.floor(totalMinutes / 60)
  const minutes: number = totalMinutes % 60
  if (hours === 0) {
    return formatUnit(minutes, 'minute', 0)
  }
  if (minutes === 0) {
    return formatUnit(hours, 'hour', 0)
  }
  return `${formatUnit(hours, 'hour', 0)} ${formatUnit(minutes, 'minute', 0)}`
}

// An altitude or a climb, in whole metres — never switched to kilometres the way
// a distance is, because "1.2 km" of altitude reads as a distance.
export function formatAltitude(meters: number): string {
  return formatUnit(Math.round(meters), 'meter', 0)
}

// A speed, in whole kilometres per hour.
export function formatSpeed(kmph: number): string {
  return formatUnit(Math.round(kmph), 'kilometer-per-hour', 0)
}

// ---------------------------------------------------------------------------
// Dates and times
// ---------------------------------------------------------------------------

// Date and time together, 24-hour. This is the recipe that used to be written
// out three separate times (the position table, the map info window and the
// chart tooltip) and drifting apart was only a matter of when.
export function formatDateTime(value: Date): string {
  return dateFormat({
    dateStyle: 'short',
    timeStyle: 'medium',
    hour12: false,
  }).format(value)
}

// Just the date — for "member since" style lines.
export function formatDate(value: Date): string {
  return dateFormat({ dateStyle: 'medium' }).format(value)
}

// Wall-clock time, no seconds. Chart axes and the schedule timeline.
export function formatTime(value: Date): string {
  return dateFormat({
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(value)
}

// Day and month, for a chart axis that spans more than a day.
export function formatDayMonth(value: Date): string {
  return dateFormat({ day: '2-digit', month: '2-digit' }).format(value)
}

// A calendar day with its weekday — "Sat, 8 Nov 2026" — for the Trips tab's day
// headers, where "which day of the week was that" is half the question.
export function formatWeekdayDate(value: Date): string {
  return dateFormat({ weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' }).format(value)
}
