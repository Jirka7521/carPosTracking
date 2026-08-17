// ============================================================
// FirmwareParameterTable — every parameter the tracker is built with, read-only.
//
// Sits under the Config.h panel in the Settings tab. It answers a question the
// file itself answers badly: "what is this thing actually configured to do?" —
// 500 lines of C++ with comments is the wrong shape for that, a grouped table is
// the right one.
//
// Nothing here is editable, and that is not a limitation to apologise for: these
// are compile-time constants. The four that a person does supply are marked as
// such, and the handful the dashboard *can* change at runtime point back up at
// the Reporting & Power section rather than pretending to be settings.
//
// The data is static (utils/firmwareParameters.ts); only the per-device rows are
// filled from the provisioning payload, so the table shows this tracker's real
// id, topics, broker and key fingerprints rather than the template's placeholders.
// ============================================================

import type { DeviceProvisioningDto } from '../services/apiTypes'
import type { FirmwareParameter } from '../utils/firmwareParameters'
import {
  FIRMWARE_PARAMETER_GROUPS,
  ORIGIN_LABELS,
  resolveParameterValue,
} from '../utils/firmwareParameters'

export type FirmwareParameterTableProps = {
  // Null renders the firmware defaults alone — the table is still useful, it
  // just cannot name this device's topics.
  provisioning: DeviceProvisioningDto | null
}

export function FirmwareParameterTable({ provisioning }: FirmwareParameterTableProps) {
  return (
    <div className="firmware-parameters">
      <p className="hint">
        Everything the firmware is built with. These are compile-time constants —
        changing one means editing <code>Config.h</code> and re-flashing. The
        reporting interval, sleep flag, GNSS timeout and queue limits are the
        exception: the values below are only the defaults a tracker falls back to,
        and <strong>Reporting &amp; Power</strong> above is what actually sets them.
      </p>

      <ul className="firmware-legend">
        <li>
          <span className="param-badge param-badge--device">{ORIGIN_LABELS.device}</span>
          filled in for this tracker
        </li>
        <li>
          <span className="param-badge param-badge--secret">{ORIGIN_LABELS.secret}</span>
          typed in above; never sent to the server
        </li>
        <li>
          <span className="param-badge param-badge--remote">{ORIGIN_LABELS.remote}</span>
          overridden by Reporting &amp; Power
        </li>
        <li>
          <span className="param-badge param-badge--fixed">{ORIGIN_LABELS.fixed}</span>
          same on every tracker
        </li>
      </ul>

      {FIRMWARE_PARAMETER_GROUPS.map((group) => (
        <section key={group.title} className="firmware-parameter-group">
          <h5>{group.title}</h5>

          {/* Wrapped so a long constant name scrolls this box rather than the page */}
          <div className="firmware-table-scroll">
            <table className="firmware-table">
              <thead>
                <tr>
                  <th scope="col">Constant</th>
                  <th scope="col">Value</th>
                  <th scope="col">What it does</th>
                </tr>
              </thead>
              <tbody>
                {group.parameters.map((parameter: FirmwareParameter) => (
                  <tr key={parameter.name}>
                    <th scope="row">
                      <code>{parameter.name}</code>
                    </th>
                    <td>
                      <code className="param-value">
                        {resolveParameterValue(parameter, provisioning)}
                      </code>
                      <span className={`param-badge param-badge--${parameter.origin}`}>
                        {ORIGIN_LABELS[parameter.origin]}
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
