// ============================================================
// JourneySidebar — the Trips tab's left column: "All trips", "Not in a
// journey", the car's journeys as a tree, a search over them, and "New journey".
//
// Every entry is a link that changes only the ?journey= part of the address, so
// the view is bookmarkable, the browser's Back button steps between views, and
// "← Trips" on a trip's page returns to the same one.
//
// The tree starts folded. The path to the open journey is unfolded for it, so a
// journey opened from a link or a sub-journey chip is visible in the tree; the
// reader can still fold that path by hand — an explicit toggle beats the default.
// Searching swaps the tree for a flat list of matches, each with its full path,
// which is the quickest way to one journey among thousands.
//
// On a phone the column would push the list off the screen, so it folds into a
// single button naming the current view; pressing it opens the same content
// above the list, and following a link folds it again.
// ============================================================

import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import type { To } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { formatInteger } from '../i18n/format'
import type { TripListFilter } from '../services/apiClient'
import type { JourneyTree } from '../utils/journeyTree'
import { childrenOf, journeyPath, journeyPathLabel, searchJourneys } from '../utils/journeyTree'
import { JourneyTreeItem } from './JourneyTreeItem'

// Search matches shown at once; past this the reader types more of the name.
const SEARCH_LIMIT: number = 50

export type JourneySidebarProps = {
  tree: JourneyTree
  allTripCount: number
  unassignedTripCount: number
  filter: TripListFilter
  linkFor: (filter: TripListFilter) => To
  // The current view's name, for the folded button on a phone.
  currentLabel: string
  canManage: boolean
  onNewJourney: () => void
}

export function JourneySidebar({
  tree,
  allTripCount,
  unassignedTripCount,
  filter,
  linkFor,
  currentLabel,
  canManage,
  onNewJourney,
}: JourneySidebarProps) {
  const { t } = useTranslation('trips')

  const [query, setQuery] = useState<string>('')
  const [isOpenOnPhone, setIsOpenOnPhone] = useState<boolean>(false)
  // Folds and unfolds the reader made by hand, which win over the default.
  const [toggled, setToggled] = useState<ReadonlyMap<string, boolean>>(new Map())

  const selectedId: string | null = filter !== 'all' && filter !== 'none' ? filter : null

  // The open journey's ancestors, unfolded by default so it shows in the tree.
  const openPath = useMemo<ReadonlySet<string>>(() => {
    if (selectedId === null) {
      return new Set<string>()
    }
    return new Set<string>(journeyPath(tree, selectedId).slice(0, -1).map((journey) => journey.id))
  }, [selectedId, tree])

  const isExpanded = (journeyId: string): boolean => toggled.get(journeyId) ?? openPath.has(journeyId)

  const handleToggle = (journeyId: string, expanded: boolean): void => {
    setToggled((current) => new Map(current).set(journeyId, expanded))
  }

  const closeOnPhone = (): void => setIsOpenOnPhone(false)

  const isSearching: boolean = query.trim().length > 0
  const found = useMemo(
    () => (isSearching ? searchJourneys(tree, query, SEARCH_LIMIT) : { matches: [], total: 0 }),
    [isSearching, query, tree],
  )

  const topLevel = childrenOf(tree, null)

  return (
    <aside className="journey-sidebar" aria-label={t('sidebar.title')}>
      <button
        type="button"
        className="journey-sidebar-toggle"
        aria-expanded={isOpenOnPhone}
        onClick={() => setIsOpenOnPhone((open) => !open)}
      >
        <span className="journey-sidebar-toggle-label">{currentLabel}</span>
        <span aria-hidden="true">{isOpenOnPhone ? '▴' : '▾'}</span>
      </button>

      <div className={`journey-sidebar-body${isOpenOnPhone ? ' journey-sidebar-body--open' : ''}`}>
        <h3 className="journey-sidebar-title">{t('sidebar.title')}</h3>

        <input
          type="search"
          className="form-input journey-sidebar-search"
          placeholder={t('sidebar.search')}
          aria-label={t('sidebar.searchLabel')}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          autoComplete="off"
        />

        {isSearching ? (
          <ul className="journey-tree">
            {found.matches.map((journey) => (
              <li key={journey.id} className="journey-tree-item">
                <div
                  className={`journey-tree-row${journey.id === selectedId ? ' journey-tree-row--active' : ''}`}
                >
                  <Link
                    to={linkFor(journey.id)}
                    className="journey-tree-link"
                    aria-current={journey.id === selectedId ? 'page' : undefined}
                    onClick={closeOnPhone}
                  >
                    <span className="journey-tree-name">
                      <span aria-hidden="true">🧭 </span>
                      {journeyPathLabel(tree, journey.id)}
                    </span>
                    <span className="journey-tree-count">{formatInteger(journey.totalTripCount)}</span>
                  </Link>
                </div>
              </li>
            ))}
            {found.matches.length === 0 ? (
              <li className="hint journey-sidebar-note">{t('sidebar.noMatches', { query: query.trim() })}</li>
            ) : null}
            {found.total > found.matches.length ? (
              <li className="hint journey-sidebar-note">
                {t('sidebar.moreMatches', {
                  count: found.total - found.matches.length,
                  value: formatInteger(found.total - found.matches.length),
                })}
              </li>
            ) : null}
          </ul>
        ) : (
          <>
            <ul className="journey-tree journey-tree--fixed">
              <li className="journey-tree-item">
                <div className={`journey-tree-row${filter === 'all' ? ' journey-tree-row--active' : ''}`}>
                  <Link
                    to={linkFor('all')}
                    className="journey-tree-link"
                    aria-current={filter === 'all' ? 'page' : undefined}
                    onClick={closeOnPhone}
                  >
                    <span className="journey-tree-name">
                      <span aria-hidden="true">🚗 </span>
                      {t('sidebar.allTrips')}
                    </span>
                    <span className="journey-tree-count">{formatInteger(allTripCount)}</span>
                  </Link>
                </div>
              </li>
              <li className="journey-tree-item">
                <div className={`journey-tree-row${filter === 'none' ? ' journey-tree-row--active' : ''}`}>
                  <Link
                    to={linkFor('none')}
                    className="journey-tree-link"
                    aria-current={filter === 'none' ? 'page' : undefined}
                    onClick={closeOnPhone}
                  >
                    <span className="journey-tree-name">
                      <span aria-hidden="true">📥 </span>
                      {t('sidebar.noJourney')}
                    </span>
                    <span className="journey-tree-count">{formatInteger(unassignedTripCount)}</span>
                  </Link>
                </div>
              </li>
            </ul>

            {topLevel.length > 0 ? (
              <ul className="journey-tree">
                {topLevel.map((journey) => (
                  <JourneyTreeItem
                    key={journey.id}
                    journey={journey}
                    tree={tree}
                    level={1}
                    selectedId={selectedId}
                    isExpanded={isExpanded}
                    onToggle={handleToggle}
                    linkFor={(journeyId) => linkFor(journeyId)}
                    onNavigate={closeOnPhone}
                  />
                ))}
              </ul>
            ) : (
              <p className="hint journey-sidebar-note">{t('sidebar.empty')}</p>
            )}
          </>
        )}

        {canManage ? (
          <button
            type="button"
            className="btn btn-secondary btn-sm journey-sidebar-new"
            onClick={() => {
              closeOnPhone()
              onNewJourney()
            }}
          >
            ＋ {t('sidebar.newJourney')}
          </button>
        ) : null}
      </div>
    </aside>
  )
}
