// ============================================================
// TripDayGroup — one calendar day of the Trips tab's list: a header with the
// day and its totals ("3 trips · 54.2 km · 1 h 12 min"), then that day's rows.
//
// The rows are passed in as children rather than built here, so the tab keeps
// the one place that knows about selection and links; this component knows only
// about the day.
//
// While some trip of the day is still pending its totals are not final, and a
// trailing "…" says so instead of presenting a partial sum as the day's total.
// ============================================================

import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { formatDistance, formatDuration, formatWeekdayDate } from '../i18n/format'
import type { TripDay } from '../utils/trips'

export type TripDayGroupProps = {
  day: TripDay
  children: ReactNode
}

export function TripDayGroup({ day, children }: TripDayGroupProps) {
  const { t } = useTranslation('trips')

  const totals: string = t('day.summary', {
    count: day.trips.length,
    distance: formatDistance(day.distanceMeters),
    duration: formatDuration(day.durationSeconds),
  })

  return (
    <section className="trip-day" aria-label={formatWeekdayDate(day.date)}>
      <header className="trip-day-header">
        <h4 className="trip-day-date">{formatWeekdayDate(day.date)}</h4>
        <span className="trip-day-totals">
          {totals}
          {day.hasPending ? ' …' : ''}
        </span>
      </header>
      <ul className="trip-day-list">{children}</ul>
    </section>
  )
}
