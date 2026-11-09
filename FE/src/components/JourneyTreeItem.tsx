// ============================================================
// JourneyTreeItem — one journey in the Trips sidebar's tree, with its
// sub-journeys beneath it when it is open.
//
// Children are rendered only while their parent is open, which is what keeps a
// car with thousands of journeys cheap to draw: a closed node costs one row,
// whatever is inside it. The count beside the name includes the sub-journeys'
// trips, so a parent that holds only sub-journeys does not read as empty.
//
// The toggle and the link are separate controls: the arrow opens and closes, the
// name opens the journey's list.
// ============================================================

import { Link } from 'react-router-dom'
import type { To } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { formatInteger } from '../i18n/format'
import type { JourneyDto } from '../services/apiTypes'
import type { JourneyTree } from '../utils/journeyTree'
import { childrenOf } from '../utils/journeyTree'

export type JourneyTreeItemProps = {
  journey: JourneyDto
  tree: JourneyTree
  // 1 for a top-level journey; sets the indent.
  level: number
  selectedId: string | null
  isExpanded: (journeyId: string) => boolean
  onToggle: (journeyId: string, expanded: boolean) => void
  linkFor: (journeyId: string) => To
  // Called when the link is followed — the sidebar folds itself away on a phone.
  onNavigate: () => void
}

export function JourneyTreeItem({
  journey,
  tree,
  level,
  selectedId,
  isExpanded,
  onToggle,
  linkFor,
  onNavigate,
}: JourneyTreeItemProps) {
  const { t } = useTranslation('trips')

  const children: readonly JourneyDto[] = childrenOf(tree, journey.id)
  const hasChildren: boolean = children.length > 0
  const expanded: boolean = hasChildren && isExpanded(journey.id)
  const selected: boolean = journey.id === selectedId

  return (
    <li className="journey-tree-item">
      <div
        className={`journey-tree-row${selected ? ' journey-tree-row--active' : ''}`}
        style={{ paddingLeft: `${(level - 1) * 14}px` }}
      >
        {hasChildren ? (
          <button
            type="button"
            className="journey-tree-toggle"
            aria-expanded={expanded}
            aria-label={
              expanded
                ? t('sidebar.collapse', { name: journey.name })
                : t('sidebar.expand', { name: journey.name })
            }
            onClick={() => onToggle(journey.id, !expanded)}
          >
            {expanded ? '▾' : '▸'}
          </button>
        ) : (
          <span className="journey-tree-toggle journey-tree-toggle--leaf" aria-hidden="true" />
        )}

        <Link
          to={linkFor(journey.id)}
          className="journey-tree-link"
          aria-current={selected ? 'page' : undefined}
          onClick={onNavigate}
        >
          <span className="journey-tree-name">
            <span aria-hidden="true">🧭 </span>
            {journey.name}
          </span>
          <span className="journey-tree-count">{formatInteger(journey.totalTripCount)}</span>
        </Link>
      </div>

      {expanded ? (
        <ul className="journey-tree-children">
          {children.map((child) => (
            <JourneyTreeItem
              key={child.id}
              journey={child}
              tree={tree}
              level={level + 1}
              selectedId={selectedId}
              isExpanded={isExpanded}
              onToggle={onToggle}
              linkFor={linkFor}
              onNavigate={onNavigate}
            />
          ))}
        </ul>
      ) : null}
    </li>
  )
}
