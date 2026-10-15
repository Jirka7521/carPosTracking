// ============================================================
// eventPager — reading more than one answer's worth of /api/devices/{id}/events.
//
// The Events tab shows one answer: the newest EVENTS_LIMIT events of the range.
// The CSV export wants all of them, and this is how it gets them. It is
// positionPager's walk (read that for the long version), applied to events:
//
//   1. ask for the window                -> the newest 1000
//   2. take the OLDEST row of that batch -> the next `to`
//   3. ask again with that bound         -> the 1000 before it
//   4. repeat until a batch comes back short
//
// What makes it work is the API's own contract: `to` is INCLUSIVE and the order is
// occurredAt descending with the id as a tie-break (DeviceEventQueryService.cs).
// Inclusive means every hop hands back the rows sitting exactly on the cursor
// instant a second time, so repeats are dropped by id — nudging the cursor
// instead would lose any event inside the nudge. The id tie-break means a batch
// that is cut in the middle of events sharing one instant is cut at the same
// place every time, and the next hop (which starts at that instant) picks up
// precisely the rest of them.
//
// The cursor is the raw `occurredAt` string of the previous batch's oldest row,
// passed through UNCHANGED. Round-tripping it through a Date would round it to
// milliseconds, and a cursor rounded up by a fraction steps over any event inside
// the rounding.
//
// Every hop depends on the previous hop's answer, so the walk is sequential; the
// caller gets an onProgress hook rather than a promise that goes quiet.
// ============================================================

import { fetchDeviceEvents } from './apiClient'
import type { DeviceEventDto, DeviceEventSeverity } from './apiTypes'

// One request's worth of events — the API's own ceiling, MaxLimit in
// API/CarPosAPI/Controllers/DeviceEventsController.cs. A batch smaller than this
// is how the last one is recognised, so the two numbers have to agree: if the API
// ever raises its cap, raising this is the matching change.
export const EVENTS_LIMIT: number = 1000

export type FetchAllEventsOptions = {
  // The range's own bounds, as ISO strings. Both optional, like the API's.
  fromIso: string | undefined
  toIso: string | undefined
  // The severity filter, passed through to every hop.
  minSeverity: DeviceEventSeverity | undefined
  // Hard ceiling on rows for one walk, so a range covering a year of a device
  // that logs several events per report cannot turn into hundreds of requests.
  maxRows: number
  // Checked between hops, in the same `let canceled` spirit as the tabs' effects.
  isCanceled: () => boolean
  // Called after each hop with the running total, for a live status line.
  onProgress?: (loaded: number) => void
}

export type FetchAllEventsResult = {
  // Every event the walk collected, newest first.
  events: DeviceEventDto[]
  // True when `maxRows` stopped the walk before the window ran out — the oldest
  // part of the range is still missing and the caller should say so.
  reachedCap: boolean
}

// Walks the whole window, hop after hop, until it is exhausted, `maxRows` is
// reached, or the caller cancels. A failed hop throws: a file silently missing
// the middle of the range is worse than no file.
export async function fetchAllDeviceEvents(
  deviceId: string,
  options: FetchAllEventsOptions,
): Promise<FetchAllEventsResult> {
  const seenIds: Set<number> = new Set<number>()
  const events: DeviceEventDto[] = []

  // The first hop uses the range's own upper bound; later hops replace it with
  // the cursor. `undefined` means "no upper bound" and the parameter is dropped.
  let cursor: string | undefined = options.toIso

  for (;;) {
    const batch: DeviceEventDto[] = await fetchDeviceEvents(deviceId, {
      from: options.fromIso,
      to: cursor,
      minSeverity: options.minSeverity,
      limit: EVENTS_LIMIT,
    })

    if (options.isCanceled()) {
      // Whatever was collected belongs to a query nobody is waiting for now.
      return { events, reachedCap: false }
    }

    let added: number = 0
    for (const event of batch) {
      if (seenIds.has(event.id)) {
        continue
      }
      seenIds.add(event.id)
      events.push(event)
      added += 1
    }

    options.onProgress?.(events.length)

    // A short batch is the server saying it had nothing more to give.
    if (batch.length < EVENTS_LIMIT) {
      return { events, reachedCap: false }
    }

    // A full batch of rows we had all seen already means the cursor cannot move:
    // more than EVENTS_LIMIT events would have to share one exact instant, so
    // every request from here returns the same thousand rows. Vanishingly
    // unlikely, and an infinite request loop if it ever did happen.
    if (added === 0) {
      return { events, reachedCap: false }
    }

    // Stop AT the ceiling rather than after crossing it: the rows already
    // fetched are kept, but no further request goes out.
    if (events.length >= options.maxRows) {
      return { events, reachedCap: true }
    }

    // Newest-first, so the last row is the oldest — the instant the next hop
    // reaches back from.
    cursor = batch[batch.length - 1].occurredAt
  }
}
