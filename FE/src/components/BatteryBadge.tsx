// ============================================================
// BatteryBadge — a compact pill showing a device's battery level.
//
// The value comes straight from the wire (DeviceDto.lastBatteryPct or
// PositionDto.batteryPct):
//   • null / undefined → the device reported no battery: render nothing, so a
//                        device without the sensor shows no empty placeholder.
//   • 0                → the agreed "charging" SENTINEL: shown as "⚡ Charging",
//                        never as a flat battery.
//   • 1–100            → "🔋 N%", coloured by how much charge is left.
//
// CSS classes are defined in App.css under "battery-badge", mirroring the
// status-badge / permission-badge pill idiom already used across the app.
// ============================================================

// A single-cell Li-ion pack; below these thresholds the badge shifts colour so a
// low battery reads at a glance without parsing the number.
const LOW_THRESHOLD = 20
const MEDIUM_THRESHOLD = 50

type BatteryBadgeProps = {
  // Battery percentage as delivered by the API. 0 means charging; null means the
  // device sent no battery reading at all.
  value: number | null | undefined
  // Larger, standalone rendering for the device page header (vs. the small pill
  // used on the cards). Purely visual — adds the `battery-badge--lg` modifier.
  large?: boolean
}

// Maps a (non-charging) percentage to the colour modifier for its charge level.
function levelModifier(pct: number): string {
  if (pct <= LOW_THRESHOLD) {
    return 'battery-badge--low'
  }
  if (pct <= MEDIUM_THRESHOLD) {
    return 'battery-badge--medium'
  }
  return 'battery-badge--high'
}

export function BatteryBadge({ value, large = false }: BatteryBadgeProps) {
  // No reading → nothing to show. Callers can drop the badge in unconditionally.
  if (value === null || value === undefined) {
    return null
  }

  const sizeClass = large ? ' battery-badge--lg' : ''
  const charging = value === 0

  if (charging) {
    return (
      <span
        className={`battery-badge battery-badge--charging${sizeClass}`}
        title="Charging"
      >
        <span aria-hidden="true">⚡</span>
        <span>Charging</span>
      </span>
    )
  }

  return (
    <span
      className={`battery-badge ${levelModifier(value)}${sizeClass}`}
      title={`Battery ${value}%`}
    >
      <span aria-hidden="true">🔋</span>
      <span>{value}%</span>
    </span>
  )
}
