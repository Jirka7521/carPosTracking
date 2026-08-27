// ============================================================
// RangeToolbar — the From/To pickers, the auto-refresh toggle and the refresh
// button, shared by the Map, Positions and Charts tabs.
//
// It lived three times over, once per tab, which is how the three copies drifted
// apart. Keeping it here means the range rules — the window is chosen once on
// load and is only ever changed by the user typing in these two inputs — are
// stated in exactly one place.
//
// The wrapper class stays with the caller: each tab has its own bar style
// (.map-controls-bar, .position-list-controls, .charts-controls-bar), and the
// `extra` slot lets a tab add its own control (the map's "Fit to positions").
//
// The refresh half now lives in RefreshToolbar, because the device header, the
// Home page and the Settings tab want exactly that control with no date range
// attached to it. What is left here is the range: the two pickers, and the rule
// that only they ever move the window.
// ============================================================

import type { ReactNode } from 'react'
import type { AutoRefresh } from '../hooks/useAutoRefresh'
import type { DateRange } from '../utils/dates'
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
  refreshLabel = '↻ Refresh',
  loadingLabel = 'Loading…',
  extra,
}: RangeToolbarProps) {
  const fromId: string = `${idPrefix}-from`
  const toId:   string = `${idPrefix}-to`

  return (
    <div className={className}>
      {/* "From" date picker */}
      <div className="form-field">
        <label className="form-label" htmlFor={fromId}>From</label>
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
          inside the window; nothing but this input ever changes it. */}
      <div className="form-field">
        <label className="form-label" htmlFor={toId}>To</label>
        <input
          id={toId}
          className="form-input"
          type="datetime-local"
          value={range.to}
          onChange={(e) => onRangeChange({ ...range, to: e.target.value })}
          style={{ width: 'auto' }}
        />
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
