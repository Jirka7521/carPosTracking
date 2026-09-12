// ---------------------------------------------------------------------------
// SharePage — what somebody sees when they open a temporary share link.
//
// This is the only page in the app that shows position data to a visitor with no
// account, so a few of its decisions are load-bearing rather than cosmetic:
//
//   * The code screen reveals NOTHING. No tracker name, no owner, no
//     confirmation that the link is even real. A leaked URL on its own must not
//     establish that somebody has a tracker, so everything the share knows about
//     itself arrives only after the code is accepted.
//
//   * The token leaves the address bar on mount. It stays in the URL long enough
//     to be read once, then history.replaceState swaps it for a bare /share, so
//     it is not sitting in a screenshot or in the visible history entry. Nothing
//     is stored in its place: a reload rides the HttpOnly share cookie instead,
//     and when that is gone the page says to reopen the original link. Keeping
//     the token in sessionStorage would put a live credential somewhere an XSS
//     could read it, to buy a convenience the cookie already provides.
//
//   * The bounds come from the server. The share's own window is what the range
//     controls are limited to, and the server clamps to it again regardless —
//     the inputs here are a convenience, not the enforcement.
//
// It deliberately does not use AppLayout or RequireAuth: a visitor has no
// session, and the page must render identically whether or not one happens to be
// signed in on this browser.
// ---------------------------------------------------------------------------

import { useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import type { SharedPositionDto, ShareSessionDto } from '../services/apiTypes'
import { fetchSharedView, leaveShare, redeemShareLink } from '../services/apiClient'
import { BASE_PATH, assetUrl, hasGoogleMapsKey, runtimeConfig } from '../services/runtimeConfig'
import DeviceMap from '../components/DeviceMap'
import { LanguageMenu } from '../components/LanguageMenu'
import { SiteFooter } from '../components/SiteFooter'
import { RefreshToolbar } from '../components/RefreshToolbar'
import { useAutoRefresh } from '../hooks/useAutoRefresh'
import { formatDateTime } from '../i18n/format'
import { parseApiTimestamp } from '../utils/dates'
import { describeError } from '../utils/errors'
import { ApiError } from '../services/apiClient'

// Matches DevicePage: one timer for the page, thirty seconds.
const AUTO_REFRESH_SEC = 30

// What the page is currently doing. `locked` is the code screen; `open` is the
// map. There is no intermediate state that shows part of a share.
type ShareStage = 'locked' | 'open'

export function SharePage() {
  const { t } = useTranslation(['share', 'common', 'device'])
  const { token: tokenFromRoute } = useParams<{ token: string }>()

  // Held in a ref rather than state: it is a credential, it never renders, and
  // it must not participate in the effect dependency graph.
  const tokenRef = useRef<string | null>(tokenFromRoute ?? null)

  const [stage, setStage] = useState<ShareStage>('locked')
  const [share, setShare] = useState<ShareSessionDto | null>(null)
  const [positions, setPositions] = useState<SharedPositionDto[]>([])
  const [passphrase, setPassphrase] = useState<string>('')
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false)
  const [isLoading, setIsLoading] = useState<boolean>(false)
  const [error, setError] = useState<string | null>(null)
  // Set when the page was opened without a token and the cookie did not work
  // either — the only honest advice then is "open the original link again".
  const [needsOriginalLink, setNeedsOriginalLink] = useState<boolean>(false)
  // Anything that wants the share read bumps this. Zero means "do not read" —
  // the state a page holding a token sits in until the code is entered, and the
  // one it returns to after the visitor closes the share.
  //
  // A counter rather than deriving the trigger from `stage`: a successful read
  // sets the stage to open, so a stage-driven effect would immediately fetch a
  // second time on every reload.
  const [loadToken, setLoadToken] = useState<number>(() => (tokenFromRoute === undefined ? 1 : 0))

  const refresh = useAutoRefresh(AUTO_REFRESH_SEC)

  // Take the token out of the address bar as soon as it has been read.
  //
  // replaceState rather than a navigate: this must not add a history entry, and
  // React Router must not remount the page underneath a request in flight.
  useEffect(() => {
    if (tokenFromRoute === undefined) {
      return
    }

    const bare: string = `${BASE_PATH}/share`.replace('//', '/')
    window.history.replaceState(null, '', bare)
  }, [tokenFromRoute])

  // The single place the share is read. Three occasions reach it, and none of
  // them fetches for itself — each just bumps `loadToken`:
  //
  //   * a reload, where the URL has no token but the share cookie may still be
  //     good, so the counter starts at 1;
  //   * a correct code, where handleSubmit bumps it;
  //   * every auto-refresh tick, which re-runs the same query without moving
  //     anything, exactly as the device page's tabs do.
  //
  // Written as one effect with the loader inline, following DeviceMapTab: a
  // `canceled` flag rather than an abort, because a stale response arriving
  // after the visitor has left the share must not repaint it.
  useEffect(() => {
    if (loadToken === 0) {
      return
    }

    let canceled = false

    const load = async (): Promise<void> => {
      setIsLoading(true)
      try {
        const view = await fetchSharedView()
        if (canceled) {
          return
        }
        setShare(view.share)
        setPositions(view.positions)
        setStage('open')
        setError(null)
      } catch (caught) {
        if (canceled) {
          return
        }

        // 401 means the share cookie is absent or expired; 404 means the share
        // itself has ended. Neither is an error to shout about on a page
        // somebody was sent by a friend.
        if (caught instanceof ApiError && caught.status === 401) {
          setStage('locked')
          if (tokenRef.current === null) {
            setNeedsOriginalLink(true)
          }
          return
        }

        if (caught instanceof ApiError && caught.status === 404) {
          setStage('locked')
          setError(t('share:visitor.expiredSession'))
          return
        }

        setError(describeError(caught, t('share:visitor.loadFailed')))
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadToken, refresh.token])

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()

    const token: string | null = tokenRef.current
    if (token === null) {
      setNeedsOriginalLink(true)
      return
    }

    setIsSubmitting(true)
    setError(null)
    try {
      const session = await redeemShareLink(token, passphrase)
      setShare(session)
      // The code is correct and no longer needed: the cookie carries the session
      // from here. Clearing it means a wrong code is not left on screen either.
      setPassphrase('')
      // Bumping the counter is the whole handoff — the effect above sees it and
      // fetches the positions, so there is exactly one place that reads a share.
      setLoadToken((current) => current + 1)
    } catch (caught) {
      setError(describeError(caught, t('share:visitor.invalidLink')))
    } finally {
      setIsSubmitting(false)
    }
  }

  async function handleLeave(): Promise<void> {
    await leaveShare()
    setStage('locked')
    setShare(null)
    setPositions([])
    // Back to "do not read", so an auto-refresh tick that is still running does
    // not quietly reopen the share the visitor just closed.
    setLoadToken(0)
    setNeedsOriginalLink(tokenRef.current === null)
  }

  return (
    <div className="legal-page">
      <header className="legal-header">
        <Link to="/" className="legal-brand" aria-label={t('common:nav.home')}>
          <img src={assetUrl('favicon.svg')} alt="" aria-hidden="true" className="legal-logo-mark" />
          <span className="legal-brand-name">{t('common:appTitle')}</span>
        </Link>

        <div className="legal-header-actions">
          <LanguageMenu />
        </div>
      </header>

      <main className="share-main">
        {stage === 'locked'
          ? renderGate()
          : renderShare()}
      </main>

      <SiteFooter />
    </div>
  )

  // The code screen. Note what it does not render: the label, the window, the
  // scope — anything at all about the share. Until the code is right, this page
  // does not admit that the link resolves to something.
  function renderGate() {
    return (
      <section className="share-gate">
        <h1 className="share-gate-title">{t('share:visitor.pageTitle')}</h1>

        {needsOriginalLink ? (
          <div className="banner banner--info">
            <span className="banner-icon" aria-hidden="true">🔗</span>
            <div className="banner-text">{t('share:visitor.needsOriginalLink')}</div>
          </div>
        ) : (
          <>
            <p className="share-gate-prompt">{t('share:visitor.prompt')}</p>

            <form className="share-gate-form" onSubmit={handleSubmit}>
              <div className="form-field">
                <label className="form-label" htmlFor="share-code">
                  {t('share:visitor.codeLabel')}
                </label>
                <input
                  id="share-code"
                  className="form-input share-code-input"
                  type="text"
                  value={passphrase}
                  onChange={(event) => setPassphrase(event.target.value)}
                  placeholder={t('share:visitor.codePlaceholder')}
                  autoComplete="off"
                  autoCapitalize="characters"
                  spellCheck={false}
                  required
                  autoFocus
                />
              </div>

              <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
                {isSubmitting ? t('share:visitor.opening') : t('share:visitor.open')}
              </button>
            </form>
          </>
        )}

        {error !== null && (
          <div className="banner banner--error">
            <span className="banner-icon" aria-hidden="true">⚠️</span>
            <div className="banner-text">{error}</div>
          </div>
        )}

        <p className="share-privacy-note">{t('share:visitor.privacyNote')}</p>
      </section>
    )
  }

  function renderShare() {
    if (share === null) {
      return null
    }

    const from = parseApiTimestamp(share.validFrom)
    const to = parseApiTimestamp(share.validUntil)

    return (
      <section className="share-view">
        <header className="share-view-header">
          <h1 className="share-view-title">{share.label}</h1>
          <p className="share-view-window">
            {t('share:visitor.window', {
              from: from === null ? share.validFrom : formatDateTime(from),
              to: to === null ? share.validUntil : formatDateTime(to),
            })}
          </p>
          <p className="share-view-scope">
            {share.scope === 'latestOnly'
              ? t('share:visitor.latestOnly')
              : t('share:visitor.fullTrack')}
          </p>
        </header>

        <div className="share-view-toolbar">
          <RefreshToolbar autoRefresh={refresh} isLoading={isLoading} />
          <button type="button" className="btn btn-quiet btn-sm" onClick={handleLeave}>
            {t('share:visitor.leave')}
          </button>
        </div>

        {error !== null && (
          <div className="banner banner--error">
            <span className="banner-icon" aria-hidden="true">⚠️</span>
            <div className="banner-text">{error}</div>
          </div>
        )}

        {positions.length === 0 ? (
          <div className="empty-state">
            <p>{t('share:visitor.noPositions')}</p>
          </div>
        ) : hasGoogleMapsKey() ? (
          /* The maps-consent gate inside DeviceMap applies to a visitor exactly
             as it does to an account holder: no request reaches Google until
             they choose. That is the whole reason this page reuses the
             component rather than drawing its own map. */
          <DeviceMap
            positions={positions}
            apiKey={runtimeConfig.googleMapsApiKey}
            fitToken={0}
          />
        ) : (
          <div className="error-state">
            <p>{t('device:map.noApiKey')}</p>
          </div>
        )}

        <p className="share-privacy-note">{t('share:visitor.privacyNote')}</p>
      </section>
    )
  }
}
