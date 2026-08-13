// ============================================================
// PositionListTab — the "Positions" tab inside DevicePage.
//
// Displays a paginated table of GPS positions, sorted newest-first
// so the most recent fix is always at the top.
//
// Features:
//   • Date range pickers matching those on the Map tab
//   • "Refresh" button
//   • Zebra-striped table with blue header row
//   • The newest row is highlighted with a light-blue tint and a
//     "Latest" badge in the timestamp column
//   • Coordinates displayed in monospace for alignment
//   • Pagination: N rows per page with Prev / Next controls
// ============================================================

import { useEffect, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import type { DevicePageContext } from './DevicePage'
import { fetchPositions } from '../services/apiClient'
import type { PositionDto } from '../services/apiTypes'
import { datetimeLocalToIso, formatDateTimeLocal, parseApiTimestamp } from '../utils/dates'
import { describeError } from '../utils/errors'

const PAGE_SIZE_OPTIONS = [25, 50, 100] as const
type PageSize = (typeof PAGE_SIZE_OPTIONS)[number]

type DateRange = {
  from: string
  to:   string
}

function getDefaultDateRange(): DateRange {
  const now       = new Date()
  const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000)
  return {
    from: formatDateTimeLocal(yesterday),
    to:   formatDateTimeLocal(now),
  }
}

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

  // Date range controls
  const [dateRange, setDateRange] = useState<DateRange>(getDefaultDateRange)

  // Current page index (0-based)
  const [page, setPage]         = useState<number>(0)
  const [pageSize, setPageSize] = useState<PageSize>(25)

  // Load positions whenever the device or date range changes
  useEffect(() => {
    let canceled = false

    const load = async (): Promise<void> => {
      setIsLoading(true)
      setStatusMessage('')
      // Reset to first page whenever the data source changes
      setPage(0)

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

  // ---- Pagination calculations ----
  const totalPages    = Math.max(1, Math.ceil(positions.length / pageSize))
  const currentPage   = Math.min(page, totalPages - 1) // clamp after data reload
  const pageStart     = currentPage * pageSize
  const pageEnd       = Math.min(pageStart + pageSize, positions.length)
  const pageRows      = positions.slice(pageStart, pageEnd)

  // Manual refresh: push "to" to now
  function handleRefresh(): void {
    setDateRange((cur) => ({
      ...cur,
      to: formatDateTimeLocal(new Date()),
    }))
  }

  return (
    <div>
      {/* ---- Controls: date pickers + refresh button ---- */}
      <div className="position-list-controls">
        <div className="form-field">
          <label className="form-label" htmlFor="pos-from">From</label>
          <input
            id="pos-from"
            className="form-input"
            type="datetime-local"
            value={dateRange.from}
            onChange={(e) =>
              setDateRange((cur) => ({ ...cur, from: e.target.value }))
            }
            style={{ width: 'auto' }}
          />
        </div>

        <div className="form-field">
          <label className="form-label" htmlFor="pos-to">To</label>
          <input
            id="pos-to"
            className="form-input"
            type="datetime-local"
            value={dateRange.to}
            onChange={(e) =>
              setDateRange((cur) => ({ ...cur, to: e.target.value }))
            }
            style={{ width: 'auto' }}
          />
        </div>

        <button
          type="button"
          className="btn btn-secondary"
          onClick={handleRefresh}
          disabled={isLoading}
          style={{ alignSelf: 'flex-end' }}
        >
          {isLoading ? (
            <>
              <span className="spinner" style={{ width: 14, height: 14, borderWidth: 2 }} />
              Loading…
            </>
          ) : (
            '↻ Refresh'
          )}
        </button>
      </div>

      {/* Status line */}
      <p className="hint" role="status" style={{ marginBottom: 12 }}>
        {statusMessage}
      </p>

      {/* ---- Loading state ---- */}
      {isLoading ? (
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
