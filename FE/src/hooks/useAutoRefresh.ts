// ============================================================
// useAutoRefresh — the shared "reload the same query again" timer.
//
// Every tab that shows positions needs the same thing: an optional countdown
// that periodically asks for fresh data, plus a manual "refresh now". What it
// must NOT do is move the goalposts. The tabs used to refresh by rewriting
// dateRange.to to the current time, which threw away any end time the user had
// chosen and — because formatDateTimeLocal only has minute resolution — often
// produced an identical string, so React bailed out and nothing was fetched at
// all.
//
// Instead this hook exposes `token`, a counter. Put it in the load effect's
// dependency array; every tick and every manual refresh bumps it, so the effect
// re-runs with the date range untouched. The token always changes, so a refresh
// always fetches.
// ============================================================

import { useCallback, useEffect, useRef, useState } from 'react'

export type AutoRefresh = {
  // Whether the periodic reload is running
  enabled: boolean
  setEnabled: (value: boolean) => void
  // Seconds until the next automatic reload, for the "↻ Ns" pill
  countdown: number
  // Bumped on every reload trigger — belongs in the load effect's deps
  token: number
  // Reload immediately and restart the countdown
  refreshNow: () => void
}

export function useAutoRefresh(intervalSec: number): AutoRefresh {
  const [enabled, setEnabledState] = useState<boolean>(true)
  const [countdown, setCountdown]  = useState<number>(intervalSec)
  const [token, setToken]          = useState<number>(0)

  // The authoritative countdown. State is only a mirror of it for rendering:
  // keeping the real value here means the interval callback never has to run a
  // state updater with a side effect inside it (which StrictMode would invoke
  // twice, firing two reloads per tick).
  const remainingRef = useRef<number>(intervalSec)

  const refreshNow = useCallback((): void => {
    remainingRef.current = intervalSec
    setCountdown(intervalSec)
    setToken((current) => current + 1)
  }, [intervalSec])

  // Toggling restarts the count rather than resuming it, so re-enabling always
  // gives a full interval. Done here rather than in the effect below: that keeps
  // the effect to its one job, wiring up the timer.
  const setEnabled = useCallback((value: boolean): void => {
    remainingRef.current = intervalSec
    setCountdown(intervalSec)
    setEnabledState(value)
  }, [intervalSec])

  useEffect(() => {
    if (!enabled) {
      return
    }

    const interval = setInterval(() => {
      const next: number = remainingRef.current - 1
      if (next <= 0) {
        remainingRef.current = intervalSec
        setCountdown(intervalSec)
        setToken((current) => current + 1)
        return
      }
      remainingRef.current = next
      setCountdown(next)
    }, 1000)

    return () => clearInterval(interval)
  }, [enabled, intervalSec])

  // Nothing is pending while the timer is off, so show the full interval
  const displayedCountdown: number = enabled ? countdown : intervalSec

  return { enabled, setEnabled, countdown: displayedCountdown, token, refreshNow }
}
