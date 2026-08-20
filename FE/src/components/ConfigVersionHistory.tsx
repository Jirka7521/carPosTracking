// ---------------------------------------------------------------------------
// ConfigVersionHistory — every revision this device's settings have ever had.
//
// The rows come straight from the API's immutable history table, so this is also
// the audit trail: who changed what, and when. Each row summarises what changed
// against the revision *below* it, which is far easier to scan than six values
// repeated on every line.
//
// "Restore" does not rewrite history — it loads those values into the form
// above, and saving then creates a *new* revision carrying them. Old rows are
// never touched, which is what keeps the applied-version lookup honest.
//
// Loaded lazily on first expand: a device retuned for years has a long history,
// and most visits to the settings tab never open it.
// ---------------------------------------------------------------------------

import { useState } from 'react'
import { fetchDeviceConfigHistory } from '../services/apiClient'
import type { DeviceConfigValuesDto, DeviceConfigVersionDto } from '../services/apiTypes'
import { parseApiTimestamp } from '../utils/dates'
import { CONFIG_FIELD_LABELS, diffConfig, formatConfigValue } from '../utils/deviceConfig'
import { describeError } from '../utils/errors'

export type ConfigVersionHistoryProps = {
  deviceId: string
  // The revision currently published, so the list can mark it.
  currentVersion: number
  // The revision the device confirmed, so the list can mark that too — the two
  // differ exactly while a change is pending, and seeing both in the history is
  // what makes "it is still on the old one" concrete.
  appliedVersion: number | null
  // Hands a revision's values back to the form. The parent decides what that
  // means; this component never saves anything itself.
  onRestore: (values: DeviceConfigValuesDto) => void
}

export function ConfigVersionHistory({
  deviceId,
  currentVersion,
  appliedVersion,
  onRestore,
}: ConfigVersionHistoryProps) {
  const [isOpen, setIsOpen] = useState<boolean>(false)
  const [isLoading, setIsLoading] = useState<boolean>(false)
  const [versions, setVersions] = useState<DeviceConfigVersionDto[]>([])
  const [error, setError] = useState<string>('')

  async function handleToggle(): Promise<void> {
    if (isOpen) {
      setIsOpen(false)
      return
    }

    setIsOpen(true)

    // Re-fetch on every open rather than caching: saving a change while the list
    // is collapsed would otherwise leave it showing a history missing its newest
    // entry, which is exactly the row the reader came to check.
    setIsLoading(true)
    setError('')
    try {
      setVersions(await fetchDeviceConfigHistory(deviceId))
    } catch (caught) {
      setError(describeError(caught, 'Failed to load the settings history.'))
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="config-history">
      <button
        type="button"
        className="btn btn-ghost btn-sm"
        onClick={handleToggle}
        aria-expanded={isOpen}
      >
        {isOpen ? '▾' : '▸'} Version history
      </button>

      {isOpen ? (
        <div className="config-history-body">
          {isLoading ? <p className="hint">Loading…</p> : null}

          {error ? (
            <div className="banner banner--error" role="alert">
              {error}
            </div>
          ) : null}

          {!isLoading && !error && versions.length === 0 ? (
            <p className="hint">No revisions recorded.</p>
          ) : null}

          {versions.map((version, index) => (
            <div key={version.version} className="config-history-row">
              <div className="config-history-meta">
                <span className="config-history-version">v{version.version}</span>
                {version.version === currentVersion ? (
                  <span className="config-sync-badge config-sync-badge--synced">published</span>
                ) : null}
                {version.version === appliedVersion && version.version !== currentVersion ? (
                  <span className="config-sync-badge config-sync-badge--pending">on device</span>
                ) : null}
                <span className="hint">
                  {formatTimestamp(version.createdAt)}
                  {version.createdBy ? ` · ${version.createdBy}` : ' · defaults'}
                </span>
              </div>

              <p className="config-history-summary">
                {summariseChange(version, versions[index + 1])}
              </p>

              {version.version === currentVersion ? null : (
                <button
                  type="button"
                  className="btn btn-ghost btn-sm"
                  onClick={() => onRestore(version.values)}
                  // Deliberately does not save. It fills the form so the values
                  // can be reviewed (and adjusted) before a new revision is made.
                  title="Load these values into the form above"
                >
                  Restore these values
                </button>
              )}
            </div>
          ))}
        </div>
      ) : null}
    </div>
  )
}

// What this revision changed relative to the one before it. `previous` is
// undefined for the oldest row on the page — either the genuine first revision,
// or simply the end of the requested page, so the wording avoids claiming which.
function summariseChange(
  version: DeviceConfigVersionDto,
  previous: DeviceConfigVersionDto | undefined,
): string {
  if (previous === undefined) {
    return 'Initial settings.'
  }

  const changed: (keyof DeviceConfigValuesDto)[] = diffConfig(previous.values, version.values)
  if (changed.length === 0) {
    return 'No values changed.'
  }

  return changed
    .map(
      (key) =>
        `${CONFIG_FIELD_LABELS[key]}: ${formatConfigValue(key, previous.values)} → ${formatConfigValue(key, version.values)}`,
    )
    .join(' · ')
}

function formatTimestamp(value: string): string {
  const parsed: Date | null = parseApiTimestamp(value)
  return parsed === null ? 'unknown date' : parsed.toLocaleString()
}
