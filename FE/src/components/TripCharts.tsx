// ============================================================
// TripCharts — every reading the tracker took during a trip, as a stack of
// charts that share one time axis and one crosshair: speed (with the trip's
// average as a dashed line), altitude, battery and acceleration.
//
// One chart per kind of reading rather than one chart of everything: on a trip
// page the question is "what happened at 14:20", and stacked charts answer it
// for every reading at once without four Y axes fighting over one plot.
//
// All charts are handed the SAME rows. Recharts syncs crosshairs by row index,
// so the rows are thinned once, keeping the peaks of every series drawn (see
// decimateChartRows), rather than per chart — per-chart thinning would put a
// different moment under the same index in each.
//
// A reading the tracker did not send during the trip (no accelerometer, say)
// simply has no chart.
// ============================================================

import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { formatSpeed } from '../i18n/format'
import type { PositionDto } from '../services/apiTypes'
import TelemetryChart from './TelemetryChart'
import type { ChartReferenceLine, SeriesDef, SeriesKey } from '../utils/telemetry'
import { SERIES, availableSeriesKeys, decimateChartRows, toChartRows } from '../utils/telemetry'

// Points handed to Recharts per chart. A trip rarely comes near it; a very long
// one is thinned with its peaks kept.
const MAX_PLOT_POINTS: number = 4000

// Dark ink for the average line, so it reads against the blue speed line.
const AVERAGE_LINE_COLOR: string = '#3A4A5A' // --gray-700

const SYNC_ID: string = 'trip'

type ChartGroup = {
  id: 'speed' | 'altitude' | 'battery' | 'acceleration'
  keys: readonly SeriesKey[]
  height: number
}

const GROUPS: readonly ChartGroup[] = [
  { id: 'speed', keys: ['speedKmph'], height: 240 },
  { id: 'altitude', keys: ['altitudeMeters'], height: 200 },
  { id: 'battery', keys: ['batteryPct'], height: 180 },
  { id: 'acceleration', keys: ['accelXG', 'accelYG', 'accelZG', 'accelMagG'], height: 240 },
]

export type TripChartsProps = {
  positions: readonly PositionDto[]
  averageSpeedKmph: number | null
}

export function TripCharts({ positions, averageSpeedKmph }: TripChartsProps) {
  const { t } = useTranslation(['trips', 'device'])

  const rows = useMemo(() => toChartRows(positions), [positions])
  const available = useMemo(() => availableSeriesKeys(rows), [rows])
  const plotRows = useMemo(
    () => decimateChartRows(rows, [...available], MAX_PLOT_POINTS),
    [rows, available],
  )

  if (rows.length === 0) {
    return null
  }

  const titles: Record<ChartGroup['id'], string> = {
    speed: t('trips:detail.charts.speed'),
    altitude: t('trips:detail.charts.altitude'),
    battery: t('trips:detail.charts.battery'),
    acceleration: t('trips:detail.charts.acceleration'),
  }

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
    <div className="trip-charts">
      {GROUPS.map((group) => {
        const series: SeriesDef[] = SERIES.filter(
          (definition) => group.keys.includes(definition.key) && available.has(definition.key),
        )
        if (series.length === 0) {
          return null
        }
        return (
          <section key={group.id} className="trip-section">
            <div className="trip-section-head">
              <h4 className="trip-section-title">{titles[group.id]}</h4>
              {series.length > 1 ? (
                <ul className="trip-chart-legend">
                  {series.map((definition) => (
                    <li key={definition.key}>
                      <span
                        className="series-swatch"
                        style={{ background: definition.color }}
                        aria-hidden="true"
                      />
                      {t(definition.labelKey)}
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
            <TelemetryChart
              rows={plotRows}
              series={series}
              height={group.height}
              referenceLines={group.id === 'speed' ? averageLine : undefined}
              syncId={SYNC_ID}
            />
          </section>
        )
      })}
    </div>
  )
}
