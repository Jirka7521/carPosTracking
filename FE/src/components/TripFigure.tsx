// ============================================================
// TripFigure — one computed figure of a trip (a distance, a duration, a speed)
// in whichever of its three states it is in:
//   • known — the formatted value;
//   • pending — the server ran out of budget for this answer and the figure is
//     being fetched on its own; a quiet "…" holds the place, so the layout does
//     not jump when it fills in;
//   • absent — the trip has no such figure at all; an em dash.
//
// The caller formats the value: this component decides only which state shows.
// ============================================================

import { useTranslation } from 'react-i18next'

export type TripFigureProps = {
  pending: boolean
  // Already formatted; null when the trip has no such figure.
  value: string | null
  title?: string
  className?: string
}

export function TripFigure({ pending, value, title, className }: TripFigureProps) {
  const { t } = useTranslation('trips')
  const classes: string = `trip-figure${className === undefined ? '' : ` ${className}`}`

  if (pending) {
    return (
      <span className={`${classes} trip-figure--pending`} aria-label={t('row.calculating')}>
        …
      </span>
    )
  }
  return (
    <span className={classes} title={title}>
      {value ?? '—'}
    </span>
  )
}
