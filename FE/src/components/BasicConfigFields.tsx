// ---------------------------------------------------------------------------
// BasicConfigFields — the remote settings as Normal mode shows them.
//
// ConfigValuesFields renders this instead of its full composition when the
// dashboard is in Normal mode (see uiMode/context.ts). It keeps the questions a
// driver actually asks:
//
//   - wake on the accelerometer, or the same settings all the time
//     (ConfigModeSwitch, unchanged);
//   - how often to report — one interval, or with motion wake on, one while
//     parked and one while moving, since the tracker really uses both;
//   - with motion wake on, how long to wait for movement after a wake.
//
// Everything else — sleep, the GNSS timeout, the wake threshold and speed, the
// queue and retry settings — is simply not rendered. It is NOT reset: the owner
// still holds all fifteen values and saves them as they are, so whatever was
// tuned in Expert mode survives an edit made here.
//
// No folding groups: three fields need no summary line to stay readable, and a
// form that opens fully is one fewer click for the thing it exists to do.
// STATELESS, like its parent; same props, so the parent can hand them straight
// through.
// ---------------------------------------------------------------------------

import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { DeviceConfigValuesDto } from '../services/apiTypes'
import {
  CONFIG_FIELD_LABEL_KEYS,
  CONFIG_LIMITS,
  describeSeconds,
} from '../utils/deviceConfig'
import type { TimeUnit } from '../utils/timeUnits'
import type { ConfigValuesFieldsProps } from './ConfigValuesFields'
import { ConfigModeSwitch } from './ConfigModeSwitch'
import { DurationField } from './DurationField'

// The same units the full form offers for these settings; see ModeValuesFields.
const SECOND_UNITS: readonly TimeUnit[] = ['seconds', 'minutes', 'hours']

export function BasicConfigFields({
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

  return (
    <>
      <ConfigModeSwitch
        motionEnabled={values.motionEnabled}
        onChange={(motionEnabled) => onChange('motionEnabled', motionEnabled)}
        disabled={disabled}
        pendingNote={pendingNote('motionEnabled')}
        idPrefix={idPrefix}
      />

      <fieldset className="config-basic-fields" disabled={disabled}>
        <div className="config-grid">
          <DurationField
            key={`interval-${seedKey}`}
            id={`${idPrefix}-interval`}
            // "Report every" alone would be ambiguous beside the moving one.
            label={values.motionEnabled
              ? t('config.basic.intervalParked')
              : t(CONFIG_FIELD_LABEL_KEYS.intervalSeconds)}
            value={values.intervalSeconds}
            baseUnit="seconds"
            units={SECOND_UNITS}
            min={CONFIG_LIMITS.intervalSeconds.min}
            max={CONFIG_LIMITS.intervalSeconds.max}
            onChange={(value) => onChange('intervalSeconds', value)}
            hint={t('config.everyDuration', { duration: describeSeconds(values.intervalSeconds) })}
            pendingNote={pendingNote('intervalSeconds')}
            required
          />

          {values.motionEnabled ? (
            <>
              <DurationField
                key={`moving-interval-${seedKey}`}
                id={`${idPrefix}-moving-interval`}
                label={t(CONFIG_FIELD_LABEL_KEYS.movingIntervalSeconds)}
                value={values.movingIntervalSeconds}
                baseUnit="seconds"
                units={SECOND_UNITS}
                min={CONFIG_LIMITS.movingIntervalSeconds.min}
                max={CONFIG_LIMITS.movingIntervalSeconds.max}
                onChange={(value) => onChange('movingIntervalSeconds', value)}
                hint={t('config.everyDuration', { duration: describeSeconds(values.movingIntervalSeconds) })}
                pendingNote={pendingNote('movingIntervalSeconds')}
                required
              />

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
            </>
          ) : null}
        </div>

        {values.motionEnabled ? (
          <p className="hint">{t('config.basic.wakeWaitHint')}</p>
        ) : null}
      </fieldset>
    </>
  )
}
