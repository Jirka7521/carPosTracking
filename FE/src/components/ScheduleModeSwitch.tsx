// ---------------------------------------------------------------------------
// ScheduleModeSwitch — the schedule panel's first question: one set of
// settings all week, or different settings on a schedule?
//
// It started as a checkbox, which read as a minor option and was easy to miss —
// yet the answer decides what the rest of the settings tab means. So it is the
// same radio cards as ConfigModeSwitch (and reuses its classes): both are a
// choice between two ways the tracker runs, and both say in a sentence what
// each answer does. Two looks for the same kind of question would be one more
// thing to learn.
//
// STATELESS and not applied on click: it reports the choice, and the panel
// saves it with its own Save button. Turning a schedule on can create a profile
// and change what the tracker runs, which is too much to happen on a stray
// click.
// ---------------------------------------------------------------------------

import { useTranslation } from 'react-i18next'

export type ScheduleModeSwitchProps = {
  // The choice shown — the saved one, or the reader's unsaved pick.
  enabled: boolean
  onChange: (enabled: boolean) => void
  disabled: boolean
}

export function ScheduleModeSwitch({
  enabled,
  onChange,
  disabled,
}: ScheduleModeSwitchProps) {
  const { t } = useTranslation(['schedule'])

  return (
    <fieldset className="config-mode-switch" disabled={disabled}>
      <legend className="config-group-title">{t('modeSwitch.legend')}</legend>

      <div className="config-mode-options">
        <label className="config-mode-option">
          <input
            type="radio"
            name="schedule-mode"
            checked={!enabled}
            onChange={() => onChange(false)}
          />
          <span className="config-mode-option-text">
            <span className="config-mode-option-label">{t('modeSwitch.off')}</span>
            <span className="hint">{t('modeSwitch.offHint')}</span>
          </span>
        </label>

        <label className="config-mode-option">
          <input
            type="radio"
            name="schedule-mode"
            checked={enabled}
            onChange={() => onChange(true)}
          />
          <span className="config-mode-option-text">
            <span className="config-mode-option-label">{t('modeSwitch.on')}</span>
            <span className="hint">{t('modeSwitch.onHint')}</span>
          </span>
        </label>
      </div>
    </fieldset>
  )
}
