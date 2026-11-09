// ============================================================
// TripNameEditor — a trip's optional name ("To grandma's"), edited in place on
// the trip's page.
//
// Trips are named by their times until someone gives them a name, so an unnamed
// trip shows only a quiet "Add a name…" — and only to readers who may organise
// the device's trips. Saving an empty name clears it. The name survives the
// server recomputing trips, as long as the trip itself does.
// ============================================================

import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { renameTrip } from '../services/apiClient'
import { describeError } from '../utils/errors'

// Mirrors TripRules.MaxNameLength on the API.
const MAX_NAME_LENGTH: number = 100

export type TripNameEditorProps = {
  deviceId: string
  tripId: number
  name: string | null
  canEdit: boolean
  onRenamed: (name: string | null) => void
}

export function TripNameEditor({ deviceId, tripId, name, canEdit, onRenamed }: TripNameEditorProps) {
  const { t } = useTranslation(['trips', 'common', 'errors'])
  const inputRef = useRef<HTMLInputElement>(null)

  const [isEditing, setIsEditing] = useState<boolean>(false)
  const [draft, setDraft] = useState<string>('')
  const [isSaving, setIsSaving] = useState<boolean>(false)
  const [errorMessage, setErrorMessage] = useState<string>('')

  useEffect(() => {
    if (isEditing) {
      inputRef.current?.focus()
    }
  }, [isEditing])

  function startEditing(): void {
    setDraft(name ?? '')
    setErrorMessage('')
    setIsEditing(true)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    const trimmed: string = draft.trim()
    setIsSaving(true)
    setErrorMessage('')
    try {
      await renameTrip(deviceId, tripId, trimmed)
      onRenamed(trimmed.length === 0 ? null : trimmed)
      setIsEditing(false)
    } catch (error) {
      setErrorMessage(describeError(error, t('errors:renameTripFailed')))
    } finally {
      setIsSaving(false)
    }
  }

  if (isEditing) {
    return (
      <form className="trip-name-form" onSubmit={(event) => void handleSubmit(event)}>
        <input
          ref={inputRef}
          className="form-input"
          aria-label={t('trips:detail.nameLabel')}
          value={draft}
          maxLength={MAX_NAME_LENGTH}
          placeholder={t('trips:detail.namePlaceholder')}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Escape' && !isSaving) {
              setIsEditing(false)
            }
          }}
          disabled={isSaving}
        />
        <button type="submit" className="btn btn-primary btn-sm" disabled={isSaving}>
          {isSaving ? t('common:actions.saving') : t('common:actions.saveChanges')}
        </button>
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={() => setIsEditing(false)}
          disabled={isSaving}
        >
          {t('common:actions.cancel')}
        </button>
        {errorMessage.length > 0 ? (
          <p className="form-message form-message--error trip-name-error" role="alert">
            {errorMessage}
          </p>
        ) : null}
      </form>
    )
  }

  if (name === null) {
    return canEdit ? (
      <button type="button" className="btn btn-quiet btn-sm trip-name-add" onClick={startEditing}>
        ✎ {t('trips:detail.addName')}
      </button>
    ) : null
  }

  return (
    <div className="trip-name">
      <h3 className="trip-name-text">{name}</h3>
      {canEdit ? (
        <button
          type="button"
          className="btn btn-quiet btn-sm"
          onClick={startEditing}
          aria-label={t('trips:detail.rename')}
          title={t('trips:detail.rename')}
        >
          ✎
        </button>
      ) : null}
    </div>
  )
}
