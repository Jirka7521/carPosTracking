// ============================================================
// JourneyPicker — choose one journey out of possibly thousands: a search box
// over a short list.
//
// A <select> would put all of a car's journeys (up to 10,000) in one menu with
// no way to narrow it and no room for the path that tells two "Day trips"
// apart. Instead the list shows the tree in order, indented, capped at
// PICKER_LIMIT rows; typing switches it to name matches shown with their full
// path ("Holidays › Croatia 2026 › Day trips"). The current choice is always
// named above the list, even when it is not among the rows shown.
//
// Journeys the caller rules out (too deep, or inside the journey being moved)
// stay in the list, greyed, with the reason in their tooltip — a journey that
// silently vanished would look like a missing one.
// ============================================================

import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { formatInteger } from '../i18n/format'
import type { JourneyDto } from '../services/apiTypes'
import type { JourneyTree } from '../utils/journeyTree'
import { flattenJourneyTree, journeyPathLabel, searchJourneys } from '../utils/journeyTree'

// Rows rendered at once. Enough to browse a modest tree; a large one is searched.
const PICKER_LIMIT: number = 100

export type JourneyPickerProps = {
  // The search box's id, for the caller's <label htmlFor>.
  id: string
  tree: JourneyTree
  value: string | null
  onChange: (journeyId: string | null) => void
  // Offers "no journey" as a choice, labelled with this; omitted, it is not one.
  nullLabel?: string
  // Whether a journey may be picked. Ruled-out journeys stay visible, greyed.
  isAllowed?: (journey: JourneyDto) => boolean
  // Why a ruled-out journey cannot be picked — its tooltip.
  disallowedTitle?: string
}

export function JourneyPicker({
  id,
  tree,
  value,
  onChange,
  nullLabel,
  isAllowed,
  disallowedTitle,
}: JourneyPickerProps) {
  const { t } = useTranslation('trips')
  const [query, setQuery] = useState<string>('')

  const flat = useMemo(() => flattenJourneyTree(tree), [tree])
  const isSearching: boolean = query.trim().length > 0

  const { rows, total } = useMemo(() => {
    if (isSearching) {
      const found = searchJourneys(tree, query, PICKER_LIMIT)
      return {
        rows: found.matches.map((journey) => ({
          journey,
          label: journeyPathLabel(tree, journey.id),
          level: 1,
        })),
        total: found.total,
      }
    }
    return {
      rows: flat.slice(0, PICKER_LIMIT).map((entry) => ({
        journey: entry.journey,
        label: entry.journey.name,
        level: entry.level,
      })),
      total: flat.length,
    }
  }, [flat, isSearching, query, tree])

  const currentLabel: string =
    value === null ? (nullLabel ?? '') : tree.byId.has(value) ? journeyPathLabel(tree, value) : ''
  const hidden: number = total - rows.length

  return (
    <div className="journey-picker">
      {currentLabel.length > 0 ? (
        <p className="journey-picker-current">
          {t('picker.current', { name: currentLabel })}
        </p>
      ) : null}

      <input
        id={id}
        type="search"
        className="form-input"
        placeholder={t('picker.search')}
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        autoComplete="off"
      />

      <ul className="journey-picker-list">
        {nullLabel !== undefined && !isSearching ? (
          <li>
            <button
              type="button"
              className={`journey-picker-option${value === null ? ' journey-picker-option--selected' : ''}`}
              aria-pressed={value === null}
              onClick={() => onChange(null)}
            >
              {nullLabel}
            </button>
          </li>
        ) : null}

        {rows.map(({ journey, label, level }) => {
          const allowed: boolean = isAllowed === undefined || isAllowed(journey)
          const selected: boolean = journey.id === value
          return (
            <li key={journey.id}>
              <button
                type="button"
                className={`journey-picker-option${selected ? ' journey-picker-option--selected' : ''}`}
                style={{ paddingLeft: `${10 + (level - 1) * 16}px` }}
                aria-pressed={selected}
                disabled={!allowed}
                title={allowed ? label : disallowedTitle}
                onClick={() => onChange(journey.id)}
              >
                🧭 {label}
              </button>
            </li>
          )
        })}

        {isSearching && rows.length === 0 ? (
          <li className="hint journey-picker-empty">{t('picker.noMatches')}</li>
        ) : null}
      </ul>

      {hidden > 0 ? (
        <p className="hint">{t('picker.more', { count: hidden, value: formatInteger(hidden) })}</p>
      ) : null}
    </div>
  )
}
