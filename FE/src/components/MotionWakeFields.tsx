// ---------------------------------------------------------------------------
// MotionWakeFields — the switch and the five knobs that decide when a tracker
// counts as moving.
//
// With the checkbox on, an ADXL345 accelerometer wakes the sleeping tracker as
// the car starts to move. A wake proves only that something shook the sensor, so
// the tracker then looks for a GNSS fix: faster than the speed below, it switches
// to the MOVING settings; otherwise it goes back to standby. It falls back the
// same way once no fix has been above that speed for the stop wait. Everything
// that governs those decisions is here; the two sets of settings they switch
// between are ModeValuesFields.
//
// The detail fields appear only while the checkbox is on. Off, the device never
// reads them, and a form full of inert numbers would only invite someone to tune
// something that does nothing.
//
// STATELESS, like ConfigValuesFields: it renders what it is given and reports
// edits. `seedKey` is passed through to each DurationField's `key`.
// ---------------------------------------------------------------------------

import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { DeviceConfigValuesDto } from '../services/apiTypes'
import {
  CONFIG_FIELD_LABEL_KEYS,
  CONFIG_LIMITS,
  describeMotionThreshold,
  describeSeconds,
} from '../utils/deviceConfig'
import type { TimeUnit } from '../utils/timeUnits'
import { DurationField } from './DurationField'

// Both waits are stored as whole seconds and top out at two hours, so days would
// only ever render as a fraction.
const SECOND_UNITS: readonly TimeUnit[] = ['seconds', 'minutes', 'hours']

export type MotionWakeFieldsProps = {
  values: DeviceConfigValuesDto
  onChange: <TKey extends keyof DeviceConfigValuesDto>(
    key: TKey,
    value: DeviceConfigValuesDto[TKey],
  ) => void
  // Bumped by the owner when server values are seeded; see ConfigValuesFields.
  seedKey: number
  // Applied to the fieldset rather than to each input.
  disabled: boolean
  // The "⚠ Device still on 60 s" note under a field; null where there is none.
  pendingNote: (key: keyof DeviceConfigValuesDto) => ReactNode
  // Prefix for the input ids; see ConfigValuesFields.
  idPrefix: string
}

export function MotionWakeFields({
  values,
  onChange,
  seedKey,
  disabled,
  pendingNote,
  idPrefix,
}: MotionWakeFieldsProps) {
  const { t } = useTranslation(['settings'])

  return (
    <fieldset className="config-fieldset" disabled={disabled}>
      <legend className="config-group-title">{t('config.group.motion')}</legend>

      <label className="checkbox-field">
        <input
          type="checkbox"
          checked={values.motionEnabled}
          onChange={(event) => onChange('motionEnabled', event.target.checked)}
        />
        <span>{t(CONFIG_FIELD_LABEL_KEYS.motionEnabled)}</span>
      </label>
      <p className="hint">{t('config.motionEnabledHint')}</p>
      {pendingNote('motionEnabled')}

      {values.motionEnabled ? (
        <>
          <div className="config-grid">
            <div className="form-field">
              <label className="form-label" htmlFor={`${idPrefix}-motion-threshold`}>
                {t('config.motionThresholdLabel')}
              </label>
              <input
                id={`${idPrefix}-motion-threshold`}
                className="form-input"
                style={{ width: 'auto' }}
                type="number"
                min={CONFIG_LIMITS.motionThresholdMg.min}
                max={CONFIG_LIMITS.motionThresholdMg.max}
                step={1}
                value={values.motionThresholdMg}
                onChange={(event) => onChange('motionThresholdMg', Number(event.target.value))}
                required
              />
              {/* What the typed number becomes on the sensor, which works in
                  62.5 mg steps — see describeMotionThreshold. */}
              <span className="hint">{describeMotionThreshold(values.motionThresholdMg)}</span>
              {pendingNote('motionThresholdMg')}
            </div>

            <div className="form-field">
              <label className="form-label" htmlFor={`${idPrefix}-motion-speed`}>
                {t('config.motionSpeedLabel')}
              </label>
              <input
                id={`${idPrefix}-motion-speed`}
                className="form-input"
                style={{ width: 'auto' }}
                type="number"
                min={CONFIG_LIMITS.motionSpeedKmph.min}
                max={CONFIG_LIMITS.motionSpeedKmph.max}
                step={1}
                value={values.motionSpeedKmph}
                onChange={(event) => onChange('motionSpeedKmph', Number(event.target.value))}
                required
              />
              {pendingNote('motionSpeedKmph')}
            </div>
          </div>
          <p className="hint">{t('config.motionThresholdHint')}</p>
          <p className="hint">{t('config.motionSpeedHint')}</p>

          <div className="config-grid">
            <DurationField
              key={`motion-wake-wait-${seedKey}`}
              id={`${idPrefix}-motion-wake-wait`}
              label={t(CONFIG_FIELD_LABEL_KEYS.motionWakeWaitSeconds)}
              value={values.motionWakeWaitSeconds}
              baseUnit="seconds"
              units={SECOND_UNITS}
              min={CONFIG_LIMITS.motionWakeWaitSeconds.min}
              max={CONFIG_LIMITS.motionWakeWaitSeconds.max}
              onChange={(value) => onChange('motionWakeWaitSeconds', value)}
              hint={describeSeconds(values.motionWakeWaitSeconds)}
              pendingNote={pendingNote('motionWakeWaitSeconds')}
              required
            />

            <DurationField
              key={`motion-stop-wait-${seedKey}`}
              id={`${idPrefix}-motion-stop-wait`}
              label={t(CONFIG_FIELD_LABEL_KEYS.motionStopWaitSeconds)}
              value={values.motionStopWaitSeconds}
              baseUnit="seconds"
              units={SECOND_UNITS}
              min={CONFIG_LIMITS.motionStopWaitSeconds.min}
              max={CONFIG_LIMITS.motionStopWaitSeconds.max}
              onChange={(value) => onChange('motionStopWaitSeconds', value)}
              hint={describeSeconds(values.motionStopWaitSeconds)}
              pendingNote={pendingNote('motionStopWaitSeconds')}
              required
            />
          </div>
          <p className="hint">{t('config.motionWaitHint')}</p>
        </>
      ) : null}
    </fieldset>
  )
}
