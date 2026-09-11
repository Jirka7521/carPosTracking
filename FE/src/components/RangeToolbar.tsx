// ============================================================
// RangeToolbar — the From/To pickers, the auto-refresh toggle and the refresh
// button, shared by the Map, Positions and Charts tabs.
//
// It lived three times over, once per tab, which is how the three copies drifted
// apart. Keeping it here means the range rules — the window is chosen once on
// load and moves only when the user moves it, either by typing in the two inputs
// or by clicking one of the presets — are stated in exactly one place. Nothing
// else may rewrite it; auto-refresh in particular re-runs the same query rather
// than pushing "to" to now.
//
// The wrapper class stays with the caller: each tab has its own bar style
// (.map-controls-bar, .position-list-controls, .charts-controls-bar), and the
// `extra` slot lets a tab add its own control (the map's "Fit to positions").
//
// The refresh half now lives in RefreshToolbar, because the device header, the
// Home page and the Settings tab want exactly that control with no date range
// attached to it. What is left here is the range: the two pickers, the presets,
// and the rule that only they ever move the window.
// ============================================================

import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { AutoRefresh } from '../hooks/useAutoRefresh'
import type { DateRange } from '../utils/dates'
import { getPastHoursRange, getTodayRange } from '../utils/dates'
import { RefreshToolbar } from './RefreshToolbar'

type RangeToolbarProps = {
  range:         DateRange
  onRangeChange: (next: DateRange) => void
  autoRefresh:   AutoRefresh
  isLoading:     boolean
  // Prefixes the input ids so each tab's labels still point at their own fields
  idPrefix:      string
  // The tab's own controls-bar class
  className:     string
  // Passed straight through to RefreshToolbar, which supplies the defaults.
  refreshLabel?: string
  loadingLabel?: string
  extra?:        ReactNode
}

function RangeToolbar({
  range,
  onRangeChange,
  autoRefresh,
  isLoading,
  idPrefix,
  className,
  refreshLabel,
  loadingLabel,
  extra,
}: RangeToolbarProps) {
  const { t } = useTranslation('common')

  const fromId: string = `${idPrefix}-from`
  const toId:   string = `${idPrefix}-to`

  return (
    <div className={className}>
      {/* "From" date picker */}
      <div className="form-field">
        <label className="form-label" htmlFor={fromId}>{t('range.from')}</label>
        <input
          id={fromId}
          className="form-input"
          type="datetime-local"
          value={range.from}
          onChange={(e) => onRangeChange({ ...range, from: e.target.value })}
          style={{ width: 'auto' }}
        />
      </div>

      {/* "To" date picker. Defaults to 12 hours ahead so incoming fixes land
          inside the window; only this input and the presets below change it. */}
      <div className="form-field">
        <label className="form-label" htmlFor={toId}>{t('range.to')}</label>
        <input
          id={toId}
          className="form-input"
          type="datetime-local"
          value={range.to}
          onChange={(e) => onRangeChange({ ...range, to: e.target.value })}
          style={{ width: 'auto' }}
        />
      </div>

      {/* The windows people actually ask for, so the common case is one click
          rather than typing a date and a time into both fields.

          They carry no pressed state on purpose. The pickers above already show
          the window a preset produced, and "still active" is not a thing that
          could be true for long: the end of every preset is pinned to the
          moment it was clicked. These are jumps, not modes. */}
      <div className="range-presets">
        <button
          type="button"
          className="btn btn-quiet btn-sm"
          onClick={() => onRangeChange(getPastHoursRange(1))}
        >
          {t('range.pastHour')}
        </button>
        <button
          type="button"
          className="btn btn-quiet btn-sm"
          onClick={() => onRangeChange(getTodayRange())}
        >
          {t('range.today')}
        </button>
        {/* Spelled 24 rather than RANGE_PAST_HOURS: the label says "24 hours"
            in every language, so reading the default-window constant here would
            let a change to that constant make the label lie. That they happen
            to agree today is a coincidence worth keeping visible. */}
        <button
          type="button"
          className="btn btn-quiet btn-sm"
          onClick={() => onRangeChange(getPastHoursRange(24))}
        >
          {t('range.past24Hours')}
        </button>
      </div>

      {/* Auto-refresh toggle + manual refresh: they re-run the same query, they
          do not move the range */}
      <RefreshToolbar
        autoRefresh={autoRefresh}
        isLoading={isLoading}
        refreshLabel={refreshLabel}
        loadingLabel={loadingLabel}
      />

      {extra}
    </div>
  )
}

export default RangeToolbar
