// ---------------------------------------------------------------------------
// ScheduleProfileEditor — a name plus the seven settings.
//
// The body is ConfigValuesFields, the same controls the manual settings form
// uses, on purpose: a profile IS a set of device settings, and an editor that
// looked or validated differently would be a second thing to learn and a second
// place for the bounds to drift out of step.
//
// The one warning it adds is about consequence rather than validity. Editing a
// profile that is in force right now retunes the tracker immediately — the
// server applies it rather than waiting for the next boundary — and that is
// worth saying before the button is pressed, not after.
// ---------------------------------------------------------------------------

import { useState } from 'react'
import type { FormEvent } from 'react'
import type {
  DeviceConfigProfileDto,
  DeviceConfigValuesDto,
  SaveConfigProfileRequestDto,
} from '../services/apiTypes'
import { validateConfigRanges } from '../utils/deviceConfig'
import { ConfigValuesFields } from './ConfigValuesFields'

// What a new profile starts from — the firmware's factory defaults, mirrored
// from the API's DeviceConfigRules. A blank form would only invite a first save
// that fails validation.
const DEFAULT_VALUES: DeviceConfigValuesDto = {
  intervalSeconds: 60,
  sleepBetween: false,
  fixTimeoutSeconds: 180,
  queueMaxFixes: 20000,
  retryIntervalHours: 24,
  retryMaxAgeHours: 168,
  configCheckSeconds: 3600,
}

export type ScheduleProfileEditorProps = {
  // The profile being edited, or null to create one.
  profile: DeviceConfigProfileDto | null
  // True when this profile is the one the schedule currently has in force, so
  // the warning below is shown only where it is actually true.
  isActive: boolean
  isSaving: boolean
  onSubmit: (payload: SaveConfigProfileRequestDto) => void
  onCancel: () => void
}

export function ScheduleProfileEditor({
  profile,
  isActive,
  isSaving,
  onSubmit,
  onCancel,
}: ScheduleProfileEditorProps) {
  // Seeded once; the panel keys this component by profile id, so switching
  // profiles remounts it rather than needing an effect that could overwrite
  // half-typed values.
  const [name, setName] = useState<string>(profile?.name ?? '')
  const [values, setValues] = useState<DeviceConfigValuesDto>(profile?.values ?? DEFAULT_VALUES)
  const [error, setError] = useState<string>('')

  function updateField<TKey extends keyof DeviceConfigValuesDto>(
    key: TKey,
    value: DeviceConfigValuesDto[TKey],
  ): void {
    setValues((current) => ({ ...current, [key]: value }))
    setError('')
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()

    const trimmed: string = name.trim()
    if (trimmed.length === 0) {
      setError('Give the profile a name — it is how the rules refer to it.')
      return
    }

    // The API validates the same bounds and answers 400; this exists so a typo
    // gets a sentence instead of a round trip. Shared with the settings form so
    // the two can never disagree about what is in range.
    const rangeError: string | null = validateConfigRanges(values)
    if (rangeError !== null) {
      setError(rangeError)
      return
    }

    onSubmit({ name: trimmed, ...values })
  }

  return (
    <form className="schedule-profile-editor" onSubmit={handleSubmit}>
      <div className="form-field">
        <label className="form-label" htmlFor="profile-name">Profile name</label>
        <input
          id="profile-name"
          className="form-input"
          type="text"
          value={name}
          onChange={(event) => { setName(event.target.value); setError('') }}
          placeholder="Night"
          maxLength={40}
          required
        />
      </div>

      {isActive ? (
        <div className="banner banner--info" role="status">
          This profile is in force right now, so saving changes what the tracker is
          running immediately — not at the next switch.
        </div>
      ) : null}

      <ConfigValuesFields
        values={values}
        onChange={updateField}
        // Fixed: this form is remounted per profile rather than re-seeded, so
        // there is never a moment where the server replaces values under the
        // reader and the unit comboboxes need re-picking.
        seedKey={0}
        disabled={isSaving}
        idPrefix="profile"
      />

      {error ? <div className="banner banner--error" role="alert">{error}</div> : null}

      <div className="config-actions">
        <button type="submit" className="btn btn-primary btn-sm" disabled={isSaving}>
          {isSaving ? 'Saving…' : profile === null ? 'Create profile' : 'Save profile'}
        </button>
        <button type="button" className="btn btn-secondary btn-sm" onClick={onCancel} disabled={isSaving}>
          Cancel
        </button>
      </div>
    </form>
  )
}
