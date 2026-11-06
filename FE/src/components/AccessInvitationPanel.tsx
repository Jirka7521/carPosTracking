// ============================================================
// AccessInvitationPanel — share a device with another account by invitation
// link, inside the device's Access Management section.
//
// Why a link and not an email address: sharing used to take an email, look the
// account up and grant it access on the spot, without asking. That told the
// sharer which addresses had an account here and, with the grant, let them read
// that person's name. Now the sharer creates a single-use link and sends it
// themselves; the other person signs in, sees what is offered and accepts or
// declines (pages/InvitePage.tsx). Nothing is shared until they accept.
//
// The link's token is shown exactly once, right after it is made — the server
// keeps only a digest of it, so there is nothing to show later. The list below
// it shows open invitations without their links, each withdrawable.
// ============================================================

import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { CapabilityCheckboxes } from './CapabilityCheckboxes'
import { EMPTY_FLAGS } from './capabilityFlags'
import type { CapabilityFlags } from './capabilityFlags'
import { PermissionBadges } from './PermissionBadges'
import {
  createAccessInvitation,
  fetchAccessInvitations,
  withdrawAccessInvitation,
} from '../services/apiClient'
import type { AccessInvitationDto } from '../services/apiTypes'
import { BASE_PATH } from '../services/runtimeConfig'
import { formatDateTime } from '../i18n/format'
import { parseApiTimestamp } from '../utils/dates'
import { describeError } from '../utils/errors'

type AccessInvitationPanelProps = {
  // The device the invitations are for.
  deviceId: string
}

// The link the recipient opens. Composed here rather than by the server, as
// ShareLinkManager does for share links, so no "public base URL" setting is
// needed — BASE_PATH already knows the prefix the app is served under.
function invitationUrl(token: string): string {
  return `${globalThis.location.origin}${BASE_PATH}/invite/${token}`.replace(
    `${globalThis.location.origin}//`,
    `${globalThis.location.origin}/`,
  )
}

// An API timestamp as the reader's local date and time, or the raw value when
// it does not parse (it always should).
function formatExpiry(value: string): string {
  const parsed: Date | null = parseApiTimestamp(value)
  return parsed === null ? value : formatDateTime(parsed)
}

export function AccessInvitationPanel({ deviceId }: AccessInvitationPanelProps) {
  const { t } = useTranslation(['settings', 'common'])

  const [flags, setFlags] = useState<CapabilityFlags>(EMPTY_FLAGS)
  const [isCreating, setIsCreating] = useState<boolean>(false)

  // The just-made link. Lives only in this component's state: gone when the
  // panel unmounts, never written to storage.
  const [createdLink, setCreatedLink] = useState<string | null>(null)
  const [createdExpiresAt, setCreatedExpiresAt] = useState<string>('')
  const [isCopied, setIsCopied] = useState<boolean>(false)

  const [invitations, setInvitations] = useState<AccessInvitationDto[]>([])
  const [isLoading, setIsLoading] = useState<boolean>(true)
  const [withdrawingId, setWithdrawingId] = useState<string | null>(null)

  const [error, setError] = useState<string>('')
  const [message, setMessage] = useState<string>('')

  async function loadInvitations(): Promise<void> {
    try {
      setInvitations(await fetchAccessInvitations(deviceId))
    } catch (caught) {
      setError(describeError(caught, t('settings:sharing.invite.loadFailed')))
    } finally {
      setIsLoading(false)
    }
  }

  // Loads once per mount. The parent keys this panel by device id, so moving to
  // another device remounts it with fresh state — including dropping a link
  // that was made for the previous device.
  useEffect(() => {
    let canceled: boolean = false

    const load = async (): Promise<void> => {
      try {
        const fetched: AccessInvitationDto[] = await fetchAccessInvitations(deviceId)
        if (!canceled) {
          setInvitations(fetched)
        }
      } catch (caught) {
        if (!canceled) {
          setError(describeError(caught, t('settings:sharing.invite.loadFailed')))
        }
      } finally {
        if (!canceled) {
          setIsLoading(false)
        }
      }
    }

    void load()

    return () => {
      canceled = true
    }
    // `t` only supplies the fallback message; a language switch must not refetch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deviceId])

  // The "Share implies Settings" rule, as everywhere else permissions are edited.
  function toggleFlag(flag: keyof CapabilityFlags): void {
    setFlags((current) => {
      if (flag === 'canModifySettings' && current.canShare) {
        return current
      }
      const next: CapabilityFlags = { ...current, [flag]: !current[flag] }
      if (flag === 'canShare' && next.canShare) {
        next.canModifySettings = true
      }
      return next
    })
  }

  async function handleCreate(): Promise<void> {
    setError('')
    setMessage('')
    setIsCopied(false)
    setIsCreating(true)

    try {
      const created = await createAccessInvitation({
        deviceId,
        canDelete: flags.canDelete,
        canShare: flags.canShare,
        canModifySettings: flags.canModifySettings,
      })

      setCreatedLink(invitationUrl(created.token))
      setCreatedExpiresAt(created.invitation.expiresAt)
      setFlags(EMPTY_FLAGS)
      await loadInvitations()
    } catch (caught) {
      setError(describeError(caught, t('settings:sharing.invite.createFailed')))
    } finally {
      setIsCreating(false)
    }
  }

  async function handleCopy(): Promise<void> {
    if (createdLink === null) {
      return
    }
    try {
      await navigator.clipboard.writeText(createdLink)
      setIsCopied(true)
      globalThis.setTimeout(() => setIsCopied(false), 2000)
    } catch {
      // Clipboard access can be refused (an insecure origin, a permissions
      // policy). The link is on screen and selectable either way.
    }
  }

  async function handleWithdraw(invitation: AccessInvitationDto): Promise<void> {
    if (!window.confirm(t('settings:sharing.invite.confirmWithdraw'))) {
      return
    }

    setError('')
    setMessage('')
    setWithdrawingId(invitation.id)

    try {
      await withdrawAccessInvitation(invitation.id)
      await loadInvitations()
      setMessage(t('settings:sharing.invite.withdrawn'))
    } catch (caught) {
      setError(describeError(caught, t('settings:sharing.invite.withdrawFailed')))
    } finally {
      setWithdrawingId(null)
    }
  }

  return (
    <div className="invite-form">
      <h4>{t('settings:sharing.invite.title')}</h4>
      <p className="hint">{t('settings:sharing.invite.hint')}</p>

      {message ? <div className="banner banner--success" role="status">{message}</div> : null}
      {error ? <div className="banner banner--error" role="alert">{error}</div> : null}

      <CapabilityCheckboxes flags={flags} canEdit onToggle={toggleFlag} />

      <button
        type="button"
        className="btn btn-primary"
        disabled={isCreating}
        style={{ marginTop: 12 }}
        onClick={() => void handleCreate()}
      >
        {isCreating ? t('settings:sharing.invite.creating') : t('settings:sharing.invite.create')}
      </button>

      {/* The one moment the link exists outside the server's digest. */}
      {createdLink !== null ? (
        <div className="banner banner--info" role="status" style={{ marginTop: 12 }}>
          <p className="info-label">{t('settings:sharing.invite.linkLabel')}</p>
          <div className="add-device-row">
            <input
              className="form-input"
              type="text"
              readOnly
              value={createdLink}
              aria-label={t('settings:sharing.invite.linkLabel')}
              onFocus={(e) => e.target.select()}
            />
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              style={{ flexShrink: 0 }}
              onClick={() => void handleCopy()}
            >
              {isCopied ? t('common:actions.copied') : t('settings:sharing.invite.copy')}
            </button>
          </div>
          <p className="hint">
            {t('settings:sharing.invite.linkNote', { when: formatExpiry(createdExpiresAt) })}
          </p>
        </div>
      ) : null}

      <p className="info-label" style={{ marginTop: 16, marginBottom: 8 }}>
        {t('settings:sharing.invite.pendingTitle')}
      </p>

      {isLoading ? (
        <div className="loading-state" style={{ minHeight: 60 }}>
          <div className="spinner" />
          <span>{t('common:states.loading')}</span>
        </div>
      ) : invitations.length === 0 ? (
        <p className="hint">{t('settings:sharing.invite.none')}</p>
      ) : (
        <div className="access-list">
          {invitations.map((invitation) => (
            <div key={invitation.id} className="share-draft">
              <PermissionBadges
                permissions={{
                  canRead: true,
                  canDelete: invitation.canDelete,
                  canShare: invitation.canShare,
                  canModifySettings: invitation.canModifySettings,
                }}
              />
              <div className="filter-row">
                <span className="hint">
                  {t('settings:sharing.invite.expires', { when: formatExpiry(invitation.expiresAt) })}
                </span>
                <button
                  type="button"
                  className="btn btn-danger btn-sm"
                  disabled={withdrawingId === invitation.id}
                  onClick={() => void handleWithdraw(invitation)}
                >
                  {t('settings:sharing.invite.withdraw')}
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
