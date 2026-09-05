// ============================================================
// AppLayout — the persistent shell wrapping every authenticated page.
//
// Renders:
//   • A sticky top navigation bar with the app logo mark,
//     application title, signed-in user's name, and logout button.
//   • An <Outlet /> where React Router mounts the active page.
//
// The header does NOT contain per-page navigation tabs — those live
// inside the individual page components so each page fully controls
// its own tab bar (e.g. the device page has Map / Positions / Settings).
// ============================================================

import { Link, NavLink, Outlet } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../auth/useAuth'
import { assetUrl } from '../services/runtimeConfig'
import { LanguageMenu } from './LanguageMenu'

export function AppLayout() {
  const { currentUser, logout } = useAuth()
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

        {/* Right: language picker + display name + profile link + logout */}
        <div className="header-user">
          <LanguageMenu />

          {currentUser ? (
            /* The name is a link to the profile page so the user can click it
               to edit their name or change their password. */
            <NavLink
              to="/profile"
              className="header-user-name"
              aria-label={t('nav.profile')}
              style={{ textDecoration: 'none', cursor: 'pointer' }}
            >
              {currentUser.firstName} {currentUser.lastName}
            </NavLink>
          ) : null}

          <button
            type="button"
            className="btn btn-ghost btn-sm"
            // Logging out is a round-trip now — the API has to expire the
            // session cookies, since this code cannot touch them itself.
            onClick={() => void logout()}
            aria-label={t('actions.signOut')}
          >
            {t('actions.signOut')}
          </button>
        </div>
      </header>

      {/* ---- Page content area — React Router mounts the active page here ---- */}
      <main style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <Outlet />
      </main>
    </div>
  )
}
