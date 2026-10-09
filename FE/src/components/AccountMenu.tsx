// ============================================================
// AccountMenu — the signed-in user's control in the AppLayout header.
//
// The header used to spell out the user's name and a "Sign out" button beside
// the language picker. On a phone that row did not fit: the name was hidden at
// 520px and Sign out (in Czech "Odhlásit se") still pushed the bar past the
// edge of the screen. So the whole thing is one icon now, on every screen size,
// and the name, the profile link and Sign out live in its popover.
//
// Same popover pattern as LanguageMenu — the export-menu classes, outside-click
// and Escape handling — because there is one menu pattern in this app.
// ============================================================

import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'

export function AccountMenu() {
  const { currentUser, logout } = useAuth()
  const { t } = useTranslation('common')
  const navigate = useNavigate()

  const [isOpen, setIsOpen] = useState<boolean>(false)
  const menuRef = useRef<HTMLDivElement | null>(null)
  const triggerRef = useRef<HTMLButtonElement | null>(null)

  useEffect(() => {
    if (!isOpen) {
      return
    }

    function handlePointerDown(event: MouseEvent): void {
      const menu: HTMLDivElement | null = menuRef.current
      if (menu !== null && !menu.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }

    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        setIsOpen(false)
        triggerRef.current?.focus()
      }
    }

    document.addEventListener('mousedown', handlePointerDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [isOpen])

  function openProfile(): void {
    setIsOpen(false)
    navigate('/profile')
  }

  function signOut(): void {
    setIsOpen(false)
    // Logging out is a round-trip — the API has to expire the session cookies,
    // since this code cannot touch them itself.
    void logout()
  }

  const fullName: string = currentUser
    ? `${currentUser.firstName} ${currentUser.lastName}`.trim()
    : ''

  return (
    <div className="export-menu account-menu" ref={menuRef}>
      <button
        type="button"
        ref={triggerRef}
        className="export-trigger account-trigger"
        onClick={() => setIsOpen((open) => !open)}
        aria-haspopup="menu"
        aria-expanded={isOpen}
        aria-label={t('account.menu')}
        title={fullName.length > 0 ? fullName : undefined}
      >
        {/* A plain person silhouette in currentColor, so it follows the
            trigger's dark-surface colours like the text beside it does. */}
        <svg
          className="account-trigger-icon"
          viewBox="0 0 24 24"
          width="20"
          height="20"
          aria-hidden="true"
          focusable="false"
        >
          <circle cx="12" cy="8" r="4" fill="currentColor" />
          <path d="M4 20c0-4.4 3.6-7 8-7s8 2.6 8 7" fill="currentColor" />
        </svg>
      </button>

      {isOpen ? (
        <div className="export-menu-panel" role="menu">
          {currentUser ? (
            // Who is signed in — information, not an action, so it is not a
            // menuitem and the arrow keys a screen reader offers skip it.
            <div className="account-menu-identity" role="presentation">
              <span className="account-menu-name">{fullName}</span>
              <span className="account-menu-email">{currentUser.email}</span>
            </div>
          ) : null}

          <button
            type="button"
            role="menuitem"
            className="export-menu-item"
            onClick={openProfile}
          >
            <span className="export-menu-check" aria-hidden="true">
              👤
            </span>
            <span className="export-menu-label">{t('account.profile')}</span>
          </button>

          <button
            type="button"
            role="menuitem"
            className="export-menu-item"
            onClick={signOut}
          >
            <span className="export-menu-check" aria-hidden="true">
              ⎋
            </span>
            <span className="export-menu-label">{t('actions.signOut')}</span>
          </button>
        </div>
      ) : null}
    </div>
  )
}
