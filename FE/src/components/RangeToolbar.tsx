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
// ============================================================

import type { ReactNode } from 'react'
import type { AutoRefresh } from '../hooks/useAutoRefresh'
import type { DateRange } from '../utils/dates'

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

      {/* Auto-refresh toggle: re-runs the same query, it does not move the range */}
      <label className="checkbox-field" style={{ alignSelf: 'center' }}>
        <input
          type="checkbox"
          checked={autoRefresh.enabled}
          onChange={(e) => autoRefresh.setEnabled(e.target.checked)}
        />
        <span>
          Auto-refresh
          {autoRefresh.enabled ? (
            <span className="refresh-pill" style={{ marginLeft: 8 }}>
              ↻ {autoRefresh.countdown}s
            </span>
          ) : null}
        </span>
      </label>

      {/* Manual refresh */}
      <button
        type="button"
        className="btn btn-secondary"
        onClick={autoRefresh.refreshNow}
        disabled={isLoading}
        style={{ alignSelf: 'flex-end' }}
      >
        {isLoading ? (
          <>
            <span className="spinner" style={{ width: 14, height: 14, borderWidth: 2 }} />
            {loadingLabel}
          </>
        ) : (
          refreshLabel
        )}
      </button>

      {extra}
    </div>
  )
}

export default RangeToolbar
