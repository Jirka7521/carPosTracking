// ============================================================
// HomePage — the main landing page after sign-in.
//
// Shows a responsive grid of device cards. Each card links to
// /device/:deviceId/map so clicking it opens the map immediately.
//
// Features:
//   • Loads the user's accessible devices from /api/me/devices
//   • "+ Add Device" expands a panel that registers a tracker: its
//     MQTT device id, an optional shared name, and optional people to
//     share it with straight away
//   • On success, shows the provisioning block (topics, key
//     fingerprint, Config.h snippet) needed to flash the firmware
//   • "Show inactive devices" checkbox filters the grid
//   • Permission-aware: all permissions are shown as badges on cards
//   • Soft-delete (deactivation) is handled in the device settings tab
//   • Auto-refresh: the cards carry a battery level and a last-fix time, which
//     went stale the moment the page loaded. The same 30 s timer the device
//     tabs use keeps them honest.
// ============================================================

import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { DeviceCard } from '../components/DeviceCard'
import { ProvisioningPanel } from '../components/ProvisioningPanel'
import { CapabilityCheckboxes } from '../components/CapabilityCheckboxes'
import { RefreshToolbar } from '../components/RefreshToolbar'
import { EMPTY_FLAGS } from '../components/capabilityFlags'
import type { CapabilityFlags } from '../components/capabilityFlags'
import { useAutoRefresh } from '../hooks/useAutoRefresh'
import { createDevice, fetchMyDevices } from '../services/apiClient'
import type { DeviceAccessGrantInput, DeviceDto, DeviceProvisioningDto } from '../services/apiTypes'
import { deviceLabel } from '../utils/devices'
import { describeError } from '../utils/errors'

// The same cadence the device page and its tabs refresh at.
const AUTO_REFRESH_SEC = 30

// Mirrors the server's own rule (CreateDeviceRequestDto). It is a security
// control there, not cosmetics: the id is interpolated into MQTT topics, so the
// separator and both wildcards must be impossible to smuggle in. Here it is
// only a hint — catching the typo before the round-trip.
const DEVICE_ID_PATTERN = /^[A-Za-z0-9_-]{1,64}$/

// One row of the "share it with…" editor inside the add-device panel.
type ShareDraft = CapabilityFlags & {
  // Local key for React — emails start empty and may collide while typing.
  key: number
  email: string
}

let nextShareKey = 1

export function HomePage() {
  const { t, i18n } = useTranslation(['home', 'common', 'errors'])

  // All devices returned by the API (includes inactive if user has access to them)
  const [devices, setDevices] = useState<DeviceDto[]>([])
  const [isLoading, setIsLoading] = useState<boolean>(true)
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false)
  const [loadError, setLoadError] = useState<string>('')

  const refresh = useAutoRefresh(AUTO_REFRESH_SEC)

  // False until the grid has rendered once. After that a reload is a refresh:
  // it must not swap the grid for a spinner, and it must not swap it for an
  // error page either — a flaky poll is not a reason to throw away a list the
  // reader is looking at.
  const hasLoadedRef = useRef<boolean>(false)

  // Controls for the "Add device" panel
  const [showAddForm, setShowAddForm] = useState<boolean>(false)
  const [newDeviceId, setNewDeviceId] = useState<string>('')
  const [newDisplayName, setNewDisplayName] = useState<string>('')
  const [shares, setShares] = useState<ShareDraft[]>([])
  const [addMessage, setAddMessage] = useState<string>('')
  const [addIsError, setAddIsError] = useState<boolean>(false)
  const [isAdding, setIsAdding] = useState<boolean>(false)

  // The provisioning block of the most recently registered device. Kept until
  // the user dismisses it or opens the form again — they need time to copy it.
  const [provisioning, setProvisioning] = useState<DeviceProvisioningDto | null>(null)

  // Filter toggle
  const [showInactive, setShowInactive] = useState<boolean>(false)

  // Filtered + sorted list of devices shown in the grid
  const visibleDevices = useMemo<DeviceDto[]>(() => {
    const filtered = showInactive ? devices : devices.filter((d) => d.isActive)
    // Sort by the label the user actually sees, not by the underlying id — and
    // in the reader's own collation, which is what puts "Č" after "C" rather
    // than at the end of the alphabet.
    const collator = new Intl.Collator(i18n.resolvedLanguage ?? i18n.language)
    return [...filtered].sort((a, b) => collator.compare(deviceLabel(a), deviceLabel(b)))
  }, [devices, showInactive, i18n.resolvedLanguage, i18n.language])

  // How many inactive devices are hidden (used for the info hint)
  const hiddenCount = useMemo(
    () => (showInactive ? 0 : devices.filter((d) => !d.isActive).length),
    [devices, showInactive],
  )

  // Load the device list on mount, and again on every refresh tick.
  useEffect(() => {
    let canceled = false
    const isInitial: boolean = !hasLoadedRef.current

    const load = async (): Promise<void> => {
      if (isInitial) {
        setIsLoading(true)
        setLoadError('')
      } else {
        setIsRefreshing(true)
      }

      try {
        const data = await fetchMyDevices()
        if (!canceled) {
          hasLoadedRef.current = true
          setDevices(data)
          setLoadError('')
        }
      } catch (error) {
        if (!canceled && isInitial) {
          setLoadError(describeError(error, t('errors:loadDevicesFailed')))
        }
      } finally {
        if (!canceled) {
          setIsLoading(false)
          setIsRefreshing(false)
        }
      }
    }

    void load()
    return () => {
      canceled = true
    }
    // `t` is deliberately not a dependency — it is only reached for the
    // fallback error message, and listing it would re-fetch the device list
    // every time the reader changes language.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refresh.token])

  function resetAddForm(): void {
    setNewDeviceId('')
    setNewDisplayName('')
    setShares([])
    setAddMessage('')
    setAddIsError(false)
  }

  // Mirrors the rule the server enforces anyway (Share ⇒ Settings), so the form
  // cannot submit a combination that comes back changed. Same logic as the
  // sharing editor in DeviceSettingsTab.
  function toggleShareFlag(key: number, flag: keyof CapabilityFlags): void {
    setShares((current) => current.map((share) => {
      if (share.key !== key) {
        return share
      }

      // Settings is locked on while Share is active.
      if (flag === 'canModifySettings' && share.canShare) {
        return share
      }

      const next: ShareDraft = { ...share, [flag]: !share[flag] }

      if (flag === 'canShare' && next.canShare) {
        next.canModifySettings = true
      }

      return next
    }))
  }

  function addShareRow(): void {
    // The key is taken before the updater runs: React invokes updaters twice in
    // StrictMode, and incrementing in there would burn a number each render.
    const key = nextShareKey++
    setShares((current) => [...current, { key, email: '', ...EMPTY_FLAGS }])
  }

  // Handle the "Register device" form submission
  async function handleAddDevice(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    setAddMessage('')
    setAddIsError(false)

    const trimmedId = newDeviceId.trim()
    if (!DEVICE_ID_PATTERN.test(trimmedId)) {
      setAddIsError(true)
      setAddMessage(t('home:add.invalidId'))
      return
    }

    // Rows with an empty email are just unfinished UI, not an error — drop them.
    const additionalAccesses: DeviceAccessGrantInput[] = shares
      .filter((share) => share.email.trim().length > 0)
      .map((share) => ({
        userEmail: share.email.trim(),
        canDelete: share.canDelete,
        canShare: share.canShare,
        canModifySettings: share.canModifySettings,
      }))

    setIsAdding(true)
    try {
      const created = await createDevice({
        deviceId: trimmedId,
        displayName: newDisplayName.trim() || undefined,
        additionalAccesses: additionalAccesses.length > 0 ? additionalAccesses : undefined,
      })

      // Append to the local list without re-fetching; the server already told
      // us exactly what the new card looks like.
      setDevices((current) => [...current, created.device])
      setProvisioning(created.provisioning)
      resetAddForm()
      setShowAddForm(false)
    } catch (error) {
      setAddIsError(true)
      setAddMessage(describeError(error, t('errors:registerDeviceFailed')))
    } finally {
      setIsAdding(false)
    }
  }

  return (
    <div className="page-content">
      {/* Page title + Add Device button */}
      <div className="page-header">
        <h1>{t('home:title')}</h1>
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => {
            setShowAddForm((current) => !current)
            setAddMessage('')
            setAddIsError(false)
            // Opening the form clears the previous result so two panels never
            // compete for attention.
            setProvisioning(null)
          }}
        >
          {showAddForm ? t('home:add.close') : t('home:add.open')}
        </button>
      </div>

      {/*
       * Inline "Add Device" panel — appears when the button above is clicked.
       * Light blue tint distinguishes it from the regular cards below.
       */}
      {showAddForm ? (
        <div className="add-device-panel">
          <h3>{t('home:add.title')}</h3>
          <p>{t('home:add.intro')}</p>

          <form onSubmit={handleAddDevice}>
            <div className="add-device-row">
              <div className="form-field">
                <label htmlFor="new-device-id">{t('home:add.deviceId')}</label>
                <input
                  id="new-device-id"
                  className="form-input"
                  type="text"
                  value={newDeviceId}
                  onChange={(e) => setNewDeviceId(e.target.value)}
                  placeholder={t('home:add.deviceIdPlaceholder')}
                  pattern="[A-Za-z0-9_\-]{1,64}"
                  maxLength={64}
                  required
                  autoFocus
                />
              </div>

              <div className="form-field">
                <label htmlFor="new-device-name">
                  {t('home:add.displayName')}{' '}
                  <span className="hint">{t('common:optional')}</span>
                </label>
                <input
                  id="new-device-name"
                  className="form-input"
                  type="text"
                  value={newDisplayName}
                  onChange={(e) => setNewDisplayName(e.target.value)}
                  placeholder={t('home:add.displayNamePlaceholder')}
                  maxLength={128}
                />
              </div>
            </div>

            {/* Optional co-owners. Addresses that match no account are skipped
                silently by the server — it will not confirm who has an account
                here. */}
            <div className="share-draft-list">
              <div className="filter-row">
                <span className="hint">{t('home:add.shareHint')}</span>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={addShareRow}
                >
                  {t('home:add.addPerson')}
                </button>
              </div>

              {shares.map((share) => (
                <div key={share.key} className="share-draft">
                  <div className="add-device-row">
                    <div className="form-field">
                      <label htmlFor={`share-email-${share.key}`}>{t('home:add.emailAddress')}</label>
                      <input
                        id={`share-email-${share.key}`}
                        className="form-input"
                        type="email"
                        value={share.email}
                        onChange={(e) => setShares((current) => current.map((s) => (
                          s.key === share.key ? { ...s, email: e.target.value } : s
                        )))}
                        placeholder={t('home:add.emailPlaceholder')}
                      />
                    </div>
                    <button
                      type="button"
                      className="btn btn-danger btn-sm"
                      style={{ flexShrink: 0 }}
                      onClick={() => setShares((current) => current.filter((s) => s.key !== share.key))}
                    >
                      {t('common:actions.remove')}
                    </button>
                  </div>

                  <CapabilityCheckboxes
                    flags={share}
                    canEdit
                    onToggle={(flag) => toggleShareFlag(share.key, flag)}
                  />
                </div>
              ))}
            </div>

            <div className="add-device-row" style={{ marginTop: 12 }}>
              <button type="submit" className="btn btn-primary" disabled={isAdding}>
                {isAdding ? t('home:add.submitting') : t('home:add.submit')}
              </button>
            </div>

            {/* Error feedback. Success is not reported here — the provisioning
                panel below replaces the form and is the confirmation. */}
            {addMessage ? (
              <p
                className={`form-message ${addIsError ? 'form-message--error' : 'form-message--success'}`}
                role={addIsError ? 'alert' : 'status'}
                style={{ marginTop: 12 }}
              >
                {addMessage}
              </p>
            ) : null}
          </form>
        </div>
      ) : null}

      {/* The freshly registered device's firmware configuration. */}
      {provisioning ? (
        <div>
          <ProvisioningPanel
            provisioning={provisioning}
            title={t('home:provisioning.title', { deviceId: provisioning.deviceId })}
          />
          <div className="filter-row">
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={() => setProvisioning(null)}
            >
              {t('home:provisioning.dismiss')}
            </button>
            <span className="hint">{t('home:provisioning.readAgainHint')}</span>
          </div>
        </div>
      ) : null}

      {/* Filter row: show/hide inactive devices + inactive count hint, and the
          refresh control that keeps each card's battery and last-fix current */}
      <div className="filter-row">
        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={showInactive}
            onChange={(e) => setShowInactive(e.target.checked)}
          />
          <span>{t('home:filter.showInactive')}</span>
        </label>

        {hiddenCount > 0 ? (
          <span className="hint">{t('home:filter.hiddenCount', { count: hiddenCount })}</span>
        ) : null}

        <span style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 12 }}>
          <RefreshToolbar autoRefresh={refresh} isLoading={isRefreshing} />
        </span>
      </div>

      {/* Loading state */}
      {isLoading ? (
        <div className="loading-state">
          <div className="spinner" aria-hidden="true" />
          <span>{t('home:loading')}</span>
        </div>
      ) : loadError ? (
        /* Error state */
        <div className="error-state">
          <p>{loadError}</p>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => window.location.reload()}
          >
            {t('common:actions.retry')}
          </button>
        </div>
      ) : visibleDevices.length === 0 ? (
        /* Empty state — no devices at all, or all hidden by the filter */
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">📡</span>
          <h3>
            {devices.length === 0 ? t('home:empty.noneTitle') : t('home:empty.allInactiveTitle')}
          </h3>
          <p>
            {devices.length === 0 ? t('home:empty.noneBody') : t('home:empty.allInactiveBody')}
          </p>
          {devices.length === 0 ? (
            <button
              type="button"
              className="btn btn-primary"
              style={{ marginTop: 12 }}
              onClick={() => setShowAddForm(true)}
            >
              {t('home:empty.addFirst')}
            </button>
          ) : null}
        </div>
      ) : (
        /* Device grid */
        <div className="device-grid">
          {visibleDevices.map((device) => (
            <DeviceCard key={device.deviceId} device={device} />
          ))}
        </div>
      )}
    </div>
  )
}

// DeviceCard is defined in src/components/DeviceCard.tsx and imported above.
