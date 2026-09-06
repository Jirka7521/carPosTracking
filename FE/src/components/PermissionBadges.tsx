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

import { useTranslation } from 'react-i18next'
import type { DevicePermissionsDto } from '../services/apiTypes'

// Descriptor for each capability shown in the badge list. The name and the
// description are translation keys — `as const` keeps them literal types so
// t() can check them against the catalogue.
const CAPABILITIES = [
  { key: 'canRead',           labelKey: 'permission.read',     descriptionKey: 'permission.readHint' },
  { key: 'canDelete',         labelKey: 'permission.delete',   descriptionKey: 'permission.deleteHint' },
  { key: 'canShare',          labelKey: 'permission.share',    descriptionKey: 'permission.shareHint' },
  { key: 'canModifySettings', labelKey: 'permission.settings', descriptionKey: 'permission.settingsHint' },
] as const satisfies readonly { key: keyof DevicePermissionsDto; labelKey: string; descriptionKey: string }[]

export function PermissionBadges({ permissions }: { permissions: DevicePermissionsDto }) {
  const { t } = useTranslation('common')

  return (
    <ul className="permission-badges" aria-label={t('permission.listLabel')}>
      {CAPABILITIES.map((cap) => {
        const granted = permissions[cap.key]

        return (
          <li
            key={cap.key}
            className={`permission-badge ${granted ? 'permission-badge--granted' : 'permission-badge--missing'}`}
            // Title gives screen readers and sighted hoverers the full description
            title={t(granted ? 'permission.grantedTitle' : 'permission.missingTitle', {
              description: t(cap.descriptionKey),
            })}
          >
            {/* Visible check / cross icon */}
            <span aria-hidden="true">{granted ? '✓' : '✕'}</span>
            <span>{t(cap.labelKey)}</span>
          </li>
        )
      })}
    </ul>
  )
}
