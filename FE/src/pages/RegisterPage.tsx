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
//   • A required privacy-policy acknowledgement, which is the whole point of
//     the extra round-trip below: the form fetches the version currently in
//     force and echoes it back with the registration, so what gets recorded
//     against the account is provably the text this person was shown.
// ============================================================

import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Trans, useTranslation } from 'react-i18next'
import { useAuth } from '../auth/useAuth'
import { fetchPrivacyPolicy } from '../services/apiClient'
import { LanguageMenu } from '../components/LanguageMenu'
import { SiteFooter } from '../components/SiteFooter'
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

  // Acceptance of the terms of use plus acknowledgement of the privacy policy.
  const [hasAcceptedTerms, setHasAcceptedTerms] = useState<boolean>(false)
  const [policyVersion, setPolicyVersion] = useState<string>('')

  // Submission state
  const [errorMessage, setErrorMessage] = useState<string>('')
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false)

  // The version in force, fetched rather than hard-coded so this page and the
  // consent the server records can never disagree about which policy was shown.
  useEffect(() => {
    let cancelled: boolean = false

    fetchPrivacyPolicy()
      .then((policy) => {
        if (!cancelled) {
          setPolicyVersion(policy.version)
        }
      })
      .catch(() => undefined)

    return () => {
      cancelled = true
    }
  }, [])

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

    if (!hasAcceptedTerms) {
      setErrorMessage(t('auth:register.consent.required'))
      return
    }

    // An empty version means the fetch on mount failed — a network blip, not a
    // policy change. Retry once here rather than dead-ending the form: the old
    // code sent the user off to reload with a message claiming the policy had
    // changed, which is never true in this branch. A genuinely stale version is
    // caught server-side and comes back through describeError below.
    let acceptedVersion: string = policyVersion
    if (acceptedVersion.length === 0) {
      try {
        const policy = await fetchPrivacyPolicy()
        acceptedVersion = policy.version
        setPolicyVersion(policy.version)
      } catch {
        setErrorMessage(t('auth:register.consent.unavailable'))
        return
      }
    }

    setIsSubmitting(true)
    try {
      // register() calls POST /api/auth/register, stores the returned JWT,
      // and updates the auth context so the user is immediately logged in.
      await register(email, password, firstName, lastName, acceptedVersion)
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

          {/*
            The acceptance. Its own block above the button because it is the last
            thing read before signing up, and because what is being agreed to — a
            personal project storing precise vehicle locations — is not what
            somebody filling in a sign-up form assumes.

            This tick is the only place anything becomes binding. The PolyForm
            licence in the repository reaches people who copy the source, not
            people who register here, so without this the no-warranty and
            no-liability sections of /legal would bind nobody. The version
            accepted is echoed back to the server and stamped on the user row.
          */}
          <div className="consent-block">
            <p className="consent-notice">{t('auth:register.consent.notice')}</p>

            <label className="consent-check" htmlFor="register-consent">
              <input
                id="register-consent"
                type="checkbox"
                checked={hasAcceptedTerms}
                onChange={(e) => setHasAcceptedTerms(e.target.checked)}
              />
              <span>
                <Trans
                  i18nKey="register.consent.label"
                  ns="auth"
                  components={{
                    // Both open in a new tab so a half-filled form is not lost
                    // to reading the thing the form is asking about.
                    terms: <Link to="/legal" target="_blank" rel="noopener noreferrer" />,
                    privacy: <Link to="/privacy" target="_blank" rel="noopener noreferrer" />,
                  }}
                />
              </span>
            </label>
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
            // Unchecked consent disables the button rather than only failing on
            // submit: the requirement should be visible before it is hit.
            disabled={isSubmitting || !hasAcceptedTerms}
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

      <SiteFooter />
    </div>
  )
}
