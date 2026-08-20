// ============================================================
// DeviceMapTab — the "Map" tab inside DevicePage.
//
// Features:
//   • Date range pickers (from / to) to filter which positions to load. The
//     range is computed once when the tab mounts (see getDefaultDateRange) and
//     is only ever changed by the user — refreshing re-runs the SAME query.
//   • "Auto-refresh" toggle: when on, reloads every AUTO_REFRESH_SEC seconds
//     with a live countdown, leaving the range and the map view untouched.
//   • "Refresh now" button for an instant manual reload
//   • "Fit to positions" button — the only control that moves the map after
//     the first load
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
import RangeToolbar from '../components/RangeToolbar'
import type { DevicePageContext } from './DevicePage'
import { useAutoRefresh } from '../hooks/useAutoRefresh'
import { fetchPositions } from '../services/apiClient'
import type { PositionDto } from '../services/apiTypes'
import type { DateRange } from '../utils/dates'
import { datetimeLocalToIso, getDefaultDateRange } from '../utils/dates'
import { describeError } from '../utils/errors'
import { hasGoogleMapsKey, runtimeConfig } from '../services/runtimeConfig'

// How many seconds between automatic refreshes when the toggle is on
const AUTO_REFRESH_SEC = 30

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

  // Date range controls. Computed once, on mount — from here on only the two
  // inputs change it, so a reload can never move the window under the user.
  const [dateRange, setDateRange] = useState<DateRange>(getDefaultDateRange)

  // Auto-refresh: bumps a token to re-run the query, never the date range
  const refresh = useAutoRefresh(AUTO_REFRESH_SEC)

  // Bumped by the "Fit to positions" button; DeviceMap re-frames on a change
  const [fitToken, setFitToken] = useState<number>(0)

  // ---- Position loader ----
  // Called on mount, when dateRange changes, and when a refresh is triggered.
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
        // Keep the last good track on the map — a momentary network blip should
        // report itself in the status line, not blank the view.
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
  }, [device.deviceId, dateRange.from, dateRange.to, refresh.token])

  return (
    <div>
      {/* ---- Controls bar: date pickers + refresh options ---- */}
      <RangeToolbar
        range={dateRange}
        onRangeChange={setDateRange}
        autoRefresh={refresh}
        isLoading={isLoading}
        idPrefix="map"
        className="map-controls-bar"
        refreshLabel="↻ Refresh now"
        loadingLabel="Refreshing…"
        extra={
          /* Refreshes never move the map, so this is how the user gets back to
             "show me everything" after panning away. */
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => setFitToken((current) => current + 1)}
            disabled={positions.length === 0}
            style={{ alignSelf: 'flex-end' }}
          >
            ⤢ Fit to positions
          </button>
        }
      />

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
            /* Keyed by device so switching trackers starts a fresh map, which
               frames the new track. Without it the "already framed" flag would
               leave the viewport parked over the previous device. */
            key={device.deviceId}
            positions={positions}
            apiKey={apiKey}
            fitToken={fitToken}
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
