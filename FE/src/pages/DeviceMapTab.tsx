// ============================================================
// DeviceMapTab — the "Map" tab inside DevicePage.
//
// Features:
//   • Date range pickers (from / to) to filter which positions to load
//   • "Auto-refresh" toggle: when on, automatically extends the "to"
//     date to "now" and reloads positions every AUTO_REFRESH_SEC seconds.
//     A live countdown shows how long until the next refresh.
//   • "Refresh now" button for an instant manual reload
//   • Positions rendered on Google Maps via the DeviceMap component
//   • Newest position shown with a BLUE marker; all older positions use RED
//   • Status line ("Loaded N positions", "No positions found", etc.)
//   • Map legend explaining the marker colors
//
// The device is received from DevicePage via React Router outlet context;
// no extra API call for the device itself is needed here.
// ============================================================

import { useEffect, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import DeviceMap from '../components/DeviceMap'
import type { DevicePageContext } from './DevicePage'
import { fetchPositions } from '../services/apiClient'
import type { PositionDto } from '../services/apiTypes'
import { datetimeLocalToIso, formatDateTimeLocal } from '../utils/dates'
import { describeError } from '../utils/errors'
import { hasGoogleMapsKey, runtimeConfig } from '../services/runtimeConfig'

// How many seconds between automatic refreshes when the toggle is on
const AUTO_REFRESH_SEC = 30

type DateRange = {
  from: string // datetime-local string (YYYY-MM-DDTHH:mm)
  to:   string // datetime-local string
}

// Returns a date range covering the last 24 hours
function getDefaultDateRange(): DateRange {
  const now      = new Date()
  const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000)
  return {
    from: formatDateTimeLocal(yesterday),
    to:   formatDateTimeLocal(now),
  }
}

export function DeviceMapTab() {
  // Device object passed down from DevicePage
  const { device } = useOutletContext<DevicePageContext>()

  // Google Maps API key from the container's runtime config (see
  // services/runtimeConfig.ts). Empty string = the map cannot be rendered.
  const apiKey = runtimeConfig.googleMapsApiKey

  // Loaded position data
  const [positions, setPositions]       = useState<PositionDto[]>([])
  const [isLoading, setIsLoading]       = useState<boolean>(false)
  const [statusMessage, setStatusMessage] = useState<string>('')

  // Date range controls
  const [dateRange, setDateRange] = useState<DateRange>(getDefaultDateRange)

  // Auto-refresh controls
  const [autoRefresh, setAutoRefresh] = useState<boolean>(true)
  const [countdown,   setCountdown]   = useState<number>(AUTO_REFRESH_SEC)

  // ---- Position loader ----
  // Called on mount, when dateRange changes, and when refresh is triggered.
  // The `canceled` flag prevents stale fetches from updating state if the
  // effect re-runs (e.g. dateRange change, StrictMode double-invocation).
  useEffect(() => {
    let canceled = false

    const load = async (): Promise<void> => {
      const fromIso = datetimeLocalToIso(dateRange.from)
      const toIso   = datetimeLocalToIso(dateRange.to)

      setIsLoading(true)

      try {
        const data = await fetchPositions(device.deviceId, fromIso, toIso)
        if (canceled) {
          return
        }
        setPositions(data)
        setStatusMessage(
          data.length === 0
            ? 'No positions found for this time range.'
            : `Loaded ${data.length} position${data.length === 1 ? '' : 's'}.`,
        )
      } catch (error) {
        if (canceled) {
          return
        }
        setPositions([])
        setStatusMessage(describeError(error, 'Failed to load positions.'))
      } finally {
        if (!canceled) {
          setIsLoading(false)
        }
      }
    }

    void load()
    return () => {
      canceled = true
    }
  }, [device.deviceId, dateRange.from, dateRange.to])

  // ---- Auto-refresh countdown timer ----
  // When autoRefresh is on, a 1-second interval ticks the countdown.
  // When it reaches zero it:
  //   1. Updates dateRange.to to the current time (which triggers the load effect above)
  //   2. Resets the countdown back to AUTO_REFRESH_SEC
  useEffect(() => {
    if (!autoRefresh) {
      // Reset the countdown so it shows the full interval when re-enabled
      setCountdown(AUTO_REFRESH_SEC)
      return
    }

    const interval = setInterval(() => {
      setCountdown((prev) => {
        if (prev <= 1) {
          // Update "to" to now — this triggers the load effect automatically
          setDateRange((cur) => ({
            ...cur,
            to: formatDateTimeLocal(new Date()),
          }))
          return AUTO_REFRESH_SEC
        }
        return prev - 1
      })
    }, 1000)

    return () => clearInterval(interval)
  }, [autoRefresh])

  // Manual refresh: push "to" to now, which triggers the load effect
  function handleRefreshNow(): void {
    setDateRange((cur) => ({
      ...cur,
      to: formatDateTimeLocal(new Date()),
    }))
    // Reset the countdown so the auto-refresh doesn't fire 1 second later
    setCountdown(AUTO_REFRESH_SEC)
  }

  return (
    <div>
      {/* ---- Controls bar: date pickers + refresh options ---- */}
      <div className="map-controls-bar">
        {/* "From" date picker */}
        <div className="form-field">
          <label className="form-label" htmlFor="map-from">From</label>
          <input
            id="map-from"
            className="form-input"
            type="datetime-local"
            value={dateRange.from}
            onChange={(e) =>
              setDateRange((cur) => ({ ...cur, from: e.target.value }))
            }
            style={{ width: 'auto' }}
          />
        </div>

        {/* "To" date picker */}
        <div className="form-field">
          <label className="form-label" htmlFor="map-to">To</label>
          <input
            id="map-to"
            className="form-input"
            type="datetime-local"
            value={dateRange.to}
            onChange={(e) =>
              setDateRange((cur) => ({ ...cur, to: e.target.value }))
            }
            style={{ width: 'auto' }}
          />
        </div>

        {/* Auto-refresh toggle */}
        <label className="checkbox-field" style={{ alignSelf: 'center' }}>
          <input
            type="checkbox"
            checked={autoRefresh}
            onChange={(e) => setAutoRefresh(e.target.checked)}
          />
          <span>
            Auto-refresh
            {autoRefresh ? (
              <span className="refresh-pill" style={{ marginLeft: 8 }}>
                ↻ {countdown}s
              </span>
            ) : null}
          </span>
        </label>

        {/* Manual refresh button */}
        <button
          type="button"
          className="btn btn-secondary"
          onClick={handleRefreshNow}
          disabled={isLoading}
          style={{ alignSelf: 'flex-end' }}
        >
          {isLoading ? (
            <>
              <span className="spinner" style={{ width: 14, height: 14, borderWidth: 2 }} />
              Refreshing…
            </>
          ) : (
            '↻ Refresh now'
          )}
        </button>
      </div>

      {/* Status line: how many positions were loaded, or an error */}
      <div className="map-status-row">
        <span className="map-status-text" role="status">
          {statusMessage}
        </span>
      </div>

      {/* Google Maps canvas */}
      <div className="map-frame">
        {/* Loading overlay while a fetch is in progress */}
        {isLoading ? (
          <div className="map-loading-overlay" aria-live="polite">
            <span className="spinner" style={{ width: 14, height: 14, borderWidth: 2 }} />
            Loading…
          </div>
        ) : null}

        {hasGoogleMapsKey() ? (
          <DeviceMap
            positions={positions}
            apiKey={apiKey}
          />
        ) : (
          /* Without a key the Maps script fails and leaves a grey box that
             looks like a bug. Say what is actually wrong instead. */
          <div className="error-state">
            <p>
              The map cannot be displayed: no Google Maps API key is configured
              for this deployment.
            </p>
          </div>
        )}
      </div>

      {/* Legend explaining the two marker colors */}
      <div className="map-legend" aria-label="Map legend">
        <div className="map-legend-item">
          <span className="legend-dot legend-dot--latest" aria-hidden="true" />
          Latest position
        </div>
        <div className="map-legend-item">
          <span className="legend-dot legend-dot--history" aria-hidden="true" />
          Historical positions
        </div>
      </div>
    </div>
  )
}
