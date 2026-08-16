// ============================================================
// DeviceSettingsTab — the "Settings" tab inside DevicePage.
//
// Four permission-gated sections are shown conditionally:
//
//   1. Device Information  (always visible — every user with canRead sees this)
//      Device ID, registration date, status, last fix, the caller's own alias
//      for the device, and their permission badges.
//
//   2. Firmware configuration (visible only if canModifySettings)
//      Re-reads the provisioning block — topics, public key fingerprint and the
//      Config.h snippet — so a tracker can be re-flashed without rotating its
//      key pair. Loaded on demand rather than with the page: it is a rarely
//      needed panel and there is no reason to fetch it for every visit.
//
//   3. Access Management   (visible only if canShare OR canModifySettings)
//      Shows who currently has access.  If the caller has canShare they can
//      also invite new users and edit / revoke existing grants.
//      If they only have canModifySettings they can view the list but not edit.
//
//   4. Danger Zone         (visible only if canDelete)
//      A confirmation-required button to soft-delete (deactivate) the device.
//      After deletion the page shows a success banner and the device status
//      is updated in the parent via reloadDevice().
//
// Permission model (backend-enforced; FE hides controls as a courtesy):
//   canRead            — always true for anyone with an active grant
//   canDelete          — may deactivate the device
//   canShare           — may view AND edit the access roster; also implies canModifySettings
//   canModifySettings  — may view the access roster but cannot modify it
// ============================================================

import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { useNavigate, useOutletContext } from 'react-router-dom'
import { PermissionBadges } from '../components/PermissionBadges'
import { ProvisioningPanel } from '../components/ProvisioningPanel'
import { SharedUserCard } from '../components/SharedUserCard'
import { CapabilityCheckboxes, EMPTY_FLAGS } from '../components/CapabilityCheckboxes'
import type { SharedUserData } from '../components/SharedUserCard'
import type { CapabilityFlags } from '../components/CapabilityCheckboxes'
import type { DevicePageContext } from './DevicePage'
import {
  createAccessGrant,
  deleteDevice,
  fetchAccessGrantsForDevice,
  fetchDeviceProvisioning,
  fetchUserById,
  fetchUsers,
  revokeAccessGrant,
  updateAccessGrant,
  updateDeviceAlias,
} from '../services/apiClient'
import type { AccessDto, DeviceProvisioningDto, UserProfileDto } from '../services/apiTypes'
import { formatRelativeTime } from '../utils/dates'
import { describeError } from '../utils/errors'
import { useAuth } from '../auth/AuthContext'

// ============================================================
// Main component
// ============================================================

export function DeviceSettingsTab() {
  const { device, reloadDevice, updateDevice } = useOutletContext<DevicePageContext>()
  const { currentUser } = useAuth()
  const navigate        = useNavigate()
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
      setAliasMessage('Name must be 100 characters or fewer.')
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
      setAliasMessage(trimmed ? 'Device name saved.' : 'Device name cleared.')
      setAliasIsError(false)
    } catch (error) {
      setAliasMessage(describeError(error, 'Failed to save device name.'))
      setAliasIsError(true)
    } finally {
      setIsSavingAlias(false)
    }
  }

  // ---- Load the access roster whenever the section is visible ----
  useEffect(() => {
    if (!canViewAccess) {
      setSharedUsers([])
      return
    }

    void loadSharedUsers()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [device.deviceId, canViewAccess])

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
      setAccessError(describeError(error, 'Failed to load access list.'))
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
      setAccessError(describeError(error, 'Failed to update permissions.'))
    } finally {
      setSavingUserId(null)
    }
  }

  // ---- Revoke access for an existing user ----
  async function removeUserRow(row: SharedUserData): Promise<void> {
    if (!canEditAccess) {
      return
    }
    const confirmed = window.confirm(
      `Remove access for ${row.userEmail}?\nThey will no longer be able to see this device.`,
    )
    if (!confirmed) {
      return
    }
    setSavingUserId(row.userId)
    setAccessError('')
    setAccessSuccess('')
    try {
      await revokeAccessGrant(row.accessId)
      await loadSharedUsers()
      setAccessSuccess(`Removed access for ${row.userEmail}.`)
    } catch (error) {
      setAccessError(describeError(error, 'Failed to remove access.'))
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
      setAccessError('Enter the email of the person to invite.')
      return
    }

    setIsInviteSubmitting(true)
    try {
      // Look up the user by email (exact match)
      const matches = await fetchUsers(email, true)
      if (matches.length === 0) {
        setAccessError(`No account found with email "${email}".`)
        return
      }
      const targetUser = matches[0]

      // Prevent sharing with yourself
      if (currentUser !== null && targetUser.id === currentUser.id) {
        setAccessError('You cannot share a device with yourself.')
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
      setAccessError(describeError(error, 'Failed to share access.'))
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
      setDeleteError(describeError(error, 'Failed to delete device.'))
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
      setProvisioningError(describeError(error, 'Failed to load the firmware configuration.'))
    } finally {
      setIsLoadingProvisioning(false)
    }
  }

  // ---- Derived: registration + deactivation dates ----
  const registeredDate = new Date(device.createdAt).toLocaleDateString()
  const deactivatedDate = device.deactivatedAt
    ? new Date(device.deactivatedAt).toLocaleDateString()
    : null

  // ---- Render ----
  return (
    <div>

      {/* ================================================================
       * Section 1: Device Information — always visible
       * ================================================================ */}
      <div className="settings-section">
        <div className="settings-section-header">
          <span className="settings-section-icon" aria-hidden="true">📡</span>
          <h3>Device Information</h3>
        </div>

        <div className="settings-section-body">
          {/* Key-value grid: UUID, dates, status */}
          <div className="info-grid">
            <div className="info-item">
              {/* The tracker's MQTT identity: the same string it publishes
                  under, authenticates with, and carries inside every encrypted
                  payload. Permanent — it cannot be changed or reused. */}
              <span className="info-label">Device ID</span>
              <span className="info-value mono">{device.deviceId}</span>
            </div>

            {device.displayName ? (
              <div className="info-item">
                <span className="info-label">Display name</span>
                <span className="info-value">{device.displayName}</span>
              </div>
            ) : null}

            <div className="info-item">
              <span className="info-label">Status</span>
              <span className="info-value">
                <span
                  className={`status-badge ${device.isActive ? 'status-badge--active' : 'status-badge--inactive'}`}
                >
                  {device.isActive ? 'Active' : 'Inactive'}
                </span>
              </span>
            </div>

            <div className="info-item">
              <span className="info-label">Registered</span>
              <span className="info-value">{registeredDate}</span>
            </div>

            <div className="info-item">
              {/* The only liveness signal there is — the firmware sends no
                  heartbeat, so this moves only when a real fix arrives. */}
              <span className="info-label">Last fix received</span>
              <span className="info-value">{formatRelativeTime(device.lastSeenAt)}</span>
            </div>

            {deactivatedDate ? (
              <div className="info-item">
                <span className="info-label">Deactivated</span>
                <span className="info-value">{deactivatedDate}</span>
              </div>
            ) : null}
          </div>

          {/* ---- Device display name (per-user alias) ---- */}
          {/* Every user with CanRead can set their own name for this device.
              The hardware UUID is always visible as the canonical identifier. */}
          <div style={{ marginTop: 20 }}>
            <p className="info-label" style={{ marginBottom: 8 }}>Your name for this device</p>
            <p className="hint" style={{ marginBottom: 10 }}>
              Only visible to you. Leave empty to fall back to the device's
              display name, or its ID.
            </p>

            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <input
                className="form-input"
                type="text"
                value={aliasInput}
                onChange={(e) => setAliasInput(e.target.value)}
                placeholder={device.displayName ?? device.deviceId}
                maxLength={100}
                style={{ flex: '1 1 200px', minWidth: 0 }}
                aria-label="Custom device name"
              />
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => void handleAliasSave()}
                disabled={isSavingAlias}
              >
                {isSavingAlias ? 'Saving…' : 'Save name'}
              </button>
              {/* Quick-clear button — only shown when an alias is currently set */}
              {aliasInput.trim() ? (
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={() => void handleAliasSave('')}
                  disabled={isSavingAlias}
                >
                  Clear
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
            <p className="info-label" style={{ marginBottom: 8 }}>Your permissions</p>
            <PermissionBadges permissions={device.permissions} />
          </div>
        </div>
      </div>

      {/* ================================================================
       * Section 2: Firmware configuration — visible if canModifySettings
       * ================================================================ */}
      {perms.canModifySettings ? (
        <div className="settings-section">
          <div className="settings-section-header">
            <span className="settings-section-icon" aria-hidden="true">🔧</span>
            <h3>Firmware Configuration</h3>
          </div>

          <div className="settings-section-body">
            <p>
              The MQTT topics, broker URI and receiver public key this device was
              provisioned with — everything needed to re-flash it. Re-reading
              this is always safe: it re-renders the stored public key rather
              than generating a new pair, so a device already in the field keeps
              working.
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
                {isLoadingProvisioning ? 'Loading…' : 'Show firmware configuration'}
              </button>
            ) : (
              <ProvisioningPanel provisioning={provisioning} />
            )}
          </div>
        </div>
      ) : null}

      {/* ================================================================
       * Section 3: Access Management — visible if canShare or canModifySettings
       * ================================================================ */}
      {canViewAccess ? (
        <div className="settings-section">
          <div className="settings-section-header">
            <span className="settings-section-icon" aria-hidden="true">👥</span>
            <h3>Access Management</h3>
          </div>

          <div className="settings-section-body">
            {/* Info banner if the user can view but not edit */}
            {!canEditAccess ? (
              <div className="banner banner--info" role="status">
                You have Settings permission — you can view who has access,
                but you need the Share permission to invite or edit users.
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
              <p className="info-label" style={{ marginBottom: 10 }}>People with access</p>

              {isLoadingAccess ? (
                <div className="loading-state" style={{ minHeight: 80 }}>
                  <div className="spinner" />
                  <span>Loading…</span>
                </div>
              ) : sharedUsers.length === 0 ? (
                <p className="hint">No other users have access to this device.</p>
              ) : (
                <div className="access-list">
                  {sharedUsers.map((row) => (
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
                <h4>Invite someone new</h4>
                <p className="hint">
                  Enter their email address. Read access is always granted.
                  Choose which additional permissions to give them.
                </p>

                <form onSubmit={handleInviteSubmit}>
                  <div className="form-field" style={{ marginBottom: 12 }}>
                    <label className="form-label" htmlFor="invite-email">Email</label>
                    <input
                      id="invite-email"
                      className="form-input"
                      type="email"
                      value={inviteEmail}
                      onChange={(e) => setInviteEmail(e.target.value)}
                      placeholder="user@example.com"
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
                    {isInviteSubmitting ? 'Sharing…' : 'Share access'}
                  </button>
                </form>
              </div>
            ) : null}
          </div>
        </div>
      ) : null}

      {/* ================================================================
       * Section 4: Danger Zone — visible only if canDelete
       * ================================================================ */}
      {perms.canDelete ? (
        <div className="settings-section settings-section--danger">
          <div className="settings-section-header">
            <span className="settings-section-icon" aria-hidden="true">⚠️</span>
            <h3>Danger Zone</h3>
          </div>

          <div className="settings-section-body">
            <p>
              Deleting the device deactivates it permanently for <strong>all users</strong> who
              have access. Historical position data is retained but no new positions will
              be accepted. This action cannot be undone.
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
                {device.isActive ? 'Delete device…' : 'Device already inactive'}
              </button>
            ) : (
              <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
                <span style={{ fontWeight: 600, color: 'var(--danger-dark)' }}>
                  Are you sure? This cannot be undone.
                </span>
                <button
                  type="button"
                  className="btn btn-danger-solid"
                  onClick={() => void handleDeleteDevice()}
                  disabled={isDeleting}
                >
                  {isDeleting ? 'Deleting…' : 'Yes, delete device'}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setDeleteConfirmVisible(false)}
                  disabled={isDeleting}
                >
                  Cancel
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

