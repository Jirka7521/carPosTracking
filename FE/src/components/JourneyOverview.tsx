// ============================================================
// JourneyOverview — the head of an open journey in the Trips tab: where it
// sits ("Holidays › Croatia 2026"), its date rule, its totals, and its
// sub-journeys, with Edit / Delete for readers who may organise trips.
//
// The totals (distance, time, average speed) include every sub-journey and are
// computed by the server on request from the positions — nothing is stored — so
// they are asked for separately, after the list, and fill in when ready. A
// refresh keeps the previous totals on screen while the new ones load. A very
// large journey may be more than one request is allowed to read; the server
// then says statsPending and the tiles say so rather than showing a partial sum.
//
// The list below this header shows the trips DIRECTLY in the journey; the trips
// of its sub-journeys are one click away on the chips, which carry their counts.
//
// Under the date rule sit the journey's place rules: those in force, and the
// ended ones folded away as history.
// ============================================================

import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { To } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { formatDate, formatDistance, formatDuration, formatInteger, formatSpeed } from '../i18n/format'
import { fetchJourneySummary } from '../services/apiClient'
import type { JourneyDto, JourneySummaryDto, LocationRuleDto } from '../services/apiTypes'
import { parseApiTimestamp } from '../utils/dates'
import type { JourneyTree } from '../utils/journeyTree'
import { MAX_JOURNEY_DEPTH, childrenOf, journeyPath } from '../utils/journeyTree'
import { rangeEndLastDay } from '../utils/trips'
import { LocationRuleList } from './LocationRuleList'
import { TripFigure } from './TripFigure'

export type JourneyOverviewProps = {
  deviceId: string
  journey: JourneyDto
  tree: JourneyTree
  // Changes whenever the totals should be asked for again: a refresh tick, or a
  // change the reader made.
  reloadToken: number
  canManage: boolean
  linkFor: (journeyId: string) => To
  onEdit: () => void
  onDelete: () => void
  onAddSubJourney: () => void
  // The device's place rules (every journey's); this header shows its own.
  rules: readonly LocationRuleDto[]
  onAddRule: () => void
  onEditRule: (rule: LocationRuleDto) => void
  onEndRule: (rule: LocationRuleDto) => void
}

export function JourneyOverview({
  deviceId,
  journey,
  tree,
  reloadToken,
  canManage,
  linkFor,
  onEdit,
  onDelete,
  onAddSubJourney,
  rules,
  onAddRule,
  onEditRule,
  onEndRule,
}: JourneyOverviewProps) {
  const { t } = useTranslation('trips')

  // Stored with the journey it describes, so another journey's totals never show
  // under this one's name while its own are loading.
  const [summary, setSummary] = useState<{ journeyId: string; value: JourneySummaryDto } | null>(null)
  const [failedFor, setFailedFor] = useState<string | null>(null)

  useEffect(() => {
    let canceled = false
    fetchJourneySummary(deviceId, journey.id)
      .then((value) => {
        if (!canceled) {
          setSummary({ journeyId: journey.id, value })
          setFailedFor(null)
        }
      })
      .catch(() => {
        if (!canceled) {
          setFailedFor(journey.id)
        }
      })
    return () => {
      canceled = true
    }
  }, [deviceId, journey.id, reloadToken])

  const totals: JourneySummaryDto | null = summary?.journeyId === journey.id ? summary.value : null
  const totalsPending: boolean = totals === null ? failedFor !== journey.id : totals.statsPending
  const totalsFailed: boolean = totals === null && failedFor === journey.id

  const path: JourneyDto[] = journeyPath(tree, journey.id)
  const ancestors: JourneyDto[] = path.slice(0, -1)
  const subJourneys: readonly JourneyDto[] = childrenOf(tree, journey.id)
  const canNestDeeper: boolean = path.length < MAX_JOURNEY_DEPTH

  const ownRules: LocationRuleDto[] = rules.filter((rule) => rule.journeyId === journey.id)
  const activeRules: LocationRuleDto[] = ownRules.filter((rule) => rule.endedAt === null)
  const endedRules: LocationRuleDto[] = ownRules.filter((rule) => rule.endedAt !== null)

  const rangeFrom: Date | null = journey.rangeFrom === null ? null : parseApiTimestamp(journey.rangeFrom)
  const rangeLastDay: Date | null = journey.rangeTo === null ? null : rangeEndLastDay(journey.rangeTo)

  const averageSpeed: number | null =
    totals !== null
    && totals.distanceMeters !== null
    && totals.durationSeconds !== null
    && totals.durationSeconds > 0
      ? (totals.distanceMeters / totals.durationSeconds) * 3.6
      : null

  return (
    <section className="journey-overview">
      {ancestors.length > 0 ? (
        <nav className="breadcrumb journey-overview-path" aria-label={t('journey.pathLabel')}>
          {ancestors.map((ancestor) => (
            <span key={ancestor.id} className="journey-overview-crumb">
              <Link to={linkFor(ancestor.id)}>{ancestor.name}</Link>
              <span className="breadcrumb-sep" aria-hidden="true">›</span>
            </span>
          ))}
          <span>{journey.name}</span>
        </nav>
      ) : null}

      <div className="journey-overview-head">
        <h3 className="journey-overview-title">
          <span aria-hidden="true">🧭 </span>
          {journey.name}
        </h3>
        {canManage ? (
          <div className="journey-overview-actions">
            <button type="button" className="btn btn-secondary btn-sm" onClick={onEdit}>
              ✎ {t('journey.edit')}
            </button>
            <button type="button" className="btn btn-danger btn-sm" onClick={onDelete}>
              {t('journey.delete')}
            </button>
          </div>
        ) : null}
      </div>

      <p className="journey-overview-range">
        {rangeFrom !== null && rangeLastDay !== null ? (
          <>
            <span aria-hidden="true">📅 </span>
            <strong>{t('journey.range', { from: formatDate(rangeFrom), to: formatDate(rangeLastDay) })}</strong>
            {' · '}
            {t('journey.rangeNote')}
          </>
        ) : (
          t('journey.noRange')
        )}
      </p>

      {ownRules.length > 0 || canManage ? (
        <div className="journey-overview-rules">
          <div className="journey-overview-rules-head">
            <span className="journey-overview-children-label">
              <span aria-hidden="true">📍 </span>
              {t('locationRule.sidebar')}
            </span>
            {canManage ? (
              <button type="button" className="btn btn-quiet btn-sm" onClick={onAddRule}>
                ＋ {t('locationRule.addForJourney')}
              </button>
            ) : null}
          </div>
          {activeRules.length > 0 ? (
            <LocationRuleList
              rules={activeRules}
              tree={tree}
              canManage={canManage}
              showJourney={false}
              linkFor={linkFor}
              onEdit={onEditRule}
              onEnd={onEndRule}
            />
          ) : null}
          {endedRules.length > 0 ? (
            <details className="journey-overview-rules-history">
              <summary>
                {t('locationRule.history', { count: endedRules.length, value: formatInteger(endedRules.length) })}
              </summary>
              <LocationRuleList
                rules={endedRules}
                tree={tree}
                canManage={canManage}
                showJourney={false}
                linkFor={linkFor}
                onEdit={onEditRule}
                onEnd={onEndRule}
              />
            </details>
          ) : null}
        </div>
      ) : null}

      <dl className="journey-overview-tiles">
        <div className="journey-tile">
          <dt>{t('journey.tiles.trips')}</dt>
          <dd>{formatInteger(totals?.tripCount ?? journey.totalTripCount)}</dd>
        </div>
        <div className="journey-tile">
          <dt>{t('journey.tiles.distance')}</dt>
          <dd>
            <TripFigure
              pending={totalsPending}
              value={totals?.distanceMeters == null ? null : formatDistance(totals.distanceMeters)}
            />
          </dd>
        </div>
        <div className="journey-tile">
          <dt>{t('journey.tiles.duration')}</dt>
          <dd>
            <TripFigure
              pending={totalsPending}
              value={totals?.durationSeconds == null ? null : formatDuration(totals.durationSeconds)}
            />
          </dd>
        </div>
        <div className="journey-tile">
          <dt>{t('journey.tiles.averageSpeed')}</dt>
          <dd>
            <TripFigure
              pending={totalsPending}
              value={averageSpeed === null ? null : formatSpeed(averageSpeed)}
              title={t('journey.tiles.averageSpeedTitle')}
            />
          </dd>
        </div>
      </dl>
      {totals?.statsPending ? <p className="hint">{t('journey.totalsPending')}</p> : null}
      {totalsFailed ? <p className="hint">{t('journey.totalsFailed')}</p> : null}

      {subJourneys.length > 0 || (canManage && canNestDeeper) ? (
        <div className="journey-overview-children">
          <span className="journey-overview-children-label">{t('journey.subJourneys')}</span>
          {subJourneys.map((child) => (
            <Link key={child.id} to={linkFor(child.id)} className="journey-chip">
              <span aria-hidden="true">🧭 </span>
              {child.name}
              <span className="journey-chip-count">
                {t('journey.chipCount', { count: child.totalTripCount, value: formatInteger(child.totalTripCount) })}
              </span>
            </Link>
          ))}
          {canManage && canNestDeeper ? (
            <button type="button" className="btn btn-quiet btn-sm" onClick={onAddSubJourney}>
              ＋ {t('journey.addSub')}
            </button>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
