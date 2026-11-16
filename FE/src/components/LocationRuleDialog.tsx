// ============================================================
// LocationRuleDialog — create a place rule, or edit one still in force: a
// point and a radius on the map, which trips it catches (those starting there,
// ending there, or both), the journey they go to, and the day it applies from.
//
// The rule is LIVE, like a journey's dates: the server decides at every read
// which journey a trip is in, so a rule applies at once to every trip since its
// valid-from day — past ones included — and to trips whose data arrives later.
// It never takes a trip the reader placed by hand, and it wins over a journey's
// dates. Where two rules both match a trip, the closer match keeps it: a match
// on where the trip ended beats one on where it started, then the smaller
// circle, then the newer rule.
//
// The place itself — map, coordinates, radius, which trips, valid-from day and
// the live preview — is LocationPlaceFields, shared with JourneyDialog (which
// sets a journey's one place together with the journey). This dialog adds the
// journey picker.
//
// The dialog sends the request itself, so the server's refusals appear under
// the form that caused them.
// ============================================================

import { useEffect, useId, useRef, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { createLocationRule, updateLocationRule } from '../services/apiClient'
import type { LocationRuleDto, LocationRuleSaveRequestDto } from '../services/apiTypes'
import { describeError } from '../utils/errors'
import type { JourneyTree } from '../utils/journeyTree'
import { journeyPathLabel } from '../utils/journeyTree'
import { JourneyPicker } from './JourneyPicker'
import type { PickedPoint } from './LocationPickerMap'
import { LocationPlaceFields } from './LocationPlaceFields'
import { placeDraftInitial, placeValues, usePlaceDraft } from '../hooks/usePlaceDraft'
import type { PlaceDraft, PlaceValues } from '../hooks/usePlaceDraft'

export type LocationRuleDialogMode =
  | { kind: 'create'; journeyId: string | null; center: PickedPoint | null }
  | { kind: 'edit'; rule: LocationRuleDto }

export type LocationRuleDialogProps = {
  deviceId: string
  tree: JourneyTree
  mode: LocationRuleDialogMode
  onSaved: (rule: LocationRuleDto) => void
  onCancel: () => void
}

export function LocationRuleDialog({ deviceId, tree, mode, onSaved, onCancel }: LocationRuleDialogProps) {
  const { t } = useTranslation(['trips', 'common', 'errors'])
  const titleId: string = useId()
  const journeyFieldId: string = useId()
  const nameRef = useRef<HTMLInputElement>(null)

  const editing: LocationRuleDto | null = mode.kind === 'edit' ? mode.rule : null
  const initialJourney: string | null =
    editing !== null ? editing.journeyId : mode.kind === 'create' ? mode.journeyId : null

  const draft: PlaceDraft = usePlaceDraft(
    placeDraftInitial(editing, mode.kind === 'create' ? mode.center : null),
  )
  const [journeyId, setJourneyId] = useState<string | null>(
    initialJourney !== null && tree.byId.has(initialJourney) ? initialJourney : null,
  )
  const [isPickingJourney, setIsPickingJourney] = useState<boolean>(
    initialJourney === null || !tree.byId.has(initialJourney),
  )
  const [isSaving, setIsSaving] = useState<boolean>(false)
  const [errorMessage, setErrorMessage] = useState<string>('')

  const canSave: boolean = draft.isComplete && journeyId !== null && !isSaving

  useEffect(() => {
    nameRef.current?.focus()
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape' && !isSaving) {
        onCancel()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [isSaving, onCancel])

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    const values: PlaceValues | null = placeValues(draft)
    if (!canSave || journeyId === null || values === null) {
      return
    }

    const payload: LocationRuleSaveRequestDto = { journeyId, ...values }

    setIsSaving(true)
    setErrorMessage('')
    try {
      const saved: LocationRuleDto =
        editing === null
          ? await createLocationRule(deviceId, payload)
          : await updateLocationRule(deviceId, editing.id, payload)
      onSaved(saved)
    } catch (error) {
      setErrorMessage(describeError(error, t('errors:saveLocationRuleFailed')))
      setIsSaving(false)
    }
  }

  const journeyField: ReactNode = (
    <div className="form-field">
      <label htmlFor={isPickingJourney ? journeyFieldId : undefined}>{t('trips:locationRule.journey')}</label>
      {tree.byId.size === 0 ? (
        <p className="form-message form-message--error">{t('trips:locationRule.noJourneys')}</p>
      ) : (
        <>
          <div className="journey-dialog-parent">
            <span>
              {journeyId !== null && tree.byId.has(journeyId)
                ? `🧭 ${journeyPathLabel(tree, journeyId)}`
                : t('trips:locationRule.journeyRequired')}
            </span>
            {!isPickingJourney ? (
              <button
                type="button"
                className="btn btn-quiet btn-sm"
                onClick={() => setIsPickingJourney(true)}
                disabled={isSaving}
              >
                {t('trips:locationRule.changeJourney')}
              </button>
            ) : null}
          </div>
          {isPickingJourney ? (
            <JourneyPicker
              id={journeyFieldId}
              tree={tree}
              value={journeyId}
              onChange={(next) => {
                if (next !== null) {
                  setJourneyId(next)
                  setIsPickingJourney(false)
                }
              }}
            />
          ) : null}
        </>
      )}
    </div>
  )

  return (
    <div
      className="modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !isSaving) {
          onCancel()
        }
      }}
    >
      <div
        className="modal-panel modal-panel--wide location-rule-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
      >
        <h4 id={titleId} className="modal-title">
          {editing === null ? t('trips:locationRule.createTitle') : t('trips:locationRule.editTitle')}
        </h4>

        <form className="journey-dialog-form" onSubmit={(event) => void handleSubmit(event)}>
          <p className="hint">{t('trips:locationRule.ruleHint')}</p>

          <LocationPlaceFields
            deviceId={deviceId}
            draft={draft}
            excludeRuleId={editing?.id}
            disabled={isSaving}
            nameInputRef={nameRef}
            beforeValidFrom={journeyField}
          />

          {errorMessage.length > 0 ? (
            <p className="form-message form-message--error" role="alert">
              {errorMessage}
            </p>
          ) : null}

          <div className="modal-actions">
            <button type="button" className="btn btn-secondary" onClick={onCancel} disabled={isSaving}>
              {t('common:actions.cancel')}
            </button>
            <button type="submit" className="btn btn-primary" disabled={!canSave}>
              {isSaving
                ? t('common:actions.saving')
                : editing === null
                  ? t('trips:locationRule.create')
                  : t('common:actions.saveChanges')}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
