// ---------------------------------------------------------------------------
// ConfigValuesFields — the remote settings as a set of form controls.
//
// Extracted from DeviceConfigSection when schedules arrived and gave it a second
// caller: a schedule PROFILE holds exactly the same values, under exactly
// the same bounds, and an editor for one that looked or behaved differently from
// the settings panel would be a second thing to learn for no reason.
//
// This component is deliberately STATELESS. It renders the values it is given
// and reports edits; every question about what is dirty, what has been saved,
// and what a background refresh may touch stays with whoever owns the state —
// which for the settings panel is a genuinely subtle set of rules and for a
// profile editor is nearly none. Sharing the controls without sharing that logic
// is the whole point of the split.
//
// What it renders is a composition. With motion wake off there is one set of
// seven settings and the form looks exactly as it always did. With it on there
// are two complete sets — STANDBY (parked) and MOVING — around the motion
// settings that decide when the device passes from one to the other, plus the
// notes about how those interact. The sets are ModeValuesFields, the motion
// settings are MotionWakeFields; this file only arranges them and says what is
// worth warning about.
//
// `seedKey` is passed straight through to each DurationField's `key`. Changing
// it remounts them, which is how a field re-picks the unit that suits a value
// the server just handed us; leaving it alone is how a reader's chosen unit
// survives a refresh. See DurationField's header for why that is the parent's
// job.
// ---------------------------------------------------------------------------

import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { formatInteger } from '../i18n/format'
import type { DeviceConfigValuesDto } from '../services/apiTypes'
import { MOVING_KEYS, STANDBY_KEYS, describeSeconds } from '../utils/deviceConfig'
import { ModeValuesFields } from './ModeValuesFields'
import { MotionWakeFields } from './MotionWakeFields'

export type ConfigValuesFieldsProps = {
  values: DeviceConfigValuesDto
  onChange: <TKey extends keyof DeviceConfigValuesDto>(
    key: TKey,
    value: DeviceConfigValuesDto[TKey],
  ) => void
  // Bumped by the owner when server values are seeded; see the header note.
  seedKey: number
  // Applied to every fieldset rather than to each input: a half-editable form
  // mid-save is a way to lose a keystroke.
  disabled?: boolean
  // The "⚠ Device still on 60 s" note under a field, when the caller has one.
  // A profile editor has nothing to say here and passes nothing.
  renderPendingNote?: (key: keyof DeviceConfigValuesDto) => ReactNode
  // Prefix for the input ids, so two of these on one page — the settings form
  // and an open profile editor — do not collide and mis-target their labels.
  idPrefix: string
}

export function ConfigValuesFields({
  values,
  onChange,
  seedKey,
  disabled = false,
  renderPendingNote,
  idPrefix,
}: ConfigValuesFieldsProps) {
  const { t } = useTranslation(['settings'])

  function pendingNote(key: keyof DeviceConfigValuesDto): ReactNode {
    return renderPendingNote ? renderPendingNote(key) : null
  }

  // The device stays awake for up to the whole wait window after EVERY wake, and
  // a timer wake is a wake. A wait at least as long as the standby interval
  // therefore leaves no gap in which to sleep. Only worth saying when standby is
  // meant to sleep at all — an awake standby has nothing to lose here.
  const isWakeWindowTooLong: boolean =
    values.motionEnabled &&
    values.sleepBetween &&
    values.intervalSeconds <= values.motionWakeWaitSeconds

  // An awake standby device has no interrupt to wait for; see the note's text.
  const isStandbyAwake: boolean = values.motionEnabled && !values.sleepBetween

  // The firmware keeps the LARGER of the two caps, in both modes, so that
  // switching mode can never be what deletes a queued fix.
  const queueCap: number = Math.max(values.queueMaxFixes, values.movingQueueMaxFixes)
  const isQueueCapMerged: boolean =
    values.motionEnabled && values.queueMaxFixes !== values.movingQueueMaxFixes

  return (
    <>
      {/* Only named while there is a second set to tell it apart from. */}
      {values.motionEnabled ? (
        <h4 className="config-mode-title">{t('config.mode.standby')}</h4>
      ) : null}

      <ModeValuesFields
        values={values}
        keys={STANDBY_KEYS}
        onChange={onChange}
        seedKey={seedKey}
        disabled={disabled}
        pendingNote={pendingNote}
        idPrefix={idPrefix}
      />

      <MotionWakeFields
        values={values}
        onChange={onChange}
        seedKey={seedKey}
        disabled={disabled}
        pendingNote={pendingNote}
        idPrefix={idPrefix}
      />

      {isWakeWindowTooLong || isStandbyAwake || isQueueCapMerged ? (
        <div className="config-notes">
          {isWakeWindowTooLong ? (
            <div className="banner banner--warning" role="status">
              {t('config.motionWakeWindowWarning', {
                wait: describeSeconds(values.motionWakeWaitSeconds),
                interval: describeSeconds(values.intervalSeconds),
              })}
            </div>
          ) : null}

          {isStandbyAwake ? (
            <div className="banner banner--info" role="status">
              {t('config.motionPollingNote')}
            </div>
          ) : null}

          {isQueueCapMerged ? (
            <div className="banner banner--info" role="status">
              {t('config.motionQueueCapNote', {
                cap: t('config.fixesCount', { count: queueCap, value: formatInteger(queueCap) }),
              })}
            </div>
          ) : null}
        </div>
      ) : null}

      {values.motionEnabled ? (
        <>
          <h4 className="config-mode-title">{t('config.mode.moving')}</h4>

          <ModeValuesFields
            values={values}
            keys={MOVING_KEYS}
            onChange={onChange}
            seedKey={seedKey}
            disabled={disabled}
            pendingNote={pendingNote}
            idPrefix={`${idPrefix}-moving`}
          />
        </>
      ) : null}
    </>
  )
}
