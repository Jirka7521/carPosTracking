// ============================================================
// InvitePage — where the recipient of an invitation link decides.
//
// Someone who can share a device made a single-use link and sent it here
// (components/AccessInvitationPanel.tsx). This page shows what the link offers —
// which device, whose, and with which permissions — and lets the signed-in
// visitor accept or decline. Nothing is shared until they accept, and declining
// spends the link.
//
// Protected by <RequireAuth>: only an account can accept, and the guard brings
// a visitor who had to sign in first straight back to this address. The token
// is sent to the API in request bodies only (never an API URL), so it stays out
// of the server's access logs; nginx also redacts /invite/ paths in its own log.
// ============================================================

import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { PermissionBadges } from '../components/PermissionBadges'
import {
  acceptAccessInvitation,
  declineAccessInvitation,
  previewAccessInvitation,
} from '../services/apiClient'
import type { AccessInvitationPreviewDto } from '../services/apiTypes'
import { formatDateTime } from '../i18n/format'
import { parseApiTimestamp } from '../utils/dates'
import { describeError } from '../utils/errors'

// loading → ready (offer shown) → the visitor leaves on accept, or 'declined';
// 'unusable' covers every link the API refuses to describe.
type InviteStatus = 'loading' | 'ready' | 'unusable' | 'declined'

export function InvitePage() {
  const { token = '' } = useParams()
  const navigate = useNavigate()
  const { t } = useTranslation(['settings', 'common'])

  const [preview, setPreview] = useState<AccessInvitationPreviewDto | null>(null)
  const [status, setStatus] = useState<InviteStatus>('loading')
  const [error, setError] = useState<string>('')
  const [busy, setBusy] = useState<'accept' | 'decline' | null>(null)

  // Asked once per link. App.tsx keys this page by the token, so opening a
  // different invitation link remounts it in the initial 'loading' state.
  useEffect(() => {
    let cancelled: boolean = false

    previewAccessInvitation(token)
      .then((offer) => {
        if (!cancelled) {
          setPreview(offer)
          setStatus('ready')
        }
      })
      .catch((caught: unknown) => {
        if (!cancelled) {
          setError(describeError(caught, t('settings:invitePage.loadFailed')))
          setStatus('unusable')
        }
      })

    return () => {
      cancelled = true
    }
    // `t` only supplies the fallback message; a language switch must not
    // re-ask the server about the link.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token])

  async function handleAccept(): Promise<void> {
    setError('')
    setBusy('accept')
    try {
      const grant = await acceptAccessInvitation(token)
      navigate(`/device/${encodeURIComponent(grant.deviceId)}`, { replace: true })
    } catch (caught) {
      setError(describeError(caught, t('settings:invitePage.acceptFailed')))
      setBusy(null)
    }
  }

  async function handleDecline(): Promise<void> {
    setError('')
    setBusy('decline')
    try {
      await declineAccessInvitation(token)
      setStatus('declined')
    } catch (caught) {
      setError(describeError(caught, t('settings:invitePage.declineFailed')))
    } finally {
      setBusy(null)
    }
  }

  const expires: Date | null = preview === null ? null : parseApiTimestamp(preview.expiresAt)

  return (
    <div className="page-content">
      <div className="page-header">
        <h1>{t('settings:invitePage.title')}</h1>
      </div>

      <div className="settings-section">
        <div className="settings-section-body">
          {error ? <div className="banner banner--error" role="alert">{error}</div> : null}

          {status === 'loading' ? (
            <div className="loading-state" style={{ minHeight: 80 }}>
              <div className="spinner" />
              <span>{t('common:states.loading')}</span>
            </div>
          ) : null}

          {status === 'declined' ? (
            <div className="banner banner--success" role="status">{t('settings:invitePage.declined')}</div>
          ) : null}

          {status === 'ready' && preview !== null ? (
            <>
              <p>
                {t('settings:invitePage.offer', {
                  inviter: `${preview.inviterFirstName} ${preview.inviterLastName}`.trim(),
                  device: preview.deviceDisplayName ?? preview.deviceId,
                })}
              </p>

              <p className="info-label" style={{ marginBottom: 8 }}>{t('settings:invitePage.permissions')}</p>
              <PermissionBadges
                permissions={{
                  canRead: true,
                  canDelete: preview.canDelete,
                  canShare: preview.canShare,
                  canModifySettings: preview.canModifySettings,
                }}
              />

              {expires !== null ? (
                <p className="hint">{t('settings:invitePage.expires', { when: formatDateTime(expires) })}</p>
              ) : null}

              {preview.alreadyHasAccess ? (
                <div className="banner banner--info" role="status">{t('settings:invitePage.alreadyHasAccess')}</div>
              ) : (
                <div className="filter-row" style={{ marginTop: 12 }}>
                  <button
                    type="button"
                    className="btn btn-primary"
                    disabled={busy !== null}
                    onClick={() => void handleAccept()}
                  >
                    {busy === 'accept' ? t('settings:invitePage.accepting') : t('settings:invitePage.accept')}
                  </button>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    disabled={busy !== null}
                    onClick={() => void handleDecline()}
                  >
                    {busy === 'decline' ? t('settings:invitePage.declining') : t('settings:invitePage.decline')}
                  </button>
                </div>
              )}
            </>
          ) : null}

          {status !== 'loading' ? (
            <p style={{ marginTop: 16 }}>
              <Link to="/home">{t('settings:invitePage.backHome')}</Link>
            </p>
          ) : null}
        </div>
      </div>
    </div>
  )
}
