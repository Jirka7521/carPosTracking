// ---------------------------------------------------------------------------
// ScheduleRuleEditor — one weekly window, entered the way a person says it.
//
//   [Sun][Mon][Tue][Wed][Thu][Fri][Sat]   Mon–Fri  Weekends  Every day
//   from [22:00] to [06:00]  (+1 day, 8 h)
//   profile [ Night ▾ ]   priority [ 100 ]   [x] enabled
//   Stored as 21:00 UTC on Mon–Fri
//
// TWO THINGS ARE DELIBERATE HERE.
//
// The reader enters an END time, not a duration, because that is how people
// describe a window — "ten at night until six" — while the API stores a start
// plus a length, which needs no midnight-wrap convention. The conversion is one
// modulo, and midnight is handled by the "+1 day" note rather than by asking the
// reader to think about it.
//
// The stored UTC line under the form is not debug output. Times are kept in UTC
// and converted by the browser, so a window entered as 22:00 in winter renders
// as 23:00 after the spring clock change — showing both is what makes that
// visible rather than mysterious, and re-entering the time is the fix.
// ---------------------------------------------------------------------------

import { useState } from 'react'
import type { FormEvent } from 'react'
import type {
  DeviceConfigProfileDto,
  DeviceScheduleRuleDto,
  SaveScheduleRuleRequestDto,
} from '../services/apiTypes'
import {
  DAY_LABELS,
  MASK_EVERY_DAY,
  MASK_WEEKDAYS,
  MASK_WEEKEND,
  MINUTES_PER_DAY,
  describeDaysMask,
  describeDuration,
  endMinuteOfDay,
  formatMinuteOfDay,
  localWindowToUtc,
  parseMinuteOfDay,
  utcWindowToLocal,
} from '../utils/schedule'

export type ScheduleRuleEditorProps = {
  profiles: readonly DeviceConfigProfileDto[]
  // The rule being edited, or null to create a new one.
  rule: DeviceScheduleRuleDto | null
  isSaving: boolean
  onSubmit: (payload: SaveScheduleRuleRequestDto) => void
  onCancel: () => void
}

export function ScheduleRuleEditor({
  profiles,
  rule,
  isSaving,
  onSubmit,
  onCancel,
}: ScheduleRuleEditorProps) {
  // Seeded once from the rule. The panel gives this component a `key` tied to
  // the rule id, so opening a different rule remounts it rather than needing an
  // effect that would fight whatever the reader has typed.
  const initialLocal = rule === null
    ? { daysMaskLocal: MASK_WEEKDAYS, startMinuteLocal: 22 * 60 }
    : utcWindowToLocal({ daysMaskUtc: rule.daysMaskUtc, startMinuteUtc: rule.startMinuteUtc })

  const [daysMask, setDaysMask] = useState<number>(initialLocal.daysMaskLocal)
  const [startTime, setStartTime] = useState<string>(
    formatMinuteOfDay(initialLocal.startMinuteLocal),
  )
  const [endTime, setEndTime] = useState<string>(
    formatMinuteOfDay(
      endMinuteOfDay(initialLocal.startMinuteLocal, rule?.durationMinutes ?? 8 * 60),
    ),
  )
  const [profileId, setProfileId] = useState<string>(
    rule?.profileId ?? profiles[0]?.id ?? '',
  )
  const [priority, setPriority] = useState<number>(rule?.priority ?? 100)
  const [isEnabled, setIsEnabled] = useState<boolean>(rule?.isEnabled ?? true)
  const [error, setError] = useState<string>('')

  const startMinute: number | null = parseMinuteOfDay(startTime)
  const endMinute: number | null = parseMinuteOfDay(endTime)

  // Equal start and end means a whole day, not an empty window — "00:00 to
  // 00:00" is how you say "all day", and a zero-length rule would be one the API
  // rejects anyway.
  const durationMinutes: number | null =
    startMinute === null || endMinute === null
      ? null
      : ((endMinute - startMinute + MINUTES_PER_DAY) % MINUTES_PER_DAY) || MINUTES_PER_DAY

  const wrapsMidnight: boolean =
    startMinute !== null && durationMinutes !== null && startMinute + durationMinutes > MINUTES_PER_DAY

  // Recomputed live from what is currently typed, so the UTC line below is never
  // one edit behind the fields it describes.
  const stored = startMinute === null
    ? null
    : localWindowToUtc({ daysMaskLocal: daysMask, startMinuteLocal: startMinute })

  function toggleDay(day: number): void {
    setDaysMask((current) => current ^ (1 << day))
    setError('')
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()

    if (profileId === '') {
      setError('Choose the profile this window should apply.')
      return
    }
    if (daysMask === 0) {
      setError('Pick at least one day. To park a rule without losing it, untick "Enabled".')
      return
    }
    if (startMinute === null || endMinute === null || durationMinutes === null || stored === null) {
      setError('Enter both times as HH:MM.')
      return
    }

    onSubmit({
      profileId,
      daysMaskUtc: stored.daysMaskUtc,
      startMinuteUtc: stored.startMinuteUtc,
      durationMinutes,
      priority,
      isEnabled,
    })
  }

  return (
    <form className="schedule-rule-editor" onSubmit={handleSubmit}>
      <div className="form-field">
        <span className="form-label">Days</span>
        <div className="schedule-day-toggles" role="group" aria-label="Days of the week">
          {DAY_LABELS.map((label, day) => (
            <button
              key={label}
              type="button"
              className={`schedule-day-toggle${daysMask & (1 << day) ? ' is-on' : ''}`}
              onClick={() => toggleDay(day)}
              aria-pressed={(daysMask & (1 << day)) !== 0}
            >
              {label}
            </button>
          ))}
        </div>
        <div className="schedule-day-presets">
          {/* The three sets people actually mean, so the common case is one
              click rather than five. */}
          <button type="button" className="btn btn-ghost btn-sm" onClick={() => setDaysMask(MASK_WEEKDAYS)}>
            Mon–Fri
          </button>
          <button type="button" className="btn btn-ghost btn-sm" onClick={() => setDaysMask(MASK_WEEKEND)}>
            Weekends
          </button>
          <button type="button" className="btn btn-ghost btn-sm" onClick={() => setDaysMask(MASK_EVERY_DAY)}>
            Every day
          </button>
        </div>
      </div>

      <div className="config-grid">
        <div className="form-field">
          <label className="form-label" htmlFor="rule-start">From</label>
          <input
            id="rule-start"
            className="form-input"
            type="time"
            value={startTime}
            onChange={(event) => { setStartTime(event.target.value); setError('') }}
            required
          />
        </div>

        <div className="form-field">
          <label className="form-label" htmlFor="rule-end">To</label>
          <input
            id="rule-end"
            className="form-input"
            type="time"
            value={endTime}
            onChange={(event) => { setEndTime(event.target.value); setError('') }}
            required
          />
          <span className="hint">
            {durationMinutes === null
              ? ''
              : `${describeDuration(durationMinutes)}${wrapsMidnight ? ' · ends the next day' : ''}`}
          </span>
        </div>
      </div>

      <div className="config-grid">
        <div className="form-field">
          <label className="form-label" htmlFor="rule-profile">Apply profile</label>
          <select
            id="rule-profile"
            className="form-input"
            value={profileId}
            onChange={(event) => { setProfileId(event.target.value); setError('') }}
            required
          >
            {profiles.length === 0 ? <option value="">No profiles yet</option> : null}
            {profiles.map((profile) => (
              <option key={profile.id} value={profile.id}>{profile.name}</option>
            ))}
          </select>
        </div>

        <div className="form-field">
          <label className="form-label" htmlFor="rule-priority">Priority</label>
          <input
            id="rule-priority"
            className="form-input"
            style={{ width: 'auto' }}
            type="number"
            min={0}
            max={1000}
            value={priority}
            onChange={(event) => setPriority(Number(event.target.value))}
            required
          />
          <span className="hint">Lower wins where windows overlap.</span>
        </div>
      </div>

      <label className="checkbox-field">
        <input
          type="checkbox"
          checked={isEnabled}
          onChange={(event) => setIsEnabled(event.target.checked)}
        />
        <span>Enabled</span>
      </label>

      {/* See the header note: this line is why a DST shift is noticeable rather
          than a mystery six months later. */}
      {stored !== null && durationMinutes !== null ? (
        <p className="hint schedule-utc-note">
          Stored as <strong>{formatMinuteOfDay(stored.startMinuteUtc)} UTC</strong> on{' '}
          {describeDaysMask(stored.daysMaskUtc)}, for {describeDuration(durationMinutes)}.
        </p>
      ) : null}

      {error ? <div className="banner banner--error" role="alert">{error}</div> : null}

      <div className="config-actions">
        <button type="submit" className="btn btn-primary btn-sm" disabled={isSaving}>
          {isSaving ? 'Saving…' : rule === null ? 'Add rule' : 'Save rule'}
        </button>
        <button type="button" className="btn btn-secondary btn-sm" onClick={onCancel} disabled={isSaving}>
          Cancel
        </button>
      </div>
    </form>
  )
}
