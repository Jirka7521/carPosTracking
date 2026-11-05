// ---------------------------------------------------------------------------
// useUiMode — the hook every component uses to ask "Normal or Expert?".
//
// Lives apart from UiModeProvider.tsx so that file can export only its
// component; see the note in context.ts.
// ---------------------------------------------------------------------------

import { useContext } from 'react'
import { UiModeContext } from './context'
import type { UiModeContextValue } from './context'

// Throws if used outside a UiModeProvider — that is always a programmer error.
export function useUiMode(): UiModeContextValue {
  const context = useContext(UiModeContext)
  if (context === null) {
    throw new Error('useUiMode must be used inside a <UiModeProvider>.')
  }
  return context
}
