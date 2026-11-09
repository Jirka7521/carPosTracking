// ============================================================
// TripSelectionBar — the bar that sticks to the bottom of the Trips list while
// trips are selected, with what can be done to them:
//   • Move to journey… — pins them to a journey picked in a dialog;
//   • Remove from journey — pins them to "no journey", whatever the dates say;
//   • Back to automatic — unpins them, so the journeys' date ranges decide again.
//
// It only reports what was pressed; the tab runs the request and refreshes the
// list. While a request is in flight every button is disabled, so a double
// click cannot send the same move twice.
//
// One request carries at most MAX_TRIPS_PER_ASSIGN trips (the API's limit). The
// tab refuses to select past it; the bar says so when the limit is reached.
// ============================================================

import { useTranslation } from 'react-i18next'
import { formatInteger } from '../i18n/format'
import { MAX_TRIPS_PER_ASSIGN } from '../utils/trips'

export type TripSelectionBarProps = {
  count: number
  // Whether every trip on screen is already selected — hides "Select all shown".
  allShownSelected: boolean
  isBusy: boolean
  onMove: () => void
  onRemove: () => void
  onAutomatic: () => void
  onSelectAllShown: () => void
  onClear: () => void
}

export function TripSelectionBar({
  count,
  allShownSelected,
  isBusy,
  onMove,
  onRemove,
  onAutomatic,
  onSelectAllShown,
  onClear,
}: TripSelectionBarProps) {
  const { t } = useTranslation('trips')
  const atLimit: boolean = count >= MAX_TRIPS_PER_ASSIGN

  return (
    <div className="trip-selection-bar" role="region" aria-label={t('selection.label')}>
      <span className="trip-selection-count" aria-live="polite">
        {t('selection.count', { count, value: formatInteger(count) })}
      </span>

      {atLimit ? (
        <span className="hint">{t('selection.limit', { max: formatInteger(MAX_TRIPS_PER_ASSIGN) })}</span>
      ) : !allShownSelected ? (
        <button type="button" className="btn btn-quiet btn-sm" onClick={onSelectAllShown} disabled={isBusy}>
          {t('selection.selectAllShown')}
        </button>
      ) : null}

      <div className="trip-selection-actions">
        <button
          type="button"
          className="btn btn-primary btn-sm"
          onClick={onMove}
          disabled={isBusy || count === 0}
        >
          {t('selection.move')}
        </button>
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={onRemove}
          disabled={isBusy || count === 0}
        >
          {t('selection.remove')}
        </button>
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={onAutomatic}
          disabled={isBusy || count === 0}
          title={t('selection.automaticTitle')}
        >
          {t('selection.automatic')}
        </button>
        <button
          type="button"
          className="btn btn-quiet btn-sm"
          onClick={onClear}
          disabled={isBusy}
          aria-label={t('selection.clear')}
          title={t('selection.clear')}
        >
          ✕
        </button>
      </div>
    </div>
  )
}
