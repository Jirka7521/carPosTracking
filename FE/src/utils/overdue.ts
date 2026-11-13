// ---------------------------------------------------------------------------
// overdue — when the map's latest-position pin turns from blue to red.
//
// The server decides how old a fix may get (GET /api/devices/{id}/report-interval:
// twice the longest interval of the plan in force); this only anchors that age to
// the newest fix actually drawn. The fix's own GNSS time is used, not the device's
// lastSeenAt — that is when the server RECEIVED something, and a backlog upload
// moves it forward without the car having been anywhere new.
//
// A share page gets the finished instant from the server instead, so no setting
// reaches a visitor. Both paths end in the same `Date | null`, which
// hooks/useIsOverdue turns into the one boolean the pin and the legend share.
// ---------------------------------------------------------------------------

import { parseApiTimestamp } from './dates'

// The instant the newest of `positions` stops being current, or null when there
// is nothing to anchor to or the threshold is unknown (the pin then stays blue).
export function overdueAfter(
  positions: ReadonlyArray<{ timestamp: string }>,
  overdueAfterSeconds: number | null,
): Date | null {
  if (overdueAfterSeconds === null || overdueAfterSeconds <= 0) {
    return null
  }

  let newestMs: number | null = null
  for (const position of positions) {
    const parsed: Date | null = parseApiTimestamp(position.timestamp)
    if (parsed !== null && (newestMs === null || parsed.getTime() > newestMs)) {
      newestMs = parsed.getTime()
    }
  }

  return newestMs === null ? null : new Date(newestMs + overdueAfterSeconds * 1000)
}
