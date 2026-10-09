// ---------------------------------------------------------------------------
// SharedValuesFields — the four settings with one value for both modes.
//
// The undelivered-fix queue, the two rejected-fix knobs and the config
// re-check govern storage and the link, not how the car is sampled, so the
// firmware applies them whichever mode is in force (SHARED_KEYS in
// utils/deviceConfig). They used to sit in ModeValuesFields with a moving copy
// of each, which the tracker then had to reconcile — the larger queue cap, the
// more lenient give-up age — because a cap that followed the mode would trim
// the queue every time the car parked. One value each, rendered once, is what
// the device actually runs, so it is what the form shows.
//
// Stateless like its siblings: it renders the values it is given and reports
// edits. With motion wake on, ConfigValuesFields folds it into a block of its
// own beside the standby and moving blocks; with it off, there is only one set
// and it renders straight after it. `idPrefix` is the standby prefix, so the
// inputs keep the ids they had before these settings moved here.
// ---------------------------------------------------------------------------

import type { ReactNode } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { formatInteger } from '../i18n/format'
import type { DeviceConfigValuesDto } from '../services/apiTypes'
import {
  CONFIG_LIMITS,
  describeHours,
  describeSeconds,
  estimateQueueSpan,
  estimateQueueSpanByMode,
} from '../utils/deviceConfig'
import type { TimeUnit } from '../utils/timeUnits'
import { ConfigCollapsible } from './ConfigCollapsible'
import { DurationField } from './DurationField'

// The re-check is stored as whole seconds; hours is the coarsest unit it reaches
// — its ceiling is 24 h — so days would only ever render as a fraction.
const SECOND_UNITS: readonly TimeUnit[] = ['seconds', 'minutes', 'hours']

// The two retry settings, stored as whole hours. Minutes is offered because a
// retry interval is something people say in minutes; DurationField's step keeps
// such a value landing on a whole hour, which is all the wire can carry.
const HOUR_UNITS: readonly TimeUnit[] = ['minutes', 'hours', 'days']

export type SharedValuesFieldsProps = {
  values: DeviceConfigValuesDto
  onChange: <TKey extends keyof DeviceConfigValuesDto>(
    key: TKey,
    value: DeviceConfigValuesDto[TKey],
  ) => void
  // Bumped by the owner when server values are seeded; see ConfigValuesFields.
  seedKey: number
  // Applied to every fieldset rather than to each input: a half-editable form
  // mid-save is a way to lose a keystroke.
  disabled: boolean
  // The "⚠ Device still on 60 s" note under a field; null where there is none.
  pendingNote: (key: keyof DeviceConfigValuesDto) => ReactNode
  // Prefix for the input ids; see the header note.
  idPrefix: string
}

export function SharedValuesFields({
  values,
  onChange,
  seedKey,
  disabled,
  pendingNote,
  idPrefix,
}: SharedValuesFieldsProps) {
  const { t } = useTranslation(['settings'])

  // Whether any of these keys carries a "device still on …" note, for the ⚠ on
  // the group; see ModeValuesFields for why it is read off the note itself.
  function isPending(...pendingKeys: (keyof DeviceConfigValuesDto)[]): boolean {
    return pendingKeys.some((key) => pendingNote(key) !== null)
  }

  // One fix is queued per reporting cycle, and with motion wake on there are two
  // cycles the one cap has to cover.
  const queueSpan: string = values.motionEnabled
    ? estimateQueueSpanByMode(
        values.queueMaxFixes,
        values.intervalSeconds,
        values.movingIntervalSeconds,
      )
    : estimateQueueSpan(values.queueMaxFixes, values.intervalSeconds)

  // The re-check only does nothing when EVERY set in use sleeps between reports:
  // with motion wake on and one of the two awake, it still backs that one up.
  const isConfigCheckIdle: boolean =
    values.sleepBetween && (!values.motionEnabled || values.movingSleepBetween)

  return (
    <>
      <ConfigCollapsible
        title={t('config.group.queue')}
        summary={t('config.fixesCount', {
          count: values.queueMaxFixes,
          value: formatInteger(values.queueMaxFixes),
        })}
        hasPending={isPending('queueMaxFixes')}
        disabled={disabled}
      >
        <div className="config-grid">
          <div className="form-field">
            <label className="form-label" htmlFor={`${idPrefix}-queue-max`}>
              {t('config.queueMaxLabel')}
            </label>
            <input
              id={`${idPrefix}-queue-max`}
              className="form-input"
              style={{ width: 'auto' }}
              type="number"
              min={CONFIG_LIMITS.queueMaxFixes.min}
              max={CONFIG_LIMITS.queueMaxFixes.max}
              step={100}
              value={values.queueMaxFixes}
              onChange={(event) => onChange('queueMaxFixes', Number(event.target.value))}
              required
            />
            <span className="hint">{queueSpan}</span>
            {pendingNote('queueMaxFixes')}
          </div>
        </div>
        <p className="hint">{t('config.queueMaxHint')}</p>
      </ConfigCollapsible>

      <ConfigCollapsible
        title={t('config.group.rejected')}
        summary={[
          t('config.everyDuration', { duration: describeHours(values.retryIntervalHours) }),
          values.retryMaxAgeHours === 0
            ? t('config.retryForever')
            : t('config.summary.maxAge', { duration: describeHours(values.retryMaxAgeHours) }),
        ].join(' · ')}
        hasPending={isPending('retryIntervalHours', 'retryMaxAgeHours')}
        disabled={disabled}
      >
        <div className="config-grid">
          <DurationField
            key={`retry-interval-${seedKey}`}
            id={`${idPrefix}-retry-interval`}
            label={t('config.retryIntervalLabel')}
            value={values.retryIntervalHours}
            baseUnit="hours"
            units={HOUR_UNITS}
            min={CONFIG_LIMITS.retryIntervalHours.min}
            max={CONFIG_LIMITS.retryIntervalHours.max}
            onChange={(value) => onChange('retryIntervalHours', value)}
            hint={describeHours(values.retryIntervalHours)}
            pendingNote={pendingNote('retryIntervalHours')}
            required
          />

          <DurationField
            key={`retry-max-age-${seedKey}`}
            id={`${idPrefix}-retry-max-age`}
            // The "0 = never" stays in the label rather than moving into the
            // unit combobox: it is a sentinel, not a duration, and nothing about
            // the unit makes it readable.
            label={t('config.retryMaxAgeLabel')}
            value={values.retryMaxAgeHours}
            baseUnit="hours"
            units={HOUR_UNITS}
            min={CONFIG_LIMITS.retryMaxAgeHours.min}
            max={CONFIG_LIMITS.retryMaxAgeHours.max}
            onChange={(value) => onChange('retryMaxAgeHours', value)}
            hint={
              values.retryMaxAgeHours === 0
                ? t('config.retryForever')
                : describeHours(values.retryMaxAgeHours)
            }
            pendingNote={pendingNote('retryMaxAgeHours')}
            required
          />
        </div>
        <p className="hint">{t('config.rejectedHint')}</p>
      </ConfigCollapsible>

      <ConfigCollapsible
        title={t('config.group.updates')}
        summary={t('config.everyDuration', { duration: describeSeconds(values.configCheckSeconds) })}
        hasPending={isPending('configCheckSeconds')}
        disabled={disabled}
      >
        {/* <Trans> rather than t(): the sentence carries a <strong> in the
            middle of it, and splitting it into three keys would leave the
            translator with fragments that cannot be reordered. */}
        <p className="hint">
          <Trans i18nKey="config.updatesHint" ns="settings" components={{ strong: <strong /> }} />
        </p>

        <div className="config-grid">
          <DurationField
            key={`config-check-${seedKey}`}
            id={`${idPrefix}-config-check`}
            label={t('config.configCheckLabel')}
            value={values.configCheckSeconds}
            baseUnit="seconds"
            units={SECOND_UNITS}
            min={CONFIG_LIMITS.configCheckSeconds.min}
            max={CONFIG_LIMITS.configCheckSeconds.max}
            onChange={(value) => onChange('configCheckSeconds', value)}
            hint={describeSeconds(values.configCheckSeconds)}
            pendingNote={pendingNote('configCheckSeconds')}
            required
          />
        </div>

        {/* Deliberately loud when sleep is on: without this, lowering the
            re-check interval looks like a way to make a sleeping tracker pick
            changes up sooner, and it is not. */}
        {isConfigCheckIdle ? (
          <div className="banner banner--info" role="status">
            {t('config.configCheckSleepNote')}
          </div>
        ) : null}
      </ConfigCollapsible>
    </>
  )
}
