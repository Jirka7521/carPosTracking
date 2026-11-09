// ============================================================
// TripRow — one trip in the Trips tab's list: when it ran, how far, how long
// and how fast on average, with a link to its detail page.
//
// The row is a link with an optional checkbox BESIDE it, never inside it: an
// interactive control nested in a link is two targets in one, and a click on
// the box would also open the trip.
//
// The figures may still be pending or absent; TripFigure shows each state.
//
// The small markers say why a trip may look odd: 🟢 still being driven, 📌 put
// where it is by hand, ⚠ the tracker went quiet for part of it.
// ============================================================

import { Link } from 'react-router-dom'
import type { To } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { formatDayMonth, formatDistance, formatDuration, formatSpeed, formatTime } from '../i18n/format'
import type { TripSummaryDto } from '../services/apiTypes'
import { parseApiTimestamp } from '../utils/dates'
import { tripShownStart } from '../utils/trips'
import { TripFigure } from './TripFigure'

export type TripRowProps = {
  trip: TripSummaryDto
  // Where the row leads: the trip's detail page, keeping the list's journey.
  to: To
  // Select mode shows the checkbox.
  selectable: boolean
  selected: boolean
  onToggleSelected: (tripId: number) => void
  // The name of the journey the trip is in, for the "All trips" view where rows
  // from many journeys sit together; null to show none.
  journeyName: string | null
}

export function TripRow({
  trip,
  to,
  selectable,
  selected,
  onToggleSelected,
  journeyName,
}: TripRowProps) {
  const { t } = useTranslation('trips')

  const start: Date | null = tripShownStart(trip)
  const end: Date | null = trip.endTime === null ? null : parseApiTimestamp(trip.endTime)
  const timeRange: string = describeTimeRange(start, end, trip.inProgress)

  return (
    <li className={`trip-row${selected ? ' trip-row--selected' : ''}`}>
      {selectable ? (
        <label className="trip-row-check">
          <input
            type="checkbox"
            checked={selected}
            onChange={() => onToggleSelected(trip.id)}
            aria-label={t('row.select', { time: timeRange })}
          />
        </label>
      ) : null}

      <Link to={to} className="trip-row-link">
        <span className="trip-row-main">
          <span className="trip-row-title">
            {trip.inProgress ? (
              <span className="trip-badge trip-badge--live">{t('row.inProgress')}</span>
            ) : null}
            {trip.name !== null ? (
              <>
                <span className="trip-row-name">{trip.name}</span>
                <span className="trip-row-time trip-row-time--secondary">{timeRange}</span>
              </>
            ) : (
              <span className="trip-row-time">{timeRange}</span>
            )}
            {trip.journeyPinned ? (
              <span
                className="trip-row-marker"
                title={trip.journeyId === null ? t('row.pinnedOutTitle') : t('row.pinnedTitle')}
                aria-label={trip.journeyId === null ? t('row.pinnedOutTitle') : t('row.pinnedTitle')}
                role="img"
              >
                📌
              </span>
            ) : null}
            {trip.hasGap || trip.endKind === 'gap' ? (
              <span
                className="trip-row-marker"
                title={trip.endKind === 'gap' ? t('row.endGapTitle') : t('row.gapTitle')}
                aria-label={trip.endKind === 'gap' ? t('row.endGapTitle') : t('row.gapTitle')}
                role="img"
              >
                ⚠
              </span>
            ) : null}
          </span>
          {journeyName !== null ? (
            <span className="trip-row-journey" title={t('row.journeyTitle', { name: journeyName })}>
              🧭 {journeyName}
            </span>
          ) : null}
        </span>

        <span className="trip-row-stats">
          <TripFigure
            pending={trip.statsPending}
            value={trip.distanceMeters === null ? null : formatDistance(trip.distanceMeters)}
            className="trip-row-distance"
          />
          <TripFigure
            pending={trip.statsPending}
            value={trip.durationSeconds === null ? null : formatDuration(trip.durationSeconds)}
          />
          <TripFigure
            pending={trip.statsPending}
            value={
              trip.averageSpeedKmph === null
                ? null
                : t('row.averageSpeed', { speed: formatSpeed(trip.averageSpeedKmph) })
            }
            title={t('row.averageSpeedTitle')}
            className="trip-row-speed"
          />
        </span>

        <span className="trip-row-chevron" aria-hidden="true">›</span>
      </Link>
    </li>
  )
}

// "14:05 → 14:47"; "14:05 → …" while the trip is still going; the end with its
// date when the trip ran past midnight, so "23:40 → 00:15" does not read as a
// trip that ended before it began.
function describeTimeRange(start: Date | null, end: Date | null, inProgress: boolean): string {
  const startText: string = start === null ? '—' : formatTime(start)
  if (inProgress || end === null) {
    return `${startText} → …`
  }
  const sameDay: boolean =
    start !== null
    && start.getFullYear() === end.getFullYear()
    && start.getMonth() === end.getMonth()
    && start.getDate() === end.getDate()
  const endText: string = sameDay ? formatTime(end) : `${formatDayMonth(end)} ${formatTime(end)}`
  return `${startText} → ${endText}`
}
