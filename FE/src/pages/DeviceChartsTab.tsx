// ============================================================
// DeviceChartsTab — the "Charts" tab inside DevicePage.
//
// The Positions tab answers "what did the device report at 14:32?". This one
// answers "what happened over the afternoon?" — the question a table of a
// thousand rows is genuinely bad at.
//
// Features:
//   • The same date range pickers as the Map and Positions tabs — chosen once
//     on mount and changed only by the user; a refresh re-runs the same query
//   • A checkbox picker for the eight plottable series; it doubles as the
//     chart legend, so colour is never the only thing identifying a line
//   • Series the device did not report in this range are disabled rather than
//     hidden, so their absence is visible
//   • A warning when the server's 1000-row cap truncated the range
//
// Auto-refresh only ever adds the points that have arrived since; the axes, the
// ticked series and the range all stay where they were, so a chart you are
// reading does not move under you. Switch it off to freeze the data entirely.
// ============================================================

import { useEffect, useMemo, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import RangeToolbar from '../components/RangeToolbar'
import TelemetryChart from '../components/TelemetryChart'
import type { DevicePageContext } from './DevicePage'
import { useAutoRefresh } from '../hooks/useAutoRefresh'
import { fetchPositions } from '../services/apiClient'
import type { PositionDto } from '../services/apiTypes'
import type { SeriesKey } from '../utils/telemetry'
import {
  SERIES,
  availableSeriesKeys,
  formatTooltipTime,
  toChartRows,
} from '../utils/telemetry'
import type { DateRange } from '../utils/dates'
import { datetimeLocalToIso, getDefaultDateRange } from '../utils/dates'
import { describeError } from '../utils/errors'

// The API caps every /positions query at this many rows and returns no
// truncation flag, so hitting the cap exactly is the only signal available.
const SERVER_ROW_CAP = 1000

// How many seconds between automatic refreshes when the toggle is on
const AUTO_REFRESH_SEC = 30

// Speed and battery on first paint: "is it moving" and "is the tracker alive"
// are the two questions worth answering without being asked, and the pair
// demonstrates the two-axis behaviour without a wall of lines.
const DEFAULT_SERIES: readonly SeriesKey[] = ['speedKmph', 'batteryPct']

export function DeviceChartsTab() {
  const { device } = useOutletContext<DevicePageContext>()

  const [positions, setPositions]         = useState<PositionDto[]>([])
  const [isLoading, setIsLoading]         = useState<boolean>(false)
  const [statusMessage, setStatusMessage] = useState<string>('')

  // Date range controls. Computed once, on mount — from here on only the two
  // inputs change it, so a reload can never move the window under the user.
  const [dateRange, setDateRange] = useState<DateRange>(getDefaultDateRange)

  // Auto-refresh: bumps a token to re-run the query, never the date range
  const refresh = useAutoRefresh(AUTO_REFRESH_SEC)

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
        // Keep the plotted data — a momentary network blip should report itself
        // in the status line, not blank the chart.
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

  return (
    <div>
      {/* ---- Controls: date pickers + refresh options ---- */}
      <RangeToolbar
        range={dateRange}
        onRangeChange={setDateRange}
        autoRefresh={refresh}
        isLoading={isLoading}
        idPrefix="chart"
        className="charts-controls-bar"
      />

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

      {/* ---- Loading / empty / chart ----
           The spinner only stands in for a chart that isn't there yet; a
           refresh leaves the drawn chart in place. */}
      {isLoading && rows.length === 0 ? (
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
