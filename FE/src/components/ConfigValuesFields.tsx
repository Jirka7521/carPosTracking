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
// What it renders is a composition, led by ConfigModeSwitch: the same settings
// all the time, or react to motion wake? Answered "the same", there is one set
// of seven settings. Answered "react", interval, sleep and fix timeout come in
// two sets — STANDBY (parked) and MOVING — each folded into its own block, after
// the motion settings that decide when the device passes from one to the other;
// the queue, retry and re-check settings follow in a third block, because they
// have one value for both modes. The notes about how those interact sit right
// under the switch, outside every block, so folding a group can never be what
// hides a warning. The sets are ModeValuesFields, the shared settings
// SharedValuesFields, the motion settings MotionWakeFields; this file only
// arranges them and says what is worth warning about.
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
import {
  MOVING_KEYS,
  SHARED_KEYS,
  STANDBY_KEYS,
  describeHours,
  describeReportingSummary,
  describeSeconds,
} from '../utils/deviceConfig'
import type { ModeKeys } from '../utils/deviceConfig'
import { ConfigCollapsible } from './ConfigCollapsible'
import { ConfigModeSwitch } from './ConfigModeSwitch'
import { ModeValuesFields } from './ModeValuesFields'
import { MotionWakeFields } from './MotionWakeFields'
import { SharedValuesFields } from './SharedValuesFields'

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

  // Whether any of a mode's three settings has not reached the device yet, for
  // the ⚠ on that mode's folded block.
  function isModePending(keys: ModeKeys): boolean {
    return Object.values(keys).some((key) => pendingNote(key) !== null)
  }

  // The same for the block of settings shared by both modes.
  const isSharedPending: boolean = SHARED_KEYS.some((key) => pendingNote(key) !== null)

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

  return (
    <>
      <ConfigModeSwitch
        motionEnabled={values.motionEnabled}
        onChange={(motionEnabled) => onChange('motionEnabled', motionEnabled)}
        disabled={disabled}
        pendingNote={pendingNote('motionEnabled')}
        idPrefix={idPrefix}
      />

      {isWakeWindowTooLong || isStandbyAwake ? (
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
        </div>
      ) : null}

      {values.motionEnabled ? (
        <>
          <MotionWakeFields
            values={values}
            onChange={onChange}
            seedKey={seedKey}
            disabled={disabled}
            pendingNote={pendingNote}
            idPrefix={idPrefix}
          />

          {/* No `disabled` on the two mode blocks: the groups inside disable
              their own fields. Standby starts open because it is the set the
              tracker spends most of its life on; moving is one click away. */}
          <ConfigCollapsible
            title={t('config.mode.standby')}
            summary={describeReportingSummary(values, STANDBY_KEYS)}
            hasPending={isModePending(STANDBY_KEYS)}
            defaultOpen
          >
            <ModeValuesFields
              values={values}
              keys={STANDBY_KEYS}
              onChange={onChange}
              seedKey={seedKey}
              disabled={disabled}
              pendingNote={pendingNote}
              idPrefix={idPrefix}
            />
          </ConfigCollapsible>

          <ConfigCollapsible
            title={t('config.mode.moving')}
            summary={describeReportingSummary(values, MOVING_KEYS)}
            hasPending={isModePending(MOVING_KEYS)}
          >
            <ModeValuesFields
              values={values}
              keys={MOVING_KEYS}
              onChange={onChange}
              seedKey={seedKey}
              disabled={disabled}
              pendingNote={pendingNote}
              idPrefix={`${idPrefix}-moving`}
            />
          </ConfigCollapsible>

          {/* The queue, retry and re-check settings have one value for both
              modes, so they get a block of their own rather than a copy in
              each — a reader should not have to wonder which one applies. */}
          <ConfigCollapsible
            title={t('config.mode.shared')}
            summary={[
              t('config.fixesCount', {
                count: values.queueMaxFixes,
                value: formatInteger(values.queueMaxFixes),
              }),
              values.retryMaxAgeHours === 0
                ? t('config.retryForever')
                : t('config.summary.maxAge', { duration: describeHours(values.retryMaxAgeHours) }),
            ].join(' · ')}
            hasPending={isSharedPending}
          >
            <SharedValuesFields
              values={values}
              onChange={onChange}
              seedKey={seedKey}
              disabled={disabled}
              pendingNote={pendingNote}
              idPrefix={idPrefix}
            />
          </ConfigCollapsible>
        </>
      ) : (
        // One set and nothing to tell it apart from, so no block around it.
        <>
          <ModeValuesFields
            values={values}
            keys={STANDBY_KEYS}
            onChange={onChange}
            seedKey={seedKey}
            disabled={disabled}
            pendingNote={pendingNote}
            idPrefix={idPrefix}
          />
          <SharedValuesFields
            values={values}
            onChange={onChange}
            seedKey={seedKey}
            disabled={disabled}
            pendingNote={pendingNote}
            idPrefix={idPrefix}
          />
        </>
      )}
    </>
  )
}
