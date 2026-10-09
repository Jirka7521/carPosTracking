// ---------------------------------------------------------------------------
// ConfigCollapsible — one group of the settings form, folded to a single line.
//
// The form grew from seven controls to two complete sets of them plus the motion
// knobs, and laid out flat it scrolled on long past the two or three values
// anyone came to change. Each group now folds to its title and a one-line
// summary of what it is set to, so even fully closed the form still reads as a
// description of the tracker; a group only has to be opened to edit it.
//
// Built on <details>, so it opens from the keyboard and is announced as
// expandable without any ARIA of our own. Two things it adds on top:
//
//   - A group holding a change the device has not picked up yet opens itself
//     and carries a ⚠ in its header. The per-field "device still on 60 s" note
//     is inside, and a note nobody can see is a note that was never given.
//   - It opens itself when a field inside fails the browser's validation. A
//     closed <details> renders nothing, so the browser cannot focus the field it
//     wants to complain about — it gives up, and Save silently does nothing.
//     `invalid` does not bubble, hence a CAPTURE listener: it runs on the way
//     down to the field, before the browser goes to focus it, and every
//     enclosing group on that path opens.
//
// The fieldset sits INSIDE the <details>, so disabling it during a save locks
// the inputs but not the header: folding a group mid-save loses nothing.
// ---------------------------------------------------------------------------

import { useEffect, useId, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

export type ConfigCollapsibleProps = {
  title: string
  // The "every 5 minutes · deep sleep" line beside the title. Built from the
  // values being typed, like every other hint on the form, so it never shows
  // what was saved instead of what is about to be.
  summary: string
  // True when a setting inside has not reached the device yet.
  hasPending?: boolean
  defaultOpen?: boolean
  // Leave unset on a group that only holds other groups: they disable their own
  // fields, and a second disabled fieldset around them would fade them twice.
  disabled?: boolean
  children: ReactNode
}

export function ConfigCollapsible({
  title,
  summary,
  hasPending = false,
  defaultOpen = false,
  disabled = false,
  children,
}: ConfigCollapsibleProps) {
  const { t } = useTranslation(['settings'])
  const titleId: string = useId()
  const detailsRef = useRef<HTMLDetailsElement>(null)

  // Seeded once. After that it is the reader's: a background refresh must not
  // reopen a group they deliberately folded, and the ⚠ in the header is enough
  // to say a later pending change landed inside.
  const [isOpen, setIsOpen] = useState<boolean>(defaultOpen || hasPending)

  useEffect(() => {
    const details: HTMLDetailsElement | null = detailsRef.current
    if (details === null) {
      return
    }

    // Opened on the element directly, not just through state: the browser
    // focuses the invalid field as soon as this event returns, long before
    // React would get round to re-rendering.
    const openOnInvalid = (): void => {
      details.open = true
      setIsOpen(true)
    }

    details.addEventListener('invalid', openOnInvalid, true)
    return () => details.removeEventListener('invalid', openOnInvalid, true)
  }, [])

  return (
    <details
      ref={detailsRef}
      className="config-collapsible"
      open={isOpen}
      onToggle={(event) => setIsOpen(event.currentTarget.open)}
    >
      <summary className="config-collapsible-summary">
        <span id={titleId} className="config-collapsible-title">{title}</span>
        {hasPending ? (
          <span className="config-collapsible-pending">⚠ {t('config.pendingBadge')}</span>
        ) : null}
        <span className="config-collapsible-values">{summary}</span>
      </summary>

      <fieldset className="config-collapsible-body" disabled={disabled} aria-labelledby={titleId}>
        {children}
      </fieldset>
    </details>
  )
}
