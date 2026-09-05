// ============================================================
// DurationField — a number input whose unit is a combobox at the end of the row.
//
//     Report every
//     [    5 ] [ minutes ▾ ]
//     every 5 minutes
//     ⚠ Device still on 60 s
//
// The problem it solves: every duration this dashboard edits is stored in one
// fixed unit — seconds for the reporting interval, hours for the retry knobs —
// and the form used to demand that unit. Entering six hours meant typing 21600
// and reading it back meant dividing. The hint line under the input already
// translated the number into English; this makes the input itself speak the
// same language.
//
// THE VALUE NEVER CHANGES UNIT. `value` goes in and comes out in the field's
// storage unit (`baseUnit`), so the form state, the request body, the range
// validation and the pending-change diff all keep working on canonical numbers
// exactly as before. The combobox only decides how that number is rendered.
// Picking a different unit therefore edits nothing — 180 seconds simply re-reads
// as 3 minutes — which is what makes the control safe to open out of curiosity.
//
// The selected unit is seeded once from the value (see bestUnit) and then left
// alone. Re-seeding it when a fresh value arrives from the server is the
// PARENT's job, done by changing this component's `key`; deriving it from
// `value` in an effect would yank the dropdown out from under someone typing.
// ============================================================

import { useState } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { TimeUnit } from '../utils/timeUnits'
import { UNIT_LABEL_KEYS, UNIT_SECONDS, bestUnit, fromUnit, toUnit } from '../utils/timeUnits'

export type DurationFieldProps = {
  // Input id, so the label points at it the way every other field here does.
  id: string
  // Plain text: it is also woven into the combobox's accessible name.
  label: string
  // The current value, in `baseUnit`.
  value: number
  // The unit the API stores this setting in. Also the fallback the combobox
  // opens in when the value divides evenly into nothing (notably zero).
  baseUnit: TimeUnit
  // Which units the combobox offers, in the order they should appear.
  units: readonly TimeUnit[]
  // Bounds, in `baseUnit` — the same numbers the API rejects outside of.
  min: number
  max: number
  // Receives the edited value, in `baseUnit`, always a whole number.
  onChange: (value: number) => void
  // The "every 5 minutes" line under the input.
  hint?: ReactNode
  // The "⚠ Device still on 60 s" note, when there is one.
  pendingNote?: ReactNode
  required?: boolean
}

export function DurationField({
  id,
  label,
  value,
  baseUnit,
  units,
  min,
  max,
  onChange,
  hint,
  pendingNote,
  required = false,
}: DurationFieldProps) {
  const { t } = useTranslation(['common'])

  // Everything is compared in seconds, because that is the only unit all four
  // of them share. `baseSeconds` converts in and out of the storage unit.
  const baseSeconds: number = UNIT_SECONDS[baseUnit]
  const canonicalSeconds: number = value * baseSeconds

  // Seeded once, deliberately — see the header note about `key`.
  const [unit, setUnit] = useState<TimeUnit>(() =>
    bestUnit(canonicalSeconds, units, baseUnit),
  )

  // What the input shows while it is being typed into, before the value has
  // been through the round trip into canonical units and back.
  //
  // Without this the field cannot be typed into fractionally at all: pressing
  // "." after a 1 gives "1.", which parses to 1, which renders as "1" — and
  // React puts that back, swallowing the decimal point. That only bites when
  // the chosen unit is coarser than storage (hours on a seconds field, days on
  // an hours field), which is exactly where a fraction is wanted. The draft is
  // dropped on blur and on a unit change, so the field always settles on what
  // is actually stored.
  const [draft, setDraft] = useState<string | null>(null)

  const factor: number = UNIT_SECONDS[unit]

  // How far one press of the spinner should move, expressed in the selected
  // unit. Two cases, and the distinction is what keeps a typed value storable:
  //
  //   • The selected unit is FINER than storage (minutes on an hours field):
  //     one stored hour is 60 selected minutes, so step 60. The arrows move a
  //     whole hour and the browser's own validation rejects 90 minutes, which
  //     could not be stored as a whole number of hours anyway.
  //   • The selected unit is COARSER (hours on a seconds field): a step of
  //     1/3600 is meaningless to type against, so anything goes and the value
  //     is rounded to a whole second on its way out.
  const stepInUnit: number = baseSeconds / factor
  const step: number | 'any' = stepInUnit >= 1 ? stepInUnit : 'any'

  function handleValueChange(raw: string): void {
    setDraft(raw)

    const entered: number = Number(raw)
    if (!Number.isFinite(entered)) {
      return
    }
    // Back to seconds, then to whole units of storage. The rounding is the only
    // place a value can lose precision, and the step rule above is what keeps
    // it from ever mattering in practice.
    const seconds: number = fromUnit(entered, unit)
    onChange(Math.round(seconds / baseSeconds))
  }

  return (
    <div className="form-field">
      <label className="form-label" htmlFor={id}>
        {label}
      </label>

      <div className="duration-field">
        <input
          id={id}
          className="form-input duration-field-value"
          type="number"
          min={toUnit(min * baseSeconds, unit)}
          max={toUnit(max * baseSeconds, unit)}
          step={step}
          value={draft ?? toUnit(canonicalSeconds, unit)}
          onChange={(event) => handleValueChange(event.target.value)}
          onBlur={() => setDraft(null)}
          required={required}
        />

        <select
          className="form-input duration-field-unit"
          // The visible label belongs to the number input, so the combobox
          // names itself — "Unit for Report every" rather than a bare "unit"
          // repeated five times down the form.
          aria-label={t('durationField.unitFor', { label })}
          value={unit}
          onChange={(event) => {
            // The draft is text in the OLD unit; keeping it would show 300 as
            // "300 hours" the instant minutes became hours.
            setDraft(null)
            setUnit(event.target.value as TimeUnit)
          }}
        >
          {units.map((option) => (
            <option key={option} value={option}>
              {t(UNIT_LABEL_KEYS[option])}
            </option>
          ))}
        </select>
      </div>

      {hint ? <span className="hint">{hint}</span> : null}
      {pendingNote}
    </div>
  )
}
