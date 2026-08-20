// ============================================================
// PositionListTab — the "Positions" tab inside DevicePage.
//
// Displays a paginated table of GPS positions, sorted newest-first
// so the most recent fix is always at the top.
//
// Features:
//   • Date range pickers matching those on the Map tab — chosen once on mount
//     and changed only by the user; a refresh re-runs the same query
//   • "Refresh" button and an auto-refresh toggle, neither of which resets the
//     page the user is on
//   • Zebra-striped table with blue header row
//   • The newest row is highlighted with a light-blue tint and a
//     "Latest" badge in the timestamp column
//   • Coordinates displayed in monospace for alignment
//   • Pagination: N rows per page with Prev / Next controls
// ============================================================

import { useEffect, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import RangeToolbar from '../components/RangeToolbar'
import type { DevicePageContext } from './DevicePage'
import { useAutoRefresh } from '../hooks/useAutoRefresh'
import { fetchPositions } from '../services/apiClient'
import type { PositionDto } from '../services/apiTypes'
import type { DateRange } from '../utils/dates'
import { datetimeLocalToIso, getDefaultDateRange, parseApiTimestamp } from '../utils/dates'
import { describeError } from '../utils/errors'

const PAGE_SIZE_OPTIONS = [25, 50, 100] as const
type PageSize = (typeof PAGE_SIZE_OPTIONS)[number]

// How many seconds between automatic refreshes when the toggle is on
const AUTO_REFRESH_SEC = 30

// Format one accelerometer axis (in g) for the table, or an em dash when the
// device sent no reading (accelerometer disabled or firmware predating it).
function formatAccel(value: number | null): string {
  return value === null ? '—' : value.toFixed(2)
}

// Battery as the device reports it: 0 is the agreed "charging" SENTINEL (see
// BatteryBadge), not a flat pack; null means the device sent no reading at all.
// Rendered as plain text rather than a <BatteryBadge> so the column stays as
// dense and as aligned as the numeric ones beside it.
function formatBattery(value: number | null): string {
  if (value === null) {
    return '—'
  }
  return value === 0 ? 'Charging' : `${value}%`
}

// Format the modem die temperature (°C) for the table, or an em dash when the
// device sent no reading (older firmware or the sensor unsupported).
function formatTemperature(value: number | null): string {
  return value === null ? '—' : `${value.toFixed(1)} °C`
}

// Formats an API timestamp to a readable local date/time string.
function formatTimestamp(value: string): string {
  const parsed = parseApiTimestamp(value)
  return parsed === null ? value : parsed.toLocaleString(undefined, { hour12: false })
}

export function PositionListTab() {
  const { device } = useOutletContext<DevicePageContext>()

  // All positions returned by the API (sorted newest-first for display)
  const [positions, setPositions]         = useState<PositionDto[]>([])
  const [isLoading, setIsLoading]         = useState<boolean>(false)
  const [statusMessage, setStatusMessage] = useState<string>('')

  // Date range controls. Computed once, on mount — from here on only the two
  // inputs change it, so a reload can never move the window under the user.
  const [dateRange, setDateRange] = useState<DateRange>(getDefaultDateRange)

  // Auto-refresh: bumps a token to re-run the query, never the date range
  const refresh = useAutoRefresh(AUTO_REFRESH_SEC)

  // Current page index (0-based)
  const [page, setPage]         = useState<number>(0)
  const [pageSize, setPageSize] = useState<PageSize>(25)

  // Back to the first page when the data SOURCE changes — a different device or
  // a different window. Deliberately NOT on every load: a refresh has to leave
  // the reader where they were.
  //
  // The device arrives as a prop and this component stays mounted when the route
  // switches to another device, so that case is adjusted during render — React's
  // documented alternative to a reset-in-an-effect, which would render the wrong
  // page once before correcting itself.
  const [lastDeviceId, setLastDeviceId] = useState<string>(device.deviceId)
  if (lastDeviceId !== device.deviceId) {
    setLastDeviceId(device.deviceId)
    setPage(0)
  }

  // A range change comes from the toolbar, so it can be handled directly
  function handleRangeChange(next: DateRange): void {
    setDateRange(next)
    setPage(0)
  }

  // Load positions on mount, on a range change, and on every refresh tick
  useEffect(() => {
    let canceled = false

    const load = async (): Promise<void> => {
      setIsLoading(true)

      const fromIso = datetimeLocalToIso(dateRange.from)
      const toIso   = datetimeLocalToIso(dateRange.to)

      try {
        const data = await fetchPositions(device.deviceId, fromIso, toIso)
        if (canceled) {
          return
        }

        // The API already orders by fix time descending (and caps the result at
        // 1000 rows), so the newest fix is row zero — no client-side sorting.
        setPositions(data)
        setStatusMessage(
          data.length === 0
            ? 'No positions found for this time range.'
            : `${data.length} position${data.length === 1 ? '' : 's'} found.`,
        )
      } catch (error) {
        if (canceled) {
          return
        }
        // Keep the rows already on screen — a momentary network blip should
        // report itself in the status line, not empty the table.
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

  // ---- Pagination calculations ----
  const totalPages    = Math.max(1, Math.ceil(positions.length / pageSize))
  const currentPage   = Math.min(page, totalPages - 1) // clamp after data reload
  const pageStart     = currentPage * pageSize
  const pageEnd       = Math.min(pageStart + pageSize, positions.length)
  const pageRows      = positions.slice(pageStart, pageEnd)

  return (
    <div>
      {/* ---- Controls: date pickers + refresh options ---- */}
      <RangeToolbar
        range={dateRange}
        onRangeChange={handleRangeChange}
        autoRefresh={refresh}
        isLoading={isLoading}
        idPrefix="pos"
        className="position-list-controls"
      />

      {/* Status line */}
      <p className="hint" role="status" style={{ marginBottom: 12 }}>
        {statusMessage}
      </p>

      {/* ---- Loading state ----
           Only while there is nothing to show yet. A refresh must not replace
           the table the user is reading with a spinner. */}
      {isLoading && positions.length === 0 ? (
        <div className="loading-state">
          <div className="spinner" />
          <span>Loading positions…</span>
        </div>
      ) : positions.length === 0 ? (
        /* ---- Empty state ---- */
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">📍</span>
          <h3>No positions</h3>
          <p>No GPS fixes were recorded in the selected time range.</p>
        </div>
      ) : (
        /* ---- Position table ---- */
        <>
          <div className="position-table-wrapper">
            <table className="position-table">
              <thead>
                <tr>
                  <th scope="col">#</th>
                  <th scope="col">Timestamp</th>
                  <th scope="col">Latitude</th>
                  <th scope="col">Longitude</th>
                  <th scope="col">Speed</th>
                  <th scope="col">Altitude</th>
                  <th scope="col">Battery</th>
                  <th scope="col">Accel X (g)</th>
                  <th scope="col">Accel Y (g)</th>
                  <th scope="col">Accel Z (g)</th>
                  <th scope="col">Temp</th>
                </tr>
              </thead>
              <tbody>
                {pageRows.map((position, rowIndex) => {
                  // The very first row (index 0 in the reversed array) is the newest
                  const isLatest = pageStart === 0 && rowIndex === 0

                  return (
                    <tr
                      key={position.id}
                      className={isLatest ? 'position-row--latest' : ''}
                    >
                      {/* Row number in the overall list (not page-relative) */}
                      <td style={{ color: 'var(--text-muted)', width: '4rem' }}>
                        {pageStart + rowIndex + 1}
                      </td>

                      {/* Timestamp with optional "Latest" badge */}
                      <td>
                        {formatTimestamp(position.timestamp)}
                        {isLatest ? (
                          <span className="latest-badge" aria-label="Latest position">
                            Latest
                          </span>
                        ) : null}
                      </td>

                      {/* Coordinates in monospace */}
                      <td className="position-coord">{position.latitude.toFixed(6)}</td>
                      <td className="position-coord">{position.longitude.toFixed(6)}</td>

                      {/* As reported by the receiver — not derived from the
                          track, so a stationary vehicle may still show a small
                          non-zero speed. */}
                      <td className="position-coord">{position.speedKmph.toFixed(1)} km/h</td>
                      <td className="position-coord">{Math.round(position.altitudeMeters)} m</td>

                      {/* "Charging" rather than 0% — see formatBattery. */}
                      <td className="position-coord">{formatBattery(position.batteryPct)}</td>

                      {/* Raw instantaneous ADXL345 sample; em dash when the
                          device sent none for this fix. */}
                      <td className="position-coord">{formatAccel(position.accelXG)}</td>
                      <td className="position-coord">{formatAccel(position.accelYG)}</td>
                      <td className="position-coord">{formatAccel(position.accelZG)}</td>
                      <td className="position-coord">{formatTemperature(position.temperatureC)}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          {/* ---- Pagination bar ---- */}
          <div className="pagination">
            <span>
              Showing {pageStart + 1}–{pageEnd} of {positions.length} positions
            </span>

            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', fontSize: '0.875rem' }}>
              <label htmlFor="page-size-select" style={{ color: 'var(--text-muted)' }}>
                Rows per page:
              </label>
              <select
                id="page-size-select"
                className="form-input"
                style={{ width: 'auto', padding: '2px 6px' }}
                value={pageSize}
                onChange={(e) => {
                  setPageSize(Number(e.target.value) as PageSize)
                  setPage(0)
                }}
              >
                {PAGE_SIZE_OPTIONS.map((n) => (
                  <option key={n} value={n}>{n}</option>
                ))}
              </select>
            </div>

            <div className="pagination-buttons">
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => setPage((p) => Math.max(0, p - 1))}
                disabled={currentPage === 0}
                aria-label="Previous page"
              >
                ← Prev
              </button>

              <span style={{ alignSelf: 'center', padding: '0 4px', fontSize: '0.875rem' }}>
                Page {currentPage + 1} / {totalPages}
              </span>

              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => setPage((p) => Math.min(totalPages - 1, p + 1))}
                disabled={currentPage >= totalPages - 1}
                aria-label="Next page"
              >
                Next →
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  )
}
