// ============================================================
// AppLayout — the persistent shell wrapping every authenticated page.
//
// Renders:
//   • A sticky top navigation bar with the app logo mark,
//     application title, language picker, and the account menu (profile
//     link and sign out).
//   • An <Outlet /> where React Router mounts the active page.
//   • A standing footer saying this is a non-commercial test project, with
//     links to the privacy policy and the legal notice. The flex column and
//     `flex: 1` on <main> are what pin it to the bottom on a short page.
//
// The header does NOT contain per-page navigation tabs — those live
// inside the individual page components so each page fully controls
// its own tab bar (e.g. the device page has Map / Positions / Settings).
// ============================================================

import { Link, Outlet } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { assetUrl } from '../services/runtimeConfig'
import { AccountMenu } from './AccountMenu'
import { LanguageMenu } from './LanguageMenu'
import { SiteFooter } from './SiteFooter'

export function AppLayout() {
  const { t } = useTranslation('common')

  return (
    <div style={{ display: 'flex', flexDirection: 'column', minHeight: '100svh' }}>
      {/* ---- Sticky top header bar ---- */}
      <header className="app-header">

        {/* Left: logo + application name + faculty sub-label */}
        <Link to="/home" className="header-brand" aria-label={t('nav.home')}>
          {/* Logo mark — same icon as the browser tab favicon. assetUrl keeps it
              loading when the app is served under a path prefix. */}
          <img
            src={assetUrl('favicon.svg')}
            alt=""
            aria-hidden="true"
            className="header-logo-mark"
          />

          <div className="header-brand-text">
            <span className="header-app-name">{t('appTitle')}</span>
            <span className="header-faculty">{t('appSubtitle')}</span>
          </div>
        </Link>

        {/* Right: language picker + account menu (profile, sign out) */}
        <div className="header-user">
          <LanguageMenu />
          <AccountMenu />
        </div>
      </header>

      {/* ---- Page content area — React Router mounts the active page here ---- */}
      <main style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <Outlet />
      </main>

      {/* ---- What this is, on every page ---- */}
      <SiteFooter />
    </div>
  )
}
