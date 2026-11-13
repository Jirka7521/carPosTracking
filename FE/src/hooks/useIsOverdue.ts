// ============================================================
// useIsOverdue — whether the latest position is overdue RIGHT NOW.
//
// Takes the instant the latest fix stops being current (utils/overdue.ts for a
// device, SharedViewDto.overdueAfter for a share) and answers true from that
// moment on. A timer is armed for the crossing, so the answer flips on screen
// without waiting for a reload.
//
// One answer for two consumers: the map's pin and the legend under it read the
// same boolean, so the pin can never be red while the legend still says the
// position is current. That is why the timer lives here and not inside DeviceMap
// — the legend is drawn even while the map itself is waiting for consent.
// ============================================================

import { useEffect, useState } from 'react'

// setTimeout takes a signed 32-bit delay; anything longer fires at once.
const MAX_TIMEOUT_MS = 2_147_483_647

export function useIsOverdue(overdueAfter: Date | null): boolean {
  // A number, not the Date: callers build a new Date on every render, and the
  // effect should only re-run when the instant itself moves.
  const overdueAtMs: number | null = overdueAfter === null ? null : overdueAfter.getTime()

  const [isOverdue, setIsOverdue] = useState<boolean>(false)

  useEffect(() => {
    // "Now" is read here, not during render — the clock is an external system,
    // as DeviceLinkBadge and ScheduleTimeline put it. One boolean is set per new
    // instant (and once more by the timer), and nothing feeds back into it.
    const nowMs: number = Date.now()
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setIsOverdue(overdueAtMs !== null && nowMs >= overdueAtMs)

    if (overdueAtMs === null || nowMs >= overdueAtMs) {
      return undefined
    }

    const timer: number = window.setTimeout(
      // Re-checked rather than assumed: a delay clamped to the timer's ceiling
      // fires early, and must not paint a current position as overdue.
      () => setIsOverdue(Date.now() >= overdueAtMs),
      Math.min(overdueAtMs - nowMs, MAX_TIMEOUT_MS),
    )
    return () => window.clearTimeout(timer)
  }, [overdueAtMs])

  return isOverdue
}
