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
import { useTranslation } from 'react-i18next'
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
import { CONFIG_FIELD_LABEL_KEYS, formatConfigValue } from '../utils/deviceConfig'
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

// Where the tab's first read of the schedule got to. Tracked separately from the
// data because "no schedule yet" and "could not read the schedule" are different
// things to show a person, and conflating them into a null is what made this
// entire panel — profiles, rules, their Edit and Delete buttons — disappear
// without a word whenever the request failed.
export type ScheduleLoadStatus = 'loading' | 'ready' | 'error'

export type ScheduleSectionProps = {
  deviceId: string
  // Null until the first successful read. Kept across a failed refresh, which is
  // why it is independent of `status`.
  schedule: DeviceScheduleStateDto | null
  status: ScheduleLoadStatus
  error: string
  onRetry: () => void
  // Hands the parent the state an endpoint just returned, so the settings panel
  // beside this one updates in the same render.
  onScheduleChanged: (state: DeviceScheduleStateDto) => void
  // The tab's shared refresh counter, passed to the timeline so its "now" marker
  // follows the clock instead of freezing where the page was opened.
  refreshToken: number
}

/**
 * The section's chrome and its load states. The panel is always on screen —
 * that is the point — and it says which of the three states it is in rather
 * than rendering nothing and leaving the reader to conclude the feature was
 * never built.
 */
export function ScheduleSection({
  deviceId,
  schedule,
  status,
  error,
  onRetry,
  onScheduleChanged,
  refreshToken,
}: ScheduleSectionProps) {
  const { t } = useTranslation(['schedule', 'common'])

  return (
    <div className="settings-section">
      <div className="settings-section-header">
        <span className="settings-section-icon" aria-hidden="true">🗓</span>
        <h3>{t('schedule:title')}</h3>
      </div>

      <div className="settings-section-body">
        <p>{t('schedule:intro')}</p>

        {status === 'loading' && schedule === null ? (
          <div className="loading-state" style={{ minHeight: 80 }}>
            <div className="spinner" />
            <span>{t('schedule:loading')}</span>
          </div>
        ) : null}

        {status === 'error' && schedule === null ? (
          <div className="banner banner--error" role="alert">
            <p style={{ margin: '0 0 8px' }}>{error}</p>
            <p className="hint" style={{ margin: '0 0 10px' }}>{t('schedule:loadErrorHint')}</p>
            <button type="button" className="btn btn-secondary btn-sm" onClick={onRetry}>
              {t('common:actions.retry')}
            </button>
          </div>
        ) : null}

        {schedule !== null ? (
          <ScheduleContent
            deviceId={deviceId}
            schedule={schedule}
            onScheduleChanged={onScheduleChanged}
            refreshToken={refreshToken}
          />
        ) : null}
      </div>
    </div>
  )
}

type ScheduleContentProps = {
  deviceId: string
  schedule: DeviceScheduleStateDto
  onScheduleChanged: (state: DeviceScheduleStateDto) => void
  refreshToken: number
}

/**
 * Everything below the intro paragraph, with the schedule known to be loaded.
 * Split out so the four parts below never have to reason about a null schedule
 * — the wrapper above has already settled that question.
 */
function ScheduleContent({
  deviceId,
  schedule,
  onScheduleChanged,
  refreshToken,
}: ScheduleContentProps) {
  // `settings` is in the list because a profile card names configuration
  // fields, whose label keys live in that namespace.
  const { t } = useTranslation(['schedule', 'common', 'errors', 'settings'])

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
      setMessage(describeError(caught, t('errors:scheduleChangeFailed')))
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
        ? t('schedule:message.enabled')
        : t('schedule:message.disabled'),
    )
  }

  function handleFallbackChange(fallbackProfileId: string): void {
    void run(
      'fallback',
      () => updateDeviceSchedule(deviceId, {
        enabled: schedule.enabled,
        fallbackProfileId: fallbackProfileId === '' ? null : fallbackProfileId,
      }),
      t('schedule:message.fallbackUpdated'),
    )
  }

  function handleResume(): void {
    void run('resume', () => resumeDeviceSchedule(deviceId), t('schedule:message.resumed'))
  }

  function handleProfileSubmit(profileId: string | null, payload: SaveConfigProfileRequestDto): void {
    void run(
      'profile',
      () => (profileId === null
        ? createConfigProfile(deviceId, payload)
        : updateConfigProfile(deviceId, profileId, payload)),
      profileId === null ? t('schedule:message.profileCreated') : t('schedule:message.profileSaved'),
      () => setEditingProfileId(null),
    )
  }

  function handleProfileDelete(profile: DeviceConfigProfileDto): void {
    // A profile is referenced by rules and possibly by the fallback, and the API
    // answers 409 rather than cascading — but the confirmation is still worth
    // having, because a profile nothing references is deleted without recourse.
    if (!window.confirm(t('schedule:confirm.deleteProfile', { name: profile.name }))) {
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
      ruleId === null ? t('schedule:message.ruleAdded') : t('schedule:message.ruleSaved'),
      () => setEditingRuleId(null),
    )
  }

  function handleRuleDelete(rule: DeviceScheduleRuleDto): void {
    if (!window.confirm(t('schedule:confirm.deleteRule', { rule: describeRule(rule) }))) {
      return
    }
    void run(
      `rule-${rule.id}`,
      () => deleteScheduleRule(deviceId, rule.id),
      t('schedule:message.ruleDeleted'),
    )
  }

  const canEnable: boolean = schedule.profiles.length > 0 && schedule.fallbackProfileId !== null

  // Why a profile cannot be deleted, or null when it can be.
  //
  // The API refuses with a 409 in exactly these two cases, and rightly so —
  // cascading would leave an hour of the week uncovered, with the tracker
  // quietly changing behaviour as the only evidence. But learning that only
  // AFTER pressing Delete, from a banner at the top of the panel, is a poor way
  // to be told. The answer is already in hand, so it is shown on the card.
  function describeDeleteBlock(profileId: string): string | null {
    if (schedule.fallbackProfileId === profileId) {
      return t('schedule:profile.isFallback')
    }
    const count: number = schedule.rules.filter((rule) => rule.profileId === profileId).length
    return count === 0 ? null : t('schedule:profile.usedByRules', { count })
  }

  return (
    <>
      {/* -------- Enable + fallback -------- */}
        <div className="schedule-controls">
          <label className="checkbox-field">
            <input
              type="checkbox"
              checked={schedule.enabled}
              disabled={busy !== '' || (!schedule.enabled && !canEnable)}
              onChange={(event) => handleToggleEnabled(event.target.checked)}
            />
            <span>{t('schedule:enable')}</span>
          </label>

          <div className="form-field">
            <label className="form-label" htmlFor="schedule-fallback">
              {t('schedule:fallback.label')}
            </label>
            <select
              id="schedule-fallback"
              className="form-input"
              value={schedule.fallbackProfileId ?? ''}
              disabled={busy !== '' || schedule.profiles.length === 0}
              onChange={(event) => handleFallbackChange(event.target.value)}
            >
              <option value="">{t('schedule:fallback.none')}</option>
              {schedule.profiles.map((profile) => (
                <option key={profile.id} value={profile.id}>{profile.name}</option>
              ))}
            </select>
            <span className="hint">{t('schedule:fallback.hint')}</span>
          </div>
        </div>

        {!canEnable && !schedule.enabled ? (
          <div className="banner banner--info" role="status">
            {t('schedule:cannotEnableYet')}
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
              {t('schedule:previewWhenOff')}
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
          <h4 className="config-group-title">{t('schedule:profile.heading')}</h4>
          <p className="hint">{t('schedule:profile.intro')}</p>

          {schedule.profiles.length === 0 ? (
            <p className="hint">{t('schedule:profile.empty')}</p>
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
                    <div className="schedule-card-row">
                      <div className="schedule-card-main">
                        <div className="schedule-card-head">
                          <span className="schedule-card-name">{profile.name}</span>
                          {profile.id === activeProfileId ? (
                            <span className="config-sync-badge config-sync-badge--synced">
                              {t('schedule:profile.inForce')}
                            </span>
                          ) : null}
                          {profile.id === schedule.fallbackProfileId ? (
                            <span className="schedule-status-tag">{t('schedule:status.fallback')}</span>
                          ) : null}
                        </div>

                        <p className="schedule-card-summary">
                          {SUMMARY_KEYS.map((key) =>
                            t('schedule:profile.summaryEntry', {
                              field: t(CONFIG_FIELD_LABEL_KEYS[key]),
                              value: formatConfigValue(key, profile.values),
                            }),
                          ).join(' · ')}
                        </p>
                      </div>

                      <div className="schedule-card-actions">
                        <div className="schedule-card-buttons">
                          <button
                            type="button"
                            className="btn btn-primary btn-sm"
                            onClick={() => { setEditingProfileId(profile.id); setMessage('') }}
                          >
                            {t('common:actions.edit')}
                          </button>
                          <button
                            type="button"
                            className="btn btn-danger btn-sm"
                            onClick={() => handleProfileDelete(profile)}
                            disabled={
                              busy === `profile-${profile.id}`
                              || describeDeleteBlock(profile.id) !== null
                            }
                          >
                            {t('common:actions.delete')}
                          </button>
                        </div>
                        {/* Under the button, not in a tooltip: the way out —
                            repoint those rules, or choose another fallback — has
                            to be readable without hovering or clicking. */}
                        {describeDeleteBlock(profile.id) !== null ? (
                          <span className="hint">{describeDeleteBlock(profile.id)}</span>
                        ) : null}
                      </div>
                    </div>
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
              {t('schedule:profile.add')}
            </button>
          )}
        </div>

        {/* -------- Rules -------- */}
        <div className="schedule-block">
          <h4 className="config-group-title">{t('schedule:ruleList.heading')}</h4>
          <p className="hint">{t('schedule:ruleList.intro')}</p>

          {schedule.rules.length === 0 ? (
            <p className="hint">{t('schedule:ruleList.empty')}</p>
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
                    <div className="schedule-card-row">
                      <div className="schedule-card-main">
                        <div className="schedule-card-head">
                          <span className="schedule-card-name">{describeRule(rule)}</span>
                          <span className="schedule-status-tag">→ {rule.profileName}</span>
                          {rule.id === schedule.status?.activeRuleId ? (
                            <span className="config-sync-badge config-sync-badge--synced">
                              {t('schedule:ruleList.matchingNow')}
                            </span>
                          ) : null}
                          {!rule.isEnabled ? (
                            <span className="config-sync-badge config-sync-badge--unknown">
                              {t('common:onOff.off')}
                            </span>
                          ) : null}
                        </div>

                        <p className="schedule-card-summary">
                          {t('schedule:ruleList.prioritySummary', { priority: rule.priority })}{' '}
                          · {describeDuration(rule.durationMinutes)}
                        </p>
                      </div>

                      <div className="schedule-card-actions">
                        <div className="schedule-card-buttons">
                          <button
                            type="button"
                            className="btn btn-primary btn-sm"
                            onClick={() => { setEditingRuleId(rule.id); setMessage('') }}
                          >
                            {t('common:actions.edit')}
                          </button>
                          {/* Unconditional, unlike a profile's: nothing
                              references a rule, so there is never a reason to
                              refuse. */}
                          <button
                            type="button"
                            className="btn btn-danger btn-sm"
                            onClick={() => handleRuleDelete(rule)}
                            disabled={busy === `rule-${rule.id}`}
                          >
                            {t('common:actions.delete')}
                          </button>
                        </div>
                      </div>
                    </div>
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
              title={schedule.profiles.length === 0 ? t('schedule:ruleList.needProfile') : undefined}
            >
              {t('schedule:ruleList.add')}
            </button>
          )}
        </div>
    </>
  )
}

// A rule as its local weekdays and clock times — the form it was entered in, so
// a reader recognises the rule they wrote without converting anything.
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
