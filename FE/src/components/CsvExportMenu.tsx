// ============================================================
// CsvExportMenu — the "Export CSV" button and the column-separator menu behind it.
//
// The one export control of the Positions and Events tabs. The click opens a
// small menu that asks which separator the reader's spreadsheet wants, and
// picking one IS the confirmation: onExport fires on that click, rather than
// asking a second time for something that could only ever mean "yes, the one I
// just picked". What happens next — including the "this range is longer than the
// table" question, see ExportLengthDialog — is the tab's.
//
// The menu is asked for on the way OUT rather than sitting in the toolbar: it is
// a question with an answer most of the time, and a control that is only relevant
// for two seconds should not occupy the bar all day. The answer is remembered
// under one key (CSV_DELIMITER_STORAGE_KEY), so it is the reader's spreadsheet's
// separator everywhere, not each tab's.
//
// Styling and popover behaviour are the shared `export-*` classes — one menu
// pattern in this app (see LanguageMenu).
// ============================================================

import { useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { CsvDelimiter } from '../utils/csv'
import { CSV_DELIMITER_STORAGE_KEY, CSV_DELIMITERS, isCsvDelimiter } from '../utils/csv'

export type CsvExportMenuProps = {
  // Called with the separator the reader picked. What gets exported, and whether
  // anything else is asked first, is the caller's business.
  onExport: (delimiter: CsvDelimiter) => void
  // Nothing to export yet, or an export already running.
  disabled: boolean
  // Swaps the button's label for the "Exporting…" one.
  isExporting: boolean
}

export function CsvExportMenu({ onExport, disabled, isExporting }: CsvExportMenuProps) {
  // The array form, with prefixed keys below: CSV_DELIMITERS holds its labels as
  // 'common:csv.…' keys, and t() only accepts a prefixed key when it was given a
  // list of namespaces — the string form types the keys as unprefixed.
  const { t } = useTranslation(['common'])

  // The choice is read back from storage on mount: a reader whose spreadsheet
  // wants semicolons wants them every time, not once per visit.
  const [delimiter, setDelimiter] = useState<CsvDelimiter>(() => {
    try {
      const stored: string | null = window.localStorage.getItem(CSV_DELIMITER_STORAGE_KEY)
      if (stored !== null && isCsvDelimiter(stored)) {
        return stored
      }
    } catch {
      // Storage can be unavailable (private mode, blocked cookies). The default
      // separator is a perfectly good answer.
    }
    return ','
  })

  const [isOpen, setIsOpen] = useState<boolean>(false)
  const menuRef = useRef<HTMLDivElement | null>(null)
  const triggerRef = useRef<HTMLButtonElement | null>(null)
  const headingId: string = useId()

  // A popover closes on the two things every popover closes on: a click
  // somewhere else, and Escape. Neither is a React event — the click that
  // dismisses it lands on whatever else the reader aimed at — so both are
  // listened for on the document, and only while the menu is actually open.
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
        // Escape threw focus away with the panel that had it; put it back on the
        // control the reader opened, not at the top of the document.
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

  function handlePick(next: CsvDelimiter): void {
    setIsOpen(false)
    setDelimiter(next)

    try {
      window.localStorage.setItem(CSV_DELIMITER_STORAGE_KEY, next)
    } catch {
      // Failing to REMEMBER the choice must not stop the reader making it.
    }

    // Passed along rather than read back from state: setDelimiter above has not
    // landed yet, and the export needs the separator that was just clicked.
    onExport(next)
  }

  return (
    <div className="export-menu" ref={menuRef}>
      <button
        ref={triggerRef}
        type="button"
        className="export-trigger"
        onClick={() => setIsOpen((open) => !open)}
        disabled={disabled}
        aria-haspopup="true"
        aria-expanded={isOpen}
      >
        <span className="export-trigger-icon" aria-hidden="true">⤓</span>
        {isExporting ? t('common:csv.exporting') : t('common:csv.export')}
      </button>

      {isOpen ? (
        <div className="export-menu-panel" aria-labelledby={headingId}>
          <p className="export-menu-heading" id={headingId}>
            {t('common:csv.columnSeparator')}
          </p>

          {CSV_DELIMITERS.map((option) => (
            <button
              key={option.value}
              type="button"
              className="export-menu-item"
              onClick={() => handlePick(option.value)}
            >
              {/* The tick marks the one used last time, which is the only sense
                  in which there is a "current" separator here. */}
              <span className="export-menu-check" aria-hidden="true">
                {option.value === delimiter ? '✓' : ''}
              </span>
              <span className="export-menu-label">{t(option.labelKey)}</span>
              <span className="export-menu-sample" aria-hidden="true">
                {option.sample}
              </span>
              <span className="export-menu-hint">{t(option.hintKey)}</span>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  )
}
