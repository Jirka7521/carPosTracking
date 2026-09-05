// ---------------------------------------------------------------------------
// ConfigPendingChanges — "what is the device running right now?", answered with
// values rather than version numbers.
//
// This is the payoff for keeping every revision in the database. While a change
// is waiting to be picked up the API can hand the dashboard *both* documents, so
// instead of "running v5, published v7" the reader gets:
//
//     Report every    60 s (1 minute)  →  300 s (5 minutes)
//
// Only the settings that actually differ are listed. A table of six rows where
// five say "unchanged" would bury the one that matters.
// ---------------------------------------------------------------------------

import { useTranslation } from 'react-i18next'
import type { DeviceConfigValuesDto, DeviceConfigVersionDto } from '../services/apiTypes'
import {
  CONFIG_FIELD_LABEL_KEYS,
  diffConfig,
  formatConfigValue,
} from '../utils/deviceConfig'

export type ConfigPendingChangesProps = {
  // What the device confirmed it is running.
  applied: DeviceConfigVersionDto
  // What is published and waiting for it.
  desired: DeviceConfigVersionDto
}

export function ConfigPendingChanges({ applied, desired }: ConfigPendingChangesProps) {
  const { t } = useTranslation(['settings'])

  const changed: (keyof DeviceConfigValuesDto)[] = diffConfig(applied.values, desired.values)

  // Reachable when the version was bumped without the values changing — the API
  // avoids that, but a restored revision or a hand-edited row could produce it.
  // Rendering an empty table would be worse than rendering nothing.
  if (changed.length === 0) {
    return null
  }

  return (
    <div className="config-compare">
      <div className="config-compare-header">
        <span className="config-compare-title">{t('pending.title')}</span>
        <span className="hint">
          v{applied.version} → v{desired.version}
        </span>
      </div>

      <table className="config-compare-table">
        <thead>
          <tr>
            <th scope="col">{t('pending.setting')}</th>
            <th scope="col">{t('pending.running', { version: applied.version })}</th>
            <th scope="col">{t('pending.willBecome', { version: desired.version })}</th>
          </tr>
        </thead>
        <tbody>
          {changed.map((key) => (
            <tr key={key}>
              <th scope="row">{t(CONFIG_FIELD_LABEL_KEYS[key])}</th>
              <td className="config-compare-from">{formatConfigValue(key, applied.values)}</td>
              <td className="config-compare-to">{formatConfigValue(key, desired.values)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
