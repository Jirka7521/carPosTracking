// ============================================================
// ProvisioningPanel — shows everything needed to flash a tracker:
// its MQTT topics, the broker URI, the receiver public key
// fingerprint, and the paste-ready Config.h block.
//
// Used in two places: right after a device is registered (Home page)
// and later on demand from the device's Settings tab.
//
// What is NOT here, and never will be: the device's private key. It is
// generated server-side, encrypted at rest under the API's master key,
// and has no code path out of the database — which is what stops the
// broker, or anyone who steals the tracker, from reading positions.
// ============================================================

import { useState } from 'react'
import type { DeviceProvisioningDto } from '../services/apiTypes'

type ProvisioningPanelProps = {
  provisioning: DeviceProvisioningDto
  // Optional heading override — "Firmware configuration" reads oddly
  // immediately after registering, where "Device registered" is the news.
  title?: string
}

export function ProvisioningPanel({ provisioning, title }: ProvisioningPanelProps) {
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle')

  async function handleCopy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(provisioning.configSnippet)
      setCopyState('copied')
      // Reset so the button does not sit on "Copied!" forever, which would
      // leave the user unsure whether a second click did anything.
      window.setTimeout(() => setCopyState('idle'), 2000)
    } catch {
      // The Clipboard API needs a secure context and user permission. When it
      // is unavailable the snippet is still selectable in the <pre> below, so
      // this is a degraded path, not a broken one.
      setCopyState('failed')
    }
  }

  return (
    <div className="add-device-panel" style={{ marginTop: 16 }}>
      <h3>{title ?? 'Firmware configuration'}</h3>
      <p>
        Paste the block below over the matching constants in{' '}
        <code>ESP32/src/config/Config.h</code>, then flash the tracker. The
        broker password is not included — the API does not issue MQTT
        credentials, so fill in the one you created with{' '}
        <code>mosquitto_passwd</code>.
      </p>

      <dl className="provisioning-facts">
        <dt>Device ID</dt>
        <dd><code>{provisioning.deviceId}</code></dd>

        <dt>Telemetry topic</dt>
        <dd><code>{provisioning.telemetryTopic}</code></dd>

        <dt>Config topic</dt>
        <dd><code>{provisioning.configTopic}</code></dd>

        <dt>Broker</dt>
        <dd><code>{provisioning.brokerUri}</code></dd>

        <dt>Public key fingerprint</dt>
        {/* SHA-256 of the SPKI bytes. Comparing this against what the flashed
            firmware reports confirms the device carries the right key without
            either side handling key material. */}
        <dd><code style={{ wordBreak: 'break-all' }}>{provisioning.publicKeyFingerprint}</code></dd>
      </dl>

      <div className="provisioning-actions">
        <button type="button" className="btn btn-primary btn-sm" onClick={() => void handleCopy()}>
          {copyState === 'copied' ? '✓ Copied' : 'Copy Config.h block'}
        </button>
        {copyState === 'failed' ? (
          <span className="hint" role="status">
            Copying is not available here — select the text below instead.
          </span>
        ) : null}
      </div>

      <pre className="provisioning-snippet" aria-label="Config.h snippet">
        {provisioning.configSnippet}
      </pre>

      <p className="hint">
        Only the public key is shown. The matching private key stays encrypted
        in the API database and never leaves the server, so this device cannot
        read back its own positions — and neither can the broker.
      </p>
    </div>
  )
}
