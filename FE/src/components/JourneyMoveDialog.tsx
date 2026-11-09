// ============================================================
// JourneyMoveDialog — "Move N trips to…": a JourneyPicker in a dialog, for the
// Trips list's selection bar and the trip detail page.
//
// A move PINS the trips: they stay in the chosen journey even when a journey's
// date range says otherwise, until "Back to automatic" releases them. The hint
// under the picker says so, because it is the one surprising thing about it.
//
// Same overlay behaviour as the app's other dialogs (ExportLengthDialog,
// ConfigOverrideDialog): Escape and a click on the backdrop cancel — except while
// the move is being sent, when closing would hide its outcome.
// ============================================================

import { useEffect, useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { formatInteger } from '../i18n/format'
import type { JourneyTree } from '../utils/journeyTree'
import { JourneyPicker } from './JourneyPicker'

export type JourneyMoveDialogProps = {
  tree: JourneyTree
  tripCount: number
  // Pre-selected journey, e.g. the one the trip is in now.
  initialJourneyId: string | null
  isBusy: boolean
  // Already translated; empty when there is nothing to report.
  errorMessage: string
  onMove: (journeyId: string) => void
  onCancel: () => void
}

export function JourneyMoveDialog({
  tree,
  tripCount,
  initialJourneyId,
  isBusy,
  errorMessage,
  onMove,
  onCancel,
}: JourneyMoveDialogProps) {
  const { t } = useTranslation(['trips', 'common'])
  const titleId: string = useId()
  const pickerId: string = useId()
  const [journeyId, setJourneyId] = useState<string | null>(initialJourneyId)

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape' && !isBusy) {
        onCancel()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [isBusy, onCancel])

  const hasJourneys: boolean = tree.byId.size > 0

  return (
    <div
      className="modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !isBusy) {
          onCancel()
        }
      }}
    >
      <div className="modal-panel" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <h4 id={titleId} className="modal-title">
          {t('trips:move.title', { count: tripCount, value: formatInteger(tripCount) })}
        </h4>

        <div className="modal-body">
          {hasJourneys ? (
            <>
              <label className="form-label" htmlFor={pickerId}>
                {t('trips:move.pickerLabel')}
              </label>
              <JourneyPicker id={pickerId} tree={tree} value={journeyId} onChange={setJourneyId} />
              <p className="hint">{t('trips:move.hint')}</p>
            </>
          ) : (
            <p>{t('trips:move.noJourneys')}</p>
          )}

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
          <button
            type="button"
            className="btn btn-primary"
            disabled={isBusy || journeyId === null || !tree.byId.has(journeyId)}
            onClick={() => {
              if (journeyId !== null) {
                onMove(journeyId)
              }
            }}
          >
            {isBusy ? t('common:actions.saving') : t('trips:move.confirm')}
          </button>
        </div>
      </div>
    </div>
  )
}
