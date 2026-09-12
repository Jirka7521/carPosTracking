// ---------------------------------------------------------------------------
// ShareLinkManager — the creator's side of temporary share links.
//
// Rendered inside the device settings tab, next to the account-to-account
// sharing roster. The two look similar and differ in one important way: an
// Access grant names somebody who has an account, while a share link names
// nobody at all. That is what makes the "shown once" panel below the most
// important part of this component — once the creator navigates away, neither
// the link nor its code can be produced again by anyone, including the server.
//
// The advice to send the code by a different channel from the link is not
// decoration. Two secrets pasted into the same message are one secret, and the
// whole design rests on them travelling separately.
// ---------------------------------------------------------------------------

import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { ShareLinkCreatedDto, ShareLinkDto, ShareScope } from '../services/apiTypes'
import {
  createShareLink,
  fetchShareLinks,
  reissueShareLink,
  revokeShareLink,
  updateShareLink,
} from '../services/apiClient'
import { BASE_PATH } from '../services/runtimeConfig'
import { formatDateTime } from '../i18n/format'
import { datetimeLocalToIso, formatDateTimeLocal, formatRelativeTime, parseApiTimestamp } from '../utils/dates'
import { describeError } from '../utils/errors'

type ShareLinkManagerProps = {
  deviceId: string
  // False when the caller may see the sharing section but not act in it. The
  // server re-checks CanShare on every call regardless; this only decides what
  // is worth rendering.
  canShare: boolean
}

// A set of secrets being shown for the only time they will ever be visible, and
// which of the two occasions produced them. A reissue needs different wording: it
// has just stopped the previous link working, and saying nothing about that would
// leave the creator wondering why their recipient went quiet.
type RevealedSecrets = {
  link: ShareLinkCreatedDto
  isReissue: boolean
}

// The window a new link starts with. Two hours from now is the "I am on my way,
// watch me arrive" case, which is what most of these are for; anything longer is
// a deliberate act rather than a default.
const DEFAULT_WINDOW_HOURS = 2

function defaultWindow(): { from: string; to: string } {
  const now = new Date()
  const later = new Date(now.getTime() + DEFAULT_WINDOW_HOURS * 60 * 60 * 1000)

  return { from: formatDateTimeLocal(now), to: formatDateTimeLocal(later) }
}

export function ShareLinkManager({ deviceId, canShare }: ShareLinkManagerProps) {
  const { t } = useTranslation(['share', 'common'])

  const [links, setLinks] = useState<ShareLinkDto[]>([])
  // Seeded from the permission rather than flipped by an effect: a caller who
  // may not share never starts a load, so "loading" would be a lie for them from
  // the first paint.
  const [isLoading, setIsLoading] = useState<boolean>(canShare)
  const [error, setError] = useState<string | null>(null)
  // Bumped by anything that has changed the list — creating a link, revoking
  // one. The effect below owns the fetch; the handlers only say "again".
  const [reloadToken, setReloadToken] = useState<number>(0)

  const [isFormOpen, setIsFormOpen] = useState<boolean>(false)
  // Which link the open form is editing, or null when it is creating one. The
  // same form serves both: the fields are identical, and duplicating it would be
  // two places for a disclosure control to drift.
  const [editing, setEditing] = useState<ShareLinkDto | null>(null)
  const [window_, setWindow] = useState<{ from: string; to: string }>(defaultWindow)
  const [label, setLabel] = useState<string>('')
  const [scope, setScope] = useState<ShareScope>('latestOnly')
  const [includeSpeed, setIncludeSpeed] = useState<boolean>(false)
  const [includeTelemetry, setIncludeTelemetry] = useState<boolean>(false)
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false)

  // The one-time reveal. Held in component state and nowhere else — not in
  // localStorage, not in a ref that outlives the panel — so it is gone the
  // moment the creator dismisses it or leaves the page.
  //
  // The secrets and "which kind of reveal this is" travel as one value rather than
  // two pieces of state: they change together at six call sites, and a reissue
  // wearing a freshly-created link's wording would omit the one warning that
  // matters — that somebody's access has just been broken.
  const [created, setCreated] = useState<RevealedSecrets | null>(null)
  const [copied, setCopied] = useState<'link' | 'code' | null>(null)

  const [confirmingRevoke, setConfirmingRevoke] = useState<string | null>(null)
  const [confirmingReissue, setConfirmingReissue] = useState<string | null>(null)

  useEffect(() => {
    if (!canShare) {
      return
    }

    let canceled = false

    const load = async (): Promise<void> => {
      setIsLoading(true)
      try {
        const fetched = await fetchShareLinks(deviceId)
        if (!canceled) {
          setLinks(fetched)
          setError(null)
        }
      } catch (caught) {
        if (!canceled) {
          setError(describeError(caught, t('share:manage.loadFailed')))
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canShare, deviceId, reloadToken])

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()

    const validFrom: string | undefined = datetimeLocalToIso(window_.from)
    const validUntil: string | undefined = datetimeLocalToIso(window_.to)

    if (validFrom === undefined || validUntil === undefined) {
      return
    }

    const trimmedLabel: string | undefined = label.trim().length > 0 ? label.trim() : undefined

    setIsSubmitting(true)
    setError(null)
    try {
      if (editing !== null) {
        // No secrets in this payload and none in the response: an edit leaves the
        // link and code exactly as they were, which is the whole reason to edit
        // rather than revoke and reissue.
        await updateShareLink(editing.id, {
          label: trimmedLabel,
          validFrom,
          validUntil,
          scope,
          includeSpeed,
          includeTelemetry,
        })
      } else {
        setCreated({
          link: await createShareLink({
            deviceId,
            label: trimmedLabel,
            validFrom,
            validUntil,
            scope,
            includeSpeed,
            includeTelemetry,
          }),
          isReissue: false,
        })
      }

      closeForm()
      setReloadToken((current) => current + 1)
    } catch (caught) {
      setError(describeError(
        caught,
        editing !== null ? t('share:form.updateFailed') : t('share:form.createFailed'),
      ))
    } finally {
      setIsSubmitting(false)
    }
  }

  function openCreateForm(): void {
    setWindow(defaultWindow())
    setLabel('')
    setScope('latestOnly')
    setIncludeSpeed(false)
    setIncludeTelemetry(false)
    setEditing(null)
    setCreated(null)
    setIsFormOpen(true)
  }

  // Pre-fills the form from an existing link. The stored bounds are UTC ISO
  // strings and the inputs are `datetime-local`, so they go back through the same
  // pair of helpers the rest of the app uses rather than being sliced by hand.
  function openEditForm(link: ShareLinkDto): void {
    const from = parseApiTimestamp(link.validFrom)
    const to = parseApiTimestamp(link.validUntil)
    const fallback = defaultWindow()

    setWindow({
      from: from === null ? fallback.from : formatDateTimeLocal(from),
      to: to === null ? fallback.to : formatDateTimeLocal(to),
    })
    setLabel(link.label)
    setScope(link.scope)
    setIncludeSpeed(link.includeSpeed)
    setIncludeTelemetry(link.includeTelemetry)
    setEditing(link)
    // The one-time secrets belong to whichever link was just created; leaving them
    // on screen next to a different link's form invites pasting the wrong pair.
    setCreated(null)
    setIsFormOpen(true)
  }

  function closeForm(): void {
    setIsFormOpen(false)
    setEditing(null)
  }

  async function handleReissue(shareId: string): Promise<void> {
    setIsSubmitting(true)
    setError(null)
    try {
      setCreated({ link: await reissueShareLink(shareId), isReissue: true })
      setConfirmingReissue(null)
      setReloadToken((current) => current + 1)
    } catch (caught) {
      setError(describeError(caught, t('share:row.reissueFailed')))
    } finally {
      setIsSubmitting(false)
    }
  }

  async function handleRevoke(shareId: string): Promise<void> {
    try {
      await revokeShareLink(shareId)
      setConfirmingRevoke(null)
      setReloadToken((current) => current + 1)
    } catch (caught) {
      setError(describeError(caught, t('share:row.revokeFailed')))
    }
  }

  // The page composes the URL rather than the server, which keeps a "public base
  // URL" setting out of the deployment — one more thing to get wrong behind the
  // path prefix, and wrong in a way that produces links nobody can open.
  function shareUrl(token: string): string {
    return `${globalThis.location.origin}${BASE_PATH}/share/${token}`.replace(
      `${globalThis.location.origin}//`,
      `${globalThis.location.origin}/`,
    )
  }

  async function copy(value: string, which: 'link' | 'code'): Promise<void> {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(which)
      globalThis.setTimeout(() => setCopied(null), 2000)
    } catch {
      // Clipboard access can be refused (an insecure origin, a permissions
      // policy). The value is on screen and selectable either way, so there is
      // nothing to report and nothing to recover.
    }
  }

  if (!canShare) {
    return (
      <div className="banner banner--info" role="status">
        {t('share:manage.noPermission')}
      </div>
    )
  }

  return (
    <div className="share-manager">
      <p className="hint">{t('share:manage.intro')}</p>

      {error !== null && (
        <div className="banner banner--error" role="alert">{error}</div>
      )}

      {created !== null && renderCreated(created)}

      {isFormOpen ? (
        renderForm()
      ) : (
        <button type="button" className="btn btn-secondary" onClick={openCreateForm}>
          {t('share:manage.createButton')}
        </button>
      )}

      <div className="share-list">
        <p className="info-label">{t('share:manage.listHeading')}</p>

        {isLoading ? (
          <div className="loading-state"><span className="spinner" aria-hidden="true" /></div>
        ) : links.length === 0 ? (
          <p className="hint">{t('share:manage.empty')}</p>
        ) : (
          links.map((link) => renderRow(link))
        )}
      </div>
    </div>
  )

  function renderCreated(result: RevealedSecrets) {
    const url: string = shareUrl(result.link.token)

    return (
      <div className="share-created">
        <h4 className="share-created-title">
          {result.isReissue ? t('share:created.reissuedTitle') : t('share:created.title')}
        </h4>

        <div className="banner banner--warning" role="status">
          <span className="banner-icon" aria-hidden="true">⚠️</span>
          <div className="banner-text">
            {result.isReissue ? t('share:created.reissuedWarning') : t('share:created.warning')}
          </div>
        </div>

        <div className="form-field">
          <span className="form-label">{t('share:created.linkLabel')}</span>
          <code className="share-secret">{url}</code>
          <button type="button" className="btn btn-quiet btn-sm" onClick={() => void copy(url, 'link')}>
            {copied === 'link' ? t('share:created.copied') : t('share:created.copyLink')}
          </button>
        </div>

        <div className="form-field">
          <span className="form-label">{t('share:created.codeLabel')}</span>
          <code className="share-secret share-secret--code">{result.link.passphrase}</code>
          <button type="button" className="btn btn-quiet btn-sm" onClick={() => void copy(result.link.passphrase, 'code')}>
            {copied === 'code' ? t('share:created.copied') : t('share:created.copyCode')}
          </button>
        </div>

        <div className="banner banner--info" role="status">
          <span className="banner-icon" aria-hidden="true">💬</span>
          <div className="banner-text">{t('share:created.channelAdvice')}</div>
        </div>

        <button type="button" className="btn btn-secondary btn-sm" onClick={() => setCreated(null)}>
          {t('share:created.done')}
        </button>
      </div>
    )
  }

  function renderForm() {
    const isEditing: boolean = editing !== null

    return (
      <form className="share-form" onSubmit={handleSubmit}>
        {isEditing && (
          <>
            <h4 className="share-created-title">{t('share:form.editTitle')}</h4>

            {/* Said plainly and before the fields, because it is the one thing
                about editing that is easy to get wrong: the recipient keeps the
                link they already have, and moving the start backwards hands them
                history they could not see a moment ago. */}
            <div className="banner banner--warning" role="status">
              <span className="banner-icon" aria-hidden="true">⚠️</span>
              <div className="banner-text">{t('share:form.editNotice')}</div>
            </div>
          </>
        )}

        <div className="form-field">
          <label className="form-label" htmlFor="share-label">{t('share:form.labelField')}</label>
          <input
            id="share-label"
            className="form-input"
            type="text"
            value={label}
            maxLength={80}
            placeholder={t('share:form.labelPlaceholder')}
            onChange={(event) => setLabel(event.target.value)}
          />
          <p className="hint">{t('share:form.labelHint')}</p>
        </div>

        <div className="share-form-window">
          <div className="form-field">
            <label className="form-label" htmlFor="share-from">{t('share:form.from')}</label>
            <input
              id="share-from"
              className="form-input"
              type="datetime-local"
              value={window_.from}
              onChange={(event) => setWindow((current) => ({ ...current, from: event.target.value }))}
              required
            />
          </div>

          <div className="form-field">
            <label className="form-label" htmlFor="share-to">{t('share:form.to')}</label>
            <input
              id="share-to"
              className="form-input"
              type="datetime-local"
              value={window_.to}
              onChange={(event) => setWindow((current) => ({ ...current, to: event.target.value }))}
              required
            />
          </div>
        </div>

        <fieldset className="share-form-scope">
          <legend className="form-label">{t('share:form.scope')}</legend>

          <label className="checkbox-field">
            <input
              type="radio"
              name="share-scope"
              checked={scope === 'latestOnly'}
              onChange={() => setScope('latestOnly')}
            />
            <span>
              {t('share:form.scopeLatest')}
              <span className="hint">{t('share:form.scopeLatestHint')}</span>
            </span>
          </label>

          <label className="checkbox-field">
            <input
              type="radio"
              name="share-scope"
              checked={scope === 'fullTrack'}
              onChange={() => setScope('fullTrack')}
            />
            <span>
              {t('share:form.scopeTrack')}
              <span className="hint">{t('share:form.scopeTrackHint')}</span>
            </span>
          </label>
        </fieldset>

        <fieldset className="share-form-extras">
          <legend className="form-label">{t('share:form.extras')}</legend>

          <label className="checkbox-field">
            <input
              type="checkbox"
              checked={includeSpeed}
              onChange={(event) => setIncludeSpeed(event.target.checked)}
            />
            <span>{t('share:form.includeSpeed')}</span>
          </label>

          <label className="checkbox-field">
            <input
              type="checkbox"
              checked={includeTelemetry}
              onChange={(event) => setIncludeTelemetry(event.target.checked)}
            />
            <span>{t('share:form.includeTelemetry')}</span>
          </label>

          <p className="hint">{t('share:form.extrasHint')}</p>
        </fieldset>

        <div className="share-form-actions">
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
            {isEditing
              ? (isSubmitting ? t('common:actions.saving') : t('common:actions.saveChanges'))
              : (isSubmitting ? t('share:form.submitting') : t('share:form.submit'))}
          </button>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={closeForm}
            disabled={isSubmitting}
          >
            {t('share:manage.cancel')}
          </button>
        </div>
      </form>
    )
  }

  function renderRow(link: ShareLinkDto) {
    const from = parseApiTimestamp(link.validFrom)
    const to = parseApiTimestamp(link.validUntil)
    const isLive: boolean = link.status !== 'revoked' && link.status !== 'expired'

    return (
      <div className="share-row" key={link.id}>
        <div className="share-row-header">
          <span className="share-row-label">{link.label}</span>
          <span className={`share-badge share-badge--${link.status}`}>
            {t(`share:status.${link.status}`)}
          </span>
        </div>

        <p className="share-row-window">
          {t('share:row.window', {
            from: from === null ? link.validFrom : formatDateTime(from),
            to: to === null ? link.validUntil : formatDateTime(to),
          })}
        </p>

        <p className="share-row-usage">
          {link.successfulRedeems === 0
            ? t('share:row.neverOpened')
            : link.successfulRedeems === 1
              ? t('share:row.openedOnce', { when: formatRelativeTime(link.lastAccessedAt) })
              : t('share:row.openedTimes', {
                count: link.successfulRedeems,
                when: formatRelativeTime(link.lastAccessedAt),
              })}
        </p>

        {link.failedAttempts > 0 && (
          <p className="share-row-failed">
            {t('share:row.failedAttempts', { count: link.failedAttempts })}
          </p>
        )}

        {/* Same shape and the same classes as a schedule profile or rule card:
            .schedule-card-actions wrapping .schedule-card-buttons, Edit as
            btn-primary and the destructive one as btn-danger, both btn-sm. The
            labels come from common:actions so they read identically too.

            Editing is offered on an expired link as well as a live one: extending
            a window that simply ran out is the ordinary case, and the recipient
            already holds the link. A REVOKED link is not offered — that was a
            deliberate withdrawal, and the server refuses it too. */}
        {link.status !== 'revoked' && confirmingRevoke !== link.id && confirmingReissue !== link.id && (
          <div className="schedule-card-actions">
            <div className="schedule-card-buttons">
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => openEditForm(link)}
              >
                {t('common:actions.edit')}
              </button>

              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => { setConfirmingReissue(link.id); setCreated(null) }}
              >
                {t('share:row.reissue')}
              </button>

              {isLive && (
                <button
                  type="button"
                  className="btn btn-danger btn-sm"
                  onClick={() => setConfirmingRevoke(link.id)}
                >
                  {t('share:row.revoke')}
                </button>
              )}
            </div>
          </div>
        )}

        {/* Confirmed rather than immediate, because it is destructive in a way the
            button label cannot carry on its own: the recipient currently using
            this share loses it the moment the new pair is minted. */}
        {confirmingReissue === link.id && (
          <div className="share-row-confirm">
            <p className="share-row-confirm-text">{t('share:row.confirmReissue')}</p>
            <p className="hint">{t('share:row.confirmReissueHint')}</p>
            <div className="share-row-actions">
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => void handleReissue(link.id)}
                disabled={isSubmitting}
              >
                {t('share:row.confirmReissueYes')}
              </button>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => setConfirmingReissue(null)}
                disabled={isSubmitting}
              >
                {t('share:row.confirmNo')}
              </button>
            </div>
          </div>
        )}

        {isLive && (
          confirmingRevoke === link.id ? (
            <div className="share-row-confirm">
              <p className="share-row-confirm-text">{t('share:row.confirmRevoke')}</p>
              <p className="hint">{t('share:row.confirmRevokeHint')}</p>
              <div className="share-row-actions">
                <button
                  type="button"
                  className="btn btn-danger-solid btn-sm"
                  onClick={() => void handleRevoke(link.id)}
                >
                  {t('share:row.confirmYes')}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={() => setConfirmingRevoke(null)}
                >
                  {t('share:row.confirmNo')}
                </button>
              </div>
            </div>
          ) : null
        )}
      </div>
    )
  }
}
