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
// While the place is set, the dialog previews it — counted by the database with
// the same place test the rule itself uses — and the map shows the matched
// starts and ends as dots, so the radius can be tuned by eye.
//
// The map is optional: without a Maps key or the reader's consent the
// coordinates are typed in. The inputs and the map edit the same values.
//
// The dialog sends the request itself, so the server's refusals appear under
// the form that caused them.
// ============================================================

import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { formatDate, formatInteger } from '../i18n/format'
import { useAccountMapsConsent } from '../hooks/useAccountMapsConsent'
import {
  createLocationRule,
  fetchPositions,
  previewLocationRule,
  updateLocationRule,
} from '../services/apiClient'
import type {
  LocationRuleDto,
  LocationRulePreviewDto,
  LocationRuleSaveRequestDto,
} from '../services/apiTypes'
import { hasGoogleMapsKey, runtimeConfig } from '../services/runtimeConfig'
import { parseApiTimestamp } from '../utils/dates'
import { describeError } from '../utils/errors'
import type { JourneyTree } from '../utils/journeyTree'
import { journeyPathLabel } from '../utils/journeyTree'
import {
  LOCATION_RULE_RADIUS,
  dateInputToRangeStart,
  rangeStartToDateInput,
  todayDateInput,
} from '../utils/trips'
import { JourneyPicker } from './JourneyPicker'
import { LocationPickerMap } from './LocationPickerMap'
import type { PickedPoint } from './LocationPickerMap'

// Mirrors TripRules.MaxNameLength on the API.
const MAX_NAME_LENGTH: number = 100

// How long the place must stay still before the preview is asked for, so
// dragging the circle or typing a coordinate does not send a request per step.
const PREVIEW_DELAY_MS: number = 400

const NO_POINTS: LocationRulePreviewDto['points'] = []

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

type Preview =
  | { key: string; state: 'ready'; value: LocationRulePreviewDto }
  | { key: string; state: 'error' }

export function LocationRuleDialog({ deviceId, tree, mode, onSaved, onCancel }: LocationRuleDialogProps) {
  const { t } = useTranslation(['trips', 'common', 'errors'])
  const titleId: string = useId()
  const nameId: string = useId()
  const latitudeId: string = useId()
  const longitudeId: string = useId()
  const radiusId: string = useId()
  const journeyFieldId: string = useId()
  const validFromId: string = useId()
  const nameRef = useRef<HTMLInputElement>(null)
  const mapsConsent = useAccountMapsConsent()

  const editing: LocationRuleDto | null = mode.kind === 'edit' ? mode.rule : null
  const initialCenter: PickedPoint | null =
    editing !== null
      ? { latitude: editing.latitude, longitude: editing.longitude }
      : mode.kind === 'create'
        ? mode.center
        : null
  const initialJourney: string | null =
    editing !== null ? editing.journeyId : mode.kind === 'create' ? mode.journeyId : null

  const [name, setName] = useState<string>(editing?.name ?? '')
  const [latitudeText, setLatitudeText] = useState<string>(initialCenter === null ? '' : String(initialCenter.latitude))
  const [longitudeText, setLongitudeText] = useState<string>(initialCenter === null ? '' : String(initialCenter.longitude))
  const [radiusText, setRadiusText] = useState<string>(
    String(editing?.radiusMeters ?? LOCATION_RULE_RADIUS.defaultValue),
  )
  const [matchStart, setMatchStart] = useState<boolean>(editing?.matchStart ?? true)
  const [matchEnd, setMatchEnd] = useState<boolean>(editing?.matchEnd ?? true)
  const [journeyId, setJourneyId] = useState<string | null>(
    initialJourney !== null && tree.byId.has(initialJourney) ? initialJourney : null,
  )
  const [isPickingJourney, setIsPickingJourney] = useState<boolean>(
    initialJourney === null || !tree.byId.has(initialJourney),
  )
  const [validDay, setValidDay] = useState<string>(
    editing !== null ? rangeStartToDateInput(editing.validFrom) : todayDateInput(),
  )
  const [initialView, setInitialView] = useState<PickedPoint | null>(null)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [isSaving, setIsSaving] = useState<boolean>(false)
  const [errorMessage, setErrorMessage] = useState<string>('')

  const latitude: number = parseCoordinate(latitudeText)
  const longitude: number = parseCoordinate(longitudeText)
  const radius: number = parseWhole(radiusText)
  const hasPlace: boolean = latitudeText.trim().length > 0 || longitudeText.trim().length > 0
  const placeValid: boolean =
    Number.isFinite(latitude) && Math.abs(latitude) <= 90 && Number.isFinite(longitude) && Math.abs(longitude) <= 180
  const radiusValid: boolean =
    Number.isFinite(radius) && radius >= LOCATION_RULE_RADIUS.min && radius <= LOCATION_RULE_RADIUS.max
  const matchValid: boolean = matchStart || matchEnd
  const validFromIso: string | undefined = dateInputToRangeStart(validDay)
  // One object per place, so the map is only re-synced when the place changes.
  const center: PickedPoint | null = useMemo(
    () => (placeValid ? { latitude, longitude } : null),
    [placeValid, latitude, longitude],
  )

  const canSave: boolean =
    placeValid && radiusValid && matchValid && validFromIso !== undefined && journeyId !== null && !isSaving

  const previewKey: string =
    placeValid && radiusValid && matchValid && validFromIso !== undefined
      ? `${latitude}|${longitude}|${radius}|${matchStart}|${matchEnd}|${validFromIso}`
      : ''

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

  // Where to point the map before a place is picked: the car's latest fix.
  useEffect(() => {
    if (initialCenter !== null) {
      return
    }
    let canceled = false
    fetchPositions(deviceId)
      .then((positions) => {
        const latest = positions[0]
        if (!canceled && latest !== undefined) {
          setInitialView({ latitude: latest.latitude, longitude: latest.longitude })
        }
      })
      .catch(() => {
        // The map then opens on its default view; nothing else depends on it.
      })
    return () => {
      canceled = true
    }
    // Read once, when the dialog opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deviceId])

  // The preview, asked for once the rule has settled. Stored with the rule it
  // describes, so a stale answer is never shown against values since changed.
  useEffect(() => {
    if (previewKey === '' || validFromIso === undefined) {
      return
    }

    let canceled = false
    const key: string = previewKey
    const timer = window.setTimeout(() => {
      previewLocationRule(deviceId, {
        latitude,
        longitude,
        radiusMeters: radius,
        matchStart,
        matchEnd,
        validFrom: validFromIso,
        excludeRuleId: editing?.id,
      })
        .then((value) => {
          if (!canceled) {
            setPreview({ key, state: 'ready', value })
          }
        })
        .catch(() => {
          if (!canceled) {
            setPreview({ key, state: 'error' })
          }
        })
    }, PREVIEW_DELAY_MS)

    return () => {
      canceled = true
      window.clearTimeout(timer)
    }
    // previewKey carries every value the request is built from.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deviceId, previewKey])

  const currentPreview: Preview | null = preview !== null && preview.key === previewKey ? preview : null

  // The map moved or resized the circle.
  const handleMapChange = useCallback((picked: PickedPoint, radiusMeters: number): void => {
    setLatitudeText(String(picked.latitude))
    setLongitudeText(String(picked.longitude))
    setRadiusText(String(radiusMeters))
  }, [])

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (!canSave || journeyId === null || validFromIso === undefined) {
      return
    }

    const trimmedName: string = name.trim()
    const payload: LocationRuleSaveRequestDto = {
      journeyId,
      name: trimmedName.length > 0 ? trimmedName : null,
      latitude,
      longitude,
      radiusMeters: radius,
      matchStart,
      matchEnd,
      validFrom: validFromIso,
    }

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

  const validFromDate: Date | null = validFromIso === undefined ? null : parseApiTimestamp(validFromIso)
  const radiusForMap: number = radiusValid ? radius : LOCATION_RULE_RADIUS.defaultValue

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

          <div className="form-field">
            <label htmlFor={nameId}>{t('trips:locationRule.name')}</label>
            <input
              ref={nameRef}
              id={nameId}
              className="form-input"
              value={name}
              maxLength={MAX_NAME_LENGTH}
              placeholder={t('trips:locationRule.namePlaceholder')}
              onChange={(e) => setName(e.target.value)}
              disabled={isSaving}
            />
          </div>

          <div className="map-frame location-picker-frame">
            {!hasGoogleMapsKey() ? (
              <div className="map-placeholder">
                <p>{t('trips:locationRule.map.noKey')}</p>
              </div>
            ) : mapsConsent.status === 'loading' ? (
              <div className="map-placeholder">
                <p>{t('common:states.loading')}</p>
              </div>
            ) : (
              <LocationPickerMap
                apiKey={runtimeConfig.googleMapsApiKey}
                center={center}
                radiusMeters={radiusForMap}
                minRadiusMeters={LOCATION_RULE_RADIUS.min}
                maxRadiusMeters={LOCATION_RULE_RADIUS.max}
                initialView={initialView}
                points={currentPreview?.state === 'ready' ? currentPreview.value.points : NO_POINTS}
                onChange={handleMapChange}
                hasStandingConsent={mapsConsent.isStanding}
                onGrantStandingConsent={() => {
                  mapsConsent.grant().catch(() => undefined)
                }}
              />
            )}
          </div>
          <div className="map-legend location-rule-legend">
            <div className="map-legend-item">
              <span className="legend-ring" aria-hidden="true" />
              {t('trips:locationRule.map.legendCircle')}
            </div>
            <div className="map-legend-item">
              <span className="legend-dot legend-dot--start" aria-hidden="true" />
              {t('trips:locationRule.map.legendStart')}
            </div>
            <div className="map-legend-item">
              <span className="legend-dot legend-dot--end" aria-hidden="true" />
              {t('trips:locationRule.map.legendEnd')}
            </div>
          </div>

          <div className="location-rule-coords">
            <div className="form-field">
              <label htmlFor={latitudeId}>{t('trips:locationRule.latitude')}</label>
              <input
                id={latitudeId}
                className="form-input"
                inputMode="decimal"
                value={latitudeText}
                onChange={(e) => setLatitudeText(e.target.value)}
                disabled={isSaving}
                required
              />
            </div>
            <div className="form-field">
              <label htmlFor={longitudeId}>{t('trips:locationRule.longitude')}</label>
              <input
                id={longitudeId}
                className="form-input"
                inputMode="decimal"
                value={longitudeText}
                onChange={(e) => setLongitudeText(e.target.value)}
                disabled={isSaving}
                required
              />
            </div>
            <div className="form-field">
              <label htmlFor={radiusId}>{t('trips:locationRule.radius')}</label>
              <input
                id={radiusId}
                type="number"
                className="form-input"
                min={LOCATION_RULE_RADIUS.min}
                max={LOCATION_RULE_RADIUS.max}
                step={10}
                value={radiusText}
                onChange={(e) => setRadiusText(e.target.value)}
                disabled={isSaving}
                required
              />
            </div>
          </div>
          {hasPlace && !placeValid ? (
            <p className="form-message form-message--error">{t('trips:locationRule.coordinatesInvalid')}</p>
          ) : null}
          {!radiusValid ? (
            <p className="form-message form-message--error">
              {t('trips:locationRule.radiusInvalid', {
                min: formatInteger(LOCATION_RULE_RADIUS.min),
                max: formatInteger(LOCATION_RULE_RADIUS.max),
              })}
            </p>
          ) : null}

          <fieldset className="location-rule-match">
            <label className="checkbox-field">
              <input
                type="checkbox"
                checked={matchStart}
                onChange={(e) => setMatchStart(e.target.checked)}
                disabled={isSaving}
              />
              <span>{t('trips:locationRule.matchStart')}</span>
            </label>
            <label className="checkbox-field">
              <input
                type="checkbox"
                checked={matchEnd}
                onChange={(e) => setMatchEnd(e.target.checked)}
                disabled={isSaving}
              />
              <span>{t('trips:locationRule.matchEnd')}</span>
            </label>
          </fieldset>
          <p className="hint">{t('trips:locationRule.matchHint')}</p>
          {!matchValid ? (
            <p className="form-message form-message--error">{t('trips:locationRule.matchNone')}</p>
          ) : null}

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

          <div className="form-field">
            <label htmlFor={validFromId}>{t('trips:locationRule.validFrom')}</label>
            <input
              id={validFromId}
              type="date"
              className="form-input location-rule-date"
              value={validDay}
              onChange={(e) => setValidDay(e.target.value)}
              disabled={isSaving}
              required
            />
            <p className="hint">{t('trips:locationRule.validFromHint')}</p>
          </div>

          <div className="journey-dialog-preview" aria-live="polite">
            {previewKey === '' ? (
              <span className="hint">{t('trips:locationRule.previewNeedsPlace')}</span>
            ) : currentPreview === null ? (
              <span className="hint">{t('trips:locationRule.previewLoading')}</span>
            ) : currentPreview.state === 'error' ? (
              <span className="hint">{t('trips:locationRule.previewFailed')}</span>
            ) : (
              <PreviewText preview={currentPreview.value} validFrom={validFromDate} />
            )}
          </div>

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

function PreviewText({ preview, validFrom }: { preview: LocationRulePreviewDto; validFrom: Date | null }) {
  const { t } = useTranslation('trips')
  const from: string = validFrom === null ? '' : formatDate(validFrom)
  const shownTrips: number = new Set(preview.points.map((point) => point.tripId)).size

  return (
    <>
      <strong>
        {t('locationRule.preview', { count: preview.tripCount, value: formatInteger(preview.tripCount), from })}
      </strong>
      {preview.tripCount > 0 ? (
        <span>
          {t('locationRule.previewBreakdown', {
            starts: formatInteger(preview.startCount),
            ends: formatInteger(preview.endCount),
          })}
        </span>
      ) : null}
      {preview.pinnedCount > 0 ? (
        <span>
          {t('locationRule.previewPinned', { count: preview.pinnedCount, value: formatInteger(preview.pinnedCount) })}
        </span>
      ) : null}
      {preview.otherRuleCount > 0 ? (
        <span>
          {t('locationRule.previewOtherRule', {
            count: preview.otherRuleCount,
            value: formatInteger(preview.otherRuleCount),
          })}
        </span>
      ) : null}
      {preview.pointsTruncated ? (
        <span className="hint">{t('locationRule.previewTruncated', { value: formatInteger(shownTrips) })}</span>
      ) : null}
    </>
  )
}

// A typed coordinate, accepting a decimal comma; NaN when it is not a number.
function parseCoordinate(text: string): number {
  const trimmed: string = text.trim().replace(',', '.')
  return trimmed.length === 0 || !/^-?\d+(\.\d+)?$/.test(trimmed) ? Number.NaN : Number(trimmed)
}

// A typed whole number; NaN when it is not one.
function parseWhole(text: string): number {
  const trimmed: string = text.trim()
  return /^\d+$/.test(trimmed) ? Number(trimmed) : Number.NaN
}
