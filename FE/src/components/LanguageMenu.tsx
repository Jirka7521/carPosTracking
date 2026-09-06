// ============================================================
// LanguageMenu — the language picker.
//
// It appears in three places, because the app has two shells: the header of
// AppLayout (every signed-in page) and the login and register pages, which
// render their own full-page shell. A visitor who cannot read English must be
// able to switch BEFORE signing in, so the picker cannot live in the header
// alone.
//
// The popover behaviour and most of its styling are the ones the CSV export
// menu in PositionListTab already established — same classes, same
// outside-click and Escape handling. There is one menu pattern in this app and
// this is it.
// ============================================================

import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { SUPPORTED_LANGUAGES } from '../i18n'

export function LanguageMenu() {
  const { t, i18n } = useTranslation('common')

  const [isOpen, setIsOpen] = useState<boolean>(false)
  const menuRef = useRef<HTMLDivElement | null>(null)
  const triggerRef = useRef<HTMLButtonElement | null>(null)

  // resolvedLanguage is what the fallback chain actually settled on, which is
  // what the reader is looking at — i18n.language can still be "cs-CZ" here.
  const activeCode: string = i18n.resolvedLanguage ?? i18n.language

  // A popover closes on the two things every popover closes on: a click
  // somewhere else, and Escape. Neither is a React event, so both are listened
  // for on the document, and only while the menu is actually open.
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
        // Escape threw focus away with the panel that had it; put it back on
        // the control the reader opened, not at the top of the document.
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

  function choose(code: string): void {
    setIsOpen(false)
    // The detector's localStorage cache records the choice; if storage is
    // unavailable the switch still applies for this page's lifetime.
    void i18n.changeLanguage(code)
    triggerRef.current?.focus()
  }

  const activeLanguage = SUPPORTED_LANGUAGES.find((language) => language.code === activeCode)

  return (
    <div className="export-menu language-menu" ref={menuRef}>
      <button
        type="button"
        ref={triggerRef}
        className="export-trigger"
        onClick={() => setIsOpen((open) => !open)}
        aria-haspopup="menu"
        aria-expanded={isOpen}
        aria-label={t('language.change')}
      >
        <span className="export-trigger-icon" aria-hidden="true">
          🌐
        </span>
        {/* The active language, in its own language — never translated. */}
        <span>{activeLanguage?.nativeName ?? activeCode}</span>
      </button>

      {isOpen ? (
        <div className="export-menu-panel" role="menu">
          <p className="export-menu-heading">{t('language.heading')}</p>

          {SUPPORTED_LANGUAGES.map((language) => {
            const isActive: boolean = language.code === activeCode
            return (
              <button
                key={language.code}
                type="button"
                role="menuitemradio"
                aria-checked={isActive}
                className="export-menu-item"
                onClick={() => choose(language.code)}
                lang={language.code}
              >
                <span className="export-menu-check" aria-hidden="true">
                  {isActive ? '✓' : ''}
                </span>
                <span className="export-menu-label">{language.nativeName}</span>
              </button>
            )
          })}
        </div>
      ) : null}
    </div>
  )
}
