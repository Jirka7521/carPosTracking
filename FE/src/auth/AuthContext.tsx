// ---------------------------------------------------------------------------
// AuthContext — a single React context that holds the currently signed-in
// user and exposes login / register / logout helpers. Every page that needs
// to know "who is the user?" reads it via the `useAuth()` hook below.
//
// Session model: the JWT lives in an HttpOnly cookie the browser attaches
// automatically. That is much safer than localStorage — no script can read it,
// so an XSS bug cannot walk off with a valid session — but it also means this
// code cannot see whether a session exists. So on mount we *ask*: one GET
// /api/me, whose answer decides between "signed in" and "not".
//
// That asking takes a moment, which is why `status` has three values rather
// than a boolean. Treating the unknown state as "signed out" would flash the
// login page on every reload and throw away the user's deep link.
// ---------------------------------------------------------------------------

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import {
  fetchMyProfile,
  loginUser,
  logoutUser,
  registerUser,
  SESSION_EXPIRED_EVENT,
} from '../services/apiClient'
import type { UserProfileDto } from '../services/apiTypes'

// 'loading'       — the session probe is still in flight; render nothing decisive yet.
// 'authenticated' — `currentUser` is populated.
// 'anonymous'     — no valid session; guarded routes redirect to /login.
export type AuthStatus = 'loading' | 'authenticated' | 'anonymous'

type AuthContextValue = {
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

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [currentUser, setCurrentUser] = useState<UserProfileDto | null>(null)
  const [status, setStatus] = useState<AuthStatus>('loading')

  // The session probe. Runs once on mount: with an HttpOnly cookie there is no
  // synchronous way to answer "am I signed in?", so the first render is always
  // 'loading' and the answer arrives one round-trip later.
  useEffect(() => {
    let canceled = false

    const probe = async (): Promise<void> => {
      try {
        const user = await fetchMyProfile()
        if (!canceled) {
          setCurrentUser(user)
          setStatus('authenticated')
        }
      } catch {
        // Any failure — 401, network, anything — means we cannot prove there is
        // a session, so treat the user as signed out. A network outage shows the
        // login page, which is honest: nothing else would work either.
        if (!canceled) {
          setCurrentUser(null)
          setStatus('anonymous')
        }
      }
    }

    void probe()
    return () => {
      canceled = true
    }
  }, [])

  // The API client raises this whenever any call comes back 401 — a session that
  // expired mid-visit. Handling it centrally means no page has to recognise an
  // expired session for itself.
  useEffect(() => {
    const onSessionExpired = (): void => {
      setCurrentUser(null)
      setStatus('anonymous')
    }

    window.addEventListener(SESSION_EXPIRED_EVENT, onSessionExpired)
    return () => {
      window.removeEventListener(SESSION_EXPIRED_EVENT, onSessionExpired)
    }
  }, [])

  const login = useCallback(async (email: string, password: string): Promise<void> => {
    const response = await loginUser(email, password)
    // The session cookies rode along on that response; only the profile needs
    // to be put into state.
    setCurrentUser(response.user)
    setStatus('authenticated')
  }, [])

  const register = useCallback(async (
    email: string,
    password: string,
    firstName: string,
    lastName: string,
  ): Promise<void> => {
    const response = await registerUser(email, password, firstName, lastName)
    setCurrentUser(response.user)
    setStatus('authenticated')
  }, [])

  const logout = useCallback(async (): Promise<void> => {
    try {
      await logoutUser()
    } catch {
      // The server call is what actually ends the session, but if it fails there
      // is nothing useful to tell the user — and refusing to clear local state
      // would be the worse outcome on a shared machine.
    }
    setCurrentUser(null)
    setStatus('anonymous')
  }, [])

  // Called by the Profile page after a successful name update so the header
  // immediately reflects the new first/last name without a page reload.
  const updateCurrentUser = useCallback((user: UserProfileDto): void => {
    setCurrentUser(user)
  }, [])

  const value = useMemo<AuthContextValue>(() => ({
    currentUser: currentUser,
    status: status,
    isAuthenticated: status === 'authenticated',
    login: login,
    register: register,
    logout: logout,
    updateCurrentUser: updateCurrentUser,
  }), [currentUser, status, login, register, logout, updateCurrentUser])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// Hook for consuming the auth context. Throws if used outside an
// AuthProvider — that is always a programmer error.
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (context === null) {
    throw new Error('useAuth must be used inside an <AuthProvider>.')
  }
  return context
}
