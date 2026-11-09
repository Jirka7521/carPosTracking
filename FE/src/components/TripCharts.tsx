// ============================================================
// TripCharts — every reading the tracker took during a trip, in one chart
// laid out exactly like the Charts tab: the series picker (which is also the
// legend) above one TelemetryChart with a Y axis per unit, plus the trip's
// average speed as a dashed line on the speed axis.
//
// The ticked series are the device page's, the same list the Charts tab uses,
// so a reader who set up the chart there finds it the same way here and back.
//
// A reading the tracker did not send during the trip (no accelerometer, say)
// is greyed out in the picker; ticking nothing gets its own empty state.
// ============================================================

import { useMemo } from 'react'
import type { Dispatch, SetStateAction } from 'react'
import { useTranslation } from 'react-i18next'
import { formatInteger, formatSpeed } from '../i18n/format'
import type { PositionDto } from '../services/apiTypes'
import { SeriesPicker } from './SeriesPicker'
import TelemetryChart from './TelemetryChart'
import type { ChartReferenceLine, SeriesDef, SeriesKey } from '../utils/telemetry'
import { SERIES, availableSeriesKeys, decimateChartRows, toChartRows } from '../utils/telemetry'

// Points handed to Recharts — the Charts tab's figure, for the same full-size
// chart. A trip rarely comes near it; a very long one is thinned with the peaks
// of every ticked series kept.
const MAX_PLOT_POINTS: number = 6000

// Dark ink for the average line, so it reads against the blue speed line.
const AVERAGE_LINE_COLOR: string = '#3A4A5A' // --gray-700

export type TripChartsProps = {
  positions: readonly PositionDto[]
  averageSpeedKmph: number | null
  selectedKeys: readonly SeriesKey[]
  onSelectedKeysChange: Dispatch<SetStateAction<readonly SeriesKey[]>>
}

export function TripCharts({
  positions,
  averageSpeedKmph,
  selectedKeys,
  onSelectedKeysChange,
}: TripChartsProps) {
  const { t } = useTranslation(['trips', 'device'])

  const rows = useMemo(() => toChartRows(positions), [positions])
  const available = useMemo(() => availableSeriesKeys(rows), [rows])
  const series: SeriesDef[] = useMemo(
    () => SERIES.filter((definition) =>
      selectedKeys.includes(definition.key) && available.has(definition.key),
    ),
    [selectedKeys, available],
  )
  const plotRows = useMemo(
    () => decimateChartRows(rows, selectedKeys, MAX_PLOT_POINTS),
    [rows, selectedKeys],
  )

  if (rows.length === 0) {
    return null
  }

  // Drawn only while speed is ticked: the chart skips a line with no axis.
  const averageLine: ChartReferenceLine[] =
    averageSpeedKmph === null
      ? []
      : [{
          unit: 'km/h',
          value: averageSpeedKmph,
          label: t('trips:detail.charts.averageLine', { speed: formatSpeed(averageSpeedKmph) }),
          color: AVERAGE_LINE_COLOR,
        }]

  return (
    <section className="trip-section">
      <div className="trip-section-head">
        <h4 className="trip-section-title">{t('trips:detail.charts.title')}</h4>
      </div>

      <SeriesPicker
        available={available}
        selected={selectedKeys}
        onSelectedChange={onSelectedKeysChange}
      />

      {series.length === 0 ? (
        /* A <Line> pointing at an axis that does not exist throws, so an empty
           selection gets its own state rather than an empty grid. */
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">📈</span>
          <h3>{t('device:charts.noneSelectedTitle')}</h3>
          <p>{t('device:charts.noneSelectedBody')}</p>
        </div>
      ) : (
        <>
          <TelemetryChart rows={plotRows} series={series} referenceLines={averageLine} />

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
    </section>
  )
}
