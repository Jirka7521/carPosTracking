// ---------------------------------------------------------------------------
// useAuth — the hook every page uses to read the signed-in user.
//
// Lives apart from AuthContext.tsx so that file can export only its component;
// see the note in context.ts.
// ---------------------------------------------------------------------------

import { useContext } from 'react'
import { AuthContext } from './context'
import type { AuthContextValue } from './context'

// Throws if used outside an AuthProvider — that is always a programmer error.
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (context === null) {
    throw new Error('useAuth must be used inside an <AuthProvider>.')
  }
  return context
}
