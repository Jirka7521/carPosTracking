// ============================================================
// DeviceCard — a single card in the device grid on the Home page.
//
// The entire card is rendered as a React Router <Link> so the whole
// area is keyboard-navigable and screen-reader accessible.
//
// Display name priority (see utils/devices.ts):
//   1. device.customName  — the user's private alias for this device
//   2. device.displayName — the shared name set at registration
//   3. device.deviceId    — the MQTT identity, which always exists
//
// When the label is not the device id, the id is also shown as a
// subtitle so the physical device stays findable.
// ============================================================

import { Link } from 'react-router-dom'
import { PermissionBadges } from './PermissionBadges'
import { BatteryBadge } from './BatteryBadge'
import type { DeviceDto } from '../services/apiTypes'
import { deviceLabel, hasDistinctLabel } from '../utils/devices'
import { formatRelativeTime } from '../utils/dates'

// Props match DeviceDto directly — the parent passes a full device object.
type DeviceCardProps = {
  device: DeviceDto
}

export function DeviceCard({ device }: DeviceCardProps) {
  const registeredDate = new Date(device.createdAt).toLocaleDateString()
  const deactivatedDate = device.deactivatedAt
    ? new Date(device.deactivatedAt).toLocaleDateString()
    : null

  const label = deviceLabel(device)

  return (
    /*
     * The entire card is a Link so keyboard users can tab to it and
     * screen readers announce it as a navigation element.
     */
    <Link
      to={`/device/${encodeURIComponent(device.deviceId)}/map`}
      className="device-card"
      aria-label={`Open device ${label}`}
    >
      {/* Display name + battery + status badge row */}
      <div className="device-card-header">
        <span className="device-card-uuid">{label}</span>

        <div className="device-card-badges">
          {/* Battery from the latest fix; renders nothing when the device sent
              none, so a sensor-less device shows no empty slot. */}
          <BatteryBadge value={device.lastBatteryPct} />

          <span
            className={`status-badge ${device.isActive ? 'status-badge--active' : 'status-badge--inactive'}`}
          >
            {device.isActive ? 'Active' : 'Inactive'}
          </span>
        </div>
      </div>

      {/* Show the MQTT device id as a subtitle when the label differs from it,
          so the underlying identifier is still visible. */}
      {hasDistinctLabel(device) ? (
        <div className="device-card-meta" style={{ fontFamily: 'monospace', fontSize: '0.75rem' }}>
          {device.deviceId}
        </div>
      ) : null}

      {/* Registration / deactivation date */}
      <div className="device-card-meta">
        {device.isActive
          ? `Registered ${registeredDate}`
          : `Deactivated ${deactivatedDate ?? ''}`}
      </div>

      {/* Liveness. The tracker sends no heartbeat, so this only moves when a
          real fix arrives — "never reported" usually means the firmware has
          not been flashed with this device's config yet. */}
      {device.isActive ? (
        <div className="device-card-meta">
          Last fix: {formatRelativeTime(device.lastSeenAt)}
        </div>
      ) : null}

      {/* What the current user is allowed to do on this device */}
      <PermissionBadges permissions={device.permissions} />

      {/* Call-to-action link text */}
      <div className="device-card-footer">
        <span className="btn btn-secondary btn-sm">Open →</span>
      </div>
    </Link>
  )
}
