// ============================================================
// TripDetectionSection — the Settings tab's "Trip detection" section (Expert
// mode): the three thresholds the server uses to cut this car's positions into
// trips.
//
//   • Minimum stop — how long the car must stay put for a trip to end. Shorter
//     waits (traffic lights, a jam, a quick drop-off) stay inside the trip.
//   • Stop radius — how far the fixes may wander while the car counts as
//     standing still. GNSS drifts by tens of metres even when parked.
//   • Shortest trip — anything shorter is dropped as noise (moving across a car
//     park, a drifting fix).
//
// These are SERVER settings: nothing is sent to the tracker, and nothing about
// how it reports changes. Saving one makes the server recompute every trip of
// the car in the background — the Trips tab says "Recalculating" meanwhile —
// and trips keep their names and journeys wherever they survive the recompute.
// The section says so before the button is pressed, because on a long history
// that is not instant.
//
// Readable by everyone who can see the device (so a Normal-mode reader can
// understand why a trip ended where it did, once in Expert mode); editable only
// with canModifySettings. Loaded once with the tab, not on the refresh timer —
// the values change only when someone saves them.
// ============================================================

import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { DurationField } from './DurationField'
import { formatDistance, formatDuration } from '../i18n/format'
import { fetchTripSettings, updateTripSettings } from '../services/apiClient'
import type { TripSettingsDto, TripSettingsUpdateRequestDto } from '../services/apiTypes'
import { describeError } from '../utils/errors'
import { TRIP_SETTING_LIMITS } from '../utils/trips'

export type TripDetectionSectionProps = {
  deviceId: string
  canEdit: boolean
}

const DEFAULTS: TripSettingsUpdateRequestDto = {
  minStopSeconds: TRIP_SETTING_LIMITS.minStopSeconds.defaultValue,
  stopRadiusMeters: TRIP_SETTING_LIMITS.stopRadiusMeters.defaultValue,
  minTripDistanceMeters: TRIP_SETTING_LIMITS.minTripDistanceMeters.defaultValue,
}

export function TripDetectionSection({ deviceId, canEdit }: TripDetectionSectionProps) {
  const { t } = useTranslation(['settings', 'common', 'errors'])

  const [settings, setSettings] = useState<TripSettingsDto | null>(null)
  const [draft, setDraft] = useState<TripSettingsUpdateRequestDto>(DEFAULTS)
  const [loadError, setLoadError] = useState<string>('')
  const [isSaving, setIsSaving] = useState<boolean>(false)
  const [saveError, setSaveError] = useState<string>('')
  const [savedNotice, setSavedNotice] = useState<string>('')
  // Bumped on load and save, so the duration field re-seeds its unit from the
  // stored value (DurationField's documented way to be reset).
  const [fieldGeneration, setFieldGeneration] = useState<number>(0)

  useEffect(() => {
    let canceled = false
    fetchTripSettings(deviceId)
      .then((value) => {
        if (!canceled) {
          setSettings(value)
          setDraft(toRequest(value))
          setFieldGeneration((current) => current + 1)
          setLoadError('')
        }
      })
      .catch((error: unknown) => {
        if (!canceled) {
          setLoadError(describeError(error, t('errors:loadTripSettingsFailed')))
        }
      })
    return () => {
      canceled = true
    }
    // `t` is deliberately absent: a language switch must not refetch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deviceId])

  const limits = TRIP_SETTING_LIMITS
  const isValid: boolean =
    inRange(draft.minStopSeconds, limits.minStopSeconds)
    && inRange(draft.stopRadiusMeters, limits.stopRadiusMeters)
    && inRange(draft.minTripDistanceMeters, limits.minTripDistanceMeters)
  const isChanged: boolean =
    settings !== null
    && (draft.minStopSeconds !== settings.minStopSeconds
      || draft.stopRadiusMeters !== settings.stopRadiusMeters
      || draft.minTripDistanceMeters !== settings.minTripDistanceMeters)
  const isDefaultDraft: boolean =
    draft.minStopSeconds === DEFAULTS.minStopSeconds
    && draft.stopRadiusMeters === DEFAULTS.stopRadiusMeters
    && draft.minTripDistanceMeters === DEFAULTS.minTripDistanceMeters

  function edit(patch: Partial<TripSettingsUpdateRequestDto>): void {
    setDraft((current) => ({ ...current, ...patch }))
    setSavedNotice('')
    setSaveError('')
  }

  async function handleSave(): Promise<void> {
    if (!isValid || !isChanged || isSaving) {
      return
    }
    setIsSaving(true)
    setSaveError('')
    setSavedNotice('')
    try {
      const saved: TripSettingsDto = await updateTripSettings(deviceId, draft)
      setSettings(saved)
      setDraft(toRequest(saved))
      setFieldGeneration((current) => current + 1)
      setSavedNotice(t('settings:trips.saved'))
    } catch (error) {
      setSaveError(describeError(error, t('errors:saveTripSettingsFailed')))
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div className="settings-section">
      <div className="settings-section-header">
        <span className="settings-section-icon" aria-hidden="true">🚗</span>
        <h3>{t('settings:trips.title')}</h3>
      </div>

      <div className="settings-section-body">
        <p>{t('settings:trips.intro')}</p>

        {loadError.length > 0 ? (
          <div className="banner banner--error" role="alert">{loadError}</div>
        ) : settings === null ? (
          <div className="loading-state" style={{ minHeight: 80 }}>
            <div className="spinner" />
          </div>
        ) : canEdit ? (
          <form
            className="trip-settings-form"
            onSubmit={(event) => {
              event.preventDefault()
              void handleSave()
            }}
          >
            <div className="trip-settings-grid">
              <DurationField
                key={`min-stop-${fieldGeneration}`}
                id="trip-min-stop"
                label={t('settings:trips.minStop')}
                value={draft.minStopSeconds}
                baseUnit="seconds"
                units={['minutes', 'hours']}
                min={limits.minStopSeconds.min}
                max={limits.minStopSeconds.max}
                onChange={(value) => edit({ minStopSeconds: value })}
                hint={t('settings:trips.minStopHint')}
                required
              />

              <div className="form-field">
                <label className="form-label" htmlFor="trip-stop-radius">
                  {t('settings:trips.stopRadius')}
                </label>
                <div className="trip-settings-unit-input">
                  <input
                    id="trip-stop-radius"
                    className="form-input"
                    type="number"
                    min={limits.stopRadiusMeters.min}
                    max={limits.stopRadiusMeters.max}
                    step={10}
                    value={Number.isFinite(draft.stopRadiusMeters) ? draft.stopRadiusMeters : ''}
                    onChange={(e) => edit({ stopRadiusMeters: parseWhole(e.target.value, 1) })}
                    required
                  />
                  <span className="trip-settings-unit" aria-hidden="true">m</span>
                </div>
                <span className="hint">{t('settings:trips.stopRadiusHint')}</span>
              </div>

              <div className="form-field">
                <label className="form-label" htmlFor="trip-min-distance">
                  {t('settings:trips.minDistance')}
                </label>
                <div className="trip-settings-unit-input">
                  <input
                    id="trip-min-distance"
                    className="form-input"
                    type="number"
                    min={limits.minTripDistanceMeters.min / 1000}
                    max={limits.minTripDistanceMeters.max / 1000}
                    step={0.1}
                    value={Number.isFinite(draft.minTripDistanceMeters) ? draft.minTripDistanceMeters / 1000 : ''}
                    onChange={(e) => edit({ minTripDistanceMeters: parseWhole(e.target.value, 1000) })}
                    required
                  />
                  <span className="trip-settings-unit" aria-hidden="true">km</span>
                </div>
                <span className="hint">{t('settings:trips.minDistanceHint')}</span>
              </div>
            </div>

            {!isValid ? (
              <p className="form-message form-message--error" role="alert">
                {t('settings:trips.outOfRange', {
                  minStop: `${formatDuration(limits.minStopSeconds.min)} – ${formatDuration(limits.minStopSeconds.max)}`,
                  radius: `${formatDistance(limits.stopRadiusMeters.min)} – ${formatDistance(limits.stopRadiusMeters.max)}`,
                  distance: `${formatDistance(limits.minTripDistanceMeters.min)} – ${formatDistance(limits.minTripDistanceMeters.max)}`,
                })}
              </p>
            ) : null}

            <p className="hint trip-settings-note">ⓘ {t('settings:trips.recalculateNote')}</p>

            {saveError.length > 0 ? (
              <div className="banner banner--error" role="alert">{saveError}</div>
            ) : null}
            {savedNotice.length > 0 ? (
              <p className="form-message form-message--success" role="status">{savedNotice}</p>
            ) : null}

            <div className="trip-settings-actions">
              <button
                type="button"
                className="btn btn-quiet"
                onClick={() => {
                  edit(DEFAULTS)
                  setFieldGeneration((current) => current + 1)
                }}
                disabled={isSaving || isDefaultDraft}
              >
                {t('settings:trips.resetDefaults')}
              </button>
              <button type="submit" className="btn btn-primary" disabled={!isValid || !isChanged || isSaving}>
                {isSaving ? t('common:actions.saving') : t('common:actions.saveChanges')}
              </button>
            </div>
          </form>
        ) : (
          <div className="info-grid">
            <div className="info-item">
              <span className="info-label">{t('settings:trips.minStop')}</span>
              <span className="info-value">{formatDuration(settings.minStopSeconds)}</span>
            </div>
            <div className="info-item">
              <span className="info-label">{t('settings:trips.stopRadius')}</span>
              <span className="info-value">{formatDistance(settings.stopRadiusMeters)}</span>
            </div>
            <div className="info-item">
              <span className="info-label">{t('settings:trips.minDistance')}</span>
              <span className="info-value">{formatDistance(settings.minTripDistanceMeters)}</span>
            </div>
          </div>
        )}

        {settings !== null && settings.isDefault ? (
          <p className="hint">{t('settings:trips.usingDefaults')}</p>
        ) : null}
      </div>
    </div>
  )
}

function toRequest(settings: TripSettingsDto): TripSettingsUpdateRequestDto {
  return {
    minStopSeconds: settings.minStopSeconds,
    stopRadiusMeters: settings.stopRadiusMeters,
    minTripDistanceMeters: settings.minTripDistanceMeters,
  }
}

// An input's text as a whole number of metres, given how many metres one unit
// of the input is. An empty or unreadable field is NaN — invalid — rather than
// 0, which for the shortest trip would be a valid value nobody typed.
function parseWhole(raw: string, metersPerUnit: number): number {
  if (raw.trim().length === 0) {
    return Number.NaN
  }
  const value: number = Number(raw)
  return Number.isFinite(value) ? Math.round(value * metersPerUnit) : Number.NaN
}

function inRange(value: number, limits: { min: number; max: number }): boolean {
  return Number.isFinite(value) && value >= limits.min && value <= limits.max
}
