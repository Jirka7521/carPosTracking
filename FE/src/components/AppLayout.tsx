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
import { useAuth } from '../auth/AuthContext'
import { assetUrl } from '../services/runtimeConfig'

export function AppLayout() {
  const { currentUser, logout } = useAuth()

  return (
    <div style={{ display: 'flex', flexDirection: 'column', minHeight: '100svh' }}>
      {/* ---- Sticky top header bar ---- */}
      <header className="app-header">

        {/* Left: logo + application name + faculty sub-label */}
        <Link to="/home" className="header-brand" aria-label="Go to home page">
          {/* Logo mark — same icon as the browser tab favicon. assetUrl keeps it
              loading when the app is served under a path prefix. */}
          <img
            src={assetUrl('favicon.svg')}
            alt=""
            aria-hidden="true"
            className="header-logo-mark"
          />

          <div className="header-brand-text">
            <span className="header-app-name">Car Position Tracker</span>
            <span className="header-faculty">Vehicle tracking dashboard</span>
          </div>
        </Link>

        {/* Right: display name + profile link + logout */}
        <div className="header-user">
          {currentUser ? (
            /* The name is a link to the profile page so the user can click it
               to edit their name or change their password. */
            <NavLink
              to="/profile"
              className="header-user-name"
              aria-label="Edit your profile"
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
            aria-label="Sign out"
          >
            Sign out
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
