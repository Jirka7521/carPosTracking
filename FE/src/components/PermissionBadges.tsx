// ============================================================
// PermissionBadges — compact visual display of a user's capabilities
// on a single device.
//
// Each capability is shown as a small pill:
//   • Green (--granted)  — the user holds this permission
//   • Gray strikethrough (--missing) — the user does not have it
//
// The four capabilities in order of privilege (lowest → highest):
//   Read           — view the device and its position history (always true)
//   Delete         — deactivate the device
//   Share          — manage who else has access (implies Settings)
//   Settings       — modify device settings
//
// CSS classes used are defined in App.css under "permission-badges".
// ============================================================

import type { DevicePermissionsDto } from '../services/apiTypes'

// Descriptor for each capability shown in the badge list
type Capability = {
  key:         keyof DevicePermissionsDto
  label:       string
  description: string  // Shown in the title attribute for accessibility
}

const CAPABILITIES: Capability[] = [
  {
    key:         'canRead',
    label:       'Read',
    description: 'View the device and its position history',
  },
  {
    key:         'canDelete',
    label:       'Delete',
    description: 'Deactivate (soft-delete) the device',
  },
  {
    key:         'canShare',
    label:       'Share',
    description: 'Manage who else has access to this device',
  },
  {
    key:         'canModifySettings',
    label:       'Settings',
    description: 'Modify device settings and view the access roster',
  },
]

export function PermissionBadges({ permissions }: { permissions: DevicePermissionsDto }) {
  return (
    <ul className="permission-badges" aria-label="Your permissions on this device">
      {CAPABILITIES.map((cap) => {
        const granted = permissions[cap.key]

        return (
          <li
            key={cap.key}
            className={`permission-badge ${granted ? 'permission-badge--granted' : 'permission-badge--missing'}`}
            // Title gives screen readers and sighted hoverers the full description
            title={`${cap.description} — ${granted ? 'granted' : 'not granted'}`}
          >
            {/* Visible check / cross icon */}
            <span aria-hidden="true">{granted ? '✓' : '✕'}</span>
            <span>{cap.label}</span>
          </li>
        )
      })}
    </ul>
  )
}
