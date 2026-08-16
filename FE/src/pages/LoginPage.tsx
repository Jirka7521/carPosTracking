// ============================================================
// LoginPage — dedicated sign-in page.
//
// Features:
//   • Full-page dark-blue gradient background
//   • Logo mark + app name above the form card
//   • Email + password form with client-side validation
//   • Inline error messages from the API
//   • Link to the separate RegisterPage (/register)
//   • After a successful login, returns the user to wherever they
//     were trying to go (stored by RequireAuth in location state),
//     or falls back to /home.
// ============================================================

import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { assetUrl } from '../services/runtimeConfig'
import { describeError } from '../utils/errors'

// Minimum password length — must match the RegisterPage and the backend.
const MIN_PASSWORD_LENGTH = 12

export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  // Form field state
  const [email, setEmail] = useState<string>('')
  const [password, setPassword] = useState<string>('')

  // Submission state
  const [errorMessage, setErrorMessage] = useState<string>('')
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false)

  // After login, return the user to the page they originally requested
  // (stored by RequireAuth in { state: { from: '/path' } }) or to /home.
  function getRedirectTarget(): string {
    const state = location.state as { from?: string } | null
    if (state?.from && state.from !== '/login' && state.from !== '/register') {
      return state.from
    }
    return '/home'
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    setErrorMessage('')

    // Basic client-side check — avoids a round-trip for obvious mistakes.
    if (password.length < MIN_PASSWORD_LENGTH) {
      setErrorMessage(`Password must be at least ${MIN_PASSWORD_LENGTH} characters.`)
      return
    }

    setIsSubmitting(true)
    try {
      // The session arrives as HttpOnly cookies on the response; login() puts
      // the returned profile into the auth context, which is what lets
      // RequireAuth through.
      await login(email, password)
      navigate(getRedirectTarget(), { replace: true })
    } catch (error) {
      // describeError() extracts a friendly message from ApiError or falls back.
      setErrorMessage(describeError(error, 'Login failed. Please check your credentials.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="auth-page">
      {/* Branding block above the white form card */}
      <div className="auth-brand">
        <img src={assetUrl('favicon.svg')} alt="" aria-hidden="true" className="auth-logo-mark" />
        <h1>Car Position Tracker</h1>
        <p className="auth-brand-subtitle">Vehicle tracking dashboard</p>
      </div>

      {/* White card containing the login form */}
      <div className="auth-card">
        <h2 className="auth-card-title">Sign in</h2>
        <p className="auth-card-subtitle">
          Enter your email and password to access the tracking dashboard.
        </p>

        <form className="auth-form" onSubmit={handleSubmit} noValidate>
          {/* Email field */}
          <div className="form-field">
            <label htmlFor="login-email">Email</label>
            <input
              id="login-email"
              className="form-input"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoComplete="email"
              required
              placeholder="you@example.com"
            />
          </div>

          {/* Password field */}
          <div className="form-field">
            <label htmlFor="login-password">Password</label>
            <input
              id="login-password"
              className="form-input"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              required
              minLength={MIN_PASSWORD_LENGTH}
              placeholder="••••••••••••"
            />
          </div>

          {/* Inline error message from the API or client validation */}
          {errorMessage ? (
            <p className="form-message form-message--error" role="alert">
              {errorMessage}
            </p>
          ) : null}

          {/* Submit button — disabled while the request is in flight */}
          <button
            type="submit"
            className="btn btn-primary"
            disabled={isSubmitting}
            style={{ marginTop: 4 }}
          >
            {isSubmitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>

        <hr className="auth-divider" />
      </div>

      {/* Link to the register page — shown below the card */}
      <p className="auth-switch-link">
        Don't have an account?{' '}
        <Link to="/register">Create one here</Link>
      </p>
    </div>
  )
}
