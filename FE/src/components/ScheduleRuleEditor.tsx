// ---------------------------------------------------------------------------
// ScheduleRuleEditor — one weekly window, entered the way a person says it.
//
//   [Sun][Mon][Tue][Wed][Thu][Fri][Sat]   Mon–Fri  Weekends  Every day
//   from [22:00] to [06:00]  (8 h · ends the next day)
//   profile [ Night ▾ ]   priority [ 100 ]   [x] enabled
//   Mon–Fri, 22:00–06:00 the next day · 8 h
//
// The reader enters an END time, not a duration, because that is how people
// describe a window — "ten at night until six" — while the API takes a start
// plus a length, which needs no midnight-wrap convention. The conversion is one
// modulo, and midnight is handled by the "ends the next day" note rather than by
// asking the reader to think about it.
//
// The summary line at the foot restates the whole window in one sentence. It
// earns its place on the days rather than the times: people tick day boxes,
// then change the hours, and lose track of which combination they have actually
// described.
// ---------------------------------------------------------------------------

import { useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type {
  DeviceConfigProfileDto,
  DeviceScheduleRuleDto,
  SaveScheduleRuleRequestDto,
} from '../services/apiTypes'
import {
  dayLabelShort,
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
  const { t } = useTranslation(['schedule', 'common'])

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
      setError(t('schedule:rule.errorNoProfile'))
      return
    }
    if (daysMask === 0) {
      setError(t('schedule:rule.errorNoDays'))
      return
    }
    if (startMinute === null || endMinute === null || durationMinutes === null || stored === null) {
      setError(t('schedule:rule.errorBadTime'))
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
        <span className="form-label">{t('schedule:rule.days')}</span>
        <div className="schedule-day-toggles" role="group" aria-label={t('schedule:rule.daysGroup')}>
          {[0, 1, 2, 3, 4, 5, 6].map((day) => (
            <button
              key={day}
              type="button"
              className={`schedule-day-toggle${daysMask & (1 << day) ? ' is-on' : ''}`}
              onClick={() => toggleDay(day)}
              aria-pressed={(daysMask & (1 << day)) !== 0}
            >
              {dayLabelShort(day)}
            </button>
          ))}
        </div>
        <div className="schedule-day-presets">
          {/* The three sets people actually mean, so the common case is one
              click rather than five. */}
          <button type="button" className="btn btn-quiet btn-sm" onClick={() => setDaysMask(MASK_WEEKDAYS)}>
            {t('schedule:days.weekdays')}
          </button>
          <button type="button" className="btn btn-quiet btn-sm" onClick={() => setDaysMask(MASK_WEEKEND)}>
            {t('schedule:days.weekendPreset')}
          </button>
          <button type="button" className="btn btn-quiet btn-sm" onClick={() => setDaysMask(MASK_EVERY_DAY)}>
            {t('schedule:days.everyDay')}
          </button>
        </div>
      </div>

      <div className="config-grid">
        <div className="form-field">
          <label className="form-label" htmlFor="rule-start">{t('schedule:rule.from')}</label>
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
          <label className="form-label" htmlFor="rule-end">{t('schedule:rule.to')}</label>
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
              : wrapsMidnight
                ? t('schedule:rule.durationWrapping', {
                    duration: describeDuration(durationMinutes),
                  })
                : describeDuration(durationMinutes)}
          </span>
        </div>
      </div>

      <div className="config-grid">
        <div className="form-field">
          <label className="form-label" htmlFor="rule-profile">{t('schedule:rule.applyProfile')}</label>
          <select
            id="rule-profile"
            className="form-input"
            value={profileId}
            onChange={(event) => { setProfileId(event.target.value); setError('') }}
            required
          >
            {profiles.length === 0 ? <option value="">{t('schedule:rule.noProfiles')}</option> : null}
            {profiles.map((profile) => (
              <option key={profile.id} value={profile.id}>{profile.name}</option>
            ))}
          </select>
        </div>

        <div className="form-field">
          <label className="form-label" htmlFor="rule-priority">{t('schedule:rule.priority')}</label>
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
          <span className="hint">{t('schedule:rule.priorityHint')}</span>
        </div>
      </div>

      <label className="checkbox-field">
        <input
          type="checkbox"
          checked={isEnabled}
          onChange={(event) => setIsEnabled(event.target.checked)}
        />
        <span>{t('schedule:rule.enabled')}</span>
      </label>

      {/* A plain restatement of the window in the reader's own terms. The
          duration hint beside "To" already covers the length; this is the line
          that confirms which days it will actually open on, which is the part
          people get wrong when they tick boxes and then change the time. */}
      {stored !== null && durationMinutes !== null ? (
        <p className="hint schedule-utc-note">
          {t(wrapsMidnight ? 'schedule:rule.summaryWrapping' : 'schedule:rule.summary', {
            days: describeDaysMask(daysMask),
            from: startTime,
            to: endTime,
            duration: describeDuration(durationMinutes),
          })}
        </p>
      ) : null}

      {error ? <div className="banner banner--error" role="alert">{error}</div> : null}

      <div className="config-actions">
        <button type="submit" className="btn btn-primary btn-sm" disabled={isSaving}>
          {isSaving
            ? t('common:actions.saving')
            : rule === null
              ? t('schedule:rule.add')
              : t('schedule:rule.save')}
        </button>
        <button type="button" className="btn btn-secondary btn-sm" onClick={onCancel} disabled={isSaving}>
          {t('common:actions.cancel')}
        </button>
      </div>
    </form>
  )
}
