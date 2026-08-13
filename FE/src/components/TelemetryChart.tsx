// ============================================================
// TelemetryChart — one overlaid line chart with a Y axis per unit.
//
// The interesting problem here is that the selectable series do not share a
// scale: 90 km/h, 420 m, 78 %, 41 °C and 0.03 g cannot sit on one axis without
// flattening everything but the largest into a straight line. Normalising them
// to 0–100 % would fix the shape but throw away the numbers.
//
// So the axes are derived from the selection: series are grouped by UNIT, and
// each unit present gets its own <YAxis>, alternating left and right. All four
// accelerometer series share a single "g" axis, which is what makes comparing
// them meaningful. Recharts stacks multiple axes on the same side outward and
// shrinks the plot area to fit, so the layout holds up to all five units.
//
// This is the only file in the app that imports Recharts. The data shaping and
// the series table live in utils/telemetry.ts.
// ============================================================

import { useMemo } from 'react'
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { ChartRow, SeriesDef, SeriesUnit } from '../utils/telemetry'
import {
  UNIT_ORDER,
  formatAxisTime,
  formatSeriesValue,
  formatTooltipTime,
} from '../utils/telemetry'

const CHART_HEIGHT = 420
const AXIS_WIDTH   = 60

// Ink for an axis shared by several series. No single series colour would be
// honest there, so it falls back to the app's muted text colour.
const AXIS_INK = '#5A6A7A' // --gray-500

// Per-unit axis behaviour. The domains matter more than they look:
// Recharts' YAxis default is [0, 'auto'], which would CLIP every negative
// accelerometer reading without warning.
const UNIT_AXIS: Record<SeriesUnit, { domain: [number | string, number | string]; decimals: number }> = {
  // Speed has a real zero, and anchoring to it keeps "stopped" readable.
  'km/h': { domain: [0, 'auto'],           decimals: 0 },
  // Altitude near sea level would waste most of an axis anchored at zero.
  'm':    { domain: ['auto', 'auto'],      decimals: 0 },
  // A battery axis auto-scaled to 78–82 % turns normal drift into a cliff.
  '%':    { domain: [0, 100],              decimals: 0 },
  '°C':   { domain: ['auto', 'auto'],      decimals: 1 },
  // Negative values are normal here — see the note above about clipping.
  'g':    { domain: ['auto', 'auto'],      decimals: 2 },
}

type TelemetryChartProps = {
  rows:   readonly ChartRow[]
  // The series to draw, already filtered to those the user selected AND that
  // carry data. Never empty — the tab renders an empty state instead.
  series: readonly SeriesDef[]
}

// Recharts hands the tooltip an untyped payload; this is the slice we use.
type TooltipPayloadItem = { payload?: ChartRow }

type TelemetryTooltipProps = {
  active?:  boolean
  payload?: readonly TooltipPayloadItem[]
  series:   readonly SeriesDef[]
}

// A shared crosshair tooltip listing every selected series for the hovered fix.
//
// It reads the whole ChartRow rather than Recharts' per-line payload, which is
// what lets it report "⚡ Charging" for a battery gap — the line has no point
// there precisely because the value was the charging sentinel.
//
// Plain JSX throughout: no dangerouslySetInnerHTML, so nothing here can become
// an injection point even if the data changes shape.
function TelemetryTooltip({ active, payload, series }: TelemetryTooltipProps) {
  const row: ChartRow | undefined = payload?.[0]?.payload
  if (!active || row === undefined) {
    return null
  }

  return (
    <div className="chart-tooltip">
      <div className="chart-tooltip-time">{formatTooltipTime(row.t)}</div>

      <ul className="chart-tooltip-list">
        {series.map((definition) => {
          const value = row[definition.key]

          return (
            <li key={definition.key}>
              <span
                className="series-swatch"
                style={{ background: definition.color }}
                aria-hidden="true"
              />
              <span>{definition.label}</span>
              <span className="chart-tooltip-value">
                {value === null
                  ? (definition.key === 'batteryPct' && row.charging ? '⚡ Charging' : '—')
                  : formatSeriesValue(definition, value)}
              </span>
            </li>
          )
        })}
      </ul>
    </div>
  )
}

export default function TelemetryChart({ rows, series }: TelemetryChartProps) {
  // Which axes to draw, and on which side. Derived from UNIT_ORDER rather than
  // from the order boxes were ticked, so the same selection always lays out the
  // same way. The trade-off: enabling a unit early in the order can flip a
  // later axis to the opposite side. Preferable to a fixed side per unit, which
  // could pile three axes on the left and leave the right empty.
  const axes = useMemo(() => {
    return UNIT_ORDER
      .filter((unit) => series.some((definition) => definition.unit === unit))
      .map((unit, index) => {
        const members = series.filter((definition) => definition.unit === unit)
        return {
          unit,
          orientation: index % 2 === 0 ? ('left' as const) : ('right' as const),
          // One series on this axis → tint the scale to match its line, so the
          // two pair at a glance. Several → neutral ink.
          color: members.length === 1 ? members[0].color : AXIS_INK,
          ...UNIT_AXIS[unit],
        }
      })
  }, [series])

  // Ticks drop the date when everything fits inside a day.
  const spanMs = rows.length > 1 ? rows[rows.length - 1].t - rows[0].t : 0

  return (
    <div className="chart-frame telemetry-chart">
      <ResponsiveContainer width="100%" height={CHART_HEIGHT}>
        <LineChart data={rows as ChartRow[]} margin={{ top: 8, right: 8, bottom: 4, left: 8 }}>
          {/* Horizontal rules only: vertical ones compete with the lines. */}
          <CartesianGrid strokeDasharray="3 3" vertical={false} />

          {/*
            A numeric time axis, not a category one. Fixes arrive irregularly —
            a category axis would space them evenly and quietly misrepresent
            how long the vehicle stood still.
          */}
          <XAxis
            dataKey="t"
            type="number"
            scale="time"
            domain={['dataMin', 'dataMax']}
            tickFormatter={(value: number) => formatAxisTime(value, spanMs)}
            minTickGap={48}
            tickMargin={8}
            height={28}
          />

          {axes.map((axis) => (
            <YAxis
              key={axis.unit}
              yAxisId={axis.unit}
              orientation={axis.orientation}
              domain={axis.domain}
              width={AXIS_WIDTH}
              tickLine={false}
              tickFormatter={(value: number) => value.toFixed(axis.decimals)}
              tick={{ fill: axis.color, fontSize: 11 }}
              label={{
                value:    axis.unit,
                angle:    -90,
                position: axis.orientation === 'left' ? 'insideLeft' : 'insideRight',
                fill:     axis.color,
                fontSize: 11,
              }}
            />
          ))}

          <Tooltip
            content={(props) => <TelemetryTooltip {...props} series={series} />}
            cursor={{ stroke: '#9DC4E8', strokeWidth: 1 }}
            isAnimationActive={false}
          />

          {series.map((definition) => (
            <Line
              key={definition.key}
              yAxisId={definition.unit}
              dataKey={definition.key}
              name={definition.label}
              // Straight segments. A monotone curve would invent motion between
              // two samples that may be minutes apart.
              type="linear"
              stroke={definition.color}
              // The derived magnitude is a summary of the three raw axes, so it
              // is drawn slightly heavier to read as the top-level line.
              strokeWidth={definition.key === 'accelMagG' ? 2.5 : 2}
              // Up to 1000 samples per series: dots would be a solid smear.
              dot={false}
              activeDot={{ r: 4, strokeWidth: 0 }}
              // A gap means the device reported nothing. Bridging it would
              // invent a reading that was never taken.
              connectNulls={false}
              // Re-animating thousands of points on every refresh is janky and
              // buys nothing on a static history chart.
              isAnimationActive={false}
            />
          ))}
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}
