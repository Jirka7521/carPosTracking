// ---------------------------------------------------------------------------
// ExportLengthDialog — the question before exporting a range that holds more rows
// than the table in front of the reader has.
//
// The Events and Positions tabs both show only a window of a long range (the
// newest thousand events; the chunks of positions paged into so far), and say so
// in a line of small print. A reader who presses "Export CSV" on such a range
// would reasonably expect the file to hold the range. Exporting only what is on
// screen would be exporting the newest rows and calling it the range, and walking
// the whole range without asking could be dozens of requests. So the export stops
// here and asks: everything, or just what the table has.
//
// The words are passed in, already translated, rather than looked up here: what
// "the rest" is called ("events", "positions") and what number the table holds
// belong to the tab that knows them, and one dialog serves both.
//
// Same overlay as ConfigOverrideDialog and for the same reasons (see there):
// Escape and a click on the backdrop both cancel, the main action takes focus on
// open, and the panel is labelled for screen readers.
// ---------------------------------------------------------------------------

import { useEffect, useId, useRef } from 'react'
import { useTranslation } from 'react-i18next'

export type ExportLengthDialogProps = {
  title: string
  // What the table has and that the range may hold more — the question's premise.
  intro: string
  // The cost of the "all" answer: that it loads the rest first, and where it stops.
  allNote: string
  // The two answers, named after what ends up in the file.
  allLabel: string
  shownLabel: string
  onExportAll: () => void
  onExportShown: () => void
  onCancel: () => void
}

export function ExportLengthDialog({
  title,
  intro,
  allNote,
  allLabel,
  shownLabel,
  onExportAll,
  onExportShown,
  onCancel,
}: ExportLengthDialogProps) {
  const { t } = useTranslation('common')

  const titleId: string = useId()
  const bodyId: string = useId()
  const exportAllRef = useRef<HTMLButtonElement>(null)

  // The whole range is what the reader came here for, so that is where focus
  // starts; Tab reaches the other two.
  useEffect(() => {
    exportAllRef.current?.focus()
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        onCancel()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  return (
    <div
      className="modal-backdrop"
      // Only a click on the backdrop itself cancels — one that started inside the
      // panel and drifted out while selecting text must not throw the choice away.
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onCancel()
        }
      }}
    >
      <div
        className="modal-panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={bodyId}
      >
        <h4 id={titleId} className="modal-title">
          {title}
        </h4>

        <div id={bodyId} className="modal-body">
          <p>{intro}</p>
          <p className="hint">{allNote}</p>
        </div>

        <div className="modal-actions">
          <button type="button" className="btn btn-secondary" onClick={onCancel}>
            {t('actions.cancel')}
          </button>
          <button type="button" className="btn btn-secondary" onClick={onExportShown}>
            {shownLabel}
          </button>
          <button
            ref={exportAllRef}
            type="button"
            className="btn btn-primary"
            onClick={onExportAll}
          >
            {allLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
