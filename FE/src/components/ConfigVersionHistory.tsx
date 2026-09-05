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
// and most visits to the settings tab never open it. The settings tab's refresh
// timer only reaches it once it IS open — a list nobody is looking at is not
// worth a request every thirty seconds.
// ---------------------------------------------------------------------------

import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import i18n from '../i18n'
import { formatDateTime } from '../i18n/format'
import { fetchDeviceConfigHistory } from '../services/apiClient'
import type { DeviceConfigValuesDto, DeviceConfigVersionDto } from '../services/apiTypes'
import { parseApiTimestamp } from '../utils/dates'
import { CONFIG_FIELD_LABEL_KEYS, diffConfig, formatConfigValue } from '../utils/deviceConfig'
import { describeError } from '../utils/errors'

export type ConfigVersionHistoryProps = {
  deviceId: string
  // The revision currently published, so the list can mark it.
  currentVersion: number
  // The revision the device confirmed, so the list can mark that too — the two
  // differ exactly while a change is pending, and seeing both in the history is
  // what makes "it is still on the old one" concrete.
  appliedVersion: number | null
  // The settings tab's shared refresh counter. Acted on only while the list is
  // expanded — see the effect below.
  refreshToken: number
  // Hands a revision's values back to the form. The parent decides what that
  // means; this component never saves anything itself.
  onRestore: (values: DeviceConfigValuesDto) => void
}

export function ConfigVersionHistory({
  deviceId,
  currentVersion,
  appliedVersion,
  refreshToken,
  onRestore,
}: ConfigVersionHistoryProps) {
  const { t } = useTranslation(['settings', 'common', 'errors'])

  const [isOpen, setIsOpen] = useState<boolean>(false)
  const [isLoading, setIsLoading] = useState<boolean>(false)
  const [versions, setVersions] = useState<DeviceConfigVersionDto[]>([])
  const [error, setError] = useState<string>('')

  // Re-fetch on every open rather than caching: saving a change while the list
  // is collapsed would otherwise leave it showing a history missing its newest
  // entry, which is exactly the row the reader came to check.
  async function loadHistory(): Promise<void> {
    setIsLoading(true)
    setError('')
    try {
      setVersions(await fetchDeviceConfigHistory(deviceId))
    } catch (caught) {
      setError(describeError(caught, t('errors:loadHistoryFailed')))
    } finally {
      setIsLoading(false)
    }
  }

  // A refresh tick reaches the list only while it is open — a history nobody is
  // looking at is not worth a request every thirty seconds. Guarding on isOpen
  // also means expanding it does not fire two requests: handleToggle does that
  // load, and this effect only runs again when the token moves.
  //
  // Deliberately quieter than handleToggle: no spinner and no error banner. The
  // rows are already on screen, and blinking them — or replacing them with a
  // failure because one poll was unlucky — is worse than showing history that
  // is half a minute old.
  useEffect(() => {
    if (!isOpen) {
      return
    }

    let canceled = false

    void (async () => {
      try {
        const rows: DeviceConfigVersionDto[] = await fetchDeviceConfigHistory(deviceId)
        if (!canceled) {
          setVersions(rows)
        }
      } catch {
        // Keep the rows we have; the next tick tries again.
      }
    })()

    return () => {
      canceled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshToken])

  async function handleToggle(): Promise<void> {
    if (isOpen) {
      setIsOpen(false)
      return
    }

    setIsOpen(true)
    await loadHistory()
  }

  return (
    <div className="config-history">
      <button
        type="button"
        className="btn btn-quiet btn-sm"
        onClick={handleToggle}
        aria-expanded={isOpen}
      >
        {isOpen ? '▾' : '▸'} {t('settings:history.title')}
      </button>

      {isOpen ? (
        <div className="config-history-body">
          {isLoading ? <p className="hint">{t('common:states.loading')}</p> : null}

          {error ? (
            <div className="banner banner--error" role="alert">
              {error}
            </div>
          ) : null}

          {!isLoading && !error && versions.length === 0 ? (
            <p className="hint">{t('settings:history.empty')}</p>
          ) : null}

          {versions.map((version, index) => (
            <div key={version.version} className="config-history-row">
              <div className="config-history-meta">
                <span className="config-history-version">v{version.version}</span>
                {version.version === currentVersion ? (
                  <span className="config-sync-badge config-sync-badge--synced">
                    {t('settings:history.published')}
                  </span>
                ) : null}
                {version.version === appliedVersion && version.version !== currentVersion ? (
                  <span className="config-sync-badge config-sync-badge--pending">
                    {t('settings:history.onDevice')}
                  </span>
                ) : null}
                {/* A scheduled revision has no author, and without this tag it
                    would render identically to the two genuinely authorless rows
                    — the one created with the device and the one the migration
                    seeded. "Why did this tracker change at 22:00?" is exactly
                    what somebody opens this list to answer. */}
                {version.source === 'schedule' ? (
                  <span className="schedule-status-tag">
                    🗓 {version.sourceProfileName ?? t('settings:history.scheduleTag')}
                  </span>
                ) : null}
                <span className="hint">
                  {formatTimestamp(version.createdAt)}
                  {version.createdBy
                    ? ` · ${version.createdBy}`
                    : version.source === 'schedule'
                      ? ` · ${t('settings:history.automatic')}`
                      : ` · ${t('settings:history.defaults')}`}
                </span>
              </div>

              <p className="config-history-summary">
                {summariseChange(version, versions[index + 1])}
              </p>

              {version.version === currentVersion ? null : (
                <button
                  type="button"
                  className="btn btn-quiet btn-sm"
                  onClick={() => onRestore(version.values)}
                  // Deliberately does not save. It fills the form so the values
                  // can be reviewed (and adjusted) before a new revision is made.
                  title={t('settings:history.restoreHint')}
                >
                  {t('settings:history.restore')}
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
    return i18n.t('settings:history.initial')
  }

  const changed: (keyof DeviceConfigValuesDto)[] = diffConfig(previous.values, version.values)
  if (changed.length === 0) {
    return i18n.t('settings:history.noChange')
  }

  return changed
    .map((key) =>
      i18n.t('settings:history.changeEntry', {
        field: i18n.t(CONFIG_FIELD_LABEL_KEYS[key]),
        from: formatConfigValue(key, previous.values),
        to: formatConfigValue(key, version.values),
      }),
    )
    .join(' · ')
}

function formatTimestamp(value: string): string {
  const parsed: Date | null = parseApiTimestamp(value)
  return parsed === null ? i18n.t('settings:history.unknownDate') : formatDateTime(parsed)
}
