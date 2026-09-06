// ---------------------------------------------------------------------------
// ConfigOverrideDialog — the warning before a manual save on a scheduled device.
//
// The surprise it exists to prevent is severe and silent: you change the
// reporting interval, walk away, and hours later the schedule puts it back with
// nothing on screen to explain why. So before the save goes out, this says in
// full sentences what will happen, WHEN it will happen, and the two ways to make
// the change permanent instead.
//
// The server enforces the same rule — the save is refused without
// `acknowledgeOverride` — so this is the explanation, not the gate. A client
// that skipped it would get an error rather than the surprise.
//
// A plain overlay rather than <dialog>: the project has no other modal to match,
// and showModal() brings focus-trap and top-layer behaviour that would need
// styling to match anyway. Escape and the backdrop both cancel, the confirm
// button takes focus on open, and the whole thing is labelled for screen readers.
// ---------------------------------------------------------------------------

import { useEffect, useRef } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { formatDateTime } from '../i18n/format'
import { parseApiTimestamp } from '../utils/dates'
import { describeTimeUntil } from '../utils/schedule'

export type ConfigOverrideDialogProps = {
  // When the schedule takes over again, as the API's UTC timestamp.
  resumesAt: string
  // The profile that will be applied then; null when it cannot be named.
  resumingProfileName: string | null
  isSaving: boolean
  onConfirm: () => void
  onCancel: () => void
}

export function ConfigOverrideDialog({
  resumesAt,
  resumingProfileName,
  isSaving,
  onConfirm,
  onCancel,
}: ConfigOverrideDialogProps) {
  const { t } = useTranslation(['settings', 'common'])

  const confirmRef = useRef<HTMLButtonElement>(null)

  // Focus the confirm button rather than the dialog: the reader arrived here by
  // pressing Save and is most likely to press Enter again, and the copy above it
  // is what they need to have read either way.
  useEffect(() => {
    confirmRef.current?.focus()
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        onCancel()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  const resumes: Date | null = parseApiTimestamp(resumesAt)
  const profileLabel: string = resumingProfileName
    ? t('settings:override.namedProfile', { name: resumingProfileName })
    : t('settings:override.unnamedProfile')

  return (
    <div
      className="modal-backdrop"
      // Only a click on the backdrop itself cancels — one that started inside the
      // panel and drifted out while selecting text must not throw the form away.
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onCancel()
        }
      }}
    >
      <div
        className="modal-panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="override-dialog-title"
        aria-describedby="override-dialog-body"
      >
        <h4 id="override-dialog-title" className="modal-title">
          {t('settings:override.title')}
        </h4>

        <div id="override-dialog-body" className="modal-body">
          <p>
            <Trans
              i18nKey="override.intro"
              ns="settings"
              values={{ profile: profileLabel }}
              components={{ strong: <strong /> }}
            />
          </p>

          <p className="modal-highlight">
            {resumes === null ? (
              <>{t('settings:override.resumesNextSwitch')}</>
            ) : (
              <Trans
                i18nKey="override.resumesAt"
                ns="settings"
                values={{
                  when: formatDateTime(resumes),
                  until: describeTimeUntil(resumes),
                }}
                components={{ strong: <strong />, quiet: <span className="hint" /> }}
              />
            )}
          </p>

          <p>
            <Trans i18nKey="override.permanentIntro" ns="settings" components={{ strong: <strong /> }} />
          </p>
          <ul className="modal-list">
            <li>
              <Trans i18nKey="override.permanentEditProfile" ns="settings" components={{ em: <em /> }} />
            </li>
            <li>{t('settings:override.permanentDisable')}</li>
          </ul>
        </div>

        <div className="modal-actions">
          <button
            type="button"
            className="btn btn-secondary"
            onClick={onCancel}
            disabled={isSaving}
          >
            {t('common:actions.cancel')}
          </button>
          <button
            ref={confirmRef}
            type="button"
            className="btn btn-primary"
            onClick={onConfirm}
            disabled={isSaving}
          >
            {isSaving ? t('common:actions.saving') : t('settings:override.confirm')}
          </button>
        </div>
      </div>
    </div>
  )
}
