// ============================================================
// ChangePasswordSection — lets the user change their password.
//
// Used on the Profile page. Three steps:
//   1. Enter current password (proves identity — a stolen session alone
//      cannot change the password and lock the real owner out).
//   2. Enter new password (≥ 12 chars, matching the registration rule).
//   3. Confirm new password (client-side check only; server validates
//      the other rules).
//
// On success all three fields are cleared and a success banner is shown.
// ============================================================

import { useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { changePassword } from '../services/apiClient'
import { describeError } from '../utils/errors'

// Must match the server-side rule on ChangePasswordRequestDto.
const MIN_PASSWORD_LENGTH = 12

type ChangePasswordSectionProps = {
  userId: number
}

export function ChangePasswordSection({ userId }: ChangePasswordSectionProps) {
  const { t } = useTranslation(['profile', 'common', 'errors'])

  const [currentPw, setCurrentPw] = useState<string>('')
  const [newPw,     setNewPw]     = useState<string>('')
  const [confirmPw, setConfirmPw] = useState<string>('')
  const [isSaving,  setIsSaving]  = useState<boolean>(false)
  const [message,   setMessage]   = useState<string>('')
  const [isError,   setIsError]   = useState<boolean>(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    setMessage('')

    // Client-side validation gives faster, friendlier feedback than waiting
    // for the server. The server enforces the same rules independently.
    if (!currentPw) {
      setMessage(t('profile:password.currentRequired'))
      setIsError(true)
      return
    }

    if (newPw.length < MIN_PASSWORD_LENGTH) {
      setMessage(t('profile:password.tooShort', { count: MIN_PASSWORD_LENGTH }))
      setIsError(true)
      return
    }

    if (newPw !== confirmPw) {
      setMessage(t('profile:password.doNotMatch'))
      setIsError(true)
      return
    }

    setIsSaving(true)
    try {
      await changePassword(userId, {
        currentPassword: currentPw,
        newPassword:     newPw,
      })
      // Clear all fields after a successful change so the form is clean.
      setCurrentPw('')
      setNewPw('')
      setConfirmPw('')
      setMessage(t('profile:password.changed'))
      setIsError(false)
    } catch (error) {
      setMessage(describeError(error, t('errors:changePasswordFailed')))
      setIsError(true)
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div className="settings-section">
      <div className="settings-section-header">
        <span className="settings-section-icon" aria-hidden="true">🔒</span>
        <h3>{t('profile:password.title')}</h3>
      </div>

      <div className="settings-section-body">
        <p className="hint" style={{ marginBottom: 16 }}>
          {t('profile:password.hint', { count: MIN_PASSWORD_LENGTH })}
        </p>

        <form onSubmit={handleSubmit}>
          <div className="form-field" style={{ marginBottom: 12 }}>
            <label className="form-label" htmlFor="pw-current">{t('profile:password.current')}</label>
            <input
              id="pw-current"
              className="form-input"
              type="password"
              value={currentPw}
              onChange={(e) => setCurrentPw(e.target.value)}
              autoComplete="current-password"
              required
            />
          </div>

          <div className="form-field" style={{ marginBottom: 12 }}>
            <label className="form-label" htmlFor="pw-new">{t('profile:password.new')}</label>
            <input
              id="pw-new"
              className="form-input"
              type="password"
              value={newPw}
              onChange={(e) => setNewPw(e.target.value)}
              minLength={MIN_PASSWORD_LENGTH}
              autoComplete="new-password"
              required
            />
          </div>

          <div className="form-field" style={{ marginBottom: 16 }}>
            <label className="form-label" htmlFor="pw-confirm">{t('profile:password.confirm')}</label>
            <input
              id="pw-confirm"
              className="form-input"
              type="password"
              value={confirmPw}
              onChange={(e) => setConfirmPw(e.target.value)}
              minLength={MIN_PASSWORD_LENGTH}
              autoComplete="new-password"
              required
            />
          </div>

          {message ? (
            <div
              className={`banner ${isError ? 'banner--error' : 'banner--success'}`}
              role={isError ? 'alert' : 'status'}
              style={{ marginBottom: 12 }}
            >
              {message}
            </div>
          ) : null}

          <button
            type="submit"
            className="btn btn-primary"
            disabled={isSaving}
          >
            {isSaving ? t('profile:password.submitting') : t('profile:password.submit')}
          </button>
        </form>
      </div>
    </div>
  )
}
