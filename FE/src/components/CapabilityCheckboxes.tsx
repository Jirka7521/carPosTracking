// ============================================================
// CapabilityCheckboxes — renders the three editable permission flags
// (Delete, Share, Settings) for one user's access grant.
//
// Design decisions:
//   • "Read" is always granted. It is shown as a permanently-checked,
//     disabled checkbox so the user understands it exists but cannot
//     be removed.
//   • "Share implies Settings": checking the Share box also forces
//     Settings on and locks it. Unchecking Share releases the lock.
//     This mirrors the server-side invariant so the UI never lets the
//     caller create a state the server would reject.
//   • `canEdit` lets parent components render the same component in
//     read-only mode (e.g. when the caller only has canModifySettings,
//     not canShare).
// ============================================================

import { useTranslation } from 'react-i18next'
import type { CapabilityFlags } from './capabilityFlags'

type CapabilityCheckboxesProps = {
  // Current state of the three flags.
  flags: CapabilityFlags
  // Whether the checkboxes are interactive (true) or read-only (false).
  canEdit: boolean
  // Callback fired when the user clicks one of the checkboxes.
  // The parent is responsible for updating the flag in its own state.
  onToggle: (flag: keyof CapabilityFlags) => void
}

export function CapabilityCheckboxes({ flags, canEdit, onToggle }: CapabilityCheckboxesProps) {
  const { t } = useTranslation('common')

  // Settings is locked (forced true) while Share is active.
  // If we allowed Settings to be unchecked while Share is on, the server
  // would just re-enable it — so we prevent the inconsistent state here.
  const settingsLocked = flags.canShare

  return (
    <div className="access-flags-row">
      {/* Read is always granted — shown as a non-interactive badge */}
      <label className="checkbox-field">
        <input type="checkbox" checked readOnly disabled />
        <span>{t('permission.readAlways')}</span>
      </label>

      <label className="checkbox-field">
        <input
          type="checkbox"
          checked={flags.canDelete}
          disabled={!canEdit}
          onChange={() => onToggle('canDelete')}
        />
        <span>{t('permission.delete')}</span>
      </label>

      <label className="checkbox-field">
        <input
          type="checkbox"
          checked={flags.canShare}
          disabled={!canEdit}
          onChange={() => onToggle('canShare')}
        />
        <span>{t('permission.share')}</span>
      </label>

      <label className="checkbox-field">
        <input
          type="checkbox"
          // Show as checked when explicitly set OR when locked by Share.
          checked={flags.canModifySettings || settingsLocked}
          disabled={!canEdit || settingsLocked}
          onChange={() => onToggle('canModifySettings')}
        />
        <span>
          {t('permission.settings')}
          {settingsLocked ? (
            <span className="hint" style={{ marginLeft: 4 }}>{t('permission.includedWithShare')}</span>
          ) : null}
        </span>
      </label>
    </div>
  )
}
