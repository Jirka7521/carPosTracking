// ---------------------------------------------------------------------------
// UiModeProvider — holds the Normal / Expert choice for the whole app and
// remembers it in this browser.
//
// localStorage rather than the account, because it is a preference about how
// much of the dashboard to show on this screen, not data about the user: no
// API round-trip to read it, and nothing to migrate. The read and the write are
// both wrapped, as in CsvExportMenu — storage can be unavailable (private mode,
// blocked site data), and then Normal for this visit is a perfectly good answer.
// ---------------------------------------------------------------------------

import { useCallback, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { UI_MODE_STORAGE_KEY, UiModeContext } from './context'
import type { UiMode, UiModeContextValue } from './context'

function readStoredMode(): UiMode {
  try {
    if (window.localStorage.getItem(UI_MODE_STORAGE_KEY) === 'expert') {
      return 'expert'
    }
  } catch {
    // Unavailable storage falls through to the default.
  }
  return 'normal'
}

export function UiModeProvider({ children }: { children: ReactNode }) {
  const [mode, setModeState] = useState<UiMode>(readStoredMode)

  const setMode = useCallback((next: UiMode): void => {
    setModeState(next)
    try {
      window.localStorage.setItem(UI_MODE_STORAGE_KEY, next)
    } catch {
      // Failing to REMEMBER the choice must not stop the reader making it.
    }
  }, [])

  const value = useMemo<UiModeContextValue>(() => ({
    mode: mode,
    isExpert: mode === 'expert',
    setMode: setMode,
  }), [mode, setMode])

  return <UiModeContext.Provider value={value}>{children}</UiModeContext.Provider>
}
