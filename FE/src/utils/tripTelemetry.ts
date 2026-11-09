// ============================================================
// Trip telemetry — the figures the trip page adds on top of what the server
// computes, worked out in the browser from the same fixes it charts.
//
// The server's figures (distance, duration, speeds) come with the trip and are
// the authoritative ones: the list shows them too, and they must agree. What is
// here only describes the readings themselves — the altitude range and climb,
// the battery at either end, and where the tracker went quiet — so computing it
// from the fixes already loaded for the charts costs nothing extra and stores
// nothing anywhere.
// ============================================================

import type { PositionDto } from '../services/apiTypes'
import { parseApiTimestamp } from './dates'

// A silence longer than this between two fixes is reported as a gap. Mirrors
// TripRules.DataGapSeconds, which is what sets the trip's `hasGap` on the API.
const DATA_GAP_SECONDS: number = 300

// Altitude changes smaller than this are not counted as climbing. GNSS altitude
// wanders by several metres between fixes on flat ground; summing every upward
// wobble would report hundreds of metres of "climb" on a motorway.
const CLIMB_HYSTERESIS_METERS: number = 5

export type TripGap = {
  from: Date
  to: Date
}

export type TripFixSummary = {
  altitudeMin: number | null
  altitudeMax: number | null
  // Total ascent, counting only rises beyond CLIMB_HYSTERESIS_METERS.
  climbMeters: number | null
  // First and last battery reading; 0 is the "charging" sentinel, kept as is
  // so the caller can show it as charging rather than as empty.
  batteryStart: number | null
  batteryEnd: number | null
  gaps: TripGap[]
}

// `positions` in any order; they are sorted by fix time here.
export function summarizeTripFixes(positions: readonly PositionDto[]): TripFixSummary {
  const timed = positions
    .map((position) => ({ position, at: parseApiTimestamp(position.timestamp) }))
    .filter((entry): entry is { position: PositionDto; at: Date } => entry.at !== null)
    .sort((a, b) => a.at.getTime() - b.at.getTime())

  let altitudeMin: number | null = null
  let altitudeMax: number | null = null
  let climb = 0
  let anchor: number | null = null
  let batteryStart: number | null = null
  let batteryEnd: number | null = null
  const gaps: TripGap[] = []

  for (let index = 0; index < timed.length; index += 1) {
    const { position, at } = timed[index]

    const altitude: number = position.altitudeMeters
    if (Number.isFinite(altitude)) {
      altitudeMin = altitudeMin === null ? altitude : Math.min(altitudeMin, altitude)
      altitudeMax = altitudeMax === null ? altitude : Math.max(altitudeMax, altitude)
      // Hysteresis: the anchor follows the altitude down freely, and moves up —
      // counting the rise — only once it has risen by more than the threshold.
      if (anchor === null || altitude < anchor) {
        anchor = altitude
      } else if (altitude - anchor > CLIMB_HYSTERESIS_METERS) {
        climb += altitude - anchor
        anchor = altitude
      }
    }

    if (position.batteryPct !== null) {
      batteryStart ??= position.batteryPct
      batteryEnd = position.batteryPct
    }

    if (index > 0) {
      const previous: Date = timed[index - 1].at
      if ((at.getTime() - previous.getTime()) / 1000 > DATA_GAP_SECONDS) {
        gaps.push({ from: previous, to: at })
      }
    }
  }

  return {
    altitudeMin,
    altitudeMax,
    climbMeters: altitudeMin === null ? null : climb,
    batteryStart,
    batteryEnd,
    gaps,
  }
}
