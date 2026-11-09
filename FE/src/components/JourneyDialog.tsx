// ============================================================
// JourneyDialog — create a journey, or edit one: its name, which journey it
// sits inside, and optionally the days whose trips it collects on its own.
//
// The date range is a LIVE RULE, not a one-off move: every trip that starts on
// those days is in the journey — including trips whose positions arrive later
// from the tracker's backlog — unless the reader placed it somewhere by hand,
// or a journey inside this one claims the same days. The range is picked as
// whole local days and sent as [local midnight of the first day, local midnight
// after the last).
//
// While a valid range is set, the dialog previews it: how many trips start on
// those days and how far they went. Trips the reader pinned elsewhere are not
// taken over by the rule; when the range holds any, a checkbox offers to move
// them too — that is "every trip between these dates goes here", said
// explicitly. It starts ticked for a new journey; when editing it starts clear,
// so renaming a journey can never quietly undo moves made by hand.
//
// The dialog sends the request itself, so the server's refusals (a duplicate
// name, a range that partly overlaps another journey's, too deep) appear right
// under the form that caused them, in the reader's language.
// ============================================================

import { useEffect, useId, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { formatDistance, formatInteger } from '../i18n/format'
import { createJourney, fetchTripRangeSummary, updateJourney } from '../services/apiClient'
import type { JourneyDto, JourneySaveRequestDto, TripRangeSummaryDto } from '../services/apiTypes'
import { describeError } from '../utils/errors'
import type { JourneyTree } from '../utils/journeyTree'
import { MAX_JOURNEY_DEPTH, canPlaceUnder, journeyPathLabel } from '../utils/journeyTree'
import {
  dateInputToRangeEnd,
  dateInputToRangeStart,
  rangeEndToDateInput,
  rangeStartToDateInput,
  todayDateInput,
} from '../utils/trips'
import { JourneyPicker } from './JourneyPicker'

// Mirrors TripRules.MaxNameLength on the API.
const MAX_NAME_LENGTH: number = 100

// How long the dates must stay still before the preview is asked for, so typing
// a date digit by digit does not send a request per keystroke.
const PREVIEW_DELAY_MS: number = 400

export type JourneyDialogMode =
  | { kind: 'create'; parentId: string | null; withRange: boolean }
  | { kind: 'edit'; journey: JourneyDto }

export type JourneyDialogProps = {
  deviceId: string
  tree: JourneyTree
  mode: JourneyDialogMode
  onSaved: (journey: JourneyDto) => void
  onCancel: () => void
}

type Preview =
  | { rangeKey: string; state: 'ready'; summary: TripRangeSummaryDto }
  | { rangeKey: string; state: 'error' }

export function JourneyDialog({ deviceId, tree, mode, onSaved, onCancel }: JourneyDialogProps) {
  const { t } = useTranslation(['trips', 'common', 'errors'])
  const titleId: string = useId()
  const nameId: string = useId()
  const parentFieldId: string = useId()
  const fromId: string = useId()
  const toId: string = useId()
  const nameRef = useRef<HTMLInputElement>(null)

  const editing: JourneyDto | null = mode.kind === 'edit' ? mode.journey : null
  const editId: string | null = editing?.id ?? null

  const [name, setName] = useState<string>(editing?.name ?? '')
  const [parentId, setParentId] = useState<string | null>(
    editing !== null ? editing.parentId : mode.kind === 'create' ? mode.parentId : null,
  )
  const [isPickingParent, setIsPickingParent] = useState<boolean>(false)
  const [hasRange, setHasRange] = useState<boolean>(
    editing !== null ? editing.rangeFrom !== null : mode.kind === 'create' && mode.withRange,
  )
  const [fromDay, setFromDay] = useState<string>(
    editing?.rangeFrom ? rangeStartToDateInput(editing.rangeFrom) : todayDateInput(),
  )
  const [toDay, setToDay] = useState<string>(
    editing?.rangeTo ? rangeEndToDateInput(editing.rangeTo) : todayDateInput(),
  )
  const [includePinned, setIncludePinned] = useState<boolean>(editing === null)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [isSaving, setIsSaving] = useState<boolean>(false)
  const [errorMessage, setErrorMessage] = useState<string>('')

  const fromIso: string | undefined = hasRange ? dateInputToRangeStart(fromDay) : undefined
  const toIso: string | undefined = hasRange ? dateInputToRangeEnd(toDay) : undefined
  const rangeValid: boolean = fromIso !== undefined && toIso !== undefined && fromIso < toIso
  const rangeKey: string = rangeValid ? `${fromIso}|${toIso}` : ''
  const rangeOutOfOrder: boolean =
    hasRange && fromIso !== undefined && toIso !== undefined && !rangeValid

  const trimmedName: string = name.trim()
  const parentAllowed: boolean = canPlaceUnder(tree, editId, parentId)
  const canSave: boolean =
    trimmedName.length > 0 && (!hasRange || rangeValid) && parentAllowed && !isSaving

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

  // The range preview, asked for once the dates have settled. The answer is
  // stored with the range it describes, so a stale one is never shown against
  // dates the reader has since changed.
  useEffect(() => {
    if (!rangeValid || fromIso === undefined || toIso === undefined) {
      return
    }

    let canceled = false
    const timer = window.setTimeout(() => {
      fetchTripRangeSummary(deviceId, fromIso, toIso, editId ?? undefined)
        .then((summary) => {
          if (!canceled) {
            setPreview({ rangeKey: `${fromIso}|${toIso}`, state: 'ready', summary })
          }
        })
        .catch(() => {
          if (!canceled) {
            setPreview({ rangeKey: `${fromIso}|${toIso}`, state: 'error' })
          }
        })
    }, PREVIEW_DELAY_MS)

    return () => {
      canceled = true
      window.clearTimeout(timer)
    }
  }, [deviceId, editId, fromIso, toIso, rangeValid])

  const currentPreview: Preview | null = preview !== null && preview.rangeKey === rangeKey ? preview : null
  const pinnedElsewhere: number =
    currentPreview?.state === 'ready' ? currentPreview.summary.pinnedElsewhereCount : 0

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (!canSave) {
      return
    }

    const payload: JourneySaveRequestDto = {
      name: trimmedName,
      parentId,
      rangeFrom: hasRange ? (fromIso ?? null) : null,
      rangeTo: hasRange ? (toIso ?? null) : null,
      includePinned: hasRange && includePinned && pinnedElsewhere > 0,
    }

    setIsSaving(true)
    setErrorMessage('')
    try {
      const saved: JourneyDto =
        editId === null
          ? await createJourney(deviceId, payload)
          : await updateJourney(deviceId, editId, payload)
      onSaved(saved)
    } catch (error) {
      setErrorMessage(describeError(error, t('errors:saveJourneyFailed')))
      setIsSaving(false)
    }
  }

  const parentLabel: string =
    parentId !== null && tree.byId.has(parentId)
      ? journeyPathLabel(tree, parentId)
      : t('trips:picker.topLevel')

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
        className="modal-panel journey-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
      >
        <h4 id={titleId} className="modal-title">
          {editId === null ? t('trips:dialog.createTitle') : t('trips:dialog.editTitle')}
        </h4>

        <form className="journey-dialog-form" onSubmit={(event) => void handleSubmit(event)}>
          <div className="form-field">
            <label htmlFor={nameId}>{t('trips:dialog.name')}</label>
            <input
              ref={nameRef}
              id={nameId}
              className="form-input"
              value={name}
              maxLength={MAX_NAME_LENGTH}
              placeholder={t('trips:dialog.namePlaceholder')}
              onChange={(e) => setName(e.target.value)}
              disabled={isSaving}
            />
          </div>

          <div className="form-field">
            <label htmlFor={isPickingParent ? parentFieldId : undefined}>{t('trips:dialog.parent')}</label>
            <div className="journey-dialog-parent">
              <span>{parentId === null ? parentLabel : `🧭 ${parentLabel}`}</span>
              {!isPickingParent && tree.byId.size > 0 ? (
                <button
                  type="button"
                  className="btn btn-quiet btn-sm"
                  onClick={() => setIsPickingParent(true)}
                  disabled={isSaving}
                >
                  {t('trips:dialog.changeParent')}
                </button>
              ) : null}
            </div>
            {isPickingParent ? (
              <JourneyPicker
                id={parentFieldId}
                tree={tree}
                value={parentId}
                onChange={(next) => {
                  setParentId(next)
                  setIsPickingParent(false)
                }}
                nullLabel={t('trips:picker.topLevel')}
                isAllowed={(candidate) => canPlaceUnder(tree, editId, candidate.id)}
                disallowedTitle={t('trips:picker.notAllowedTitle', { max: MAX_JOURNEY_DEPTH })}
              />
            ) : null}
            {!parentAllowed ? (
              <p className="form-message form-message--error">
                {t('trips:picker.notAllowedTitle', { max: MAX_JOURNEY_DEPTH })}
              </p>
            ) : null}
          </div>

          <label className="checkbox-field">
            <input
              type="checkbox"
              checked={hasRange}
              onChange={(e) => setHasRange(e.target.checked)}
              disabled={isSaving}
            />
            <span>{t('trips:dialog.autoInclude')}</span>
          </label>

          {hasRange ? (
            <div className="journey-dialog-range">
              <div className="journey-dialog-dates">
                <div className="form-field">
                  <label htmlFor={fromId}>{t('trips:dialog.from')}</label>
                  <input
                    id={fromId}
                    type="date"
                    className="form-input"
                    value={fromDay}
                    onChange={(e) => setFromDay(e.target.value)}
                    disabled={isSaving}
                    required
                  />
                </div>
                <div className="form-field">
                  <label htmlFor={toId}>{t('trips:dialog.to')}</label>
                  <input
                    id={toId}
                    type="date"
                    className="form-input"
                    value={toDay}
                    min={fromDay}
                    onChange={(e) => setToDay(e.target.value)}
                    disabled={isSaving}
                    required
                  />
                </div>
              </div>
              <p className="hint">{t('trips:dialog.rangeHint')}</p>

              {rangeOutOfOrder ? (
                <p className="form-message form-message--error">{t('trips:dialog.rangeOrder')}</p>
              ) : rangeValid ? (
                <div className="journey-dialog-preview" aria-live="polite">
                  {currentPreview === null ? (
                    <span className="hint">{t('trips:dialog.previewLoading')}</span>
                  ) : currentPreview.state === 'error' ? (
                    <span className="hint">{t('trips:dialog.previewFailed')}</span>
                  ) : (
                    <>
                      <strong>
                        {t('trips:dialog.preview', {
                          count: currentPreview.summary.tripCount,
                          value: formatInteger(currentPreview.summary.tripCount),
                        })}
                        {currentPreview.summary.distanceMeters !== null
                          && currentPreview.summary.tripCount > 0
                          ? ` · ${formatDistance(currentPreview.summary.distanceMeters)}`
                          : ''}
                      </strong>
                      {pinnedElsewhere > 0 ? (
                        <label className="checkbox-field">
                          <input
                            type="checkbox"
                            checked={includePinned}
                            onChange={(e) => setIncludePinned(e.target.checked)}
                            disabled={isSaving}
                          />
                          <span>
                            {t('trips:dialog.includePinned', {
                              count: pinnedElsewhere,
                              value: formatInteger(pinnedElsewhere),
                            })}
                          </span>
                        </label>
                      ) : null}
                    </>
                  )}
                </div>
              ) : null}
            </div>
          ) : null}

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
                : editId === null
                  ? t('trips:dialog.create')
                  : t('common:actions.saveChanges')}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
