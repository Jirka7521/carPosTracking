// ---------------------------------------------------------------------------
// ModeValuesFields — one operating mode's three settings as form controls.
//
// Extracted from ConfigValuesFields when motion wake gave the device a second
// copy of the settings that differ by mode: interval, sleep and fix timeout. The
// STANDBY set (parked) and the MOVING set (driving) have the same bounds, the
// same units, the same hints and the same quirks — so they are the same JSX,
// handed a ModeKeys map that says which key each control reads and writes. Two
// hand-written copies would drift the first time one of them gained a hint. The
// queue, retry and re-check settings have one value for both modes and live in
// SharedValuesFields.
//
// Like its parent it is STATELESS: it renders the values it is given and reports
// edits. `idPrefix` is what keeps the two instances apart on one page — the
// standby one keeps the prefix the form always had, the moving one gets its own,
// so a label never points at the other mode's input. `seedKey` is passed
// straight through to each DurationField's `key`; see ConfigValuesFields.
//
// Each group is a ConfigCollapsible that folds to a one-line summary. Only
// reporting and power start open: they are what people come here to change,
// and GNSS is tuned once, if ever, and reads fine as a summary until then.
// Interval and sleep share a group because neither means much without the
// other; see sleepBetweenHint.
// ---------------------------------------------------------------------------

import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { DeviceConfigValuesDto } from '../services/apiTypes'
import {
  CONFIG_FIELD_LABEL_KEYS,
  CONFIG_LIMITS,
  describeReportingSummary,
  describeSeconds,
} from '../utils/deviceConfig'
import type { ModeKeys } from '../utils/deviceConfig'
import type { TimeUnit } from '../utils/timeUnits'
import { ConfigCollapsible } from './ConfigCollapsible'
import { DurationField } from './DurationField'

// The settings the API stores as whole seconds. Hours is the coarsest unit any
// of them reaches — the highest ceiling here is 24 h — so days would only ever
// render as a fraction.
const SECOND_UNITS: readonly TimeUnit[] = ['seconds', 'minutes', 'hours']

export type ModeValuesFieldsProps = {
  values: DeviceConfigValuesDto
  // Which three keys of `values` this instance edits: STANDBY_KEYS or MOVING_KEYS.
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
    </>
  )
}
