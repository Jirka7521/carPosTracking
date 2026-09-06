// ---------------------------------------------------------------------------
// Pure helpers for the settings-schedule panel.
//
// THIS FILE OWNS THE ONLY TIMEZONE CONVERSION IN THE SYSTEM. The API stores and
// evaluates weekly windows entirely in UTC minutes and never converts one; the
// browser is the only party that knows the reader's offset, so the browser does
// it — in both directions, for display and for submission.
//
// The consequence, accepted knowingly: a window entered as 22:00 in winter is
// stored as 21:00Z, and after the spring DST change the browser renders it —
// correctly — as 23:00 local. The stored instant did not move; the local clock
// did. Every rule is therefore shown with BOTH times, so the drift is visible
// rather than mysterious, and re-entering the time is the fix.
//
// A second, smaller caveat lives in localWindowToUtc: the day-shift is computed
// from one reference date, so a rule saved in the same week as a DST change may
// be off by an hour for the days on the far side of it. Same fix, same visibility.
//
// The conversion below is a pure function of its arguments — no API calls, no
// state, no reads of the clock except through an injectable `reference`. The
// describe* helpers at the bottom are presentation rather than arithmetic, and
// those do read the active language from the i18next singleton; see the note at
// the top of utils/dates.ts for why it is not passed in as a parameter.
// ---------------------------------------------------------------------------

import i18n from '../i18n'
import { formatInteger } from '../i18n/format'
import type { DeviceScheduleRuleDto } from '../services/apiTypes'

// Weekday names come from the catalogue, not from this file. Index 0 is Sunday,
// matching both JavaScript's Date.getDay() and .NET's DayOfWeek — which is
// exactly why the API chose that numbering.
//
// The short form is its own set of strings rather than the long one truncated:
// slicing three characters off a weekday is an English-only habit, and Czech
// abbreviates "pondělí" to "po", not to "pon".
const DAY_SHORT_KEYS = [
  'common:weekday.short.sun',
  'common:weekday.short.mon',
  'common:weekday.short.tue',
  'common:weekday.short.wed',
  'common:weekday.short.thu',
  'common:weekday.short.fri',
  'common:weekday.short.sat',
] as const

const DAY_LONG_KEYS = [
  'common:weekday.long.sun',
  'common:weekday.long.mon',
  'common:weekday.long.tue',
  'common:weekday.long.wed',
  'common:weekday.long.thu',
  'common:weekday.long.fri',
  'common:weekday.long.sat',
] as const

export function dayLabelShort(day: number): string {
  return i18n.t(DAY_SHORT_KEYS[day] ?? DAY_SHORT_KEYS[0])
}

export function dayLabelLong(day: number): string {
  return i18n.t(DAY_LONG_KEYS[day] ?? DAY_LONG_KEYS[0])
}

export const MINUTES_PER_DAY = 1440
export const MINUTES_PER_WEEK = MINUTES_PER_DAY * 7

// Masks worth naming, because they are what people actually mean.
export const MASK_EVERY_DAY = 0b1111111
export const MASK_WEEKDAYS = 0b0111110  // Mon–Fri
export const MASK_WEEKEND = 0b1000001   // Sat & Sun

// A window as the reader enters it: local weekdays and a local clock time.
export type LocalWindow = {
  daysMaskLocal: number
  startMinuteLocal: number
}

// The same window as the API stores it.
export type UtcWindow = {
  daysMaskUtc: number
  startMinuteUtc: number
}

// ---------------------------------------------------------------------------
// Conversion
// ---------------------------------------------------------------------------

// Local weekdays + local start time -> the UTC pair the API takes.
//
// The day mask has to rotate, not just the clock: at UTC+2, a window starting at
// 00:30 on Monday local begins at 22:30 on SUNDAY in UTC. Forgetting this is the
// bug that would make a Monday-morning rule fire on Sunday nights, so the shift
// is derived from a real Date rather than from arithmetic on the offset.
//
// `reference` fixes which date's offset is used — today, unless a caller needs
// to be deterministic. Only one date is consulted, which is the caveat in the
// header note.
export function localWindowToUtc(local: LocalWindow, reference: Date = new Date()): UtcWindow {
  const probe = new Date(reference.getTime())
  probe.setHours(Math.floor(local.startMinuteLocal / 60), local.startMinuteLocal % 60, 0, 0)

  const dayShift: number = modulo(probe.getUTCDay() - probe.getDay(), 7)

  return {
    daysMaskUtc: rotateMask(local.daysMaskLocal, dayShift),
    startMinuteUtc: probe.getUTCHours() * 60 + probe.getUTCMinutes(),
  }
}

// The reverse: what the API stored -> what to put in the form.
export function utcWindowToLocal(utc: UtcWindow, reference: Date = new Date()): LocalWindow {
  const probe = new Date(Date.UTC(
    reference.getUTCFullYear(),
    reference.getUTCMonth(),
    reference.getUTCDate(),
    Math.floor(utc.startMinuteUtc / 60),
    utc.startMinuteUtc % 60,
    0,
    0,
  ))

  const dayShift: number = modulo(probe.getDay() - probe.getUTCDay(), 7)

  return {
    daysMaskLocal: rotateMask(utc.daysMaskUtc, dayShift),
    startMinuteLocal: probe.getHours() * 60 + probe.getMinutes(),
  }
}

// Moves every set bit `shift` days later, wrapping around the week.
function rotateMask(mask: number, shift: number): number {
  let rotated = 0
  for (let day = 0; day < 7; day++) {
    if (mask & (1 << day)) {
      rotated |= 1 << ((day + shift) % 7)
    }
  }
  return rotated
}

// JavaScript's % keeps the sign of the dividend; every wrap here needs the
// non-negative answer instead.
function modulo(value: number, divisor: number): number {
  return ((value % divisor) + divisor) % divisor
}

// ---------------------------------------------------------------------------
// Formatting
// ---------------------------------------------------------------------------

// Minutes past midnight as "HH:MM", for both an <input type="time"> value and a
// label. One function for both so a rule can never display one and submit another.
export function formatMinuteOfDay(minute: number): string {
  const safe: number = modulo(Math.round(minute), MINUTES_PER_DAY)
  const hours: string = String(Math.floor(safe / 60)).padStart(2, '0')
  const minutes: string = String(safe % 60).padStart(2, '0')
  return `${hours}:${minutes}`
}

// "HH:MM" from an <input type="time"> back to minutes, or null if unparseable.
export function parseMinuteOfDay(value: string): number | null {
  const match = /^(\d{1,2}):(\d{2})$/.exec(value.trim())
  if (match === null) {
    return null
  }
  const hours = Number(match[1])
  const minutes = Number(match[2])
  if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59) {
    return null
  }
  return hours * 60 + minutes
}

// The end of a window as a clock time. It may be "earlier" than the start, which
// is the honest rendering of a window that runs past midnight.
export function endMinuteOfDay(startMinute: number, durationMinutes: number): number {
  return modulo(startMinute + durationMinutes, MINUTES_PER_DAY)
}

// Whether a window runs into the following day, so the UI can say "+1 day"
// instead of showing 22:00 → 06:00 and leaving the reader to work it out.
export function crossesMidnight(startMinute: number, durationMinutes: number): boolean {
  return startMinute + durationMinutes > MINUTES_PER_DAY
}

// A day mask as the phrase a person would use.
export function describeDaysMask(mask: number): string {
  if (mask === MASK_EVERY_DAY) {
    return i18n.t('schedule:days.everyDay')
  }
  if (mask === MASK_WEEKDAYS) {
    return i18n.t('schedule:days.weekdays')
  }
  if (mask === MASK_WEEKEND) {
    return i18n.t('schedule:days.weekend')
  }

  const days: string[] = []
  for (let day = 0; day < 7; day++) {
    if (mask & (1 << day)) {
      days.push(dayLabelShort(day))
    }
  }
  return days.length === 0 ? i18n.t('schedule:days.never') : days.join(', ')
}

// A window length in the units a person would say it in.
export function describeDuration(minutes: number): string {
  if (minutes % MINUTES_PER_DAY === 0) {
    const days: number = minutes / MINUTES_PER_DAY
    return i18n.t('common:duration.days', { count: days, value: formatInteger(days) })
  }
  if (minutes % 60 === 0) {
    return i18n.t('common:units.abbrevHours', { value: minutes / 60 })
  }
  if (minutes < 60) {
    return i18n.t('common:units.abbrevMinutes', { value: minutes })
  }
  return i18n.t('common:units.abbrevHoursMinutes', {
    hours: Math.floor(minutes / 60),
    minutes: minutes % 60,
  })
}

// "in 3 h 12 m" for the next switch, or "now" once the moment has arrived.
//
// Deliberately coarse: the boundary itself is minute-granular and the worker
// runs every thirty seconds, so a ticking seconds countdown would be promising a
// precision that does not exist.
export function describeTimeUntil(target: Date, now: Date = new Date()): string {
  const totalMinutes: number = Math.round((target.getTime() - now.getTime()) / 60000)
  if (totalMinutes <= 0) {
    return i18n.t('schedule:until.now')
  }
  if (totalMinutes < 60) {
    return i18n.t('schedule:until.minutes', { minutes: totalMinutes })
  }

  const hours: number = Math.floor(totalMinutes / 60)
  const minutes: number = totalMinutes % 60
  if (hours < 24) {
    return minutes === 0
      ? i18n.t('schedule:until.hours', { hours })
      : i18n.t('schedule:until.hoursMinutes', { hours, minutes })
  }

  const days: number = Math.floor(hours / 24)
  const remainingHours: number = hours % 24
  return remainingHours === 0
    ? i18n.t('schedule:until.days', { count: days, value: formatInteger(days) })
    : i18n.t('schedule:until.daysHours', { days, hours: remainingHours })
}

// A UTC instant as the reader's local weekday and clock time — "Thu 06:00".
export function formatLocalDayTime(value: Date): string {
  const time: string = formatMinuteOfDay(value.getHours() * 60 + value.getMinutes())
  return `${dayLabelShort(value.getDay())} ${time}`
}

// ---------------------------------------------------------------------------
// Week timeline
//
// Expands the rules into the blocks the strip draws. This deliberately mirrors
// the server's evaluator rather than approximating it: same half-open windows,
// same wrap, same precedence — because a timeline that disagreed with what the
// device is doing would be worse than no timeline at all.
//
// The one simplification it can safely make is the tie-break. The API returns
// rules ALREADY in evaluation order (priority, then age), so "first match in the
// array wins" is exactly the server's rule, without the DTO having to carry a
// createdAt nothing else would use.
// ---------------------------------------------------------------------------

export type TimelineSegment = {
  startMs: number
  endMs: number
  profileId: string | null
  profileName: string | null
  // Null when the fallback filled the gap rather than a rule.
  ruleId: string | null
}

// Local Sunday 00:00 of the week containing `reference`.
export function localWeekStart(reference: Date = new Date()): Date {
  const start = new Date(reference.getTime())
  start.setHours(0, 0, 0, 0)
  start.setDate(start.getDate() - start.getDay())
  return start
}

export function buildWeekTimeline(
  rules: readonly DeviceScheduleRuleDto[],
  fallbackProfileId: string | null,
  fallbackProfileName: string | null,
  weekStart: Date,
): TimelineSegment[] {
  const weekStartMs: number = weekStart.getTime()
  const weekEndMs: number = weekStart.getTime() + MINUTES_PER_WEEK * 60_000

  const active: readonly DeviceScheduleRuleDto[] = rules.filter((rule) => rule.isEnabled)
  const occurrences: Occurrence[] = expandOccurrences(active, weekStart)

  // Only instants where some window opens or closes can change the answer, so
  // those plus the two ends of the week are the whole search space.
  const boundaries: number[] = [weekStartMs, weekEndMs]
  for (const occurrence of occurrences) {
    if (occurrence.startMs > weekStartMs && occurrence.startMs < weekEndMs) {
      boundaries.push(occurrence.startMs)
    }
    if (occurrence.endMs > weekStartMs && occurrence.endMs < weekEndMs) {
      boundaries.push(occurrence.endMs)
    }
  }

  const sorted: number[] = Array.from(new Set(boundaries)).sort((a, b) => a - b)
  const segments: TimelineSegment[] = []

  for (let index = 0; index < sorted.length - 1; index++) {
    const startMs: number = sorted[index]
    const endMs: number = sorted[index + 1]
    // Sampled at the midpoint rather than at the start, so a half-open window is
    // resolved without having to replicate its exact endpoint convention here.
    const midpoint: number = startMs + (endMs - startMs) / 2

    const winner: Occurrence | null = occurrences.find(
      (occurrence) => occurrence.startMs <= midpoint && midpoint < occurrence.endMs,
    ) ?? null

    const profileId: string | null = winner?.rule.profileId ?? fallbackProfileId
    const profileName: string | null = winner?.rule.profileName ?? fallbackProfileName

    const previous: TimelineSegment | undefined = segments[segments.length - 1]
    if (previous !== undefined && previous.profileId === profileId) {
      // Two rules naming the same profile back to back are one block, not two —
      // the device would notice nothing at the seam, so neither should the strip.
      previous.endMs = endMs
      continue
    }

    segments.push({
      startMs,
      endMs,
      profileId,
      profileName,
      ruleId: winner?.rule.id ?? null,
    })
  }

  return segments
}

type Occurrence = {
  rule: DeviceScheduleRuleDto
  startMs: number
  endMs: number
}

// Every occurrence of every rule that could touch the displayed week.
//
// Three weeks are generated, not one: a window opening late on the previous
// Saturday can still be running on Sunday morning, and one opening on the last
// Saturday of this week runs into the next. Clipping without them would leave
// the two ends of the strip wrong in exactly the case the wrap exists for.
function expandOccurrences(rules: readonly DeviceScheduleRuleDto[], weekStart: Date): Occurrence[] {
  // Sunday 00:00 UTC on or before the local week start, which is the origin the
  // API's day mask and start minute are measured from.
  const utcWeekStart = new Date(Date.UTC(
    weekStart.getUTCFullYear(),
    weekStart.getUTCMonth(),
    weekStart.getUTCDate(),
    0, 0, 0, 0,
  ))
  utcWeekStart.setUTCDate(utcWeekStart.getUTCDate() - utcWeekStart.getUTCDay())

  const occurrences: Occurrence[] = []

  // Rules stay in the order the API returned them — evaluation order — so the
  // first match found later is the winning one.
  for (const rule of rules) {
    for (let day = 0; day < 7; day++) {
      if ((rule.daysMaskUtc & (1 << day)) === 0) {
        continue
      }

      for (const weekOffset of [-1, 0, 1]) {
        const offsetMinutes: number =
          weekOffset * MINUTES_PER_WEEK + day * MINUTES_PER_DAY + rule.startMinuteUtc
        const startMs: number = utcWeekStart.getTime() + offsetMinutes * 60_000
        occurrences.push({
          rule,
          startMs,
          endMs: startMs + rule.durationMinutes * 60_000,
        })
      }
    }
  }

  return occurrences
}
