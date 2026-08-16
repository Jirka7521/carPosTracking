// ============================================================
// ProvisioningPanel — shows everything needed to flash a tracker:
// its MQTT topics, the broker URI, the receiver public key
// fingerprint, and the paste-ready Config.h block.
//
// Used in two places: right after a device is registered (Home page)
// and later on demand from the device's Settings tab.
//
// What is NOT here, and never will be: any private key. The receiver
// private key is generated server-side, encrypted at rest under the
// API's master key, and has no code path out of the database — which is
// what stops the broker, or anyone who steals the tracker, from reading
// positions.
//
// The ack key runs the other way (the device decrypts, so the device
// holds the private half), which makes this panel the obvious place to
// leak one. It never shows more than a fingerprint: the ack private key
// is generated off-server and pasted straight into Config.h, so it never
// reaches the API, this payload, or the clipboard.
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

        <dt>Ack topic</dt>
        <dd><code>{provisioning.ackTopic}</code></dd>

        <dt>Broker</dt>
        <dd><code>{provisioning.brokerUri}</code></dd>

        <dt>Public key fingerprint</dt>
        {/* SHA-256 of the SPKI bytes. Comparing this against what the flashed
            firmware reports confirms the device carries the right key without
            either side handling key material. */}
        <dd><code style={{ wordBreak: 'break-all' }}>{provisioning.publicKeyFingerprint}</code></dd>

        <dt>Ack key fingerprint</dt>
        {/* Null until an ack public key is imported. Saying so explicitly matters:
            firmware flashed with acks enabled against a device that has no ack key
            would retry every fix forever, and the cause would be invisible here. */}
        <dd>
          {provisioning.ackPublicKeyFingerprint === null ? (
            <span className="hint">Not configured — delivery acks are off</span>
          ) : (
            <code style={{ wordBreak: 'break-all' }}>{provisioning.ackPublicKeyFingerprint}</code>
          )}
        </dd>
      </dl>

      {provisioning.ackPublicKeyFingerprint === null ? (
        <p className="hint">
          To turn on delivery acks — so the tracker only clears a fix from its SD
          card once the API confirms it reached the database — generate an ack key
          pair yourself and import the <strong>public</strong> half with{' '}
          <code>import-device-key --ack-public-pem</code>. The private half goes
          into <code>Config.h</code> and must never reach the server; the exact
          commands are in the block below.
        </p>
      ) : null}

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
