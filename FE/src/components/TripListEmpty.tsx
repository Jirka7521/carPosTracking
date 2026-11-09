// ============================================================
// TripListEmpty — what the Trips list shows when the open view has no trips.
//
// An empty list means something different in each view, and each says so in
// its own words: no trips detected yet at all; every trip already in a journey;
// or a journey whose trips all sit in its sub-journeys (the list shows only the
// trips directly in it — the chips above lead to the rest).
// ============================================================

import { useTranslation } from 'react-i18next'
import type { TripListFilter } from '../services/apiClient'

export type TripListEmptyProps = {
  filter: TripListFilter
  // For a journey: whether it has sub-journeys the reader could open instead.
  hasSubJourneys: boolean
}

export function TripListEmpty({ filter, hasSubJourneys }: TripListEmptyProps) {
  const { t } = useTranslation('trips')

  if (filter === 'all') {
    return (
      <div className="empty-state">
        <span className="empty-state-icon" aria-hidden="true">🚗</span>
        <h3>{t('list.emptyAllTitle')}</h3>
        <p>{t('list.emptyAllBody')}</p>
      </div>
    )
  }
  if (filter === 'none') {
    return (
      <div className="empty-state">
        <span className="empty-state-icon" aria-hidden="true">📥</span>
        <h3>{t('list.emptyNoneTitle')}</h3>
        <p>{t('list.emptyNoneBody')}</p>
      </div>
    )
  }
  return (
    <div className="empty-state">
      <span className="empty-state-icon" aria-hidden="true">🧭</span>
      <h3>{t('list.emptyJourneyTitle')}</h3>
      <p>{hasSubJourneys ? t('list.emptyJourneyWithSubsBody') : t('list.emptyJourneyBody')}</p>
    </div>
  )
}
