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
