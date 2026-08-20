// ============================================================
// positionPager — reading more than one page of /api/positions.
//
// The API has no paging parameters at all. GET /api/positions?deviceId&from&to
// answers with the NEWEST 1000 fixes of the window, ordered by fix time
// descending, and says nothing about what it left behind: no total, no "there is
// more" flag, not even which end got cut. A window holding more than 1000 fixes
// therefore loses its OLDEST rows silently, which on a chart looks like a device
// that simply was not reporting yet.
//
// What the endpoint does give us is an inclusive `to` bound, and that is enough
// to walk the window backwards a thousand rows at a time:
//
//   1. ask for the window                -> the newest 1000
//   2. take the OLDEST row of that batch -> the next `to`
//   3. ask again with that bound         -> the 1000 before it
//   4. repeat until a batch comes back short
//
// Because the bound is INCLUSIVE, step 3 hands back the rows sitting exactly on
// the cursor instant a second time. That is not a flaw to work around by nudging
// the cursor a millisecond earlier — doing so would drop any fix inside the nudge.
// The repeats are dropped by id instead, which is exact.
//
// Every hop depends on the previous hop's answer, so the walk is unavoidably
// sequential. That is why callers get an onProgress hook rather than a promise
// that goes quiet for half a minute.
// ============================================================

import { fetchPositions } from './apiClient'
import type { PositionDto } from './apiTypes'
import { parseApiTimestamp } from '../utils/dates'

// The server's own ceiling, MaxPositionsPerQuery in
// API/CarPosAPI/Services/Positions/PositionQueryService.cs. A batch smaller than
// this is how the last one is recognised, so the two numbers have to agree: if
// the API ever raises its cap, raising this is the matching change.
export const SERVER_ROW_CAP: number = 1000

// One hop's worth of newly seen rows, plus where the next hop starts.
export type PositionChunk = {
  // Rows from this hop that were not already in `seenIds`, newest first.
  rows: PositionDto[]
  // The `to` bound for the following hop, or null when the walk is over — either
  // the window is exhausted or the cursor cannot be advanced.
  nextCursor: string | null
}

export type LoadAllOptions = {
  // Hard ceiling on rows for one walk, so a range covering a year cannot turn
  // into hundreds of requests and a frozen tab.
  maxRows: number
  // Checked between hops. The tabs already guard their effects with a
  // `let canceled = false` flag, so the walk speaks that language rather than
  // asking every caller to build an AbortController.
  isCanceled: () => boolean
  // Called after each hop with the running total, for a live status line.
  onProgress?: (loaded: number) => void
}

export type LoadAllResult = {
  // Every row the walk collected, newest first.
  positions: PositionDto[]
  // True when `maxRows` stopped the walk before the window ran out — the oldest
  // part of the range is still missing and the caller should say so.
  reachedCap: boolean
}

// Fetches one batch and reports what was new about it.
//
// `cursorIso` is the raw `timestamp` string of the previous batch's oldest row,
// passed through UNCHANGED. It must never be round-tripped through a Date on the
// way: fix times are stored as Postgres microseconds, JavaScript dates hold
// milliseconds, and a cursor rounded up by a fraction would step over any fix
// inside the rounding — rows lost with nothing to show for it.
export async function fetchPositionChunk(
  deviceId: string,
  fromIso: string | undefined,
  cursorIso: string | undefined,
  seenIds: Set<number>,
): Promise<PositionChunk> {
  const batch: PositionDto[] = await fetchPositions(deviceId, fromIso, cursorIso)

  const rows: PositionDto[] = []
  for (const position of batch) {
    if (seenIds.has(position.id)) {
      continue
    }
    seenIds.add(position.id)
    rows.push(position)
  }

  // A short batch is the server saying it had nothing more to give.
  if (batch.length < SERVER_ROW_CAP) {
    return { rows, nextCursor: null }
  }

  // A full batch of rows we had all seen already means the cursor cannot move:
  // more than SERVER_ROW_CAP fixes would have to share one exact instant, so
  // every request from here returns the same thousand rows. Vanishingly
  // unlikely, and an infinite request loop if it ever did happen.
  if (rows.length === 0) {
    return { rows, nextCursor: null }
  }

  // The batch is ordered newest-first, so its last row is the oldest one — the
  // instant the next hop reaches back from.
  return { rows, nextCursor: batch[batch.length - 1].timestamp }
}

// Walks the whole window, hop after hop, until it is exhausted, `maxRows` is
// reached, or the caller cancels.
export async function fetchAllPositions(
  deviceId: string,
  fromIso: string | undefined,
  toIso: string | undefined,
  options: LoadAllOptions,
): Promise<LoadAllResult> {
  const seenIds: Set<number> = new Set<number>()
  const positions: PositionDto[] = []

  // The first hop uses the range's own upper bound; later hops replace it with
  // the cursor. `undefined` is a legitimate value here — it means "no upper
  // bound", and buildUrl() drops the parameter entirely.
  let cursor: string | undefined = toIso

  for (;;) {
    const chunk: PositionChunk = await fetchPositionChunk(deviceId, fromIso, cursor, seenIds)

    if (options.isCanceled()) {
      // Whatever was collected belongs to a query nobody is waiting for now.
      return { positions, reachedCap: false }
    }

    for (const position of chunk.rows) {
      positions.push(position)
    }

    options.onProgress?.(positions.length)

    if (chunk.nextCursor === null) {
      return { positions, reachedCap: false }
    }

    // Stop AT the ceiling rather than after crossing it: the rows already
    // fetched are kept, but no further request goes out.
    if (positions.length >= options.maxRows) {
      return { positions, reachedCap: true }
    }

    cursor = chunk.nextCursor
  }
}

// Folds a freshly fetched newest-chunk into rows already on screen.
//
// This is what an auto-refresh tick uses. Positions are append-only — a fix is
// written once and never edited — so the only thing a tick can bring is rows
// NEWER than the ones held. Re-walking forty chunks every thirty seconds to
// discover that would be absurd; one request and a merge says the same thing.
//
// The one case this cannot cover is more than SERVER_ROW_CAP fixes arriving
// between two ticks, which at a 30 s interval means 33 fixes a second — faster
// than the device reports or the ingest pipeline writes.
export function mergeNewest(
  existing: readonly PositionDto[],
  incoming: readonly PositionDto[],
): PositionDto[] {
  const byId: Map<number, PositionDto> = new Map<number, PositionDto>()

  for (const position of existing) {
    byId.set(position.id, position)
  }
  // Incoming wins on a collision: it is the fresher read of the same row.
  for (const position of incoming) {
    byId.set(position.id, position)
  }

  const merged: PositionDto[] = Array.from(byId.values())

  // Restore the API's newest-first order, which the table's "Latest" badge and
  // the paging cursor both depend on. Ties fall back to the id, which ascends
  // with insertion, so the order is total rather than merely mostly-sorted.
  merged.sort((left, right) => {
    const leftTime: number  = parseApiTimestamp(left.timestamp)?.getTime()  ?? 0
    const rightTime: number = parseApiTimestamp(right.timestamp)?.getTime() ?? 0
    return rightTime === leftTime ? right.id - left.id : rightTime - leftTime
  })

  return merged
}
