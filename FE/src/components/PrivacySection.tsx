// ---------------------------------------------------------------------------
// PrivacySection — the data-subject controls, on the profile page.
//
// Three things, all of which the GDPR says a person is entitled to and which
// this project therefore does rather than describes:
//
//   1. Export my data      — Art. 15 access and Art. 20 portability
//   2. Map loading         — withdrawing the consent that lets Google be told
//                            where a tracked vehicle is
//   3. Delete my account   — Art. 17 erasure, and a real DELETE
//
// Deliberately not hidden behind an "advanced" disclosure. A right nobody can
// find is a right nobody has, and the whole argument for keeping positions
// indefinitely (see docs/PRIVACY.md) rests on these controls being obvious and
// immediate.
// ---------------------------------------------------------------------------

import { useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../auth/useAuth'
import { deleteMyAccount, exportMyData } from '../services/apiClient'
import type { AccountErasureResultDto } from '../services/apiTypes'
import { describeError } from '../utils/errors'
import { hasStandingMapsConsent, revokeMapsConsent } from '../utils/mapsConsent'

export function PrivacySection() {
  const { t } = useTranslation(['profile', 'common', 'errors'])

  return (
    <div className="settings-section">
      <div className="settings-section-header">
        <span className="settings-section-icon" aria-hidden="true">🛡️</span>
        <h3>{t('profile:privacy.title')}</h3>
      </div>

      <div className="settings-section-body">
        <p className="hint" style={{ marginBottom: 20 }}>{t('profile:privacy.intro')}</p>

        <ExportBlock />
        <MapsConsentBlock />
        <DeleteAccountBlock />
      </div>
    </div>
  )
}

// ---- 1. Export ------------------------------------------------------------

function ExportBlock() {
  const { t } = useTranslation(['profile', 'errors'])

  const [isExporting, setIsExporting] = useState<boolean>(false)
  const [errorMessage, setErrorMessage] = useState<string>('')

  async function handleExport(): Promise<void> {
    setErrorMessage('')
    setIsExporting(true)

    try {
      const { fileName, blob } = await exportMyData()

      // Saved straight from the response blob rather than through
      // downloadTextFile, which takes a string: a complete position history can
      // be tens of megabytes, and turning it into a JavaScript string first
      // would double the memory for no reason.
      const url: string = URL.createObjectURL(blob)
      const link: HTMLAnchorElement = document.createElement('a')
      link.href = url
      link.download = fileName
      document.body.appendChild(link)
      link.click()
      document.body.removeChild(link)
      URL.revokeObjectURL(url)
    } catch (error) {
      setErrorMessage(describeError(error, t('profile:privacy.export.failed')))
    } finally {
      setIsExporting(false)
    }
  }

  return (
    <div className="privacy-block">
      <h4>{t('profile:privacy.export.title')}</h4>
      <p className="hint">{t('profile:privacy.export.hint')}</p>

      {errorMessage ? (
        <p className="form-message form-message--error" role="alert">{errorMessage}</p>
      ) : null}

      <button
        type="button"
        className="btn btn-secondary btn-sm"
        onClick={() => void handleExport()}
        disabled={isExporting}
      >
        {isExporting ? t('profile:privacy.export.submitting') : t('profile:privacy.export.submit')}
      </button>
    </div>
  )
}

// ---- 2. Map loading consent ----------------------------------------------

function MapsConsentBlock() {
  const { t } = useTranslation('profile')

  const [isAllowed, setIsAllowed] = useState<boolean>(() => hasStandingMapsConsent())
  const [wasRevoked, setWasRevoked] = useState<boolean>(false)

  return (
    <div className="privacy-block">
      <h4>{t('privacy.maps.title')}</h4>
      <p className="hint">
        {isAllowed ? t('privacy.maps.allowed') : t('privacy.maps.notAllowed')}
      </p>

      {wasRevoked ? (
        <p className="form-message form-message--success" role="status">{t('privacy.maps.revoked')}</p>
      ) : null}

      {isAllowed ? (
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={() => {
            revokeMapsConsent()
            setIsAllowed(false)
            setWasRevoked(true)
          }}
        >
          {t('privacy.maps.revoke')}
        </button>
      ) : null}
    </div>
  )
}

// ---- 3. Account erasure ---------------------------------------------------

function DeleteAccountBlock() {
  const { t } = useTranslation(['profile', 'common', 'errors'])
  const { logout } = useAuth()

  const [password, setPassword] = useState<string>('')
  const [confirmation, setConfirmation] = useState<string>('')
  const [errorMessage, setErrorMessage] = useState<string>('')
  const [isDeleting, setIsDeleting] = useState<boolean>(false)
  const [result, setResult] = useState<AccountErasureResultDto | null>(null)

  // The word the user has to type. Translated, because asking a Czech speaker
  // to type an English word to confirm something irreversible is a trap rather
  // than a safeguard.
  const confirmWord: string = t('profile:privacy.delete.confirmWord')

  const canSubmit: boolean =
    password.length > 0 && confirmation.trim().toUpperCase() === confirmWord.toUpperCase()

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    setErrorMessage('')
    setIsDeleting(true)

    try {
      const erasure: AccountErasureResultDto = await deleteMyAccount(password)

      // Shown before signing out, because it is the only chance the user will
      // ever have to see what actually went — the account it describes no
      // longer exists.
      setResult(erasure)
    } catch (error) {
      setErrorMessage(describeError(error, t('profile:privacy.delete.failed')))
    } finally {
      setIsDeleting(false)
    }
  }

  // The account is gone. The server has already expired the session cookies; all
  // that is left is to drop the cached user so the app stops pretending.
  if (result) {
    return (
      <div className="privacy-block privacy-block--danger">
        <h4>{t('profile:privacy.delete.title')}</h4>

        <p className="form-message form-message--success" role="status">
          {t('profile:privacy.delete.summary', {
            devices: result.devicesDeleted,
            positions: result.positionsDeleted,
            retained: result.devicesRetained,
          })}
        </p>

        <button type="button" className="btn btn-primary btn-sm" onClick={() => void logout()}>
          {t('common:actions.signOut')}
        </button>
      </div>
    )
  }

  return (
    <div className="privacy-block privacy-block--danger">
      <h4>{t('profile:privacy.delete.title')}</h4>
      <p className="hint">{t('profile:privacy.delete.hint')}</p>
      <p className="privacy-warning">{t('profile:privacy.delete.warning')}</p>

      <form onSubmit={handleSubmit}>
        <div className="form-field" style={{ marginBottom: 12 }}>
          <label className="form-label" htmlFor="delete-password">
            {t('profile:privacy.delete.passwordLabel')}
          </label>
          <input
            id="delete-password"
            className="form-input"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
          />
        </div>

        <div className="form-field" style={{ marginBottom: 16 }}>
          <label className="form-label" htmlFor="delete-confirm">
            {t('profile:privacy.delete.confirmLabel')}
          </label>
          <input
            id="delete-confirm"
            className="form-input"
            type="text"
            value={confirmation}
            onChange={(e) => setConfirmation(e.target.value)}
            autoComplete="off"
          />
        </div>

        {errorMessage ? (
          <p className="form-message form-message--error" role="alert">{errorMessage}</p>
        ) : null}

        <button type="submit" className="btn btn-danger btn-sm" disabled={!canSubmit || isDeleting}>
          {isDeleting ? t('profile:privacy.delete.submitting') : t('profile:privacy.delete.submit')}
        </button>
      </form>
    </div>
  )
}
