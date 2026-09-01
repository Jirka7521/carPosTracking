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
// this component never edits history. The seven controls themselves live in
// ConfigValuesFields, shared with the schedule's profile editor — a profile is
// the same seven settings under a name, and two editors that validated
// differently would be two places for the bounds to drift.
//
// SCHEDULES. When this device is on a schedule, a save here is TEMPORARY: it
// holds until the next scheduled switch and is then reasserted. That is a severe
// surprise to spring on somebody hours later, so it is announced before the save
// (ConfigOverrideDialog) and while it is live (the amber banner) — and the API
// refuses the save outright without the acknowledgement the dialog collects, so
// it cannot be skipped by a client that has not been updated.
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
import {
  fetchDeviceConfig,
  republishDeviceConfig,
  resumeDeviceSchedule,
  updateDeviceConfig,
} from '../services/apiClient'
import type {
  DeviceConfigStateDto,
  DeviceConfigValuesDto,
  DeviceScheduleStateDto,
} from '../services/apiTypes'
import { diffConfig, formatConfigValue, validateConfigRanges } from '../utils/deviceConfig'
import { describeError } from '../utils/errors'
import { ConfigOverrideDialog } from './ConfigOverrideDialog'
import { ConfigPendingChanges } from './ConfigPendingChanges'
import { ConfigSyncIndicator } from './ConfigSyncIndicator'
import { ConfigValuesFields } from './ConfigValuesFields'
import { ConfigVersionHistory } from './ConfigVersionHistory'
import { ScheduleStatusBanner } from './ScheduleStatusBanner'

export type DeviceConfigSectionProps = {
  deviceId: string
  // Bumped by the settings tab's shared auto-refresh timer. Every change of it
  // re-reads the config state; see the header note for what that may touch.
  refreshToken: number
  // The device's schedule, owned and fetched by the tab. Null while it is still
  // loading or could not be read — in which case this panel behaves exactly as
  // it did before schedules existed, which is the right failure mode.
  schedule: DeviceScheduleStateDto | null
  // Hands the tab a schedule state this panel caused to change, so the schedule
  // section beside it updates in the same render.
  onScheduleChanged: (state: DeviceScheduleStateDto) => void
  // Asks the tab to re-read the schedule. Used after a save that creates an
  // override, whose response carries the settings but not the new override.
  onScheduleReloadNeeded: () => void
}

export function DeviceConfigSection({
  deviceId,
  refreshToken,
  schedule,
  onScheduleChanged,
  onScheduleReloadNeeded,
}: DeviceConfigSectionProps) {
  const [state, setState]         = useState<DeviceConfigStateDto | null>(null)
  const [form, setForm]           = useState<DeviceConfigValuesDto | null>(null)
  const [isLoading, setIsLoading] = useState<boolean>(true)
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false)
  const [isSaving, setIsSaving]   = useState<boolean>(false)
  const [isRepublishing, setIsRepublishing] = useState<boolean>(false)
  const [isResuming, setIsResuming] = useState<boolean>(false)
  const [loadError, setLoadError] = useState<string>('')
  const [message, setMessage]     = useState<string>('')
  const [isError, setIsError]     = useState<boolean>(false)
  // True once a background refresh has deliberately stepped around unsaved
  // edits, so the form can say so rather than leaving the reader wondering
  // whether the values beside their typing are current.
  const [didKeepEdits, setDidKeepEdits] = useState<boolean>(false)
  // Open while the reader is being told their save is temporary. Holding it in
  // state rather than using window.confirm is what lets the dialog name the
  // returning profile and the exact time it returns.
  const [isOverrideDialogOpen, setIsOverrideDialogOpen] = useState<boolean>(false)

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

  // A save is temporary exactly when the schedule is on. Everything about the
  // dialog, the banner and the acknowledgement flag hangs off this one fact.
  const isScheduled: boolean = schedule?.enabled === true

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

  function handleSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    if (form === null) {
      return
    }

    setMessage('')

    // The API validates the same bounds and answers 400, and the firmware clamps
    // rather than rejecting — this check exists purely so a typo gets a sentence
    // instead of a round trip. See CONFIG_LIMITS for where the numbers come from.
    const rangeError: string | null = validateConfigRanges(form)
    if (rangeError !== null) {
      setMessage(rangeError)
      setIsError(true)
      return
    }

    if (isScheduled) {
      // Stop and explain. The save itself happens from the dialog's confirm, so
      // there is exactly one path that sets acknowledgeOverride.
      setIsOverrideDialogOpen(true)
      return
    }

    void save(false)
  }

  async function save(acknowledgeOverride: boolean): Promise<void> {
    if (form === null) {
      return
    }

    setIsSaving(true)
    try {
      // The response already carries the new state (with the bumped version), so
      // there is no follow-up read and no window where the UI shows stale values.
      const saved: DeviceConfigStateDto = await updateDeviceConfig(deviceId, {
        ...form,
        acknowledgeOverride,
      })
      setState(saved)
      seedForm(saved.desired.values, true)
      setMessage(
        acknowledgeOverride
          ? `Saved as v${saved.desired.version}. These values hold until the next scheduled switch.`
          : saved.isInSync
            ? 'Settings saved.'
            : `Settings saved and published as v${saved.desired.version}. The device applies them on its next report.`,
      )
      setIsError(false)
      setIsOverrideDialogOpen(false)

      if (acknowledgeOverride) {
        // The settings response cannot carry the override the server just
        // stamped, so the schedule has to be re-read for the banner to appear.
        onScheduleReloadNeeded()
      }
    } catch (caught) {
      setMessage(describeError(caught, 'Failed to save the settings.'))
      setIsError(true)
      setIsOverrideDialogOpen(false)
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

  // Duplicated on purpose from the schedule panel's own banner: the override was
  // created here, so the way out of it belongs here too. Both call the same
  // endpoint and hand the same state up, so they cannot disagree.
  async function handleResume(): Promise<void> {
    setIsResuming(true)
    setMessage('')
    try {
      onScheduleChanged(await resumeDeviceSchedule(deviceId))
      // The scheduled profile has just been applied, so the settings on screen
      // are now the wrong ones — bumping through a re-read is what puts the form
      // back in step.
      setMessage('Schedule resumed. Refreshing the settings…')
      setIsError(false)
      const reloaded: DeviceConfigStateDto = await fetchDeviceConfig(deviceId)
      setState(reloaded)
      seedForm(reloaded.desired.values, true)
      setMessage('Schedule resumed — the scheduled profile is back in force.')
    } catch (caught) {
      setMessage(describeError(caught, 'Failed to resume the schedule.'))
      setIsError(true)
    } finally {
      setIsResuming(false)
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

        {/* Said before the form rather than after the save. Somebody about to
            edit these values needs to know they are on a timetable. */}
        {isScheduled ? (
          <div className="banner banner--info" role="status">
            <span className="banner-icon" aria-hidden="true">🗓️</span>
            {/* One flex child, so the copy flows as a sentence instead of the
                banner's flex gap prising every <strong> apart. */}
            <div className="banner-text">
              <p className="banner-title">
                {schedule?.status?.activeProfileName
                  ? <>A schedule is running the <strong>{schedule.status.activeProfileName}</strong> profile on this device.</>
                  : <>A schedule is in charge of these settings.</>}
              </p>
              <p className="banner-detail">
                Saving here applies straight away, but the next profile switch replaces it.
                To change it for good, edit the profile in <em>Settings › Schedule</em> above.
              </p>
            </div>
          </div>
        ) : null}

        {isLoading ? <p className="hint">Loading settings…</p> : null}

        {loadError ? (
          <div className="banner banner--error" role="alert">
            {loadError}
          </div>
        ) : null}

        {/* The amber "manual settings are holding the schedule off" banner, with
            its way out. Shown only while an override is actually live. */}
        {schedule !== null && schedule.status !== null && schedule.override !== null ? (
          <ScheduleStatusBanner
            status={schedule.status}
            override={schedule.override}
            evaluatedAt={schedule.evaluatedAt}
            isResuming={isResuming}
            onResume={() => void handleResume()}
          />
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

            <form onSubmit={handleSubmit}>
              <ConfigValuesFields
                values={form}
                onChange={updateField}
                seedKey={formSeedKey}
                disabled={isSaving}
                renderPendingNote={renderPendingNote}
                idPrefix="config"
              />

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
                  {isSaving ? 'Saving…' : isScheduled ? 'Save settings…' : 'Save settings'}
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

      {isOverrideDialogOpen && schedule !== null ? (
        <ConfigOverrideDialog
          // The next switch is what the override will expire at, which is
          // exactly what the server stamps — so the dialog and the banner that
          // follows it quote the same instant.
          resumesAt={schedule.status?.nextChangeAt ?? ''}
          resumingProfileName={schedule.status?.nextProfileName ?? null}
          isSaving={isSaving}
          onConfirm={() => void save(true)}
          onCancel={() => setIsOverrideDialogOpen(false)}
        />
      ) : null}
    </div>
  )
}
