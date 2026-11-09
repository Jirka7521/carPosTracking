// ============================================================
// JourneyDeleteDialog — the confirmation before a journey is deleted, saying
// exactly where its contents go.
//
// Deleting a journey never deletes a trip. What it held moves up one level: its
// sub-journeys become its parent's, and its trips are placed in the parent by
// hand — or, for a top-level journey, in no journey at all. The dialog names the
// parent, so "where did my trips go" has its answer before the button is pressed.
// ============================================================

import { useEffect, useId } from 'react'
import { useTranslation } from 'react-i18next'
import type { JourneyDto } from '../services/apiTypes'

export type JourneyDeleteDialogProps = {
  journey: JourneyDto
  // The parent's name, or null for a top-level journey.
  parentName: string | null
  subJourneyCount: number
  isBusy: boolean
  // Already translated; empty when there is nothing to report.
  errorMessage: string
  onConfirm: () => void
  onCancel: () => void
}

export function JourneyDeleteDialog({
  journey,
  parentName,
  subJourneyCount,
  isBusy,
  errorMessage,
  onConfirm,
  onCancel,
}: JourneyDeleteDialogProps) {
  const { t } = useTranslation(['trips', 'common'])
  const titleId: string = useId()
  const bodyId: string = useId()

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape' && !isBusy) {
        onCancel()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [isBusy, onCancel])

  return (
    <div
      className="modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !isBusy) {
          onCancel()
        }
      }}
    >
      <div
        className="modal-panel"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={bodyId}
      >
        <h4 id={titleId} className="modal-title">
          {t('trips:delete.title', { name: journey.name })}
        </h4>

        <div id={bodyId} className="modal-body">
          <ul className="modal-list">
            <li>
              {parentName === null
                ? t('trips:delete.tripsOut')
                : t('trips:delete.tripsToParent', { parent: parentName })}
            </li>
            {subJourneyCount > 0 ? (
              <li>
                {parentName === null
                  ? t('trips:delete.childrenToTop', { count: subJourneyCount })
                  : t('trips:delete.childrenToParent', { count: subJourneyCount, parent: parentName })}
              </li>
            ) : null}
            <li>{t('trips:delete.noTripDeleted')}</li>
          </ul>

          {errorMessage.length > 0 ? (
            <p className="form-message form-message--error" role="alert">
              {errorMessage}
            </p>
          ) : null}
        </div>

        <div className="modal-actions">
          <button type="button" className="btn btn-secondary" onClick={onCancel} disabled={isBusy}>
            {t('common:actions.cancel')}
          </button>
          <button type="button" className="btn btn-danger-solid" onClick={onConfirm} disabled={isBusy}>
            {t('trips:delete.confirm')}
          </button>
        </div>
      </div>
    </div>
  )
}
