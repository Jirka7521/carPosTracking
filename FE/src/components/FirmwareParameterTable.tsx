// ============================================================
// FirmwareParameterTable — every parameter the tracker is built with, read-only.
//
// Sits under the Config.h panel in the Settings tab. It answers a question the
// file itself answers badly: "what is this thing actually configured to do?" —
// 700 lines of C++ with comments is the wrong shape for that, a grouped table is
// the right one.
//
// Nothing here is editable, and that is not a limitation to apologise for: these
// are compile-time constants. The four that a person does supply are marked as
// such, and the handful the dashboard *can* change at runtime point back up at
// the Reporting & Power section rather than pretending to be settings.
//
// The rows are parsed out of the very file shown above (utils/parseFirmwareConfig),
// so the table cannot fall behind the firmware — it is the same text, read twice.
// It used to be a hand transcription, and it was missing fifteen constants.
// ============================================================

import { useMemo } from 'react'
import { Trans, useTranslation } from 'react-i18next'

import type { FirmwareParameter } from '../utils/firmwareParameters'
import { ORIGIN_LABEL_KEYS } from '../utils/firmwareParameters'
import { parseFirmwareConfig } from '../utils/parseFirmwareConfig'

export type FirmwareParameterTableProps = {
  // The Config.h the API rendered for this device, EXACTLY as it arrived —
  // never the copy the panel has woven the operator's secrets into, which
  // carries a WiFi password and a private key.
  configSnippet: string
}

export function FirmwareParameterTable({ configSnippet }: FirmwareParameterTableProps) {
  const { t } = useTranslation(['settings'])

  // Parsing 700 lines on every keystroke in the secrets form would be pure
  // waste: the file this reads does not change while the operator types.
  const groups = useMemo(() => parseFirmwareConfig(configSnippet), [configSnippet])

  return (
    <div className="firmware-parameters">
      {/* <Trans> because the sentence carries a <code> and a <strong> inside
          it; three separate keys would leave a translator with fragments. */}
      <p className="hint">
        <Trans
          i18nKey="firmware.intro"
          ns="settings"
          components={{ code: <code />, strong: <strong /> }}
        />
      </p>

      <ul className="firmware-legend">
        <li>
          <span className="param-badge param-badge--device">{t(ORIGIN_LABEL_KEYS.device)}</span>
          {t('firmware.legend.device')}
        </li>
        <li>
          <span className="param-badge param-badge--secret">{t(ORIGIN_LABEL_KEYS.secret)}</span>
          {t('firmware.legend.secret')}
        </li>
        <li>
          <span className="param-badge param-badge--remote">{t(ORIGIN_LABEL_KEYS.remote)}</span>
          {t('firmware.legend.remote')}
        </li>
        <li>
          <span className="param-badge param-badge--fixed">{t(ORIGIN_LABEL_KEYS.fixed)}</span>
          {t('firmware.legend.fixed')}
        </li>
      </ul>

      {groups.map((group) => (
        <section key={group.title} className="firmware-parameter-group">
          <h5>{group.title}</h5>

          {/* Wrapped so a long constant name scrolls this box rather than the page */}
          <div className="firmware-table-scroll">
            <table className="firmware-table">
              <thead>
                <tr>
                  <th scope="col">{t('firmware.column.constant')}</th>
                  <th scope="col">{t('firmware.column.value')}</th>
                  <th scope="col">{t('firmware.column.meaning')}</th>
                </tr>
              </thead>
              <tbody>
                {group.parameters.map((parameter: FirmwareParameter) => (
                  <tr key={parameter.name}>
                    <th scope="row">
                      <code>{parameter.name}</code>
                    </th>
                    <td>
                      <code className="param-value">{parameter.value}</code>
                      <span className={`param-badge param-badge--${parameter.origin}`}>
                        {t(ORIGIN_LABEL_KEYS[parameter.origin])}
                      </span>
                    </td>
                    <td>{parameter.meaning}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ))}
    </div>
  )
}
