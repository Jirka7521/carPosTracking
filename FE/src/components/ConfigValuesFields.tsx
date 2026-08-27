// ---------------------------------------------------------------------------
// ConfigValuesFields — the seven remote settings as a set of form controls.
//
// Extracted from DeviceConfigSection when schedules arrived and gave it a second
// caller: a schedule PROFILE holds exactly the same seven values, under exactly
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
// `seedKey` is passed straight through to each DurationField's `key`. Changing
// it remounts them, which is how a field re-picks the unit that suits a value
// the server just handed us; leaving it alone is how a reader's chosen unit
// survives a refresh. See DurationField's header for why that is the parent's
// job.
// ---------------------------------------------------------------------------

import type { ReactNode } from 'react'
import type { DeviceConfigValuesDto } from '../services/apiTypes'
import {
  CONFIG_FIELD_LABELS,
  CONFIG_LIMITS,
  describeHours,
  describeSeconds,
  estimateQueueSpan,
} from '../utils/deviceConfig'
import type { TimeUnit } from '../utils/timeUnits'
import { DurationField } from './DurationField'

// The settings the API stores as whole seconds. Hours is the coarsest unit any
// of them reaches — the highest ceiling here is 24 h — so days would only ever
// render as a fraction.
const SECOND_UNITS: readonly TimeUnit[] = ['seconds', 'minutes', 'hours']

// The two retry settings, stored as whole hours. Minutes is offered because a
// retry interval is something people say in minutes; DurationField's step keeps
// such a value landing on a whole hour, which is all the wire can carry.
const HOUR_UNITS: readonly TimeUnit[] = ['minutes', 'hours', 'days']

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
  function pendingNote(key: keyof DeviceConfigValuesDto): ReactNode {
    return renderPendingNote ? renderPendingNote(key) : null
  }

  return (
    <>
      <fieldset className="config-fieldset" disabled={disabled}>
        <legend className="config-group-title">Reporting</legend>

        <div className="config-grid">
          <DurationField
            key={`interval-${seedKey}`}
            id={`${idPrefix}-interval`}
            label={CONFIG_FIELD_LABELS.intervalSeconds}
            value={values.intervalSeconds}
            baseUnit="seconds"
            units={SECOND_UNITS}
            min={CONFIG_LIMITS.intervalSeconds.min}
            max={CONFIG_LIMITS.intervalSeconds.max}
            onChange={(value) => onChange('intervalSeconds', value)}
            // Recomputed from the input being typed, not from the saved value —
            // the point is to read back what you are entering, and it stays in
            // seconds whatever unit was picked, because seconds is what actually
            // goes on the wire.
            hint={`every ${describeSeconds(values.intervalSeconds)}`}
            pendingNote={pendingNote('intervalSeconds')}
            required
          />
        </div>
      </fieldset>

      <fieldset className="config-fieldset" disabled={disabled}>
        <legend className="config-group-title">Power</legend>

        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={values.sleepBetween}
            onChange={(event) => onChange('sleepBetween', event.target.checked)}
          />
          <span>Deep-sleep between reports</span>
        </label>
        <p className="hint">
          Powers the modem down and reboots the tracker on wake. A large battery
          saving above roughly five minutes, but every cycle then pays for a cold
          GNSS fix and a fresh TLS handshake — below that it can cost more than it
          saves.
        </p>
        {pendingNote('sleepBetween')}
      </fieldset>

      <fieldset className="config-fieldset" disabled={disabled}>
        <legend className="config-group-title">GNSS</legend>

        <div className="config-grid">
          <DurationField
            key={`fix-timeout-${seedKey}`}
            id={`${idPrefix}-fix-timeout`}
            label="Give up on a lock after"
            value={values.fixTimeoutSeconds}
            baseUnit="seconds"
            units={SECOND_UNITS}
            min={CONFIG_LIMITS.fixTimeoutSeconds.min}
            max={CONFIG_LIMITS.fixTimeoutSeconds.max}
            onChange={(value) => onChange('fixTimeoutSeconds', value)}
            hint={describeSeconds(values.fixTimeoutSeconds)}
            pendingNote={pendingNote('fixTimeoutSeconds')}
            required
          />
        </div>
        <p className="hint">
          A cold start under a poor sky view can legitimately take minutes. Too
          short and a parked car never reports; too long and a sleeping tracker
          burns the battery the sleep was meant to save.
        </p>
      </fieldset>

      <fieldset className="config-fieldset" disabled={disabled}>
        <legend className="config-group-title">Undelivered fixes</legend>

        <div className="config-grid">
          <div className="form-field">
            <label className="form-label" htmlFor={`${idPrefix}-queue-max`}>
              Keep at most (fixes)
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
            <span className="hint">
              {estimateQueueSpan(values.queueMaxFixes, values.intervalSeconds)}
            </span>
            {pendingNote('queueMaxFixes')}
          </div>
        </div>
        <p className="hint">
          While the broker is unreachable each fix is encrypted and queued on the
          SD card. Past this many, the oldest are dropped so the card can never
          fill up. It is a count rather than a duration because a queued entry is
          bare ciphertext with no timestamp to age it by.
        </p>
      </fieldset>

      <fieldset className="config-fieldset" disabled={disabled}>
        <legend className="config-group-title">Rejected fixes</legend>

        <div className="config-grid">
          <DurationField
            key={`retry-interval-${seedKey}`}
            id={`${idPrefix}-retry-interval`}
            label="Retry every"
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
            label="Give up after (0 = never)"
            value={values.retryMaxAgeHours}
            baseUnit="hours"
            units={HOUR_UNITS}
            min={CONFIG_LIMITS.retryMaxAgeHours.min}
            max={CONFIG_LIMITS.retryMaxAgeHours.max}
            onChange={(value) => onChange('retryMaxAgeHours', value)}
            hint={
              values.retryMaxAgeHours === 0
                ? 'keep retrying forever'
                : describeHours(values.retryMaxAgeHours)
            }
            pendingNote={pendingNote('retryMaxAgeHours')}
            required
          />
        </div>
        <p className="hint">
          Fixes this server refused are kept apart from the live queue and
          re-offered slowly, because several reject reasons are server-side and
          clear on their own. Giving up is the only path that deliberately
          discards data.
        </p>
      </fieldset>

      <fieldset className="config-fieldset" disabled={disabled}>
        <legend className="config-group-title">Configuration updates</legend>

        <p className="hint">
          Settings normally reach this tracker <strong>within a second</strong>: it
          holds an open subscription, so the broker pushes a change the moment it
          is saved, and a device that reconnects is handed the current settings
          automatically. The value below is only the backstop — how often it asks
          the broker to re-send them, in case a connection was quietly dead.
        </p>

        <div className="config-grid">
          <DurationField
            key={`config-check-${seedKey}`}
            id={`${idPrefix}-config-check`}
            label="Re-check every"
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
        {values.sleepBetween ? (
          <div className="banner banner--info" role="status">
            Deep sleep is on, so this setting does nothing. A sleeping tracker has
            no connection to re-check — it reads its configuration afresh on every
            wake, which already makes this redundant.
          </div>
        ) : null}
      </fieldset>
    </>
  )
}
