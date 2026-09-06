// ============================================================
// RefreshToolbar — the auto-refresh toggle, its countdown pill and the manual
// "↻ Refresh" button.
//
// This markup started life inside RangeToolbar, which the Map, Positions and
// Charts tabs share. The device page header, the Home page and the Settings tab
// now want the same control WITHOUT the From/To pickers beside it — so the
// three-part refresh control moved down here and RangeToolbar renders it, which
// keeps one copy of the pill, one copy of the spinner state, and one place
// where the wording is decided.
//
// It owns no state: the countdown, the enabled flag and the token all live in
// useAutoRefresh, so a page can drive several loads off one timer.
// ============================================================

import { useTranslation } from 'react-i18next'
import type { AutoRefresh } from '../hooks/useAutoRefresh'

export type RefreshToolbarProps = {
  autoRefresh: AutoRefresh
  // Disables the button and swaps in a spinner. Callers pass whatever "a load
  // is in flight" means for them — which is not always the same flag that
  // blanks the page, see DevicePage.
  isLoading: boolean
  // Already-translated text. Left undefined, the two standard labels below
  // are used — a default of an English literal could not be one of them.
  refreshLabel?: string
  loadingLabel?: string
}

export function RefreshToolbar({
  autoRefresh,
  isLoading,
  refreshLabel,
  loadingLabel,
}: RefreshToolbarProps) {
  const { t } = useTranslation('common')

  return (
    <>
      {/* Auto-refresh toggle: re-runs the same query, it does not move the range */}
      <label className="checkbox-field" style={{ alignSelf: 'center' }}>
        <input
          type="checkbox"
          checked={autoRefresh.enabled}
          onChange={(e) => autoRefresh.setEnabled(e.target.checked)}
        />
        <span>
          {t('refresh.auto')}
          {autoRefresh.enabled ? (
            <span className="refresh-pill" style={{ marginLeft: 8 }}>
              ↻ {t('refresh.countdown', { seconds: autoRefresh.countdown })}
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
            {loadingLabel ?? t('states.loading')}
          </>
        ) : (
          refreshLabel ?? t('refresh.now')
        )}
      </button>
    </>
  )
}
