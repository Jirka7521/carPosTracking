// ============================================================
// DeviceConfigSection — the remote settings a tracker actually runs on.
//
// Mounted as a section of DeviceSettingsTab, gated on canModifySettings like
// its neighbours. It owns three things the user needs to keep apart, and the
// design goes out of its way not to conflate them:
//
//   1. UNSAVED edits      — typed but not submitted. Neutral; the Save button
//                           enables.
//   2. PENDING changes    — saved and published, but the device has not picked
//                           them up yet. Amber, with the running values shown
//                           beside the new ones (ConfigPendingChanges) and a
//                           per-field note under each input that differs.
//   3. CONFIRMED settings — the device reported this revision back. Green.
//
// Only (2) needs explaining: the server is authoritative and publishes retained,
// so a device that is deep-sleeping simply collects the change on its next wake.
// That is normal operation, not a fault, and the copy says so.
//
// Saving is a full replacement that creates a new immutable revision server-side;
// this component never edits history. The number inputs are the first in the
// codebase — they follow the .form-input convention with min/max/step plus a JS
// range check in the submit handler, the same layering ChangePasswordSection
// uses for its password rules. Every duration among them is a DurationField, so
// the unit is chosen rather than dictated; the form state stays canonical.
//
// REFRESHING. `refreshToken` bumps on the settings tab's 30 s tick and on its
// manual refresh. The panel then re-reads the whole state — which is the point,
// because "Pending" only becomes "In sync" when the DEVICE says so, and until
// this existed the only way to learn that was to reload the page. The one thing
// a tick must never do is disturb work in progress: a form with unsaved edits is
// left exactly as typed while everything around it updates, and a tick landing
// mid-save is skipped outright.
// ============================================================

import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { fetchDeviceConfig, republishDeviceConfig, updateDeviceConfig } from '../services/apiClient'
import type { DeviceConfigStateDto, DeviceConfigValuesDto } from '../services/apiTypes'
import {
  CONFIG_FIELD_LABELS,
  CONFIG_LIMITS,
  describeHours,
  describeSeconds,
  diffConfig,
  estimateQueueSpan,
  formatConfigValue,
} from '../utils/deviceConfig'
import type { TimeUnit } from '../utils/timeUnits'
import { describeError } from '../utils/errors'
import { ConfigPendingChanges } from './ConfigPendingChanges'
import { ConfigSyncIndicator } from './ConfigSyncIndicator'
import { ConfigVersionHistory } from './ConfigVersionHistory'
import { DurationField } from './DurationField'

// The settings the API stores as whole seconds. Hours is the coarsest unit any
// of them reaches — the highest ceiling here is 24 h — so days would only ever
// render as a fraction.
const SECOND_UNITS: readonly TimeUnit[] = ['seconds', 'minutes', 'hours']

// The two retry settings, stored as whole hours. Minutes is offered because a
// retry interval is something people say in minutes; DurationField's step keeps
// such a value landing on a whole hour, which is all the wire can carry.
const HOUR_UNITS: readonly TimeUnit[] = ['minutes', 'hours', 'days']

export type DeviceConfigSectionProps = {
  deviceId: string
  // Bumped by the settings tab's shared auto-refresh timer. Every change of it
  // re-reads the config state; see the header note for what that may touch.
  refreshToken: number
}

export function DeviceConfigSection({ deviceId, refreshToken }: DeviceConfigSectionProps) {
  const [state, setState]         = useState<DeviceConfigStateDto | null>(null)
  const [form, setForm]           = useState<DeviceConfigValuesDto | null>(null)
  const [isLoading, setIsLoading] = useState<boolean>(true)
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false)
  const [isSaving, setIsSaving]   = useState<boolean>(false)
  const [isRepublishing, setIsRepublishing] = useState<boolean>(false)
  const [loadError, setLoadError] = useState<string>('')
  const [message, setMessage]     = useState<string>('')
  const [isError, setIsError]     = useState<boolean>(false)
  // True once a background refresh has deliberately stepped around unsaved
  // edits, so the form can say so rather than leaving the reader wondering
  // whether the values beside their typing are current.
  const [didKeepEdits, setDidKeepEdits] = useState<boolean>(false)

  // Changing this remounts every DurationField, which is how a field re-picks
  // the unit that suits a value the server just handed us. It is bumped only
  // when the seeded VALUES actually changed — a refresh that finds nothing new
  // must not reset a unit the reader deliberately chose.
  const [formSeedKey, setFormSeedKey] = useState<number>(0)

  // Mirrors read by the async load below. They are refs rather than deps
  // because the load must not restart on every keystroke, and because a
  // background tick has to compare against the values as they are *now*.
  const formRef   = useRef<DeviceConfigValuesDto | null>(null)
  const seededRef = useRef<DeviceConfigValuesDto | null>(null)
  const isSavingRef = useRef<boolean>(false)
  // The device the panel has actually loaded, which is what separates a first
  // load (spinner, errors shown loudly) from a refresh tick (neither).
  const loadedDeviceIdRef = useRef<string | null>(null)

  useEffect(() => {
    formRef.current = form
  }, [form])

  useEffect(() => {
    isSavingRef.current = isSaving
  }, [isSaving])

  // Every path that puts server-supplied values INTO the form goes through
  // here. Recording what was seeded is what keeps "dirty" meaningful across a
  // background refresh: without it, a refresh would compare the form against
  // values it had just replaced and always conclude it was clean.
  function seedForm(values: DeviceConfigValuesDto, rekey: boolean): void {
    seededRef.current = values
    setForm(values)
    setDidKeepEdits(false)
    if (rekey) {
      setFormSeedKey((key) => key + 1)
    }
  }

  useEffect(() => {
    let canceled = false

    // A first load for this device, as opposed to a refresh tick for one
    // already on screen.
    const isInitial: boolean = loadedDeviceIdRef.current !== deviceId

    // A tick landing mid-save would race the save's own response, which already
    // carries the new revision. Skip it; the next tick catches up.
    if (!isInitial && isSavingRef.current) {
      return
    }

    async function load(): Promise<void> {
      if (isInitial) {
        setIsLoading(true)
        setLoadError('')
      } else {
        // Never isLoading on a tick: that would replace the whole panel with
        // "Loading settings…" every half minute.
        setIsRefreshing(true)
      }

      try {
        const loaded: DeviceConfigStateDto = await fetchDeviceConfig(deviceId)
        if (canceled) {
          return
        }

        loadedDeviceIdRef.current = deviceId
        // Always replaced, even with a dirty form: the sync badge, the pending
        // table and the per-field "device still on 60 s" notes all read from
        // here, and they are exactly what the reader is refreshing to see.
        setState(loaded)

        // The form always starts from the *desired* revision, not the applied
        // one: the desired values are what the operator last decided, and
        // seeding from the device's older copy would silently offer to roll the
        // pending change back.
        const current: DeviceConfigValuesDto | null = formRef.current
        const seeded:  DeviceConfigValuesDto | null = seededRef.current
        const hasUnsavedEdits: boolean =
          !isInitial &&
          current !== null &&
          seeded !== null &&
          diffConfig(seeded, current).length > 0

        if (hasUnsavedEdits) {
          setDidKeepEdits(true)
        } else {
          // Re-key only on a real change, so a quiet refresh leaves the unit
          // comboboxes as the reader set them.
          const changed: boolean =
            seeded === null || diffConfig(seeded, loaded.desired.values).length > 0
          seedForm(loaded.desired.values, changed)
        }
      } catch (caught) {
        if (canceled) {
          return
        }
        if (isInitial) {
          setLoadError(describeError(caught, 'Failed to load the device settings.'))
        } else {
          // A failed background tick must not replace a working panel with an
          // error state. Say it quietly and keep showing the last good values.
          setMessage(describeError(caught, 'Could not refresh the settings just now.'))
          setIsError(true)
        }
      } finally {
        if (!canceled) {
          setIsLoading(false)
          setIsRefreshing(false)
        }
      }
    }

    void load()
    return () => {
      canceled = true
    }
  }, [deviceId, refreshToken])

  // Unsaved edits. Compared against the desired revision, so it goes false again
  // the moment a save comes back — a pending change is not an unsaved one.
  const isDirty: boolean =
    state !== null && form !== null && diffConfig(state.desired.values, form).length > 0

  // Settings the device has not caught up with yet. Drives both the comparison
  // table and the per-field notes below, so they can never disagree.
  const pendingKeys: (keyof DeviceConfigValuesDto)[] =
    state !== null && state.applied !== null && !state.isInSync
      ? diffConfig(state.applied.values, state.desired.values)
      : []

  function updateField<TKey extends keyof DeviceConfigValuesDto>(
    key: TKey,
    value: DeviceConfigValuesDto[TKey],
  ): void {
    setForm((current) => (current === null ? current : { ...current, [key]: value }))
    setMessage('')
  }

  // Renders the amber "the device is still on the old value" note under a field.
  // Returns null for anything the device has already caught up with, so the form
  // stays quiet except where it has something to say.
  function renderPendingNote(key: keyof DeviceConfigValuesDto) {
    if (state === null || state.applied === null || !pendingKeys.includes(key)) {
      return null
    }
    return (
      <span className="config-field-state">
        ⚠ Device still on {formatConfigValue(key, state.applied.values)}
      </span>
    )
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (form === null) {
      return
    }

    setMessage('')

    // The API validates the same bounds and answers 400, and the firmware clamps
    // rather than rejecting — this check exists purely so a typo gets a sentence
    // instead of a round trip. See CONFIG_LIMITS for where the numbers come from.
    const rangeError: string | null = validateRanges(form)
    if (rangeError !== null) {
      setMessage(rangeError)
      setIsError(true)
      return
    }

    setIsSaving(true)
    try {
      // The response already carries the new state (with the bumped version), so
      // there is no follow-up read and no window where the UI shows stale values.
      const saved: DeviceConfigStateDto = await updateDeviceConfig(deviceId, form)
      setState(saved)
      seedForm(saved.desired.values, true)
      setMessage(
        saved.isInSync
          ? 'Settings saved.'
          : `Settings saved and published as v${saved.desired.version}. The device applies them on its next report.`,
      )
      setIsError(false)
    } catch (caught) {
      setMessage(describeError(caught, 'Failed to save the settings.'))
      setIsError(true)
    } finally {
      setIsSaving(false)
    }
  }

  async function handleRepublish(): Promise<void> {
    setIsRepublishing(true)
    setMessage('')
    try {
      await republishDeviceConfig(deviceId)
      setMessage('Settings re-published to the broker.')
      setIsError(false)
    } catch (caught) {
      setMessage(describeError(caught, 'Failed to re-publish the settings.'))
      setIsError(true)
    } finally {
      setIsRepublishing(false)
    }
  }

  function handleReset(): void {
    if (state !== null) {
      // Re-keyed: discarding the edits should also put every unit combobox back
      // to the one that suits the saved value.
      seedForm(state.desired.values, true)
    }
    setMessage('')
  }

  return (
    <div className="settings-section">
      <div className="settings-section-header">
        <span className="settings-section-icon" aria-hidden="true">⚙️</span>
        <h3>Reporting &amp; Power</h3>
      </div>

      <div className="settings-section-body">
        <p>
          How often this tracker reports, whether it sleeps in between, and what it
          does with fixes it could not deliver. Changes are published to the broker,
          which holds them — so the device does not need to be online right now, and
          nothing has to be reflashed. An awake tracker applies a change within
          seconds; a sleeping one on its next wake.
        </p>

        {isLoading ? <p className="hint">Loading settings…</p> : null}

        {loadError ? (
          <div className="banner banner--error" role="alert">
            {loadError}
          </div>
        ) : null}

        {state !== null && form !== null ? (
          <>
            <ConfigSyncIndicator
              state={state}
              isRepublishing={isRepublishing}
              onRepublish={() => void handleRepublish()}
            />

            {state.applied !== null && !state.isInSync ? (
              <ConfigPendingChanges applied={state.applied} desired={state.desired} />
            ) : null}

            <form onSubmit={(event) => void handleSubmit(event)}>
              {/* fieldset, not per-input disabling: a half-editable form mid-save
                  is a way to lose a keystroke. */}
              <fieldset className="config-fieldset" disabled={isSaving}>
                <legend className="config-group-title">Reporting</legend>

                <div className="config-grid">
                  <DurationField
                    key={`interval-${formSeedKey}`}
                    id="config-interval"
                    label={CONFIG_FIELD_LABELS.intervalSeconds}
                    value={form.intervalSeconds}
                    baseUnit="seconds"
                    units={SECOND_UNITS}
                    min={CONFIG_LIMITS.intervalSeconds.min}
                    max={CONFIG_LIMITS.intervalSeconds.max}
                    onChange={(value) => updateField('intervalSeconds', value)}
                    // Recomputed from the input being typed, not from the saved
                    // value — the point is to read back what you are entering,
                    // and it stays in seconds whatever unit was picked, because
                    // seconds is what actually goes on the wire.
                    hint={`every ${describeSeconds(form.intervalSeconds)}`}
                    pendingNote={renderPendingNote('intervalSeconds')}
                    required
                  />
                </div>
              </fieldset>

              <fieldset className="config-fieldset" disabled={isSaving}>
                <legend className="config-group-title">Power</legend>

                <label className="checkbox-field">
                  <input
                    type="checkbox"
                    checked={form.sleepBetween}
                    onChange={(event) => updateField('sleepBetween', event.target.checked)}
                  />
                  <span>Deep-sleep between reports</span>
                </label>
                <p className="hint">
                  Powers the modem down and reboots the tracker on wake. A large
                  battery saving above roughly five minutes, but every cycle then
                  pays for a cold GNSS fix and a fresh TLS handshake — below that
                  it can cost more than it saves.
                </p>
                {renderPendingNote('sleepBetween')}
              </fieldset>

              <fieldset className="config-fieldset" disabled={isSaving}>
                <legend className="config-group-title">GNSS</legend>

                <div className="config-grid">
                  <DurationField
                    key={`fix-timeout-${formSeedKey}`}
                    id="config-fix-timeout"
                    label="Give up on a lock after"
                    value={form.fixTimeoutSeconds}
                    baseUnit="seconds"
                    units={SECOND_UNITS}
                    min={CONFIG_LIMITS.fixTimeoutSeconds.min}
                    max={CONFIG_LIMITS.fixTimeoutSeconds.max}
                    onChange={(value) => updateField('fixTimeoutSeconds', value)}
                    hint={describeSeconds(form.fixTimeoutSeconds)}
                    pendingNote={renderPendingNote('fixTimeoutSeconds')}
                    required
                  />
                </div>
                <p className="hint">
                  A cold start under a poor sky view can legitimately take minutes.
                  Too short and a parked car never reports; too long and a sleeping
                  tracker burns the battery the sleep was meant to save.
                </p>
              </fieldset>

              <fieldset className="config-fieldset" disabled={isSaving}>
                <legend className="config-group-title">Undelivered fixes</legend>

                <div className="config-grid">
                  <div className="form-field">
                    <label className="form-label" htmlFor="config-queue-max">
                      Keep at most (fixes)
                    </label>
                    <input
                      id="config-queue-max"
                      className="form-input"
                      style={{ width: 'auto' }}
                      type="number"
                      min={CONFIG_LIMITS.queueMaxFixes.min}
                      max={CONFIG_LIMITS.queueMaxFixes.max}
                      step={100}
                      value={form.queueMaxFixes}
                      onChange={(event) => updateField('queueMaxFixes', Number(event.target.value))}
                      required
                    />
                    <span className="hint">
                      {estimateQueueSpan(form.queueMaxFixes, form.intervalSeconds)}
                    </span>
                    {renderPendingNote('queueMaxFixes')}
                  </div>
                </div>
                <p className="hint">
                  While the broker is unreachable each fix is encrypted and queued on
                  the SD card. Past this many, the oldest are dropped so the card can
                  never fill up. It is a count rather than a duration because a queued
                  entry is bare ciphertext with no timestamp to age it by.
                </p>
              </fieldset>

              <fieldset className="config-fieldset" disabled={isSaving}>
                <legend className="config-group-title">Rejected fixes</legend>

                <div className="config-grid">
                  <DurationField
                    key={`retry-interval-${formSeedKey}`}
                    id="config-retry-interval"
                    label="Retry every"
                    value={form.retryIntervalHours}
                    baseUnit="hours"
                    units={HOUR_UNITS}
                    min={CONFIG_LIMITS.retryIntervalHours.min}
                    max={CONFIG_LIMITS.retryIntervalHours.max}
                    onChange={(value) => updateField('retryIntervalHours', value)}
                    hint={describeHours(form.retryIntervalHours)}
                    pendingNote={renderPendingNote('retryIntervalHours')}
                    required
                  />

                  <DurationField
                    key={`retry-max-age-${formSeedKey}`}
                    id="config-retry-max-age"
                    // The "0 = never" stays in the label rather than moving into
                    // the unit combobox: it is a sentinel, not a duration, and
                    // nothing about the unit makes it readable.
                    label="Give up after (0 = never)"
                    value={form.retryMaxAgeHours}
                    baseUnit="hours"
                    units={HOUR_UNITS}
                    min={CONFIG_LIMITS.retryMaxAgeHours.min}
                    max={CONFIG_LIMITS.retryMaxAgeHours.max}
                    onChange={(value) => updateField('retryMaxAgeHours', value)}
                    hint={
                      form.retryMaxAgeHours === 0
                        ? 'keep retrying forever'
                        : describeHours(form.retryMaxAgeHours)
                    }
                    pendingNote={renderPendingNote('retryMaxAgeHours')}
                    required
                  />
                </div>
                <p className="hint">
                  Fixes this server refused are kept apart from the live queue and
                  re-offered slowly, because several reject reasons are server-side and
                  clear on their own. Giving up is the only path that deliberately
                  discards data.
                </p>
              </fieldset>

              <fieldset className="config-fieldset" disabled={isSaving}>
                <legend className="config-group-title">Configuration updates</legend>

                <p className="hint">
                  Settings normally reach this tracker <strong>within a second</strong>:
                  it holds an open subscription, so the broker pushes a change the
                  moment you save it, and a device that reconnects is handed the
                  current settings automatically. The value below is only the
                  backstop — how often it asks the broker to re-send them, in case a
                  connection was quietly dead.
                </p>

                <div className="config-grid">
                  <DurationField
                    key={`config-check-${formSeedKey}`}
                    id="config-check"
                    label="Re-check every"
                    value={form.configCheckSeconds}
                    baseUnit="seconds"
                    units={SECOND_UNITS}
                    min={CONFIG_LIMITS.configCheckSeconds.min}
                    max={CONFIG_LIMITS.configCheckSeconds.max}
                    onChange={(value) => updateField('configCheckSeconds', value)}
                    hint={describeSeconds(form.configCheckSeconds)}
                    pendingNote={renderPendingNote('configCheckSeconds')}
                    required
                  />
                </div>

                {/* Deliberately loud when sleep is on: without this, lowering the
                    re-check interval looks like a way to make a sleeping tracker
                    pick changes up sooner, and it is not. */}
                {form.sleepBetween ? (
                  <div className="banner banner--info" role="status">
                    Deep sleep is on, so this setting does nothing. A sleeping tracker
                    has no connection to re-check — it reads its configuration afresh
                    on every wake, which already makes this redundant.
                  </div>
                ) : null}
              </fieldset>

              {message ? (
                <div
                  className={`banner ${isError ? 'banner--error' : 'banner--success'}`}
                  role={isError ? 'alert' : 'status'}
                  style={{ marginBottom: 12 }}
                >
                  {message}
                </div>
              ) : null}

              <div className="config-actions">
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={isSaving || !isDirty}
                >
                  {isSaving ? 'Saving…' : 'Save settings'}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={handleReset}
                  disabled={isSaving || !isDirty}
                >
                  Reset
                </button>
                {isDirty ? <span className="hint">Unsaved changes</span> : null}
                {/* Only while both are true: a refresh happened AND it found
                    edits to step around. Otherwise the reader is left guessing
                    whether the sync badge above them is stale. */}
                {isDirty && didKeepEdits ? (
                  <span className="hint">
                    Refreshed around your edits — the values you typed were kept.
                  </span>
                ) : null}
                {isRefreshing ? <span className="hint">Refreshing…</span> : null}
              </div>
            </form>

            <ConfigVersionHistory
              deviceId={deviceId}
              currentVersion={state.desired.version}
              appliedVersion={state.applied?.version ?? null}
              // Only matters once the list is open; see the component.
              refreshToken={refreshToken}
              // Fills the form rather than saving, so the values can be reviewed
              // (and adjusted) before they become a new revision.
              //
              // Deliberately NOT routed through seedForm: a restored revision is
              // the reader's intent, not the server's current answer, so it has
              // to read as an unsaved edit. Recording it as seeded would let the
              // next refresh tick decide the form was clean and quietly put the
              // current revision back. Re-keyed all the same, so each unit
              // combobox suits the value it is now showing.
              onRestore={(values) => {
                setForm(values)
                setFormSeedKey((key) => key + 1)
                setDidKeepEdits(false)
                setMessage('Loaded those values into the form — review them, then save.')
                setIsError(false)
              }}
            />
          </>
        ) : null}
      </div>
    </div>
  )
}

// Mirrors the API's [Range] attributes and the firmware's clamps. Returns the
// first problem found, phrased for a person, or null when everything is in range.
function validateRanges(form: DeviceConfigValuesDto): string | null {
  const checks: { key: keyof typeof CONFIG_LIMITS; value: number }[] = [
    { key: 'intervalSeconds', value: form.intervalSeconds },
    { key: 'fixTimeoutSeconds', value: form.fixTimeoutSeconds },
    { key: 'queueMaxFixes', value: form.queueMaxFixes },
    { key: 'retryIntervalHours', value: form.retryIntervalHours },
    { key: 'retryMaxAgeHours', value: form.retryMaxAgeHours },
    { key: 'configCheckSeconds', value: form.configCheckSeconds },
  ]

  for (const check of checks) {
    const limit = CONFIG_LIMITS[check.key]
    if (!Number.isInteger(check.value) || check.value < limit.min || check.value > limit.max) {
      return `"${CONFIG_FIELD_LABELS[check.key]}" must be a whole number between ${limit.min} and ${limit.max}.`
    }
  }

  return null
}
