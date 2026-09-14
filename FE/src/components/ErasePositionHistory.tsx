// ---------------------------------------------------------------------------
// ErasePositionHistory — permanently deletes a device's stored positions.
//
// This is the counterpart to the fact that nothing else ever does. Position
// history is kept indefinitely by design (docs/PRIVACY.md § retention): there
// is no pruning job and no TTL, so a location history ends only when somebody
// decides it should. This button is that decision.
//
// It is distinct from deleting the device, which is a SOFT delete precisely so
// the history survives. Erasing here destroys rows and keeps the device.
//
// Typed confirmation rather than a second click: the two actions sit next to
// each other in the danger zone, they are both irreversible, and the difference
// between them is not obvious from the buttons alone.
// ---------------------------------------------------------------------------

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { erasePositions } from '../services/apiClient'
import { describeError } from '../utils/errors'

interface ErasePositionHistoryProps {
  deviceId: string
  // Lets the parent reload whatever it is showing once the rows are gone.
  onErased?: () => void
}

export function ErasePositionHistory({ deviceId, onErased }: ErasePositionHistoryProps) {
  const { t } = useTranslation(['device', 'common', 'errors'])

  const [confirmation, setConfirmation] = useState<string>('')
  const [isErasing, setIsErasing] = useState<boolean>(false)
  const [errorMessage, setErrorMessage] = useState<string>('')
  const [erasedCount, setErasedCount] = useState<number | null>(null)

  // Translated, so a Czech speaker is not asked to type an English word to
  // confirm something irreversible.
  const confirmWord: string = t('device:erasePositions.confirmWord')

  const canSubmit: boolean = confirmation.trim().toUpperCase() === confirmWord.toUpperCase()

  async function handleErase(): Promise<void> {
    setErrorMessage('')
    setErasedCount(null)
    setIsErasing(true)

    try {
      // No range: the button erases the whole history. A partial erase is a
      // different, narrower thing and would need its own controls.
      const result = await erasePositions(deviceId)

      setErasedCount(result.deletedCount)
      setConfirmation('')
      onErased?.()
    } catch (error) {
      setErrorMessage(describeError(error, t('device:erasePositions.failed')))
    } finally {
      setIsErasing(false)
    }
  }

  return (
    <div className="privacy-block privacy-block--danger" style={{ marginBottom: 20 }}>
      <h4>{t('device:erasePositions.title')}</h4>
      <p className="hint">{t('device:erasePositions.hint')}</p>
      <p className="privacy-warning">{t('device:erasePositions.warning')}</p>

      {errorMessage ? (
        <div className="banner banner--error" role="alert">{errorMessage}</div>
      ) : null}

      {erasedCount !== null ? (
        <p className="form-message form-message--success" role="status">
          {t('device:erasePositions.done', { count: erasedCount })}
        </p>
      ) : null}

      <div className="form-field" style={{ marginBottom: 12 }}>
        <label className="form-label" htmlFor="erase-positions-confirm">
          {t('device:erasePositions.confirmLabel')}
        </label>
        <input
          id="erase-positions-confirm"
          className="form-input"
          type="text"
          value={confirmation}
          onChange={(e) => setConfirmation(e.target.value)}
          autoComplete="off"
        />
      </div>

      <button
        type="button"
        className="btn btn-danger"
        onClick={() => void handleErase()}
        disabled={!canSubmit || isErasing}
      >
        {isErasing ? t('device:erasePositions.submitting') : t('device:erasePositions.submit')}
      </button>
    </div>
  )
}
