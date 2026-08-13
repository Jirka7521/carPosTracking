// ============================================================
// PersonalInfoSection — edit first name and last name.
//
// Used on the Profile page. Shows email read-only (email changes
// are out of scope — they would need a verification round-trip).
//
// On a successful save the `onSaved` callback receives the updated
// profile returned by the server. The Profile page passes this to
// AuthContext.updateCurrentUser() so the header name refreshes
// immediately without a page reload.
// ============================================================

import { useState } from 'react'
import type { FormEvent } from 'react'
import { updateUserProfile } from '../services/apiClient'
import type { UserProfileDto } from '../services/apiTypes'
import { describeError } from '../utils/errors'

type PersonalInfoSectionProps = {
  userId:           number
  initialFirstName: string
  initialLastName:  string
  // Shown read-only — email cannot be changed through this form.
  email:            string
  // Called with the updated profile on a successful save. The parent
  // should forward this to AuthContext to refresh the header name.
  onSaved: (user: UserProfileDto) => void
}

export function PersonalInfoSection({
  userId,
  initialFirstName,
  initialLastName,
  email,
  onSaved,
}: PersonalInfoSectionProps) {
  const [firstName, setFirstName] = useState<string>(initialFirstName)
  const [lastName,  setLastName]  = useState<string>(initialLastName)
  const [isSaving,  setIsSaving]  = useState<boolean>(false)
  const [message,   setMessage]   = useState<string>('')
  const [isError,   setIsError]   = useState<boolean>(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    setMessage('')

    const trimmedFirst = firstName.trim()
    const trimmedLast  = lastName.trim()

    // Quick client-side check — the server enforces the same rule,
    // but this gives faster, friendlier feedback.
    if (!trimmedFirst || !trimmedLast) {
      setMessage('First name and last name are required.')
      setIsError(true)
      return
    }

    setIsSaving(true)
    try {
      const updated = await updateUserProfile(userId, {
        firstName: trimmedFirst,
        lastName:  trimmedLast,
      })
      // Notify parent so it can refresh AuthContext (and thus the header).
      onSaved(updated)
      setMessage('Name updated successfully.')
      setIsError(false)
    } catch (error) {
      setMessage(describeError(error, 'Failed to update name.'))
      setIsError(true)
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div className="settings-section">
      <div className="settings-section-header">
        <span className="settings-section-icon" aria-hidden="true">👤</span>
        <h3>Personal Information</h3>
      </div>

      <div className="settings-section-body">
        {/* Email is shown read-only — changing it requires a verification
            flow that is out of scope for this project. */}
        <div className="form-field">
          <label className="form-label" htmlFor="profile-email">Email</label>
          <input
            id="profile-email"
            className="form-input"
            type="email"
            value={email}
            readOnly
            aria-readonly="true"
            style={{ opacity: 0.7, cursor: 'default' }}
          />
          <p className="hint" style={{ marginTop: 4 }}>Email address cannot be changed.</p>
        </div>

        <form onSubmit={handleSubmit} style={{ marginTop: 16 }}>
          <div className="form-field" style={{ marginBottom: 12 }}>
            <label className="form-label" htmlFor="profile-first-name">First name</label>
            <input
              id="profile-first-name"
              className="form-input"
              type="text"
              value={firstName}
              onChange={(e) => setFirstName(e.target.value)}
              maxLength={100}
              required
              autoComplete="given-name"
            />
          </div>

          <div className="form-field" style={{ marginBottom: 16 }}>
            <label className="form-label" htmlFor="profile-last-name">Last name</label>
            <input
              id="profile-last-name"
              className="form-input"
              type="text"
              value={lastName}
              onChange={(e) => setLastName(e.target.value)}
              maxLength={100}
              required
              autoComplete="family-name"
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
            {isSaving ? 'Saving…' : 'Save changes'}
          </button>
        </form>
      </div>
    </div>
  )
}
