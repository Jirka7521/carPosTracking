// ============================================================
// LocationPlaceFields — the place half of a location rule: an optional name,
// the point and radius on the map (or typed), which trips it catches (starting
// there, ending there, or both), the day it applies from, and a live preview.
//
// Shared by LocationRuleDialog (a rule on its own, with a journey picker) and
// JourneyDialog (the journey's one place, saved together with the journey), so
// both look and behave the same. The values live in usePlaceDraft
// (hooks/usePlaceDraft.ts), owned by the dialog, which builds the request.
//
// The preview is counted by the database with the same place test the rule
// itself uses, and the map shows the matched starts and ends as dots, so the
// radius can be tuned by eye. The map is optional: without a Maps key or the
// reader's consent the coordinates are typed in. The inputs and the map edit
// the same values.
// ============================================================

import { useCallback, useEffect, useId, useState } from 'react'
import type { ReactNode, Ref } from 'react'
import { useTranslation } from 'react-i18next'
import { formatDate, formatInteger } from '../i18n/format'
import { useAccountMapsConsent } from '../hooks/useAccountMapsConsent'
import { fetchPositions, previewLocationRule } from '../services/apiClient'
import type { PlaceDraft } from '../hooks/usePlaceDraft'
import type { LocationRulePreviewDto } from '../services/apiTypes'
import { hasGoogleMapsKey, runtimeConfig } from '../services/runtimeConfig'
import { parseApiTimestamp } from '../utils/dates'
import { LOCATION_RULE_RADIUS } from '../utils/trips'
import { LocationPickerMap } from './LocationPickerMap'
import type { PickedPoint } from './LocationPickerMap'

// Mirrors TripRules.MaxNameLength on the API.
const MAX_NAME_LENGTH: number = 100

// How long the place must stay still before the preview is asked for, so
// dragging the circle or typing a coordinate does not send a request per step.
const PREVIEW_DELAY_MS: number = 400

const NO_POINTS: LocationRulePreviewDto['points'] = []

type Preview =
  | { key: string; state: 'ready'; value: LocationRulePreviewDto }
  | { key: string; state: 'error' }

export type LocationPlaceFieldsProps = {
  deviceId: string
  draft: PlaceDraft
  // The rule being edited, left out of "trips another rule places".
  excludeRuleId?: string
  disabled: boolean
  nameInputRef?: Ref<HTMLInputElement>
  // Rendered between the "which trips" checkboxes and the valid-from day.
  beforeValidFrom?: ReactNode
}

export function LocationPlaceFields({
  deviceId,
  draft,
  excludeRuleId,
  disabled,
  nameInputRef,
  beforeValidFrom,
}: LocationPlaceFieldsProps) {
  const { t } = useTranslation(['trips', 'common'])
  const nameId: string = useId()
  const latitudeId: string = useId()
  const longitudeId: string = useId()
  const radiusId: string = useId()
  const validFromId: string = useId()
  const mapsConsent = useAccountMapsConsent()

  // Whether the map needs somewhere to open before a place is picked; decided once.
  const [needsInitialView] = useState<boolean>(draft.center === null)
  const [initialView, setInitialView] = useState<PickedPoint | null>(null)
  const [preview, setPreview] = useState<Preview | null>(null)

  const { setLatitudeText, setLongitudeText, setRadiusText, previewKey } = draft

  // Where to point the map before a place is picked: the car's latest fix.
  useEffect(() => {
    if (!needsInitialView) {
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
  }, [deviceId, needsInitialView])

  // The preview, asked for once the place has settled. Stored with the values it
  // describes, so a stale answer is never shown against values since changed.
  useEffect(() => {
    const validFrom: string | undefined = draft.validFromIso
    if (previewKey === '' || validFrom === undefined) {
      return
    }

    let canceled = false
    const key: string = previewKey
    const timer = window.setTimeout(() => {
      previewLocationRule(deviceId, {
        latitude: draft.latitude,
        longitude: draft.longitude,
        radiusMeters: draft.radius,
        matchStart: draft.matchStart,
        matchEnd: draft.matchEnd,
        validFrom,
        excludeRuleId,
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
  }, [deviceId, previewKey, excludeRuleId])

  const currentPreview: Preview | null = preview !== null && preview.key === previewKey ? preview : null

  // The map moved or resized the circle.
  const handleMapChange = useCallback(
    (picked: PickedPoint, radiusMeters: number): void => {
      setLatitudeText(String(picked.latitude))
      setLongitudeText(String(picked.longitude))
      setRadiusText(String(radiusMeters))
    },
    [setLatitudeText, setLongitudeText, setRadiusText],
  )

  const validFromDate: Date | null = draft.validFromIso === undefined ? null : parseApiTimestamp(draft.validFromIso)
  const radiusForMap: number = draft.radiusValid ? draft.radius : LOCATION_RULE_RADIUS.defaultValue

  return (
    <>
      <div className="form-field">
        <label htmlFor={nameId}>{t('trips:locationRule.name')}</label>
        <input
          ref={nameInputRef}
          id={nameId}
          className="form-input"
          value={draft.name}
          maxLength={MAX_NAME_LENGTH}
          placeholder={t('trips:locationRule.namePlaceholder')}
          onChange={(e) => draft.setName(e.target.value)}
          disabled={disabled}
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
            center={draft.center}
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
            value={draft.latitudeText}
            onChange={(e) => draft.setLatitudeText(e.target.value)}
            disabled={disabled}
            required
          />
        </div>
        <div className="form-field">
          <label htmlFor={longitudeId}>{t('trips:locationRule.longitude')}</label>
          <input
            id={longitudeId}
            className="form-input"
            inputMode="decimal"
            value={draft.longitudeText}
            onChange={(e) => draft.setLongitudeText(e.target.value)}
            disabled={disabled}
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
            value={draft.radiusText}
            onChange={(e) => draft.setRadiusText(e.target.value)}
            disabled={disabled}
            required
          />
        </div>
      </div>
      {draft.hasPlace && !draft.placeValid ? (
        <p className="form-message form-message--error">{t('trips:locationRule.coordinatesInvalid')}</p>
      ) : null}
      {!draft.radiusValid ? (
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
            checked={draft.matchStart}
            onChange={(e) => draft.setMatchStart(e.target.checked)}
            disabled={disabled}
          />
          <span>{t('trips:locationRule.matchStart')}</span>
        </label>
        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={draft.matchEnd}
            onChange={(e) => draft.setMatchEnd(e.target.checked)}
            disabled={disabled}
          />
          <span>{t('trips:locationRule.matchEnd')}</span>
        </label>
      </fieldset>
      <p className="hint">{t('trips:locationRule.matchHint')}</p>
      {!draft.matchValid ? (
        <p className="form-message form-message--error">{t('trips:locationRule.matchNone')}</p>
      ) : null}

      {beforeValidFrom}

      <div className="form-field">
        <label htmlFor={validFromId}>{t('trips:locationRule.validFrom')}</label>
        <input
          id={validFromId}
          type="date"
          className="form-input location-rule-date"
          value={draft.validDay}
          onChange={(e) => draft.setValidDay(e.target.value)}
          disabled={disabled}
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
    </>
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
