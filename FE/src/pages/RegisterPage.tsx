// ============================================================
// RegisterPage — dedicated account registration page.
//
// Features:
//   • Same visual style as LoginPage
//   • First name, last name, email, password, confirm password
//   • Client-side validation: password length + match check
//   • After successful registration, immediately redirects to /home
//     (the API logs the user in automatically upon registration)
//   • Link back to /login for existing users
// ============================================================

import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { assetUrl } from '../services/runtimeConfig'
import { describeError } from '../utils/errors'

// Must match the backend's minimum — API will reject shorter passwords too.
const MIN_PASSWORD_LENGTH = 12

export function RegisterPage() {
  const { register } = useAuth()
  const navigate = useNavigate()

  // Form fields
  const [firstName, setFirstName] = useState<string>('')
  const [lastName, setLastName] = useState<string>('')
  const [email, setEmail] = useState<string>('')
  const [password, setPassword] = useState<string>('')
  const [passwordConfirm, setPasswordConfirm] = useState<string>('')

  // Submission state
  const [errorMessage, setErrorMessage] = useState<string>('')
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    setErrorMessage('')

    // Client-side validation — gives instant feedback before the round-trip.
    if (password.length < MIN_PASSWORD_LENGTH) {
      setErrorMessage(`Password must be at least ${MIN_PASSWORD_LENGTH} characters long.`)
      return
    }

    if (password !== passwordConfirm) {
      setErrorMessage('Passwords do not match.')
      return
    }

    setIsSubmitting(true)
    try {
      // register() calls POST /api/auth/register, stores the returned JWT,
      // and updates the auth context so the user is immediately logged in.
      await register(email, password, firstName, lastName)
      navigate('/home', { replace: true })
    } catch (error) {
      setErrorMessage(describeError(error, 'Registration failed. Please try again.'))
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

      {/* White card containing the registration form */}
      <div className="auth-card">
        <h2 className="auth-card-title">Create account</h2>
        <p className="auth-card-subtitle">
          Fill in your details below to start tracking your devices.
        </p>

        <form className="auth-form" onSubmit={handleSubmit} noValidate>
          {/* First + last name side-by-side */}
          <div className="auth-name-row">
            <div className="form-field">
              <label htmlFor="reg-firstname">First name</label>
              <input
                id="reg-firstname"
                className="form-input"
                type="text"
                value={firstName}
                onChange={(e) => setFirstName(e.target.value)}
                autoComplete="given-name"
                required
                placeholder="Jan"
              />
            </div>

            <div className="form-field">
              <label htmlFor="reg-lastname">Last name</label>
              <input
                id="reg-lastname"
                className="form-input"
                type="text"
                value={lastName}
                onChange={(e) => setLastName(e.target.value)}
                autoComplete="family-name"
                required
                placeholder="Novák"
              />
            </div>
          </div>

          {/* Email */}
          <div className="form-field">
            <label htmlFor="reg-email">Email</label>
            <input
              id="reg-email"
              className="form-input"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoComplete="email"
              required
              placeholder="you@example.com"
            />
          </div>

          {/* Password */}
          <div className="form-field">
            <label htmlFor="reg-password">
              Password <span style={{ color: 'var(--text-muted)', fontWeight: 400 }}>
                (min. {MIN_PASSWORD_LENGTH} characters)
              </span>
            </label>
            <input
              id="reg-password"
              className="form-input"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="new-password"
              required
              minLength={MIN_PASSWORD_LENGTH}
              placeholder="••••••••••••"
            />
          </div>

          {/* Confirm password — client-side match check only */}
          <div className="form-field">
            <label htmlFor="reg-password-confirm">Confirm password</label>
            <input
              id="reg-password-confirm"
              className="form-input"
              type="password"
              value={passwordConfirm}
              onChange={(e) => setPasswordConfirm(e.target.value)}
              autoComplete="new-password"
              required
              minLength={MIN_PASSWORD_LENGTH}
              placeholder="••••••••••••"
            />
          </div>

          {/* Error message from validation or the API */}
          {errorMessage ? (
            <p className="form-message form-message--error" role="alert">
              {errorMessage}
            </p>
          ) : null}

          <button
            type="submit"
            className="btn btn-primary"
            disabled={isSubmitting}
            style={{ marginTop: 4 }}
          >
            {isSubmitting ? 'Creating account…' : 'Create account'}
          </button>
        </form>

        <hr className="auth-divider" />
      </div>

      {/* Link back to login for users who already have an account */}
      <p className="auth-switch-link">
        Already have an account?{' '}
        <Link to="/login">Sign in here</Link>
      </p>
    </div>
  )
}
