// ============================================================
// SharedUserCard — one row in the "People with access" list
// inside DeviceSettingsTab.
//
// Shows the user's email, full name, and their three capability
// checkboxes. When the caller has canShare the checkboxes are
// editable and "Save changes" / "Remove" buttons appear.
// When the caller only has canModifySettings the checkboxes are
// read-only and no action buttons are shown.
// ============================================================

import { useTranslation } from 'react-i18next'
import { CapabilityCheckboxes } from './CapabilityCheckboxes'
import type { CapabilityFlags } from './capabilityFlags'

// Data for one user's access grant as displayed in the access list.
export type SharedUserData = {
  accessId:          number
  userId:            number
  userEmail:         string
  fullName:          string
  canDelete:         boolean
  canShare:          boolean
  canModifySettings: boolean
}

type SharedUserCardProps = {
  row:          SharedUserData
  // Whether the current viewer has canShare and may edit this row.
  canEdit:      boolean
  // True while an API call for this specific row is in-flight.
  isSaving:     boolean
  // Fired when the user clicks one of the permission checkboxes.
  onToggleFlag: (flag: keyof CapabilityFlags) => void
  // Fired when the user clicks "Save changes".
  onSave:       () => void
  // Fired when the user clicks "Remove".
  onRemove:     () => void
}

export function SharedUserCard({
  row,
  canEdit,
  isSaving,
  onToggleFlag,
  onSave,
  onRemove,
}: SharedUserCardProps) {
  const { t } = useTranslation(['settings', 'common'])

  return (
    <div className="access-row">
      {/* User identity + remove button */}
      <div className="access-row-header">
        <div className="access-row-user">
          <strong>{row.userEmail}</strong>
          {row.fullName ? <span>{row.fullName}</span> : null}
        </div>

        {/* Remove button — only shown when the caller can edit */}
        {canEdit ? (
          <button
            type="button"
            className="btn btn-danger btn-sm"
            onClick={onRemove}
            disabled={isSaving}
          >
            {t('settings:sharing.remove')}
          </button>
        ) : null}
      </div>

      {/* Capability toggles (read-only or interactive depending on canEdit) */}
      <CapabilityCheckboxes
        flags={{
          canDelete:         row.canDelete,
          canShare:          row.canShare,
          canModifySettings: row.canModifySettings,
        }}
        canEdit={canEdit}
        onToggle={onToggleFlag}
      />

      {/* Save button — only shown when the caller can edit */}
      {canEdit ? (
        <div className="access-row-actions">
          <button
            type="button"
            className="btn btn-primary btn-sm"
            onClick={onSave}
            disabled={isSaving}
          >
            {isSaving ? t('common:actions.saving') : t('common:actions.saveChanges')}
          </button>
        </div>
      ) : null}
    </div>
  )
}
