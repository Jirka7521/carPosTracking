// ============================================================
// telemetry — turning PositionDto rows into something chartable.
//
// Everything the Charts tab needs to know about *what* can be plotted lives in
// the SERIES table below: the label, the unit, the colour and the number of
// decimals. That one array drives the checkbox picker, the <Line> elements, the
// Y axes and the tooltip, so adding a series later means editing one place
// rather than four that can drift apart.
//
// Deliberately free of any Recharts import. The chart component is the only
// file that knows which library draws the pixels; keeping the data shaping out
// of it means this logic stays readable and independently reusable.
// ============================================================

import type { PositionDto } from '../services/apiTypes'
import { parseApiTimestamp } from './dates'

// Every value the chart can plot. The key doubles as the Recharts `dataKey` and
// as the field name on ChartRow, so the two can never fall out of sync.
export type SeriesKey =
  | 'speedKmph'
  | 'altitudeMeters'
  | 'batteryPct'
  | 'temperatureC'
  | 'accelXG'
  | 'accelYG'
  | 'accelZG'
  | 'accelMagG'

// Series sharing a unit share a single Y axis — that is the whole reason the
// unit is modelled explicitly rather than baked into the label.
export type SeriesUnit = 'km/h' | 'm' | '%' | '°C' | 'g'

export type SeriesDef = {
  key:      SeriesKey
  label:    string
  unit:     SeriesUnit
  color:    string
  decimals: number
}

// One row per GPS fix, in the shape Recharts wants: a flat object with numeric
// fields, `t` being the epoch milliseconds the whole chart is keyed on.
// `null` means "the device reported nothing for this fix" and is drawn as a gap.
export type ChartRow = {
  t:              number
  speedKmph:      number | null
  altitudeMeters: number | null
  batteryPct:     number | null
  temperatureC:   number | null
  accelXG:        number | null
  accelYG:        number | null
  accelZG:        number | null
  accelMagG:      number | null
  // Not plottable — carried alongside so the tooltip can say "⚡ Charging"
  // where the battery line breaks. See toChartRows() for why it breaks.
  charging:       boolean
}

// The order Y axes are laid out in, alternating left / right. Fixed rather than
// derived from click order so the layout is a pure function of the selection:
// ticking the same boxes always produces the same chart.
export const UNIT_ORDER = ['km/h', 'm', '%', '°C', 'g'] as const

// The palette avoids red entirely: this app reserves red for danger and delete
// (see index.css), so a red line would read as an alarm. Speed takes the app's
// own primary blue as the headline series; the rest are spaced far enough apart
// in hue and lightness to stay distinguishable with colour-vision deficiency.
// Colour is never the only cue — the picker pairs every swatch with a text
// label and the tooltip names each series.
export const SERIES: readonly SeriesDef[] = [
  { key: 'speedKmph',      label: 'Speed',       unit: 'km/h', color: '#0065BD', decimals: 1 },
  { key: 'altitudeMeters', label: 'Altitude',    unit: 'm',    color: '#9085E9', decimals: 0 },
  { key: 'batteryPct',     label: 'Battery',     unit: '%',    color: '#199E70', decimals: 0 },
  { key: 'temperatureC',   label: 'Temperature', unit: '°C',   color: '#EB6834', decimals: 1 },
  { key: 'accelXG',        label: 'Accel X',     unit: 'g',    color: '#C06FD0', decimals: 2 },
  { key: 'accelYG',        label: 'Accel Y',     unit: 'g',    color: '#EDA100', decimals: 2 },
  { key: 'accelZG',        label: 'Accel Z',     unit: 'g',    color: '#B5651D', decimals: 2 },
  { key: 'accelMagG',      label: 'Accel |a|',   unit: 'g',    color: '#00A3A3', decimals: 2 },
]

// Total acceleration √(x² + y² + z²). Undefined unless all three axes were
// reported for this fix — a missing axis would silently understate the result,
// which is worse than showing a gap.
//
// Caveat when the device runs with `kAccelPeakEnabled`: the firmware then reports
// each axis's own maximum over the reporting interval, and those three maxima can
// come from three different moments. This magnitude is computed from them all the
// same, so it reads HIGHER than any acceleration the device actually measured. It
// is an upper bound in that mode, not a reading.
function accelMagnitude(position: PositionDto): number | null {
  if (position.accelXG === null || position.accelYG === null || position.accelZG === null) {
    return null
  }
  return Math.sqrt(
    position.accelXG ** 2 + position.accelYG ** 2 + position.accelZG ** 2,
  )
}

// Map the API's positions onto chart rows, oldest first.
export function toChartRows(positions: readonly PositionDto[]): ChartRow[] {
  const rows: ChartRow[] = []

  for (const position of positions) {
    // A timestamp we cannot parse would land at epoch 0 and stretch the time
    // axis across five decades, hiding every real sample in one pixel. Dropping
    // the fix is the only sane option.
    const fixedAt: Date | null = parseApiTimestamp(position.timestamp)
    if (fixedAt === null) {
      continue
    }

    rows.push({
      t:              fixedAt.getTime(),
      speedKmph:      position.speedKmph,
      altitudeMeters: position.altitudeMeters,
      // 0 is the agreed "charging" SENTINEL (see BatteryBadge), not zero
      // percent. Plotting it literally would draw a plunge to the floor of a
      // 0–100 axis every time the engine runs, so the line breaks instead and
      // `charging` carries the fact into the tooltip.
      batteryPct:     position.batteryPct === 0 ? null : position.batteryPct,
      charging:       position.batteryPct === 0,
      temperatureC:   position.temperatureC,
      accelXG:        position.accelXG,
      accelYG:        position.accelYG,
      accelZG:        position.accelZG,
      accelMagG:      accelMagnitude(position),
    })
  }

  // The API orders newest-first; a time axis reads oldest-first. Sorting rather
  // than reversing means the chart does not silently depend on that ordering.
  rows.sort((a, b) => a.t - b.t)
  return rows
}

// Thins a long row set down to something a browser can draw without giving up
// the extremes.
//
// Since the Charts tab walks the whole window it can arrive here with tens of
// thousands of rows, which is far more than the chart has pixels — every one of
// them still costs a path segment and a hit-test on every mouse move. The
// obvious fix, keeping every Nth row, is the wrong one for this data: a 3 g
// pothole lasts one fix, and dropping it is dropping the only reason anyone
// plots acceleration.
//
// So the time axis is cut into equal buckets, and each bucket contributes the
// row holding the MINIMUM and the row holding the MAXIMUM of every selected
// series, plus its first row to keep gaps and timing honest. Peaks survive by
// construction; what is lost is only detail finer than one bucket, which is
// finer than one pixel.
//
// The bucket count divides by (2 * series + 1) so the output stays under
// `maxPoints` no matter how many series are ticked.
export function decimateChartRows(
  rows: readonly ChartRow[],
  seriesKeys: readonly SeriesKey[],
  maxPoints: number,
): ChartRow[] {
  // Nothing to gain below the threshold, and no reason to pay for the pass.
  if (rows.length <= maxPoints || seriesKeys.length === 0) {
    return rows as ChartRow[]
  }

  const perBucket: number = 2 * seriesKeys.length + 1
  const bucketCount: number = Math.max(1, Math.floor(maxPoints / perBucket))

  // `rows` is sorted by time (toChartRows guarantees it), so the span is simply
  // the two ends. A zero span means every fix shares one instant — one bucket.
  const firstT: number = rows[0].t
  const lastT: number  = rows[rows.length - 1].t
  const spanMs: number = Math.max(1, lastT - firstT)

  // Indices rather than rows: a Set of row objects would work, but ordering the
  // result then means comparing timestamps again. Indices are already in time
  // order and dedupe just as well.
  const keep: Set<number> = new Set<number>()

  // Walking the rows once and deriving the bucket from the timestamp beats
  // slicing per bucket — the rows are not evenly spaced in time, so bucket
  // boundaries do not fall on regular indices.
  let bucketStart: number = 0
  while (bucketStart < rows.length) {
    const bucketIndex: number = Math.min(
      bucketCount - 1,
      Math.floor(((rows[bucketStart].t - firstT) / spanMs) * bucketCount),
    )

    // How far this bucket reaches.
    let bucketEnd: number = bucketStart
    while (
      bucketEnd < rows.length &&
      Math.min(bucketCount - 1, Math.floor(((rows[bucketEnd].t - firstT) / spanMs) * bucketCount)) === bucketIndex
    ) {
      bucketEnd += 1
    }

    // Anchors the bucket in time even when no series has a reading in it, so a
    // silent stretch stays a visible gap rather than closing up.
    keep.add(bucketStart)

    for (const key of seriesKeys) {
      let minIndex: number = -1
      let maxIndex: number = -1

      for (let index: number = bucketStart; index < bucketEnd; index += 1) {
        const value: number | null = rows[index][key]
        if (value === null) {
          continue
        }
        if (minIndex === -1 || value < (rows[minIndex][key] as number)) {
          minIndex = index
        }
        if (maxIndex === -1 || value > (rows[maxIndex][key] as number)) {
          maxIndex = index
        }
      }

      if (minIndex !== -1) {
        keep.add(minIndex)
      }
      if (maxIndex !== -1) {
        keep.add(maxIndex)
      }
    }

    bucketStart = bucketEnd
  }

  // The very last fix is worth keeping whatever its bucket decided — it is the
  // right-hand end of every line, and the most recent thing the device said.
  keep.add(rows.length - 1)

  const thinned: ChartRow[] = []
  for (let index: number = 0; index < rows.length; index += 1) {
    if (keep.has(index)) {
      thinned.push(rows[index])
    }
  }
  return thinned
}

// Which series actually carry a reading in the loaded rows. Used to disable a
// checkbox rather than hide it, so a series the device is not currently
// reporting (modem temperature, say) is visibly absent rather than mysteriously
// missing — and comes back on its own once the hardware reports again.
export function availableSeriesKeys(rows: readonly ChartRow[]): Set<SeriesKey> {
  return new Set(
    SERIES
      .filter((series) => rows.some((row) => row[series.key] !== null))
      .map((series) => series.key),
  )
}

// One decimal place too many turns "12 km/h" into noise; one too few hides a
// 0.02 g bump. The per-series `decimals` is the answer to both.
export function formatSeriesValue(series: SeriesDef, value: number): string {
  return `${value.toFixed(series.decimals)} ${series.unit}`
}

// Axis tick labels. Within a day the date is redundant and costs horizontal
// room; across days it is the only thing that disambiguates the tick.
export function formatAxisTime(value: number, spanMs: number): string {
  const date = new Date(value)
  const time = date.toLocaleTimeString(undefined, {
    hour:   '2-digit',
    minute: '2-digit',
    hour12: false,
  })

  if (spanMs < 24 * 60 * 60 * 1000) {
    return time
  }

  const day = date.toLocaleDateString(undefined, { day: '2-digit', month: '2-digit' })
  return `${day} ${time}`
}

// The tooltip heading: full date and time, matching how the Positions table
// renders a timestamp so the two tabs read the same.
export function formatTooltipTime(value: number): string {
  return new Date(value).toLocaleString(undefined, { hour12: false })
}
