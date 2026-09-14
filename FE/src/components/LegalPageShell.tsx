// ---------------------------------------------------------------------------
// LegalPageShell — the frame the privacy policy and the legal notice share.
//
// Both pages are PUBLIC. That is the point of this component existing rather
// than the pages living inside AppLayout: somebody deciding whether to register
// has to be able to read what happens to their data first, and AppLayout is
// behind RequireAuth. So these pages carry their own header — logo, title,
// language picker, a way back — exactly as the login and registration pages do.
//
// "Back" goes to the browser's previous page when there is one, and to the app
// root otherwise. A reader who arrived from the registration form should land
// back on it with what they had typed intact.
// ---------------------------------------------------------------------------

import type { ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { assetUrl } from '../services/runtimeConfig'
import { LanguageMenu } from './LanguageMenu'
import { SiteFooter } from './SiteFooter'

interface LegalPageShellProps {
  title: string
  subtitle: string
  // Rendered under the subtitle, above the body — the policy version and the
  // controller's details on the privacy page, nothing on the legal notice.
  meta?: ReactNode
  // The document body. Each page writes its own sections out with literal t()
  // keys rather than looping over a table: i18next-cli's extractor can only
  // resolve a key it can see as a string, and a table walked through a prop is
  // invisible to it — which produced a junk "…" key in the catalogue and would
  // have silently deleted the whole policy on the next `npm run i18n:extract`.
  children: ReactNode
}

export function LegalPageShell({ title, subtitle, meta, children }: LegalPageShellProps) {
  const { t } = useTranslation(['legal', 'common'])
  const navigate = useNavigate()

  function goBack(): void {
    // history.length is 1 on a page opened directly in a fresh tab — following
    // a link from an email, say — where there is nothing to go back to.
    if (window.history.length > 1) {
      navigate(-1)
      return
    }

    navigate('/')
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
          <button type="button" className="btn btn-ghost btn-sm" onClick={goBack}>
            {t('legal:back')}
          </button>
        </div>
      </header>

      <main className="legal-main">
        <article className="legal-article">
          <h1>{title}</h1>
          <p className="legal-subtitle">{subtitle}</p>

          {meta}

          {children}
        </article>
      </main>

      <SiteFooter />
    </div>
  )
}
