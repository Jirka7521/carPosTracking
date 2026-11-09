// ============================================================
// Trips — the Trips tab's pure, language-free logic: grouping a page of trips
// into calendar days, keeping a paged list current across refreshes, and
// turning a journey's whole-day range into the instants the API stores.
//
// The server sends trips newest first, 50 at a time, and recomputes every one's
// end and statistics on each answer. Two consequences shape this file:
//
//   • A refresh asks for the newest page only and splices it over the head of
//     what is already loaded (mergeNewestPage). Re-reading every loaded page on
//     each 30-second tick would multiply the server's work for trips that are
//     days old and cannot change unless a recompute runs.
//   • A row the server could not finish within one answer's budget comes back
//     with `statsPending`. A refresh must not blank figures the reader already
//     has for that trip, so the merge keeps them (see keepKnownStats).
//
// Times are shown in the reader's own time zone, so days are local days. The
// journey dialog works in whole local days as well: a range is "from local
// midnight of the first day to local midnight after the last", built from the
// calendar fields so it stays right across a DST change.
// ============================================================

import { formatDayMonth, formatTime } from '../i18n/format'
import type { TripSummaryDto } from '../services/apiTypes'
import { parseApiTimestamp } from './dates'

// How many trips one list request asks for — the API's default page.
export const TRIP_PAGE_SIZE: number = 50

// Most trips one "Move to journey" may carry. Mirrors TripRules.MaxTripsPerAssign
// on the API, which rejects a larger request.
export const MAX_TRIPS_PER_ASSIGN: number = 500

// The trip-detection thresholds' bounds and defaults, mirroring TripRules on the
// API (which enforces them): the minimum stop in seconds, the stop radius and
// the shortest trip kept in metres.
export const TRIP_SETTING_LIMITS = {
  minStopSeconds: { min: 60, max: 14_400, defaultValue: 1_200 },
  stopRadiusMeters: { min: 20, max: 1_000, defaultValue: 150 },
  minTripDistanceMeters: { min: 0, max: 20_000, defaultValue: 500 },
} as const

// One calendar day of the list, with the totals its header shows.
export type TripDay = {
  // The local date as YYYY-MM-DD — stable, so it can key the rendered group.
  key: string
  // Local midnight of that day, for formatting the header.
  date: Date
  trips: TripSummaryDto[]
  // Sums of the figures known so far.
  distanceMeters: number
  durationSeconds: number
  // True while some trip of the day still has its figures pending, so the
  // totals are not final yet.
  hasPending: boolean
}

// When a trip is shown as starting: the moment the car left (the last parked
// fix, when the tracker was awake for it), or the first moving fix when it was
// not. The list is ordered by the latter; the two differ by minutes at most.
export function tripShownStart(trip: TripSummaryDto): Date | null {
  return parseApiTimestamp(trip.departure ?? trip.startTime)
}

// "14:05 → 14:47"; "14:05 → …" while the trip is still going; the end with its
// date when the trip ran past midnight, so "23:40 → 00:15" does not read as a
// trip that ended before it began. Digits only, so not a translation.
export function describeTripTimes(trip: TripSummaryDto): string {
  const start: Date | null = tripShownStart(trip)
  const end: Date | null = trip.endTime === null ? null : parseApiTimestamp(trip.endTime)
  const startText: string = start === null ? '—' : formatTime(start)
  if (trip.inProgress || end === null) {
    return `${startText} → …`
  }
  const sameDay: boolean = start !== null && localDateKey(start) === localDateKey(end)
  const endText: string = sameDay ? formatTime(end) : `${formatDayMonth(end)} ${formatTime(end)}`
  return `${startText} → ${endText}`
}

// The trips of a page, newest first, cut into local calendar days, newest day
// first. A trip belongs to the day it started on, even when it ran past
// midnight.
export function groupTripsByDay(trips: readonly TripSummaryDto[]): TripDay[] {
  const days: TripDay[] = []
  const byKey = new Map<string, TripDay>()

  for (const trip of trips) {
    const start: Date | null = tripShownStart(trip)
    if (start === null) {
      continue
    }

    const key: string = localDateKey(start)
    let day: TripDay | undefined = byKey.get(key)
    if (day === undefined) {
      day = {
        key,
        date: new Date(start.getFullYear(), start.getMonth(), start.getDate()),
        trips: [],
        distanceMeters: 0,
        durationSeconds: 0,
        hasPending: false,
      }
      byKey.set(key, day)
      days.push(day)
    }

    day.trips.push(trip)
    day.distanceMeters += trip.distanceMeters ?? 0
    day.durationSeconds += trip.durationSeconds ?? 0
    day.hasPending = day.hasPending || trip.statsPending
  }

  return days
}

// The loaded list after a refresh brought a fresh newest page. The fresh page
// replaces the head; older trips already loaded are kept where they start
// before the fresh page's oldest one. When the fresh page is the whole list (it
// has no next page), it replaces everything, which is also how a trip the
// server merged away disappears.
export function mergeNewestPage(
  current: readonly TripSummaryDto[],
  newest: readonly TripSummaryDto[],
  newestHasMore: boolean,
): TripSummaryDto[] {
  const previousById = new Map<number, TripSummaryDto>(current.map((trip) => [trip.id, trip]))
  const head: TripSummaryDto[] = newest.map((trip) => keepKnownStats(trip, previousById.get(trip.id)))

  if (!newestHasMore || head.length === 0) {
    return head
  }

  const oldestFresh: number = startMillis(head[head.length - 1])
  const freshIds = new Set<number>(head.map((trip) => trip.id))
  const tail: TripSummaryDto[] = current.filter(
    (trip) => !freshIds.has(trip.id) && startMillis(trip) < oldestFresh,
  )
  return [...head, ...tail]
}

// The loaded list with an older page appended, without repeating a trip that
// a refresh in between already brought in.
export function appendOlderPage(
  current: readonly TripSummaryDto[],
  older: readonly TripSummaryDto[],
): TripSummaryDto[] {
  const seen = new Set<number>(current.map((trip) => trip.id))
  return [...current, ...older.filter((trip) => !seen.has(trip.id))]
}

// The list with one trip replaced by a fresher copy of itself — a pending row
// filled in by fetching it alone.
export function replaceTrip(
  current: readonly TripSummaryDto[],
  trip: TripSummaryDto,
): TripSummaryDto[] {
  return current.map((existing) => (existing.id === trip.id ? trip : existing))
}

// A fresh row that came back pending, given the figures the reader already has
// for the same trip: those figures, with the fresh row's own placement and
// name. Only when the trip still starts at the same moment — a start that moved
// means the server recomputed it, and the old figures describe something else.
function keepKnownStats(fresh: TripSummaryDto, previous: TripSummaryDto | undefined): TripSummaryDto {
  if (
    !fresh.statsPending
    || previous === undefined
    || previous.statsPending
    || previous.startTime !== fresh.startTime
  ) {
    return fresh
  }
  return {
    ...previous,
    journeyId: fresh.journeyId,
    journeyPinned: fresh.journeyPinned,
    name: fresh.name,
  }
}

function startMillis(trip: TripSummaryDto): number {
  return parseApiTimestamp(trip.startTime)?.getTime() ?? 0
}

function localDateKey(value: Date): string {
  const month: string = String(value.getMonth() + 1).padStart(2, '0')
  const day: string = String(value.getDate()).padStart(2, '0')
  return `${value.getFullYear()}-${month}-${day}`
}

// ---------------------------------------------------------------------------
// Journey ranges in whole days
// ---------------------------------------------------------------------------

// Today as an <input type="date"> value.
export function todayDateInput(): string {
  return localDateKey(new Date())
}

// The instant a range starting on that day starts: local midnight, as UTC ISO.
export function dateInputToRangeStart(value: string): string | undefined {
  const date: Date | null = parseDateInput(value, 0)
  return date?.toISOString()
}

// The instant a range whose last day is that day ends: local midnight AFTER it,
// as UTC ISO. Ranges are half-open, so this is the first moment not included.
export function dateInputToRangeEnd(value: string): string | undefined {
  const date: Date | null = parseDateInput(value, 1)
  return date?.toISOString()
}

// A stored range start as the first day to show in the date input.
export function rangeStartToDateInput(iso: string): string {
  const date: Date | null = parseApiTimestamp(iso)
  return date === null ? '' : localDateKey(date)
}

// A stored range end as the last day to show: the day holding the last instant
// before the (exclusive) end. For a range ending at local midnight that is the
// day before; a range set to end mid-day keeps that day.
export function rangeEndToDateInput(iso: string): string {
  const date: Date | null = parseApiTimestamp(iso)
  return date === null ? '' : localDateKey(new Date(date.getTime() - 1))
}

// The last day of a stored range, as a Date, for formatting.
export function rangeEndLastDay(iso: string): Date | null {
  const date: Date | null = parseApiTimestamp(iso)
  return date === null ? null : new Date(date.getTime() - 1)
}

// "YYYY-MM-DD" plus a number of days, at local midnight; null when the input
// is not a date. Built from the calendar fields, never by adding 24 hours.
function parseDateInput(value: string, addDays: number): Date | null {
  const match: RegExpExecArray | null = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (match === null) {
    return null
  }
  const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]) + addDays)
  return Number.isNaN(date.getTime()) ? null : date
}
