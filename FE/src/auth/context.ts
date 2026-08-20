// ---------------------------------------------------------------------------
// authContext — the context object and its value type, split out from
// AuthContext.tsx so that file exports nothing but the <AuthProvider>
// component.
//
// Vite's Fast Refresh can only hot-swap a module whose exports are all
// components; a context object or a hook sitting alongside the provider forces
// a full page reload on every edit (and loses whatever state you were testing).
// Hence three files: the context here, the provider in AuthContext.tsx, and the
// useAuth() hook in useAuth.ts.
// ---------------------------------------------------------------------------

import { createContext } from 'react'
import type { UserProfileDto } from '../services/apiTypes'

// 'loading'       — the session probe is still in flight; render nothing decisive yet.
// 'authenticated' — `currentUser` is populated.
// 'anonymous'     — no valid session; guarded routes redirect to /login.
export type AuthStatus = 'loading' | 'authenticated' | 'anonymous'

export type AuthContextValue = {
  // Null unless status is 'authenticated'.
  currentUser: UserProfileDto | null

  status: AuthStatus

  // True only once the probe has finished and found a session. Route guards
  // must check `status` too — `!isAuthenticated` is not the same as "signed out"
  // while the probe is still running.
  isAuthenticated: boolean

  // Sign in with email + password. Throws ApiError on failure; the caller
  // should catch it and render the message.
  login: (email: string, password: string) => Promise<void>

  // Create a new account and immediately sign in.
  register: (email: string, password: string, firstName: string, lastName: string) => Promise<void>

  // Ends the session server-side (the API expires the cookies) and clears local
  // state, which redirects to /login on the next render.
  logout: () => Promise<void>

  // Persist an updated user profile (e.g. after a name change) so the header
  // reflects the new name without requiring a full page reload.
  updateCurrentUser: (user: UserProfileDto) => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)
