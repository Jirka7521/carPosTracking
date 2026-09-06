// ============================================================
// DeviceSettingsTab — the "Settings" tab inside DevicePage.
//
// Six permission-gated sections are shown conditionally:
//
//   1. Device Information  (always visible — every user with canRead sees this)
//      Device ID, registration date, status, last fix, the caller's own alias
//      for the device, and their permission badges.
//
//   2. Settings Schedule  (visible only if canModifySettings)
//      Named profiles and the weekly windows that select them, plus a week-long
//      timeline of what is in force when. Owned by ScheduleSection.
//
//      It sits ABOVE Reporting & Power on purpose: on a scheduled device the
//      schedule is what decides the settings, and the panel below it edits them
//      by hand only until the next switch. The authority reads first.
//
//      The schedule STATE is fetched here rather than inside that component,
//      because section 3 needs it too: a manual save on a scheduled device is
//      temporary, so the settings panel has to know whether to warn, and has to
//      show the override banner afterwards. One fetch, two consumers, one timer.
//
//   3. Reporting & Power  (visible only if canModifySettings)
//      The remote settings the tracker actually runs on — reporting interval,
//      deep sleep, GNSS lock timeout and what happens to undelivered fixes.
//      Owned by DeviceConfigSection, which also shows whether the device has
//      picked the latest change up yet. Unlike section 4 this loads with the
//      page: it is the reason most people open this tab.
//
//   4. Firmware configuration (visible only if canModifySettings)
//      Re-reads the provisioning payload — topics, key fingerprints and a
//      complete, ready-to-flash Config.h — so a tracker can be re-flashed
//      without rotating its receiver key pair, and a read-only table of every
//      parameter the firmware is built with. Loaded on demand rather than with
//      the page: it is a rarely needed panel and there is no reason to fetch it
//      for every visit.
//
//   5. Access Management   (visible only if canShare OR canModifySettings)
//      Shows who currently has access.  If the caller has canShare they can
//      also invite new users and edit / revoke existing grants.
//      If they only have canModifySettings they can view the list but not edit.
//
//   6. Danger Zone         (visible only if canDelete)
//      A confirmation-required button to soft-delete (deactivate) the device.
//      After deletion the page shows a success banner and the device status
//      is updated in the parent via reloadDevice().
//
// Permission model (backend-enforced; FE hides controls as a courtesy):
//   canRead            — always true for anyone with an active grant
//   canDelete          — may deactivate the device
//   canShare           — may view AND edit the access roster; also implies canModifySettings
//   canModifySettings  — may view the access roster but cannot modify it
//
// REFRESHING. Sections 1–3 are live: they run off DevicePage's shared timer
// (30 s, with a "Refresh" button), so the device's battery, its last fix, which
// profile the schedule currently has in force and — the one that actually
// mattered — whether the tracker has PICKED UP the last settings change all
// update in place. Before this the sync badge could read "Pending" for hours
// after the device had applied the change, and the only way to find out was to
// reload the page.
//
// Sections 4 and 5 are deliberately NOT on the timer. The firmware
// configuration is an on-demand panel holding a key block nobody wants
// re-rendered under them, and the access roster changes when a person changes
// it, not on its own.
// ============================================================

import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useNavigate, useOutletContext } from 'react-router-dom'
import { Trans, useTranslation } from 'react-i18next'
import { formatDate } from '../i18n/format'
import { BatteryBadge } from '../components/BatteryBadge'
import { DeviceConfigSection } from '../components/DeviceConfigSection'
import { FirmwareParameterTable } from '../components/FirmwareParameterTable'
import { PermissionBadges } from '../components/PermissionBadges'
import { ProvisioningPanel } from '../components/ProvisioningPanel'
import { RefreshToolbar } from '../components/RefreshToolbar'
import { ScheduleSection } from '../components/ScheduleSection'
import type { ScheduleLoadStatus } from '../components/ScheduleSection'
import { SharedUserCard } from '../components/SharedUserCard'
import { CapabilityCheckboxes } from '../components/CapabilityCheckboxes'
import { EMPTY_FLAGS } from '../components/capabilityFlags'
import type { SharedUserData } from '../components/SharedUserCard'
import type { CapabilityFlags } from '../components/capabilityFlags'
import type { DevicePageContext } from './DevicePage'
import {
  createAccessGrant,
  deleteDevice,
  fetchAccessGrantsForDevice,
  fetchDeviceProvisioning,
  fetchDeviceSchedule,
  fetchUserById,
  fetchUsers,
  revokeAccessGrant,
  updateAccessGrant,
  updateDeviceAlias,
} from '../services/apiClient'
import type {
  AccessDto,
  DeviceProvisioningDto,
  DeviceScheduleStateDto,
  UserProfileDto,
} from '../services/apiTypes'
import { formatRelativeTime } from '../utils/dates'
import { describeError } from '../utils/errors'
import { useAuth } from '../auth/useAuth'

// ============================================================
// Main component
// ============================================================

export function DeviceSettingsTab() {
  const {
    device,
    reloadDevice,
    updateDevice,
    autoRefresh,
    isRefreshingDevice,
  } = useOutletContext<DevicePageContext>()
  const { currentUser } = useAuth()
  const navigate        = useNavigate()
  const { t }           = useTranslation(['settings', 'common', 'errors'])
  const perms           = device.permissions

  // ---- Convenience booleans derived from permissions ----
  // These gate which UI sections are displayed.
  const canViewAccess = perms.canShare || perms.canModifySettings
  const canEditAccess = perms.canShare

  // ---- Device alias state ----
  // Initialise from the device data so the input is pre-filled on load.
  const [aliasInput,      setAliasInput]      = useState<string>(device.customName ?? '')
  const [isSavingAlias,   setIsSavingAlias]   = useState<boolean>(false)
  const [aliasMessage,    setAliasMessage]    = useState<string>('')
  const [aliasIsError,    setAliasIsError]    = useState<boolean>(false)

  // ---- Shared access state ----
  const [sharedUsers, setSharedUsers] = useState<SharedUserData[]>([])
  const [isLoadingAccess, setIsLoadingAccess] = useState<boolean>(false)
  const [savingUserId, setSavingUserId]       = useState<number | null>(null)
  const [accessError, setAccessError]         = useState<string>('')
  const [accessSuccess, setAccessSuccess]     = useState<string>('')

  // ---- Invite form state ----
  const [inviteEmail, setInviteEmail]               = useState<string>('')
  const [inviteFlags, setInviteFlags]               = useState<CapabilityFlags>(EMPTY_FLAGS)
  const [isInviteSubmitting, setIsInviteSubmitting] = useState<boolean>(false)

  // ---- Firmware configuration state ----
  // Loaded on demand: it is rarely needed, and fetching it on every visit would
  // re-render an RSA key block nobody asked for.
  const [provisioning, setProvisioning]           = useState<DeviceProvisioningDto | null>(null)
  const [isLoadingProvisioning, setIsLoadingProvisioning] = useState<boolean>(false)
  const [provisioningError, setProvisioningError] = useState<string>('')

  // ---- Settings schedule state ----
  // Held here rather than in ScheduleSection because DeviceConfigSection needs
  // it too — see the header note.
  //
  // The status is tracked separately from the data, and that separation is the
  // whole point: `schedule === null` used to mean both "still loading" and
  // "could not be read", and because the panel was only rendered once it went
  // non-null, ANY failure removed the entire section silently — no spinner, no
  // error, no trace. That is indistinguishable from the feature not existing,
  // and it is exactly how it looked to the first person who went looking for it.
  const [schedule, setSchedule] = useState<DeviceScheduleStateDto | null>(null)
  const [scheduleStatus, setScheduleStatus] = useState<ScheduleLoadStatus>('loading')
  const [scheduleError, setScheduleError] = useState<string>('')

  // The device the schedule has actually loaded for. It is what separates a
  // first load — which may show a spinner and must report a failure — from a
  // refresh tick, which may do neither.
  const loadedScheduleDeviceIdRef = useRef<string | null>(null)

  // ---- Delete state ----
  const [deleteConfirmVisible, setDeleteConfirmVisible] = useState<boolean>(false)
  const [isDeleting, setIsDeleting]                     = useState<boolean>(false)
  const [deleteError, setDeleteError]                   = useState<string>('')

  // ---- Save or clear the device alias ----
  // Accepts an explicit value so the Clear button can pass '' without waiting
  // for the React state update from setAliasInput to flush first.
  async function handleAliasSave(valueOverride?: string): Promise<void> {
    setAliasMessage('')
    const trimmed = (valueOverride !== undefined ? valueOverride : aliasInput).trim()

    if (trimmed.length > 100) {
      setAliasMessage(t('settings:alias.tooLong', { count: 100 }))
      setAliasIsError(true)
      return
    }

    setIsSavingAlias(true)
    try {
      // Sending an empty string removes the alias (reverts to UUID).
      await updateDeviceAlias(device.deviceId, trimmed)
      setAliasInput(trimmed)
      // Push the new name up to DevicePage so the breadcrumb and heading
      // update immediately without a full reload.
      updateDevice({ customName: trimmed || null })
      setAliasMessage(trimmed ? t('settings:alias.saved') : t('settings:alias.cleared'))
      setAliasIsError(false)
    } catch (error) {
      setAliasMessage(describeError(error, t('errors:saveAliasFailed')))
      setAliasIsError(true)
    } finally {
      setIsSavingAlias(false)
    }
  }

  // ---- Load the access roster whenever the section is visible ----
  useEffect(() => {
    if (!canViewAccess) {
      return
    }

    void loadSharedUsers()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [device.deviceId, canViewAccess])

  // ---- Load the schedule, and keep it on the tab's timer ----
  //
  // On the timer because the answer moves on its own: the active profile changes
  // when a window opens, the countdown to the next switch runs down, and an
  // override expires. A schedule that only refreshed on a page reload would show
  // "switches in 2 min" indefinitely.
  //
  // FAILURES ARE SPLIT, the way DeviceConfigSection and ConfigVersionHistory
  // already split them:
  //
  //   first load  — say so. There is nothing on screen yet, so a silent failure
  //                 leaves a person staring at a panel that never arrives.
  //   refresh tick — stay quiet and keep the last good state. Replacing a working
  //                 panel with an error because one poll was unlucky is worse
  //                 than showing a schedule half a minute out of date.
  //
  // The tab having only half of that rule is what made this feature invisible.
  useEffect(() => {
    if (!perms.canModifySettings) {
      return
    }

    let canceled = false
    // A first load for this device, as opposed to a tick for one already shown.
    const isInitial: boolean = loadedScheduleDeviceIdRef.current !== device.deviceId

    void (async () => {
      try {
        const loaded: DeviceScheduleStateDto = await fetchDeviceSchedule(device.deviceId)
        if (canceled) {
          return
        }
        loadedScheduleDeviceIdRef.current = device.deviceId
        setSchedule(loaded)
        setScheduleStatus('ready')
        setScheduleError('')
      } catch (error) {
        if (canceled || !isInitial) {
          // Keep whatever is on screen; the next tick tries again.
          return
        }
        setScheduleStatus('error')
        setScheduleError(describeError(error, t('errors:loadScheduleFailed')))
      }
    })()

    return () => {
      canceled = true
    }
    // `t` is deliberately not a dependency — it is only reached for the
    // fallback error message, and listing it would re-fetch on a language
    // change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [device.deviceId, perms.canModifySettings, autoRefresh.token])

  // Accepts a schedule state that an endpoint just returned. Every schedule
  // mutation answers with the whole recomputed state, so this is also the point
  // at which a panel that failed its first load recovers: a successful save
  // proves the endpoint works, and leaving the error banner up would be absurd.
  function handleScheduleChanged(next: DeviceScheduleStateDto): void {
    loadedScheduleDeviceIdRef.current = device.deviceId
    setSchedule(next)
    setScheduleStatus('ready')
    setScheduleError('')
  }

  // Re-reads the schedule on demand — the "Try again" button, and the follow-up
  // after a manual save creates an override, whose response carries the settings
  // but not the new override.
  async function reloadSchedule(): Promise<void> {
    // Only shows the spinner when there is nothing on screen to keep. A retry
    // behind a working panel stays invisible until it either succeeds or fails.
    if (schedule === null) {
      setScheduleStatus('loading')
    }
    try {
      setSchedule(await fetchDeviceSchedule(device.deviceId))
      loadedScheduleDeviceIdRef.current = device.deviceId
      setScheduleStatus('ready')
      setScheduleError('')
    } catch (error) {
      // Only escalates to the error state when there is nothing to fall back on.
      // A retry that fails while a good schedule is still on screen leaves it
      // there — same reasoning as the tick above.
      if (schedule === null) {
        setScheduleStatus('error')
        setScheduleError(describeError(error, t('errors:loadScheduleFailed')))
      }
    }
  }

  // An empty roster when access cannot be viewed is a consequence of
  // canViewAccess, not state of its own — deriving it keeps the last-loaded
  // roster from showing for one render after the permission is revoked.
  const visibleSharedUsers = canViewAccess ? sharedUsers : []

  // Fetches the current access grants and resolves user profile for each
  async function loadSharedUsers(): Promise<void> {
    setIsLoadingAccess(true)
    setAccessError('')
    try {
      const grants: AccessDto[] = await fetchAccessGrantsForDevice(device.deviceId)

      // Exclude the caller's own row — they already know their permissions
      const otherGrants = grants.filter(
        (g) => currentUser === null || g.userId !== currentUser.id,
      )

      // Fetch user profiles in parallel to minimise round-trips
      const profiles: UserProfileDto[] = await Promise.all(
        otherGrants.map((g) => fetchUserById(g.userId)),
      )
      const profileById = new Map(profiles.map((p) => [p.id, p]))

      const rows: SharedUserData[] = otherGrants
        .map((grant): SharedUserData | null => {
          const profile = profileById.get(grant.userId)
          if (!profile) {
            return null
          }
          return {
            accessId:          grant.id,
            userId:            grant.userId,
            userEmail:         profile.email,
            fullName:          `${profile.firstName} ${profile.lastName}`.trim(),
            canDelete:         grant.canDelete,
            canShare:          grant.canShare,
            canModifySettings: grant.canModifySettings,
          }
        })
        .filter((row): row is SharedUserData => row !== null)
        .sort((a, b) => a.userEmail.localeCompare(b.userEmail))

      setSharedUsers(rows)
    } catch (error) {
      setAccessError(describeError(error, t('errors:loadAccessFailed')))
    } finally {
      setIsLoadingAccess(false)
    }
  }

  // ---- Flag toggle helpers ----
  // The "Share implies Settings" rule: ticking canShare auto-ticks
  // canModifySettings and locks it until Share is unchecked.

  function toggleFlag(
    userId: number,
    flag: keyof CapabilityFlags,
  ): void {
    setSharedUsers((rows) =>
      rows.map((row) => {
        if (row.userId !== userId) {
          return row
        }
        // Settings is locked while Share is active
        if (flag === 'canModifySettings' && row.canShare) {
          return row
        }
        const next = { ...row, [flag]: !row[flag] }
        // Ticking Share also enables Settings
        if (flag === 'canShare' && next.canShare) {
          next.canModifySettings = true
        }
        return next
      }),
    )
  }

  function toggleInviteFlag(flag: keyof CapabilityFlags): void {
    setInviteFlags((cur) => {
      if (flag === 'canModifySettings' && cur.canShare) {
        return cur
      }
      const next = { ...cur, [flag]: !cur[flag] }
      if (flag === 'canShare' && next.canShare) {
        next.canModifySettings = true
      }
      return next
    })
  }

  // ---- Save updated flags for an existing user ----
  async function saveUserRow(row: SharedUserData): Promise<void> {
    if (!canEditAccess) {
      return
    }
    setSavingUserId(row.userId)
    setAccessError('')
    setAccessSuccess('')
    try {
      await updateAccessGrant(row.accessId, {
        canDelete:         row.canDelete,
        canShare:          row.canShare,
        canModifySettings: row.canModifySettings,
      })
      await loadSharedUsers()
      setAccessSuccess(`Permissions updated for ${row.userEmail}.`)
    } catch (error) {
      setAccessError(describeError(error, t('errors:updatePermissionsFailed')))
    } finally {
      setSavingUserId(null)
    }
  }

  // ---- Revoke access for an existing user ----
  async function removeUserRow(row: SharedUserData): Promise<void> {
    if (!canEditAccess) {
      return
    }
    const confirmed = window.confirm(t('settings:sharing.confirmRemove', { email: row.userEmail }))
    if (!confirmed) {
      return
    }
    setSavingUserId(row.userId)
    setAccessError('')
    setAccessSuccess('')
    try {
      await revokeAccessGrant(row.accessId)
      await loadSharedUsers()
      setAccessSuccess(t('settings:sharing.removed', { email: row.userEmail }))
    } catch (error) {
      setAccessError(describeError(error, t('errors:removeAccessFailed')))
    } finally {
      setSavingUserId(null)
    }
  }

  // ---- Invite a new user ----
  async function handleInviteSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    setAccessError('')
    setAccessSuccess('')

    const email = inviteEmail.trim().toLowerCase()
    if (!email) {
      setAccessError(t('settings:sharing.emailRequired'))
      return
    }

    setIsInviteSubmitting(true)
    try {
      // Look up the user by email (exact match)
      const matches = await fetchUsers(email, true)
      if (matches.length === 0) {
        setAccessError(t('settings:sharing.noAccount', { email }))
        return
      }
      const targetUser = matches[0]

      // Prevent sharing with yourself
      if (currentUser !== null && targetUser.id === currentUser.id) {
        setAccessError(t('settings:sharing.cannotShareWithSelf'))
        return
      }

      await createAccessGrant({
        userId:            targetUser.id,
        deviceId:          device.deviceId,
        canDelete:         inviteFlags.canDelete,
        canShare:          inviteFlags.canShare,
        canModifySettings: inviteFlags.canModifySettings,
      })

      await loadSharedUsers()
      setAccessSuccess(`Shared access with ${targetUser.email}.`)
      setInviteEmail('')
      setInviteFlags(EMPTY_FLAGS)
    } catch (error) {
      setAccessError(describeError(error, t('errors:shareAccessFailed')))
    } finally {
      setIsInviteSubmitting(false)
    }
  }

  // ---- Delete device ----
  async function handleDeleteDevice(): Promise<void> {
    if (!perms.canDelete) {
      return
    }
    setIsDeleting(true)
    setDeleteError('')
    try {
      await deleteDevice(device.deviceId)
      // Reload the device in the parent shell — this updates the status badge
      await reloadDevice()
      // Return the user to the home page; the deleted device will show as inactive
      navigate('/home')
    } catch (error) {
      setDeleteError(describeError(error, t('errors:deleteDeviceFailed')))
    } finally {
      setIsDeleting(false)
      setDeleteConfirmVisible(false)
    }
  }

  // ---- Load the firmware configuration block on demand ----
  async function loadProvisioning(): Promise<void> {
    setIsLoadingProvisioning(true)
    setProvisioningError('')
    try {
      setProvisioning(await fetchDeviceProvisioning(device.deviceId))
    } catch (error) {
      setProvisioningError(describeError(error, t('errors:loadProvisioningFailed')))
    } finally {
      setIsLoadingProvisioning(false)
    }
  }

  // ---- Derived: registration + deactivation dates ----
  const registeredDate = formatDate(new Date(device.createdAt))
  const deactivatedDate = device.deactivatedAt
    ? formatDate(new Date(device.deactivatedAt))
    : null

  // ---- Render ----
  return (
    <div>

      {/* The tab's refresh control. One toggle and one countdown for the whole
          tab, driving DevicePage's timer — which is already reloading the
          device — plus the config state below. */}
      <div className="filter-row" style={{ justifyContent: 'flex-end' }}>
        <RefreshToolbar autoRefresh={autoRefresh} isLoading={isRefreshingDevice} />
      </div>

      {/* ================================================================
       * Section 1: Device Information — always visible
       * ================================================================ */}
      <div className="settings-section">
        <div className="settings-section-header">
          <span className="settings-section-icon" aria-hidden="true">📡</span>
          <h3>{t('settings:info.title')}</h3>
        </div>

        <div className="settings-section-body">
          {/* Key-value grid: UUID, dates, status */}
          <div className="info-grid">
            <div className="info-item">
              {/* The tracker's MQTT identity: the same string it publishes
                  under, authenticates with, and carries inside every encrypted
                  payload. Permanent — it cannot be changed or reused. */}
              <span className="info-label">{t('settings:info.deviceId')}</span>
              <span className="info-value mono">{device.deviceId}</span>
            </div>

            {device.displayName ? (
              <div className="info-item">
                <span className="info-label">{t('settings:info.displayName')}</span>
                <span className="info-value">{device.displayName}</span>
              </div>
            ) : null}

            <div className="info-item">
              <span className="info-label">{t('settings:info.status')}</span>
              <span className="info-value">
                <span
                  className={`status-badge ${device.isActive ? 'status-badge--active' : 'status-badge--inactive'}`}
                >
                  {device.isActive ? t('common:deviceState.active') : t('common:deviceState.inactive')}
                </span>
              </span>
            </div>

            <div className="info-item">
              <span className="info-label">{t('settings:info.registered')}</span>
              <span className="info-value">{registeredDate}</span>
            </div>

            <div className="info-item">
              {/* The only liveness signal there is — the firmware sends no
                  heartbeat, so this moves only when a real fix arrives. */}
              <span className="info-label">{t('settings:info.lastFix')}</span>
              <span className="info-value">{formatRelativeTime(device.lastSeenAt)}</span>
            </div>

            {/* Battery as of that same fix. BatteryBadge renders nothing when
                the device has reported none, so the row would be an empty
                label — hence the guard rather than an "unknown" placeholder. */}
            {device.lastBatteryPct !== null && device.lastBatteryPct !== undefined ? (
              <div className="info-item">
                <span className="info-label">{t('settings:info.battery')}</span>
                <span className="info-value">
                  <BatteryBadge value={device.lastBatteryPct} />
                </span>
              </div>
            ) : null}

            {deactivatedDate ? (
              <div className="info-item">
                <span className="info-label">{t('settings:info.deactivated')}</span>
                <span className="info-value">{deactivatedDate}</span>
              </div>
            ) : null}
          </div>

          {/* ---- Device display name (per-user alias) ---- */}
          {/* Every user with CanRead can set their own name for this device.
              The hardware UUID is always visible as the canonical identifier. */}
          <div style={{ marginTop: 20 }}>
            <p className="info-label" style={{ marginBottom: 8 }}>{t('settings:alias.title')}</p>
            <p className="hint" style={{ marginBottom: 10 }}>{t('settings:alias.hint')}</p>

            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <input
                className="form-input"
                type="text"
                value={aliasInput}
                onChange={(e) => setAliasInput(e.target.value)}
                placeholder={device.displayName ?? device.deviceId}
                maxLength={100}
                style={{ flex: '1 1 200px', minWidth: 0 }}
                aria-label={t('settings:alias.inputLabel')}
              />
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => void handleAliasSave()}
                disabled={isSavingAlias}
              >
                {isSavingAlias ? t('common:actions.saving') : t('settings:alias.save')}
              </button>
              {/* Quick-clear button — only shown when an alias is currently set */}
              {aliasInput.trim() ? (
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={() => void handleAliasSave('')}
                  disabled={isSavingAlias}
                >
                  {t('settings:alias.clear')}
                </button>
              ) : null}
            </div>

            {aliasMessage ? (
              <p
                className={aliasIsError ? 'hint' : 'hint'}
                style={{ marginTop: 6, color: aliasIsError ? 'var(--danger-dark)' : 'var(--success-dark, green)' }}
                role={aliasIsError ? 'alert' : 'status'}
              >
                {aliasMessage}
              </p>
            ) : null}
          </div>

          {/* The caller's own permission set on this device */}
          <div style={{ marginTop: 20 }}>
            <p className="info-label" style={{ marginBottom: 8 }}>{t('settings:info.yourPermissions')}</p>
            <PermissionBadges permissions={device.permissions} />
          </div>
        </div>
      </div>

      {/* ================================================================
       * Section 2: Settings Schedule — visible if canModifySettings
       *
       * Above Reporting & Power deliberately. On a scheduled device the
       * schedule is what decides the settings; the panel below it edits them by
       * hand, and only until the next switch. Putting the authority first means
       * somebody reads why the values are what they are before reading the form
       * that can temporarily override them.
       *
       * Rendered on permission alone, NOT on the data having arrived. Gating it
       * on `schedule !== null` is what made the whole feature — profiles, rules,
       * their Edit and Delete buttons — silently absent whenever the first fetch
       * failed. The section now always occupies its place and reports its own
       * state; see the load effect above.
       * ================================================================ */}
      {perms.canModifySettings ? (
        <ScheduleSection
          deviceId={device.deviceId}
          schedule={schedule}
          status={scheduleStatus}
          error={scheduleError}
          onRetry={() => void reloadSchedule()}
          onScheduleChanged={handleScheduleChanged}
          refreshToken={autoRefresh.token}
        />
      ) : null}

      {/* ================================================================
       * Section 3: Reporting & Power — visible if canModifySettings
       *
       * Self-contained: it loads its own state and owns its own form, so this
       * page stays the composition it already was rather than growing a second
       * set of loading/saving flags. It only borrows the tab's refresh token,
       * and decides for itself what a tick may safely touch.
       * ================================================================ */}
      {perms.canModifySettings ? (
        <DeviceConfigSection
          deviceId={device.deviceId}
          refreshToken={autoRefresh.token}
          schedule={schedule}
          onScheduleChanged={handleScheduleChanged}
          onScheduleReloadNeeded={() => void reloadSchedule()}
        />
      ) : null}

      {/* ================================================================
       * Section 4: Firmware configuration — visible if canModifySettings
       * ================================================================ */}
      {perms.canModifySettings ? (
        <div className="settings-section">
          <div className="settings-section-header">
            <span className="settings-section-icon" aria-hidden="true">🔧</span>
            <h3>{t('settings:firmware.title')}</h3>
          </div>

          <div className="settings-section-body">
            <p>
              <Trans i18nKey="firmware.sectionIntro" ns="settings" components={{ code: <code /> }} />
            </p>

            {provisioningError ? (
              <div className="banner banner--error" role="alert">{provisioningError}</div>
            ) : null}

            {provisioning === null ? (
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => void loadProvisioning()}
                disabled={isLoadingProvisioning}
              >
                {isLoadingProvisioning ? t('common:states.loading') : t('settings:firmware.show')}
              </button>
            ) : (
              <>
                <ProvisioningPanel
                  provisioning={provisioning}
                  // Re-read after a key rotation so the fingerprint shown is the
                  // one the server actually holds, not the one we just sent.
                  onAckKeyActivated={() => void loadProvisioning()}
                />

                {/* Collapsed by default: it is a reference, not something you
                    came here to do, and expanded it would dwarf the page. */}
                <details className="firmware-parameters-details">
                  <summary>{t('settings:firmware.allParameters')}</summary>
                  <FirmwareParameterTable configSnippet={provisioning.configSnippet} />
                </details>
              </>
            )}
          </div>
        </div>
      ) : null}

      {/* ================================================================
       * Section 5: Access Management — visible if canShare or canModifySettings
       * ================================================================ */}
      {canViewAccess ? (
        <div className="settings-section">
          <div className="settings-section-header">
            <span className="settings-section-icon" aria-hidden="true">👥</span>
            <h3>{t('settings:sharing.title')}</h3>
          </div>

          <div className="settings-section-body">
            {/* Info banner if the user can view but not edit */}
            {!canEditAccess ? (
              <div className="banner banner--info" role="status">
                {t('settings:sharing.readOnlyNote')}
              </div>
            ) : null}

            {/* Success / error feedback */}
            {accessSuccess ? (
              <div className="banner banner--success" role="status">{accessSuccess}</div>
            ) : null}
            {accessError ? (
              <div className="banner banner--error" role="alert">{accessError}</div>
            ) : null}

            {/* ---- People with access ---- */}
            <div>
              <p className="info-label" style={{ marginBottom: 10 }}>{t('settings:sharing.peopleWithAccess')}</p>

              {isLoadingAccess ? (
                <div className="loading-state" style={{ minHeight: 80 }}>
                  <div className="spinner" />
                  <span>{t('common:states.loading')}</span>
                </div>
              ) : visibleSharedUsers.length === 0 ? (
                <p className="hint">{t('settings:sharing.noOtherUsers')}</p>
              ) : (
                <div className="access-list">
                  {visibleSharedUsers.map((row) => (
                    <SharedUserCard
                      key={row.userId}
                      row={row}
                      canEdit={canEditAccess}
                      isSaving={savingUserId === row.userId}
                      onToggleFlag={(flag) => toggleFlag(row.userId, flag)}
                      onSave={() => void saveUserRow(row)}
                      onRemove={() => void removeUserRow(row)}
                    />
                  ))}
                </div>
              )}
            </div>

            {/* ---- Invite form — only shown when the caller has canShare ---- */}
            {canEditAccess ? (
              <div className="invite-form">
                <h4>{t('settings:sharing.inviteTitle')}</h4>
                <p className="hint">{t('settings:sharing.inviteHint')}</p>

                <form onSubmit={handleInviteSubmit}>
                  <div className="form-field" style={{ marginBottom: 12 }}>
                    <label className="form-label" htmlFor="invite-email">{t('common:fields.email')}</label>
                    <input
                      id="invite-email"
                      className="form-input"
                      type="email"
                      value={inviteEmail}
                      onChange={(e) => setInviteEmail(e.target.value)}
                      placeholder={t('settings:sharing.emailPlaceholder')}
                      required
                    />
                  </div>

                  {/* Capability checkboxes for the invite */}
                  <CapabilityCheckboxes
                    flags={inviteFlags}
                    canEdit={true}
                    onToggle={toggleInviteFlag}
                  />

                  <button
                    type="submit"
                    className="btn btn-primary"
                    disabled={isInviteSubmitting}
                    style={{ marginTop: 12 }}
                  >
                    {isInviteSubmitting ? t('settings:sharing.sharing') : t('settings:sharing.share')}
                  </button>
                </form>
              </div>
            ) : null}
          </div>
        </div>
      ) : null}

      {/* ================================================================
       * Section 6: Danger Zone — visible only if canDelete
       * ================================================================ */}
      {perms.canDelete ? (
        <div className="settings-section settings-section--danger">
          <div className="settings-section-header">
            <span className="settings-section-icon" aria-hidden="true">⚠️</span>
            <h3>{t('settings:danger.title')}</h3>
          </div>

          <div className="settings-section-body">
            <p>
              <Trans i18nKey="danger.intro" ns="settings" components={{ strong: <strong /> }} />
            </p>

            {deleteError ? (
              <div className="banner banner--error" role="alert">{deleteError}</div>
            ) : null}

            {/* Two-step confirmation: first button reveals a confirm row */}
            {!deleteConfirmVisible ? (
              <button
                type="button"
                className="btn btn-danger"
                onClick={() => setDeleteConfirmVisible(true)}
                disabled={!device.isActive}
              >
                {device.isActive ? t('settings:danger.delete') : t('settings:danger.alreadyInactive')}
              </button>
            ) : (
              <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
                <span style={{ fontWeight: 600, color: 'var(--danger-dark)' }}>
                  {t('settings:danger.confirmPrompt')}
                </span>
                <button
                  type="button"
                  className="btn btn-danger-solid"
                  onClick={() => void handleDeleteDevice()}
                  disabled={isDeleting}
                >
                  {isDeleting ? t('settings:danger.deleting') : t('settings:danger.confirmDelete')}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setDeleteConfirmVisible(false)}
                  disabled={isDeleting}
                >
                  {t('common:actions.cancel')}
                </button>
              </div>
            )}
          </div>
        </div>
      ) : null}
    </div>
  )
}

// SharedUserCard is defined in src/components/SharedUserCard.tsx
// CapabilityCheckboxes is defined in src/components/CapabilityCheckboxes.tsx

