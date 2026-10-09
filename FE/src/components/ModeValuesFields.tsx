// ---------------------------------------------------------------------------
// ModeValuesFields — one operating mode's seven settings as form controls.
//
// Extracted from ConfigValuesFields when motion wake gave the device a second,
// complete copy of the same seven settings. The STANDBY set (parked) and the
// MOVING set (driving) have the same bounds, the same units, the same hints and
// the same quirks — so they are the same JSX, handed a ModeKeys map that says
// which key each control reads and writes. Two hand-written copies would drift
// the first time one of them gained a hint.
//
// Like its parent it is STATELESS: it renders the values it is given and reports
// edits. `idPrefix` is what keeps the two instances apart on one page — the
// standby one keeps the prefix the form always had, the moving one gets its own,
// so a label never points at the other mode's input. `seedKey` is passed
// straight through to each DurationField's `key`; see ConfigValuesFields.
//
// Each group is a ConfigCollapsible that folds to a one-line summary. Only
// reporting and power start open: they are what people come here to change,
// and the rest — GNSS, the two queues, the re-check backstop — are tuned once,
// if ever, and read fine as a summary until then. Interval and sleep share a
// group because neither means much without the other; see sleepBetweenHint.
// ---------------------------------------------------------------------------

import type { ReactNode } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { formatInteger } from '../i18n/format'
import type { DeviceConfigValuesDto } from '../services/apiTypes'
import {
  CONFIG_FIELD_LABEL_KEYS,
  CONFIG_LIMITS,
  describeHours,
  describeReportingSummary,
  describeSeconds,
  estimateQueueSpan,
} from '../utils/deviceConfig'
import type { ModeKeys } from '../utils/deviceConfig'
import type { TimeUnit } from '../utils/timeUnits'
import { ConfigCollapsible } from './ConfigCollapsible'
import { DurationField } from './DurationField'

// The settings the API stores as whole seconds. Hours is the coarsest unit any
// of them reaches — the highest ceiling here is 24 h — so days would only ever
// render as a fraction.
const SECOND_UNITS: readonly TimeUnit[] = ['seconds', 'minutes', 'hours']

// The two retry settings, stored as whole hours. Minutes is offered because a
// retry interval is something people say in minutes; DurationField's step keeps
// such a value landing on a whole hour, which is all the wire can carry.
const HOUR_UNITS: readonly TimeUnit[] = ['minutes', 'hours', 'days']

export type ModeValuesFieldsProps = {
  values: DeviceConfigValuesDto
  // Which seven keys of `values` this instance edits: STANDBY_KEYS or MOVING_KEYS.
  keys: ModeKeys
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

export function ModeValuesFields({
  values,
  keys,
  onChange,
  seedKey,
  disabled,
  pendingNote,
  idPrefix,
}: ModeValuesFieldsProps) {
  const { t } = useTranslation(['settings'])

  // Whether any of these keys carries a "device still on …" note. Read off the
  // note itself rather than a second list of pending keys, so a group's ⚠ and
  // the notes inside it come from one answer.
  function isPending(...pendingKeys: (keyof DeviceConfigValuesDto)[]): boolean {
    return pendingKeys.some((key) => pendingNote(key) !== null)
  }

  return (
    <>
      <ConfigCollapsible
        title={t('config.group.reportingPower')}
        summary={describeReportingSummary(values, keys)}
        hasPending={isPending(keys.interval, keys.sleepBetween)}
        defaultOpen
        disabled={disabled}
      >
        <div className="config-grid">
          <DurationField
            key={`interval-${seedKey}`}
            id={`${idPrefix}-interval`}
            label={t(CONFIG_FIELD_LABEL_KEYS[keys.interval])}
            value={values[keys.interval]}
            baseUnit="seconds"
            units={SECOND_UNITS}
            min={CONFIG_LIMITS[keys.interval].min}
            max={CONFIG_LIMITS[keys.interval].max}
            onChange={(value) => onChange(keys.interval, value)}
            // Recomputed from the input being typed, not from the saved value —
            // the point is to read back what you are entering, and it stays in
            // seconds whatever unit was picked, because seconds is what actually
            // goes on the wire.
            hint={t('config.everyDuration', { duration: describeSeconds(values[keys.interval]) })}
            pendingNote={pendingNote(keys.interval)}
            required
          />
        </div>

        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={values[keys.sleepBetween]}
            onChange={(event) => onChange(keys.sleepBetween, event.target.checked)}
          />
          <span>{t(CONFIG_FIELD_LABEL_KEYS[keys.sleepBetween])}</span>
        </label>
        <p className="hint">{t('config.sleepBetweenHint')}</p>
        {pendingNote(keys.sleepBetween)}
      </ConfigCollapsible>

      <ConfigCollapsible
        title={t('config.group.gnss')}
        summary={t('config.summary.fixTimeout', { duration: describeSeconds(values[keys.fixTimeout]) })}
        hasPending={isPending(keys.fixTimeout)}
        disabled={disabled}
      >
        <div className="config-grid">
          <DurationField
            key={`fix-timeout-${seedKey}`}
            id={`${idPrefix}-fix-timeout`}
            label={t('config.fixTimeoutLabel')}
            value={values[keys.fixTimeout]}
            baseUnit="seconds"
            units={SECOND_UNITS}
            min={CONFIG_LIMITS[keys.fixTimeout].min}
            max={CONFIG_LIMITS[keys.fixTimeout].max}
            onChange={(value) => onChange(keys.fixTimeout, value)}
            hint={describeSeconds(values[keys.fixTimeout])}
            pendingNote={pendingNote(keys.fixTimeout)}
            required
          />
        </div>
        <p className="hint">{t('config.fixTimeoutHint')}</p>
      </ConfigCollapsible>

      <ConfigCollapsible
        title={t('config.group.queue')}
        summary={t('config.fixesCount', {
          count: values[keys.queueMax],
          value: formatInteger(values[keys.queueMax]),
        })}
        hasPending={isPending(keys.queueMax)}
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
              min={CONFIG_LIMITS[keys.queueMax].min}
              max={CONFIG_LIMITS[keys.queueMax].max}
              step={100}
              value={values[keys.queueMax]}
              onChange={(event) => onChange(keys.queueMax, Number(event.target.value))}
              required
            />
            <span className="hint">
              {estimateQueueSpan(values[keys.queueMax], values[keys.interval])}
            </span>
            {pendingNote(keys.queueMax)}
          </div>
        </div>
        <p className="hint">{t('config.queueMaxHint')}</p>
      </ConfigCollapsible>

      <ConfigCollapsible
        title={t('config.group.rejected')}
        summary={[
          t('config.everyDuration', { duration: describeHours(values[keys.retryInterval]) }),
          values[keys.retryMaxAge] === 0
            ? t('config.retryForever')
            : t('config.summary.maxAge', { duration: describeHours(values[keys.retryMaxAge]) }),
        ].join(' · ')}
        hasPending={isPending(keys.retryInterval, keys.retryMaxAge)}
        disabled={disabled}
      >
        <div className="config-grid">
          <DurationField
            key={`retry-interval-${seedKey}`}
            id={`${idPrefix}-retry-interval`}
            label={t('config.retryIntervalLabel')}
            value={values[keys.retryInterval]}
            baseUnit="hours"
            units={HOUR_UNITS}
            min={CONFIG_LIMITS[keys.retryInterval].min}
            max={CONFIG_LIMITS[keys.retryInterval].max}
            onChange={(value) => onChange(keys.retryInterval, value)}
            hint={describeHours(values[keys.retryInterval])}
            pendingNote={pendingNote(keys.retryInterval)}
            required
          />

          <DurationField
            key={`retry-max-age-${seedKey}`}
            id={`${idPrefix}-retry-max-age`}
            // The "0 = never" stays in the label rather than moving into the
            // unit combobox: it is a sentinel, not a duration, and nothing about
            // the unit makes it readable.
            label={t('config.retryMaxAgeLabel')}
            value={values[keys.retryMaxAge]}
            baseUnit="hours"
            units={HOUR_UNITS}
            min={CONFIG_LIMITS[keys.retryMaxAge].min}
            max={CONFIG_LIMITS[keys.retryMaxAge].max}
            onChange={(value) => onChange(keys.retryMaxAge, value)}
            hint={
              values[keys.retryMaxAge] === 0
                ? t('config.retryForever')
                : describeHours(values[keys.retryMaxAge])
            }
            pendingNote={pendingNote(keys.retryMaxAge)}
            required
          />
        </div>
        <p className="hint">{t('config.rejectedHint')}</p>
      </ConfigCollapsible>

      <ConfigCollapsible
        title={t('config.group.updates')}
        summary={t('config.everyDuration', { duration: describeSeconds(values[keys.configCheck]) })}
        hasPending={isPending(keys.configCheck)}
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
            value={values[keys.configCheck]}
            baseUnit="seconds"
            units={SECOND_UNITS}
            min={CONFIG_LIMITS[keys.configCheck].min}
            max={CONFIG_LIMITS[keys.configCheck].max}
            onChange={(value) => onChange(keys.configCheck, value)}
            hint={describeSeconds(values[keys.configCheck])}
            pendingNote={pendingNote(keys.configCheck)}
            required
          />
        </div>

        {/* Deliberately loud when sleep is on: without this, lowering the
            re-check interval looks like a way to make a sleeping tracker pick
            changes up sooner, and it is not. */}
        {values[keys.sleepBetween] ? (
          <div className="banner banner--info" role="status">
            {t('config.configCheckSleepNote')}
          </div>
        ) : null}
      </ConfigCollapsible>
    </>
  )
}
