// ============================================================
// LocationRuleList — place rules as a list: what each one catches (its name
// or centre, radius, starts and/or ends, since when), how many trips it places
// right now, and — for a rule in force — Edit and End for readers who may
// organise trips. An ended rule is history: read-only, with when and why it
// ended.
//
// Used twice: under an open journey (its own rules, without the journey's
// name), and on the "Place rules" view (every rule, each naming its journey).
// ============================================================

import { Link } from 'react-router-dom'
import type { To } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { formatDate, formatDistance, formatInteger } from '../i18n/format'
import type { LocationRuleDto } from '../services/apiTypes'
import { parseApiTimestamp } from '../utils/dates'
import type { JourneyTree } from '../utils/journeyTree'
import { journeyPathLabel } from '../utils/journeyTree'
import { locationRuleLabel } from '../utils/trips'

export type LocationRuleListProps = {
  rules: readonly LocationRuleDto[]
  tree: JourneyTree
  canManage: boolean
  // Name each rule's journey (with a link to it); off under a journey's own header.
  showJourney: boolean
  linkFor: (journeyId: string) => To
  onEdit: (rule: LocationRuleDto) => void
  onEnd: (rule: LocationRuleDto) => void
}

export function LocationRuleList({
  rules,
  tree,
  canManage,
  showJourney,
  linkFor,
  onEdit,
  onEnd,
}: LocationRuleListProps) {
  const { t } = useTranslation('trips')

  return (
    <ul className="location-rule-list">
      {rules.map((rule) => {
        const isEnded: boolean = rule.endedAt !== null
        const validFrom: Date | null = parseApiTimestamp(rule.validFrom)
        const endedAt: Date | null = rule.endedAt === null ? null : parseApiTimestamp(rule.endedAt)
        const from: string = validFrom === null ? '' : formatDate(validFrom)

        const matches: string =
          rule.matchStart && rule.matchEnd
            ? t('locationRule.matchBoth')
            : rule.matchStart
              ? t('locationRule.matchStartOnly')
              : t('locationRule.matchEndOnly')
        const period: string =
          endedAt === null
            ? t('locationRule.period', { from })
            : t('locationRule.periodEnded', { from, to: formatDate(endedAt) })
        const status: string =
          endedAt === null
            ? t('locationRule.inForce')
            : rule.endReason === 'journeyDeleted'
              ? t('locationRule.endedJourneyDeleted', { date: formatDate(endedAt) })
              : t('locationRule.ended', { date: formatDate(endedAt) })

        return (
          <li key={rule.id} className={`location-rule-item${isEnded ? ' location-rule-item--ended' : ''}`}>
            <div className="location-rule-main">
              <span className="location-rule-title">
                <span aria-hidden="true">📍 </span>
                {locationRuleLabel(rule)}
              </span>
              <span className="location-rule-meta">
                {[formatDistance(rule.radiusMeters), matches, period].join(' · ')}
              </span>
              {showJourney ? (
                <span className="location-rule-journey">
                  {'→ '}
                  {rule.journeyId !== null && tree.byId.has(rule.journeyId) ? (
                    <Link to={linkFor(rule.journeyId)}>🧭 {journeyPathLabel(tree, rule.journeyId)}</Link>
                  ) : (
                    t('locationRule.journeyDeleted')
                  )}
                </span>
              ) : null}
            </div>

            <div className="location-rule-side">
              <span className={`status-badge ${isEnded ? 'status-badge--inactive' : 'status-badge--active'}`}>
                {status}
              </span>
              <span className="location-rule-count">
                {t('locationRule.tripCount', { count: rule.tripCount, value: formatInteger(rule.tripCount) })}
              </span>
              {canManage && !isEnded ? (
                <span className="location-rule-actions">
                  <button type="button" className="btn btn-secondary btn-sm" onClick={() => onEdit(rule)}>
                    ✎ {t('locationRule.edit')}
                  </button>
                  <button type="button" className="btn btn-danger btn-sm" onClick={() => onEnd(rule)}>
                    {t('locationRule.end')}
                  </button>
                </span>
              ) : null}
            </div>
          </li>
        )
      })}
    </ul>
  )
}
