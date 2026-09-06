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

import { useEffect, useMemo, useRef, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { formatInteger } from '../i18n/format'
import RangeToolbar from '../components/RangeToolbar'
import TelemetryChart from '../components/TelemetryChart'
import type { DevicePageContext } from './DevicePage'
import type { PositionDto } from '../services/apiTypes'
import { fetchAllPositions, fetchPositionChunk, mergeNewest } from '../services/positionPager'
import type { SeriesKey } from '../utils/telemetry'
import {
  SERIES,
  availableSeriesKeys,
  decimateChartRows,
  formatTooltipTime,
  toChartRows,
} from '../utils/telemetry'
import type { DateRange } from '../utils/dates'
import { datetimeLocalToIso, getDefaultDateRange } from '../utils/dates'
import { describeError } from '../utils/errors'

// Ceiling on one full load. The API hands out 1000 rows at a time, so this is
// fifty sequential requests in the worst case — enough for weeks of history at
// a normal reporting interval, and a stopping point for a range that would
// otherwise walk back through a year.
const MAX_CHART_ROWS = 50_000

// How many points actually reach Recharts. Every point costs a path segment and
// a hit-test on every mouse move, and the chart is about a thousand pixels wide;
// past this the extra rows buy nothing but lag. decimateChartRows() keeps the
// peaks, so the shape survives the thinning.
const MAX_PLOT_POINTS = 6000

// Speed and battery on first paint: "is it moving" and "is the tracker alive"
// are the two questions worth answering without being asked, and the pair
// demonstrates the two-axis behaviour without a wall of lines.
const DEFAULT_SERIES: readonly SeriesKey[] = ['speedKmph', 'batteryPct']

export function DeviceChartsTab() {
  const { t } = useTranslation(['device', 'common', 'errors'])

  // The auto-refresh here is the DEVICE PAGE's timer, not one of this tab's own. It
  // bumps a token to re-run the query below and never touches the date range —
  // and because the header's battery and last-fix hang off the same token,
  // pressing Refresh here can never leave the two disagreeing.
  const { device, autoRefresh: refresh } = useOutletContext<DevicePageContext>()

  const [positions, setPositions]         = useState<PositionDto[]>([])
  const [isLoading, setIsLoading]         = useState<boolean>(false)
  const [statusMessage, setStatusMessage] = useState<string>('')

  // True when MAX_CHART_ROWS stopped the walk before the window ran out, so the
  // oldest part of the chosen range is genuinely missing. Reported by the loader
  // rather than guessed at from the row count.
  const [reachedCap, setReachedCap] = useState<boolean>(false)

  // Which query the rows on screen belong to. A refresh tick re-runs the effect
  // with this unchanged, which is how a top-up is told apart from a fresh load.
  const loadedQueryKey = useRef<string>('')

  // A mirror of `positions` the effect can read without listing it as a
  // dependency — a merge needs the current rows, and depending on them would
  // make the effect re-run on its own result.
  const positionsRef = useRef<PositionDto[]>([])

  function applyPositions(next: PositionDto[]): void {
    positionsRef.current = next
    setPositions(next)
  }

  // Date range controls. Computed once, on mount — from here on only the two
  // inputs change it, so a reload can never move the window under the user.
  const [dateRange, setDateRange] = useState<DateRange>(getDefaultDateRange)

  // Which series the user has ticked. Kept as keys rather than as full
  // definitions so the SERIES table stays the single source of truth.
  const [selectedKeys, setSelectedKeys] = useState<readonly SeriesKey[]>(DEFAULT_SERIES)

  // Load positions whenever the device or date range changes. Same shape as the
  // Map and Positions tabs — the `canceled` flag stops a slow response from
  // overwriting a newer one (and covers StrictMode's double invocation), and it
  // is what stops a long walk mid-way when the range moves under it.
  //
  // Two different loads share this effect:
  //
  //   • a new device or range walks the WHOLE window, however many requests
  //     that takes, because a chart missing its oldest half says nothing about
  //     it being missing;
  //   • a refresh tick fetches only the newest chunk and merges it, because
  //     fixes are append-only and re-walking forty chunks every thirty seconds
  //     would be absurd.
  useEffect(() => {
    let canceled = false
    const isCanceled = (): boolean => canceled

    const fromIso = datetimeLocalToIso(dateRange.from)
    const toIso   = datetimeLocalToIso(dateRange.to)

    const queryKey = `${device.deviceId}|${fromIso ?? ''}|${toIso ?? ''}`
    const isTopUp  = loadedQueryKey.current === queryKey

    const load = async (): Promise<void> => {
      setIsLoading(true)

      // A different device or window: the chart on screen is now answering the
      // wrong question, so it goes rather than lingering through the walk.
      if (!isTopUp) {
        applyPositions([])
        setReachedCap(false)
        setStatusMessage(t('device:charts.loadingPositions'))
      }

      try {
        if (isTopUp) {
          // One request. `seenIds` starts empty because everything in the batch
          // is either new or already held, and mergeNewest settles that by id.
          const chunk = await fetchPositionChunk(
            device.deviceId, fromIso, toIso, new Set<number>(),
          )
          if (canceled) {
            return
          }
          applyPositions(mergeNewest(positionsRef.current, chunk.rows))
        } else {
          const result = await fetchAllPositions(device.deviceId, fromIso, toIso, {
            maxRows: MAX_CHART_ROWS,
            isCanceled,
            // The walk is sequential by necessity, so a wide range can take a
            // while. Counting up beats a spinner that says nothing.
            onProgress: (loaded) => {
              if (!canceled) {
                setStatusMessage(t('device:charts.loadingProgress', {
                  count: loaded,
                  value: formatInteger(loaded),
                }))
              }
            },
          })
          if (canceled) {
            return
          }
          applyPositions(result.positions)
          setReachedCap(result.reachedCap)
          loadedQueryKey.current = queryKey
        }

        const total = positionsRef.current.length
        setStatusMessage(
          total === 0
            ? t('device:charts.noPositions')
            : t('device:charts.found', { count: total, value: formatInteger(total) }),
        )
      } catch (error) {
        if (canceled) {
          return
        }
        // Keep the plotted data — a momentary network blip should report itself
        // in the status line, not blank the chart.
        setStatusMessage(describeError(error, t('errors:loadPositionsFailed')))
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
    // `t` is deliberately not a dependency: it only produces the status line,
    // and listing it would re-walk the whole range on a language change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [device.deviceId, dateRange.from, dateRange.to, refresh.token])

  // `positions` only gets a new identity on a fetch, so the reshaping — which
  // now touches tens of thousands of rows — runs once per load rather than once
  // per render.
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

  // What Recharts is actually handed. Depends on the SELECTION as well as the
  // rows, because which peaks have to survive the thinning is exactly the
  // question of which series are drawn.
  const plotRows = useMemo(
    () => decimateChartRows(rows, selectedKeys, MAX_PLOT_POINTS),
    [rows, selectedKeys],
  )

  // The loader walks backwards from the newest fix, so a range that hit the row
  // ceiling starts later than the "From" that was asked for — invisible on a
  // chart in a way it is not on a table.
  const isTruncated = reachedCap && rows.length > 0

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
        <legend className="form-label">{t('device:charts.series.legend')}</legend>

        {SERIES.map((definition) => {
          const isAvailable = available.has(definition.key)

          return (
            <label
              key={definition.key}
              className="checkbox-field series-chip"
              title={isAvailable ? undefined : t('device:charts.notReported')}
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
                {t(definition.labelKey)}{' '}
                <span className="series-chip-unit">({definition.unit})</span>
                {isAvailable ? null : (
                  <span className="series-chip-note"> {t('device:charts.noData')}</span>
                )}
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
            {t('device:charts.truncated', {
              limit: formatInteger(MAX_CHART_ROWS),
              start: formatTooltipTime(rows[0].t),
            })}
          </span>
        </div>
      ) : null}

      {/* ---- Loading / empty / chart ----
           The spinner only stands in for a chart that isn't there yet; a
           refresh leaves the drawn chart in place. */}
      {isLoading && rows.length === 0 ? (
        <div className="loading-state">
          <div className="spinner" />
          <span>{t('device:charts.loading')}</span>
        </div>
      ) : rows.length === 0 ? (
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">📈</span>
          <h3>{t('device:charts.emptyTitle')}</h3>
          <p>{t('device:charts.emptyBody')}</p>
        </div>
      ) : series.length === 0 ? (
        /* A <Line> pointing at an axis that does not exist throws, so an empty
           selection gets its own state rather than an empty grid. */
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">📈</span>
          <h3>{t('device:charts.noneSelectedTitle')}</h3>
          <p>{t('device:charts.noneSelectedBody')}</p>
        </div>
      ) : (
        <>
          <TelemetryChart rows={plotRows} series={series} />

          <p className="hint" style={{ marginTop: 10 }}>
            {t('device:charts.axisNote')}
            {plotRows.length < rows.length ? (
              <>
                {' '}
                {t('device:charts.decimated', {
                  drawn: formatInteger(plotRows.length),
                  total: formatInteger(rows.length),
                })}
              </>
            ) : null}
          </p>
        </>
      )}
    </div>
  )
}
