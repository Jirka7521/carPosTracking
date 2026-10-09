// ---------------------------------------------------------------------------
// ConfigModeSwitch — the first question the settings form asks: one set of
// settings all the time, or react when motion wakes the tracker?
//
// It edits nothing but `motionEnabled`. That used to be a checkbox halfway down
// the form, BELOW the settings it changes the meaning of: ticking it turned the
// set above it into "standby" and grew a second one underneath. A choice that
// reshapes the rest of the form belongs above it, phrased as the two ways a
// tracker can run rather than as an add-on to one of them.
//
// Radio buttons rather than a toggle, because both answers are real modes and
// each needs its own sentence saying what the tracker then does.
//
// `idPrefix` names the radio group. The settings form and an open profile
// editor can be on one page at once, and two groups sharing a name would be one
// group — picking a mode in one would untick the other.
// ---------------------------------------------------------------------------

import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

export type ConfigModeSwitchProps = {
  motionEnabled: boolean
  onChange: (motionEnabled: boolean) => void
  disabled: boolean
  // The "⚠ Device still on Off" note, when the caller has one.
  pendingNote: ReactNode
  idPrefix: string
}

export function ConfigModeSwitch({
  motionEnabled,
  onChange,
  disabled,
  pendingNote,
  idPrefix,
}: ConfigModeSwitchProps) {
  const { t } = useTranslation(['settings'])

  return (
    <fieldset className="config-mode-switch" disabled={disabled}>
      <legend className="config-group-title">{t('config.modeSwitch.legend')}</legend>

      <div className="config-mode-options">
        <label className="config-mode-option">
          <input
            type="radio"
            name={`${idPrefix}-mode`}
            checked={!motionEnabled}
            onChange={() => onChange(false)}
          />
          <span className="config-mode-option-text">
            <span className="config-mode-option-label">{t('config.modeSwitch.constant')}</span>
            <span className="hint">{t('config.modeSwitch.constantHint')}</span>
          </span>
        </label>

        <label className="config-mode-option">
          <input
            type="radio"
            name={`${idPrefix}-mode`}
            checked={motionEnabled}
            onChange={() => onChange(true)}
          />
          <span className="config-mode-option-text">
            <span className="config-mode-option-label">{t('config.modeSwitch.motion')}</span>
            <span className="hint">{t('config.modeSwitch.motionHint')}</span>
          </span>
        </label>
      </div>

      {pendingNote}
    </fieldset>
  )
}
