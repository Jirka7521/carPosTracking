// Route guard: renders its children only when a user is signed in. Otherwise
// it redirects to /login. Used in <Route element=...> on every protected page.
//
// While the session probe in AuthContext is still running the answer is not
// "signed out" — it is "not known yet". Redirecting then would flash the login
// page on every reload and discard the deep link the user actually opened, so
// this waits instead.

import { Navigate, useLocation } from 'react-router-dom'
import type { ReactElement } from 'react'
import { useAuth } from './useAuth'
import { SessionLoading } from '../components/SessionLoading'

export function RequireAuth({ children }: { children: ReactElement }): ReactElement {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return <SessionLoading />
  }

  if (status === 'anonymous') {
    // Preserve where the user was trying to go so we can send them back after
    // a successful login.
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  return children
}
