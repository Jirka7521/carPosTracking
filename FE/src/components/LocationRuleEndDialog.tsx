// ============================================================
// LocationRuleEndDialog — the confirmation before a place rule is ended,
// saying what ending it does and does not do.
//
// Ending is the only way a rule stops: rules are never deleted. The trips it
// placed while it was in force stay in its journey (the rule keeps covering
// trips that started before it ended), new trips are no longer placed by it,
// and it stays visible in the history.
// ============================================================

import { useEffect, useId } from 'react'
import { useTranslation } from 'react-i18next'
import type { LocationRuleDto } from '../services/apiTypes'
import { locationRuleLabel } from '../utils/trips'

export type LocationRuleEndDialogProps = {
  rule: LocationRuleDto
  // The journey's path, or null when it is not known.
  journeyLabel: string | null
  isBusy: boolean
  // Already translated; empty when there is nothing to report.
  errorMessage: string
  onConfirm: () => void
  onCancel: () => void
}

export function LocationRuleEndDialog({
  rule,
  journeyLabel,
  isBusy,
  errorMessage,
  onConfirm,
  onCancel,
}: LocationRuleEndDialogProps) {
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
          {t('trips:locationRule.endTitle', { name: locationRuleLabel(rule) })}
        </h4>

        <div id={bodyId} className="modal-body">
          <ul className="modal-list">
            {journeyLabel !== null ? <li>{t('trips:locationRule.endKeeps', { journey: journeyLabel })}</li> : null}
            <li>{t('trips:locationRule.endNoNew')}</li>
            <li>{t('trips:locationRule.endHistory')}</li>
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
            {t('trips:locationRule.endConfirm')}
          </button>
        </div>
      </div>
    </div>
  )
}
