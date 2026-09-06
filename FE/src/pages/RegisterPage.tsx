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
import { useTranslation } from 'react-i18next'
import { useAuth } from '../auth/useAuth'
import { LanguageMenu } from '../components/LanguageMenu'
import { assetUrl } from '../services/runtimeConfig'
import { describeError } from '../utils/errors'

// Must match the backend's minimum — API will reject shorter passwords too.
const MIN_PASSWORD_LENGTH = 12

export function RegisterPage() {
  const { register } = useAuth()
  const navigate = useNavigate()
  const { t } = useTranslation(['auth', 'common', 'errors'])

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
      setErrorMessage(t('auth:validation.passwordTooShort', { count: MIN_PASSWORD_LENGTH }))
      return
    }

    if (password !== passwordConfirm) {
      setErrorMessage(t('auth:validation.passwordsDoNotMatch'))
      return
    }

    setIsSubmitting(true)
    try {
      // register() calls POST /api/auth/register, stores the returned JWT,
      // and updates the auth context so the user is immediately logged in.
      await register(email, password, firstName, lastName)
      navigate('/home', { replace: true })
    } catch (error) {
      setErrorMessage(describeError(error, t('errors:registrationFailed')))
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="auth-page">
      {/* Own shell, own picker — same reason as LoginPage. */}
      <div className="auth-language">
        <LanguageMenu />
      </div>

      {/* Branding block above the white form card */}
      <div className="auth-brand">
        <img src={assetUrl('favicon.svg')} alt="" aria-hidden="true" className="auth-logo-mark" />
        <h1>{t('common:appTitle')}</h1>
        <p className="auth-brand-subtitle">{t('common:appSubtitle')}</p>
      </div>

      {/* White card containing the registration form */}
      <div className="auth-card">
        <h2 className="auth-card-title">{t('auth:register.title')}</h2>
        <p className="auth-card-subtitle">{t('auth:register.subtitle')}</p>

        <form className="auth-form" onSubmit={handleSubmit} noValidate>
          {/* First + last name side-by-side */}
          <div className="auth-name-row">
            <div className="form-field">
              <label htmlFor="reg-firstname">{t('auth:fields.firstName')}</label>
              <input
                id="reg-firstname"
                className="form-input"
                type="text"
                value={firstName}
                onChange={(e) => setFirstName(e.target.value)}
                autoComplete="given-name"
                required
                placeholder={t('auth:fields.firstNamePlaceholder')}
              />
            </div>

            <div className="form-field">
              <label htmlFor="reg-lastname">{t('auth:fields.lastName')}</label>
              <input
                id="reg-lastname"
                className="form-input"
                type="text"
                value={lastName}
                onChange={(e) => setLastName(e.target.value)}
                autoComplete="family-name"
                required
                placeholder={t('auth:fields.lastNamePlaceholder')}
              />
            </div>
          </div>

          {/* Email */}
          <div className="form-field">
            <label htmlFor="reg-email">{t('auth:fields.email')}</label>
            <input
              id="reg-email"
              className="form-input"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoComplete="email"
              required
              placeholder={t('auth:fields.emailPlaceholder')}
            />
          </div>

          {/* Password */}
          <div className="form-field">
            <label htmlFor="reg-password">
              {t('auth:fields.password')}{' '}
              <span style={{ color: 'var(--text-muted)', fontWeight: 400 }}>
                {t('auth:fields.passwordMinHint', { count: MIN_PASSWORD_LENGTH })}
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
            <label htmlFor="reg-password-confirm">{t('auth:fields.confirmPassword')}</label>
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
            {isSubmitting ? t('auth:register.submitting') : t('auth:register.submit')}
          </button>
        </form>

        <hr className="auth-divider" />
      </div>

      {/* Link back to login for users who already have an account */}
      <p className="auth-switch-link">
        {t('auth:register.haveAccount')}{' '}
        <Link to="/login">{t('auth:register.signIn')}</Link>
      </p>
    </div>
  )
}
