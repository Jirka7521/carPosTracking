// ---------------------------------------------------------------------------
// ScheduleSection — the "Settings schedule" panel on the device settings tab.
//
// Four parts, top to bottom, in the order somebody reads them:
//
//   1. STATUS   what is in force, since when, what it changes to and when —
//               or the amber override banner, if a manual save is holding the
//               schedule off. (ScheduleStatusBanner)
//   2. TIMELINE the whole week as coloured blocks, so a gap, an overlap or a
//               DST-shifted switch time is visible instead of deduced.
//               (ScheduleTimeline)
//   3. PROFILES the named value sets, each editable in place.
//   4. RULES    the weekly windows, in the order they are actually evaluated.
//
// STATE LIVES ABOVE THIS COMPONENT. DeviceSettingsTab owns the schedule and
// fetches it once, because the settings panel needs it too — for the override
// banner and the confirmation dialog — and two components polling the same
// endpoint would be one request too many every thirty seconds. Everything here
// reports a change by handing the parent the state the API returned.
//
// That is also why no mutation is followed by a re-read: every schedule endpoint
// answers with the whole recomputed state, so "add a rule" and "what is in force
// now" arrive together and can never be one render out of step.
// ---------------------------------------------------------------------------

import { useState } from 'react'
import {
  createConfigProfile,
  createScheduleRule,
  deleteConfigProfile,
  deleteScheduleRule,
  resumeDeviceSchedule,
  updateConfigProfile,
  updateDeviceSchedule,
  updateScheduleRule,
} from '../services/apiClient'
import type {
  DeviceConfigProfileDto,
  DeviceScheduleRuleDto,
  DeviceScheduleStateDto,
  SaveConfigProfileRequestDto,
  SaveScheduleRuleRequestDto,
} from '../services/apiTypes'
import { CONFIG_FIELD_LABELS, formatConfigValue } from '../utils/deviceConfig'
import { describeError } from '../utils/errors'
import {
  describeDaysMask,
  describeDuration,
  endMinuteOfDay,
  formatMinuteOfDay,
  utcWindowToLocal,
} from '../utils/schedule'
import { ScheduleProfileEditor } from './ScheduleProfileEditor'
import { ScheduleRuleEditor } from './ScheduleRuleEditor'
import { ScheduleStatusBanner } from './ScheduleStatusBanner'
import { ScheduleTimeline } from './ScheduleTimeline'

// Which value differences are worth summarising on a collapsed profile row. The
// full set would wrap to three lines on every card; these three are what people
// actually distinguish profiles by.
const SUMMARY_KEYS = ['intervalSeconds', 'sleepBetween', 'fixTimeoutSeconds'] as const

export type ScheduleSectionProps = {
  deviceId: string
  schedule: DeviceScheduleStateDto
  // Hands the parent the state an endpoint just returned, so the settings panel
  // beside this one updates in the same render.
  onScheduleChanged: (state: DeviceScheduleStateDto) => void
  // The tab's shared refresh counter, passed to the timeline so its "now" marker
  // follows the clock instead of freezing where the page was opened.
  refreshToken: number
}

export function ScheduleSection({
  deviceId,
  schedule,
  onScheduleChanged,
  refreshToken,
}: ScheduleSectionProps) {
  const [busy, setBusy] = useState<string>('')
  const [message, setMessage] = useState<string>('')
  const [isError, setIsError] = useState<boolean>(false)

  // Which editor is open. 'new' creates; a Guid edits that row; null is closed.
  const [editingProfileId, setEditingProfileId] = useState<string | null>(null)
  const [editingRuleId, setEditingRuleId] = useState<string | null>(null)

  const activeProfileId: string | null = schedule.status?.activeProfileId ?? null
  const profileOrder: string[] = schedule.profiles.map((profile) => profile.id)

  // Every mutation goes through here: one place that runs the call, hands the
  // returned state up, closes the editor and reports the outcome. Without it,
  // eight handlers would each be five lines of the same error plumbing.
  async function run(
    key: string,
    action: () => Promise<DeviceScheduleStateDto>,
    successMessage: string,
    onDone?: () => void,
  ): Promise<void> {
    setBusy(key)
    setMessage('')
    try {
      onScheduleChanged(await action())
      setMessage(successMessage)
      setIsError(false)
      onDone?.()
    } catch (caught) {
      setMessage(describeError(caught, 'That change could not be saved.'))
      setIsError(true)
    } finally {
      setBusy('')
    }
  }

  function handleToggleEnabled(enabled: boolean): void {
    void run(
      'enabled',
      () => updateDeviceSchedule(deviceId, {
        enabled,
        fallbackProfileId: schedule.fallbackProfileId,
      }),
      enabled
        ? 'Schedule enabled — the profile in force has been applied.'
        : 'Schedule turned off. The device keeps its current settings until you change them.',
    )
  }

  function handleFallbackChange(fallbackProfileId: string): void {
    void run(
      'fallback',
      () => updateDeviceSchedule(deviceId, {
        enabled: schedule.enabled,
        fallbackProfileId: fallbackProfileId === '' ? null : fallbackProfileId,
      }),
      'Fallback profile updated.',
    )
  }

  function handleResume(): void {
    void run('resume', () => resumeDeviceSchedule(deviceId), 'Schedule resumed.')
  }

  function handleProfileSubmit(profileId: string | null, payload: SaveConfigProfileRequestDto): void {
    void run(
      'profile',
      () => (profileId === null
        ? createConfigProfile(deviceId, payload)
        : updateConfigProfile(deviceId, profileId, payload)),
      profileId === null ? 'Profile created.' : 'Profile saved.',
      () => setEditingProfileId(null),
    )
  }

  function handleProfileDelete(profile: DeviceConfigProfileDto): void {
    // A profile is referenced by rules and possibly by the fallback, and the API
    // answers 409 rather than cascading — but the confirmation is still worth
    // having, because a profile nothing references is deleted without recourse.
    if (!window.confirm(`Delete the "${profile.name}" profile?`)) {
      return
    }
    void run(
      `profile-${profile.id}`,
      () => deleteConfigProfile(deviceId, profile.id),
      `Deleted "${profile.name}".`,
    )
  }

  function handleRuleSubmit(ruleId: string | null, payload: SaveScheduleRuleRequestDto): void {
    void run(
      'rule',
      () => (ruleId === null
        ? createScheduleRule(deviceId, payload)
        : updateScheduleRule(deviceId, ruleId, payload)),
      ruleId === null ? 'Rule added.' : 'Rule saved.',
      () => setEditingRuleId(null),
    )
  }

  function handleRuleDelete(rule: DeviceScheduleRuleDto): void {
    if (!window.confirm(`Delete the ${describeRule(rule)} rule?`)) {
      return
    }
    void run(`rule-${rule.id}`, () => deleteScheduleRule(deviceId, rule.id), 'Rule deleted.')
  }

  const canEnable: boolean = schedule.profiles.length > 0 && schedule.fallbackProfileId !== null

  return (
    <div className="settings-section">
      <div className="settings-section-header">
        <span className="settings-section-icon" aria-hidden="true">🗓</span>
        <h3>Settings Schedule</h3>
      </div>

      <div className="settings-section-body">
        <p>
          Switch this tracker between named profiles on a weekly timetable — reporting
          less often at night, say, or sleeping through the weekend. The server
          evaluates the schedule and pushes each change the same way saving settings
          by hand does, so nothing has to be reflashed and the tracker does not need
          to be online at the moment a window opens.
        </p>

        {/* -------- Enable + fallback -------- */}
        <div className="schedule-controls">
          <label className="checkbox-field">
            <input
              type="checkbox"
              checked={schedule.enabled}
              disabled={busy !== '' || (!schedule.enabled && !canEnable)}
              onChange={(event) => handleToggleEnabled(event.target.checked)}
            />
            <span>Run this device on a schedule</span>
          </label>

          <div className="form-field">
            <label className="form-label" htmlFor="schedule-fallback">
              Fallback profile
            </label>
            <select
              id="schedule-fallback"
              className="form-input"
              value={schedule.fallbackProfileId ?? ''}
              disabled={busy !== '' || schedule.profiles.length === 0}
              onChange={(event) => handleFallbackChange(event.target.value)}
            >
              <option value="">— none —</option>
              {schedule.profiles.map((profile) => (
                <option key={profile.id} value={profile.id}>{profile.name}</option>
              ))}
            </select>
            <span className="hint">
              What the device runs at any hour no rule covers. Required before the
              schedule can be turned on.
            </span>
          </div>
        </div>

        {!canEnable && !schedule.enabled ? (
          <div className="banner banner--info" role="status">
            Create at least one profile and choose it as the fallback, then you can turn
            the schedule on.
          </div>
        ) : null}

        {message ? (
          <div
            className={`banner ${isError ? 'banner--error' : 'banner--success'}`}
            role={isError ? 'alert' : 'status'}
          >
            {message}
          </div>
        ) : null}

        {/* -------- Status + timeline -------- */}
        {schedule.enabled && schedule.status !== null ? (
          <>
            <ScheduleStatusBanner
              status={schedule.status}
              override={schedule.override}
              evaluatedAt={schedule.evaluatedAt}
              isResuming={busy === 'resume'}
              onResume={handleResume}
            />

            <ScheduleTimeline
              rules={schedule.rules}
              fallbackProfileId={schedule.fallbackProfileId}
              fallbackProfileName={
                schedule.profiles.find((profile) => profile.id === schedule.fallbackProfileId)?.name
                ?? null
              }
              profileOrder={profileOrder}
              refreshToken={refreshToken}
            />
          </>
        ) : null}

        {/* A disabled schedule still shows its timeline — that is how you check
            a schedule reads the way you meant BEFORE handing it a tracker. */}
        {!schedule.enabled && schedule.rules.length > 0 ? (
          <>
            <div className="banner banner--info" role="status">
              The schedule is off, so nothing below is being applied. This is what it
              would do.
            </div>
            <ScheduleTimeline
              rules={schedule.rules}
              fallbackProfileId={schedule.fallbackProfileId}
              fallbackProfileName={
                schedule.profiles.find((profile) => profile.id === schedule.fallbackProfileId)?.name
                ?? null
              }
              profileOrder={profileOrder}
              refreshToken={refreshToken}
            />
          </>
        ) : null}

        {/* -------- Profiles -------- */}
        <div className="schedule-block">
          <h4 className="config-group-title">Profiles</h4>
          <p className="hint">
            A profile is a complete set of this device&rsquo;s settings under a name the
            rules refer to.
          </p>

          {schedule.profiles.length === 0 ? (
            <p className="hint">No profiles yet.</p>
          ) : (
            <div className="schedule-list">
              {schedule.profiles.map((profile) => (
                <div className="schedule-card" key={profile.id}>
                  {editingProfileId === profile.id ? (
                    <ScheduleProfileEditor
                      key={profile.id}
                      profile={profile}
                      isActive={profile.id === activeProfileId}
                      isSaving={busy === 'profile'}
                      onSubmit={(payload) => handleProfileSubmit(profile.id, payload)}
                      onCancel={() => setEditingProfileId(null)}
                    />
                  ) : (
                    <>
                      <div className="schedule-card-head">
                        <span className="schedule-card-name">{profile.name}</span>
                        {profile.id === activeProfileId ? (
                          <span className="config-sync-badge config-sync-badge--synced">in force</span>
                        ) : null}
                        {profile.id === schedule.fallbackProfileId ? (
                          <span className="schedule-status-tag">fallback</span>
                        ) : null}
                      </div>

                      <p className="schedule-card-summary">
                        {SUMMARY_KEYS
                          .map((key) => `${CONFIG_FIELD_LABELS[key]}: ${formatConfigValue(key, profile.values)}`)
                          .join(' · ')}
                      </p>

                      <div className="schedule-card-actions">
                        <button
                          type="button"
                          className="btn btn-ghost btn-sm"
                          onClick={() => { setEditingProfileId(profile.id); setMessage('') }}
                        >
                          Edit
                        </button>
                        <button
                          type="button"
                          className="btn btn-ghost btn-sm"
                          onClick={() => handleProfileDelete(profile)}
                          disabled={busy === `profile-${profile.id}`}
                        >
                          Delete
                        </button>
                      </div>
                    </>
                  )}
                </div>
              ))}
            </div>
          )}

          {editingProfileId === 'new' ? (
            <div className="schedule-card">
              <ScheduleProfileEditor
                key="new-profile"
                profile={null}
                isActive={false}
                isSaving={busy === 'profile'}
                onSubmit={(payload) => handleProfileSubmit(null, payload)}
                onCancel={() => setEditingProfileId(null)}
              />
            </div>
          ) : (
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={() => { setEditingProfileId('new'); setMessage('') }}
            >
              Add profile
            </button>
          )}
        </div>

        {/* -------- Rules -------- */}
        <div className="schedule-block">
          <h4 className="config-group-title">Rules</h4>
          <p className="hint">
            Listed in the order they are evaluated — lower priority number first. The
            first rule whose window contains the moment wins; if none does, the
            fallback applies.
          </p>

          {schedule.rules.length === 0 ? (
            <p className="hint">No rules yet — the fallback profile applies at all times.</p>
          ) : (
            <div className="schedule-list">
              {schedule.rules.map((rule) => (
                <div className="schedule-card" key={rule.id}>
                  {editingRuleId === rule.id ? (
                    <ScheduleRuleEditor
                      key={rule.id}
                      profiles={schedule.profiles}
                      rule={rule}
                      isSaving={busy === 'rule'}
                      onSubmit={(payload) => handleRuleSubmit(rule.id, payload)}
                      onCancel={() => setEditingRuleId(null)}
                    />
                  ) : (
                    <>
                      <div className="schedule-card-head">
                        <span className="schedule-card-name">{describeRule(rule)}</span>
                        <span className="schedule-status-tag">→ {rule.profileName}</span>
                        {rule.id === schedule.status?.activeRuleId ? (
                          <span className="config-sync-badge config-sync-badge--synced">matching now</span>
                        ) : null}
                        {!rule.isEnabled ? (
                          <span className="config-sync-badge config-sync-badge--unknown">off</span>
                        ) : null}
                      </div>

                      <p className="schedule-card-summary">
                        priority {rule.priority} ·{' '}
                        {formatMinuteOfDay(rule.startMinuteUtc)} UTC for{' '}
                        {describeDuration(rule.durationMinutes)}
                      </p>

                      <div className="schedule-card-actions">
                        <button
                          type="button"
                          className="btn btn-ghost btn-sm"
                          onClick={() => { setEditingRuleId(rule.id); setMessage('') }}
                        >
                          Edit
                        </button>
                        <button
                          type="button"
                          className="btn btn-ghost btn-sm"
                          onClick={() => handleRuleDelete(rule)}
                          disabled={busy === `rule-${rule.id}`}
                        >
                          Delete
                        </button>
                      </div>
                    </>
                  )}
                </div>
              ))}
            </div>
          )}

          {editingRuleId === 'new' ? (
            <div className="schedule-card">
              <ScheduleRuleEditor
                key="new-rule"
                profiles={schedule.profiles}
                rule={null}
                isSaving={busy === 'rule'}
                onSubmit={(payload) => handleRuleSubmit(null, payload)}
                onCancel={() => setEditingRuleId(null)}
              />
            </div>
          ) : (
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={() => { setEditingRuleId('new'); setMessage('') }}
              disabled={schedule.profiles.length === 0}
              title={schedule.profiles.length === 0 ? 'Create a profile first' : undefined}
            >
              Add rule
            </button>
          )}
        </div>
      </div>
    </div>
  )
}

// A rule as its local weekdays and clock times — the form it was entered in,
// rebuilt for the collapsed row so a reader never has to convert UTC in their
// head to recognise the rule they wrote.
function describeRule(rule: DeviceScheduleRuleDto): string {
  const local = utcWindowToLocal({
    daysMaskUtc: rule.daysMaskUtc,
    startMinuteUtc: rule.startMinuteUtc,
  })
  const start: string = formatMinuteOfDay(local.startMinuteLocal)
  const end: string = formatMinuteOfDay(
    endMinuteOfDay(local.startMinuteLocal, rule.durationMinutes),
  )
  return `${describeDaysMask(local.daysMaskLocal)} ${start}–${end}`
}
