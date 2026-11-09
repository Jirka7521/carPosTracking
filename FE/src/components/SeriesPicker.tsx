// ============================================================
// SeriesPicker — the checkboxes that choose which readings a TelemetryChart
// draws, used by the Charts tab and by a trip's page.
//
// It doubles as the chart's legend: every entry pairs its colour with a text
// label and the unit (which says which axis the line will land on), so the
// chart never relies on colour alone.
//
// A series the device did not report is disabled rather than hidden, so its
// absence is visible. The selection itself belongs to the caller — the device
// page keeps it, so the same ticks follow the reader between the two pages.
// ============================================================

import type { Dispatch, SetStateAction } from 'react'
import { useTranslation } from 'react-i18next'
import type { SeriesKey } from '../utils/telemetry'
import { SERIES } from '../utils/telemetry'

export type SeriesPickerProps = {
  // The series that carry at least one value in the rows being charted.
  available: ReadonlySet<SeriesKey>
  selected: readonly SeriesKey[]
  onSelectedChange: Dispatch<SetStateAction<readonly SeriesKey[]>>
}

export function SeriesPicker({ available, selected, onSelectedChange }: SeriesPickerProps) {
  const { t } = useTranslation(['device'])

  function toggle(key: SeriesKey, checked: boolean): void {
    onSelectedChange((current) =>
      checked ? [...current, key] : current.filter((existing) => existing !== key),
    )
  }

  return (
    <fieldset className="series-picker">
      <legend className="form-label">{t('device:charts.series.legend')}</legend>

      {SERIES.map((definition) => {
        const isAvailable: boolean = available.has(definition.key)

        return (
          <label
            key={definition.key}
            className="checkbox-field series-chip"
            title={isAvailable ? undefined : t('device:charts.notReported')}
          >
            <input
              type="checkbox"
              checked={selected.includes(definition.key)}
              disabled={!isAvailable}
              onChange={(e) => toggle(definition.key, e.target.checked)}
            />
            <span
              className="series-swatch"
              style={{ background: definition.color }}
              aria-hidden="true"
            />
            <span>
              {t(definition.labelKey)}{' '}
              <span className="series-chip-unit">({definition.unit})</span>
              {isAvailable ? null : (
                <span className="series-chip-note"> {t('device:charts.noData')}</span>
              )}
            </span>
          </label>
        )
      })}
    </fieldset>
  )
}
