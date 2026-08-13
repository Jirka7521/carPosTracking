// ============================================================
// DeviceChartsTab — the "Charts" tab inside DevicePage.
//
// The Positions tab answers "what did the device report at 14:32?". This one
// answers "what happened over the afternoon?" — the question a table of a
// thousand rows is genuinely bad at.
//
// Features:
//   • The same date range pickers as the Map and Positions tabs
//   • A checkbox picker for the eight plottable series; it doubles as the
//     chart legend, so colour is never the only thing identifying a line
//   • Series the device did not report in this range are disabled rather than
//     hidden, so their absence is visible
//   • A warning when the server's 1000-row cap truncated the range
//
// Deliberately no auto-refresh, unlike the Map tab: a chart that redraws while
// you are reading a spike is hostile. The refresh button is right there.
// ============================================================

import { useEffect, useMemo, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import TelemetryChart from '../components/TelemetryChart'
import type { DevicePageContext } from './DevicePage'
import { fetchPositions } from '../services/apiClient'
import type { PositionDto } from '../services/apiTypes'
import type { SeriesKey } from '../utils/telemetry'
import {
  SERIES,
  availableSeriesKeys,
  formatTooltipTime,
  toChartRows,
} from '../utils/telemetry'
import { datetimeLocalToIso, formatDateTimeLocal } from '../utils/dates'
import { describeError } from '../utils/errors'

// The API caps every /positions query at this many rows and returns no
// truncation flag, so hitting the cap exactly is the only signal available.
const SERVER_ROW_CAP = 1000

// Speed and battery on first paint: "is it moving" and "is the tracker alive"
// are the two questions worth answering without being asked, and the pair
// demonstrates the two-axis behaviour without a wall of lines.
const DEFAULT_SERIES: readonly SeriesKey[] = ['speedKmph', 'batteryPct']

type DateRange = {
  from: string // datetime-local string (YYYY-MM-DDTHH:mm)
  to:   string // datetime-local string
}

// Returns a date range covering the last 24 hours
function getDefaultDateRange(): DateRange {
  const now       = new Date()
  const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000)
  return {
    from: formatDateTimeLocal(yesterday),
    to:   formatDateTimeLocal(now),
  }
}

export function DeviceChartsTab() {
  const { device } = useOutletContext<DevicePageContext>()

  const [positions, setPositions]         = useState<PositionDto[]>([])
  const [isLoading, setIsLoading]         = useState<boolean>(false)
  const [statusMessage, setStatusMessage] = useState<string>('')

  // Date range controls
  const [dateRange, setDateRange] = useState<DateRange>(getDefaultDateRange)

  // Which series the user has ticked. Kept as keys rather than as full
  // definitions so the SERIES table stays the single source of truth.
  const [selectedKeys, setSelectedKeys] = useState<readonly SeriesKey[]>(DEFAULT_SERIES)

  // Load positions whenever the device or date range changes. Same shape as the
  // Map and Positions tabs — the `canceled` flag stops a slow response from
  // overwriting a newer one (and covers StrictMode's double invocation).
  useEffect(() => {
    let canceled = false

    const load = async (): Promise<void> => {
      setIsLoading(true)
      setStatusMessage('')

      const fromIso = datetimeLocalToIso(dateRange.from)
      const toIso   = datetimeLocalToIso(dateRange.to)

      try {
        const data = await fetchPositions(device.deviceId, fromIso, toIso)
        if (canceled) {
          return
        }

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

  // `positions` only gets a new identity on a fetch, so the reshaping — which
  // touches up to 1000 rows — runs once per load rather than once per render.
  const rows = useMemo(() => toChartRows(positions), [positions])

  // Derived, not stored: a series the user ticked reappears on its own once the
  // device starts reporting it again, with no state to keep in sync.
  const available = useMemo(() => availableSeriesKeys(rows), [rows])

  const series = useMemo(
    () => SERIES.filter((definition) =>
      selectedKeys.includes(definition.key) && available.has(definition.key),
    ),
    [selectedKeys, available],
  )

  // The server returns the NEWEST rows, so a range that hit the cap starts
  // later than the "From" that was asked for — invisible on a chart in a way it
  // is not on a table.
  const isTruncated = positions.length >= SERVER_ROW_CAP && rows.length > 0

  function toggleSeries(key: SeriesKey, checked: boolean): void {
    setSelectedKeys((current) =>
      checked ? [...current, key] : current.filter((existing) => existing !== key),
    )
  }

  // Manual refresh: push "to" to now, which re-triggers the load effect
  function handleRefresh(): void {
    setDateRange((cur) => ({
      ...cur,
      to: formatDateTimeLocal(new Date()),
    }))
  }

  return (
    <div>
      {/* ---- Controls: date pickers + refresh button ---- */}
      <div className="charts-controls-bar">
        <div className="form-field">
          <label className="form-label" htmlFor="chart-from">From</label>
          <input
            id="chart-from"
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
          <label className="form-label" htmlFor="chart-to">To</label>
          <input
            id="chart-to"
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

      {/* ---- Series picker. Also the legend: every entry pairs its colour
              with a text label, so the chart never relies on colour alone. ---- */}
      <fieldset className="series-picker">
        <legend className="form-label">Series</legend>

        {SERIES.map((definition) => {
          const isAvailable = available.has(definition.key)

          return (
            <label
              key={definition.key}
              className="checkbox-field series-chip"
              title={isAvailable ? undefined : 'Not reported in this time range'}
            >
              <input
                type="checkbox"
                checked={selectedKeys.includes(definition.key)}
                disabled={!isAvailable}
                onChange={(e) => toggleSeries(definition.key, e.target.checked)}
              />
              <span
                className="series-swatch"
                style={{ background: definition.color }}
                aria-hidden="true"
              />
              <span>
                {definition.label} <span className="series-chip-unit">({definition.unit})</span>
                {isAvailable ? null : <span className="series-chip-note"> — no data</span>}
              </span>
            </label>
          )
        })}
      </fieldset>

      {/* Status line */}
      <p className="hint" role="status" style={{ marginBottom: 12 }}>
        {statusMessage}
      </p>

      {/* Truncation warning. Naming the first plotted timestamp is the part
          that matters — "1000 rows" alone would not say what is missing. */}
      {isTruncated ? (
        <div className="banner banner--warning" role="status" style={{ marginBottom: 12 }}>
          <span aria-hidden="true">⚠️</span>
          <span>
            The server returns at most {SERVER_ROW_CAP} fixes per query and this
            range reached that limit, so the chart starts at{' '}
            {formatTooltipTime(rows[0].t)} rather than at the “From” you chose.
            Narrow the range to see earlier data.
          </span>
        </div>
      ) : null}

      {/* ---- Loading / empty / chart ---- */}
      {isLoading ? (
        <div className="loading-state">
          <div className="spinner" />
          <span>Loading telemetry…</span>
        </div>
      ) : rows.length === 0 ? (
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">📈</span>
          <h3>No telemetry</h3>
          <p>No GPS fixes were recorded in the selected time range.</p>
        </div>
      ) : series.length === 0 ? (
        /* A <Line> pointing at an axis that does not exist throws, so an empty
           selection gets its own state rather than an empty grid. */
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">📈</span>
          <h3>Nothing selected</h3>
          <p>Tick one or more series above to plot them.</p>
        </div>
      ) : (
        <>
          <TelemetryChart rows={rows} series={series} />

          <p className="hint" style={{ marginTop: 10 }}>
            Each unit gets its own vertical axis. A break in a line means the
            device reported no value for those fixes — for Battery, that also
            happens while it is charging.
          </p>
        </>
      )}
    </div>
  )
}
