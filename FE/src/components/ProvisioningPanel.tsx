// ============================================================
// ProvisioningPanel — everything needed to flash a tracker, as one file.
//
// The API renders a COMPLETE Config.h for this device: its id, topics, broker
// URI, receiver public key and current setting defaults, dropped into the
// firmware's own template. Save it over ESP32/src/config/Config.h and build —
// there is nothing left to merge by hand, which is what the old paste-a-block
// flow got wrong (a missed constant produces firmware that builds and then talks
// to the wrong topic).
//
// Used in two places: right after a device is registered (Home page) and later
// on demand from the device's Settings tab.
//
// ---- Why the secrets are typed in here ----
//
// Four constants arrive empty and are filled in by this component, in the
// browser, before the file is ever copied or downloaded:
//
//   kWifiSsid / kWifiPassword   your network; the server has no business knowing
//   kMqttPassword               the broker account is made by hand on the server
//                               with mosquitto_passwd — the API issues no MQTT
//                               credentials and must not pretend to know one
//   kDeviceAckPrivateKeyPem     THE important one, below
//
// None of them is sent anywhere, stored, or kept across a reload. The file you
// download is assembled from a template the server rendered plus values that
// never left this tab.
//
// ---- Why the ack key is generated here ----
//
// The ack direction inverts the key roles. For a position the device encrypts
// and the API decrypts, so the API holds the private key (encrypted at rest,
// with no code path out of the database). For a delivery ack the API encrypts
// and the DEVICE decrypts — so that private half belongs to the device alone
// and must never exist on the server. Generating the pair with WebCrypto here
// keeps that literally true: only the public half is ever POSTed.
//
// The ordering of the rotation flow is the other half of that argument, and it
// is deliberate: generate → save the file → only then activate. If the public
// key went into the database first and the download were lost, the device would
// be left with a server-side key whose private half no longer exists anywhere,
// and every fix would sit waiting out the ack timeout with nothing to explain
// why. Abandoning the flow before the last step costs one regeneration; getting
// it wrong the other way costs a site visit.
// ============================================================

import { useMemo, useState } from 'react'
import { importDeviceAckKey } from '../services/apiClient'
import type { DeviceProvisioningDto } from '../services/apiTypes'
import type { AckKeyPair } from '../utils/ackKeyPair'
import { generateAckKeyPair, isKeyGenerationAvailable } from '../utils/ackKeyPair'
import type { ConfigSecrets } from '../utils/configSecrets'
import { EMPTY_SECRETS, fillSecrets } from '../utils/configSecrets'
import { downloadTextFile } from '../utils/downloadTextFile'
import { describeError } from '../utils/errors'

type ProvisioningPanelProps = {
  provisioning: DeviceProvisioningDto
  // Optional heading override — "Firmware configuration" reads oddly
  // immediately after registering, where "Device registered" is the news.
  title?: string
  // Called after a rotated ack key has been stored, so the parent can re-read
  // the provisioning payload and show the new fingerprint.
  onAckKeyActivated?: () => void
}

// What the file is called when downloaded. Matching the firmware's own name
// means the operator can drop it straight into src/config/ with no rename.
const CONFIG_FILE_NAME = 'Config.h'

export function ProvisioningPanel({ provisioning, title, onAckKeyActivated }: ProvisioningPanelProps) {
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle')

  // ---- Secrets the operator supplies ----
  // Held in component state only: not persisted, not lifted, not logged.
  const [secrets, setSecrets] = useState<ConfigSecrets>(EMPTY_SECRETS)
  const [arePasswordsVisible, setArePasswordsVisible] = useState<boolean>(false)

  // ---- Ack key rotation ----
  const [keyPair, setKeyPair] = useState<AckKeyPair | null>(null)
  const [isGenerating, setIsGenerating] = useState<boolean>(false)
  // Whether the file carrying the new private key has been copied or saved. The
  // activate button stays disabled until it has: see the banner comment.
  const [isFileSaved, setIsFileSaved] = useState<boolean>(false)
  const [isActivating, setIsActivating] = useState<boolean>(false)
  const [activatedFingerprint, setActivatedFingerprint] = useState<string | null>(null)
  const [keyError, setKeyError] = useState<string>('')

  const canGenerate = isKeyGenerationAvailable()

  // The file as it will be copied or downloaded: the server's render plus
  // whatever has been typed or generated. Recomputed on every keystroke, which
  // is the point — the <pre> below shows exactly what you would save.
  const configFile: string = useMemo(
    () =>
      fillSecrets(provisioning.configSnippet, {
        ...secrets,
        ackPrivateKeyPem: keyPair?.privateKeyPem ?? null,
      }),
    [provisioning.configSnippet, secrets, keyPair],
  )

  function updateSecret(field: keyof ConfigSecrets, value: string): void {
    setSecrets((current) => ({ ...current, [field]: value }))
  }

  async function handleCopy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(configFile)
      setCopyState('copied')
      setIsFileSaved(true)
      // Reset so the button does not sit on "Copied!" forever, which would
      // leave the user unsure whether a second click did anything.
      window.setTimeout(() => setCopyState('idle'), 2000)
    } catch {
      // The Clipboard API needs a secure context and user permission. When it
      // is unavailable the file is still selectable in the <pre> below, so
      // this is a degraded path, not a broken one.
      setCopyState('failed')
    }
  }

  function handleDownload(): void {
    downloadTextFile(CONFIG_FILE_NAME, configFile)
    setIsFileSaved(true)
  }

  async function handleGenerate(): Promise<void> {
    setKeyError('')
    setIsGenerating(true)
    try {
      const generated = await generateAckKeyPair()
      setKeyPair(generated)
      // A new pair invalidates any previous save: the file on disk carries the
      // old private key, so it must be taken again before this one is activated.
      setIsFileSaved(false)
      setActivatedFingerprint(null)
    } catch (error) {
      setKeyError(describeError(error, 'Could not generate a key pair in this browser.'))
    } finally {
      setIsGenerating(false)
    }
  }

  async function handleActivate(): Promise<void> {
    if (keyPair === null) {
      return
    }
    setKeyError('')
    setIsActivating(true)
    try {
      const result = await importDeviceAckKey(provisioning.deviceId, keyPair.publicKeyPem)
      setActivatedFingerprint(result.ackPublicKeyFingerprint)
      onAckKeyActivated?.()
    } catch (error) {
      // The private key deliberately stays on screen so the operator can retry
      // — losing it here would mean regenerating and re-flashing for nothing.
      setKeyError(describeError(error, 'Could not store the new key. The device still uses its previous one.'))
    } finally {
      setIsActivating(false)
    }
  }

  return (
    <div className="add-device-panel" style={{ marginTop: 16 }}>
      <h3>{title ?? 'Firmware configuration'}</h3>
      <p>
        A complete <code>Config.h</code> for this device — save it as{' '}
        <code>ESP32/src/config/Config.h</code> and run <code>pio run</code>.
        Everything specific to this tracker is already in it; the only blanks are
        the secrets below, which your browser fills in without sending them
        anywhere.
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
        {/* Null until an ack public key is stored. Saying so explicitly matters:
            firmware flashed with acks enabled against a device that has no ack key
            would retry every fix forever, and the cause would be invisible here. */}
        <dd>
          {activatedFingerprint !== null ? (
            <code style={{ wordBreak: 'break-all' }}>{activatedFingerprint}</code>
          ) : provisioning.ackPublicKeyFingerprint === null ? (
            <span className="hint">Not configured — delivery acks are off</span>
          ) : (
            <code style={{ wordBreak: 'break-all' }}>{provisioning.ackPublicKeyFingerprint}</code>
          )}
        </dd>
      </dl>

      {/* ================================================================
       * Secrets — typed here, woven in here, never uploaded
       * ================================================================ */}
      <div className="config-secrets">
        <h4>Your secrets</h4>
        <p className="hint">
          These four values are the only blanks left in the file. They are
          inserted <strong>in this browser</strong> — they are never sent to the
          server, never stored, and are gone when you leave this page. Leave any
          of them empty and the file still builds; the matching feature just
          stays off.
        </p>

        <div className="config-grid">
          <div className="form-field">
            <label className="form-label" htmlFor="config-wifi-ssid">WiFi network (SSID)</label>
            <input
              id="config-wifi-ssid"
              className="form-input"
              type="text"
              value={secrets.wifiSsid}
              onChange={(event) => updateSecret('wifiSsid', event.target.value)}
              autoComplete="off"
              spellCheck={false}
            />
            <span className="hint">
              {/* Explaining the flip, because it is a change the file makes on
                  your behalf and a silent one would be surprising. */}
              Filling this in also switches <code>kWifiEnabled</code> on. Left
              empty, WiFi stays off so the tracker does not wait out a connect
              timeout on every boot.
            </span>
          </div>

          <div className="form-field">
            <label className="form-label" htmlFor="config-wifi-password">WiFi password</label>
            <input
              id="config-wifi-password"
              className="form-input"
              type={arePasswordsVisible ? 'text' : 'password'}
              value={secrets.wifiPassword}
              onChange={(event) => updateSecret('wifiPassword', event.target.value)}
              autoComplete="new-password"
            />
          </div>

          <div className="form-field">
            <label className="form-label" htmlFor="config-mqtt-password">MQTT broker password</label>
            <input
              id="config-mqtt-password"
              className="form-input"
              type={arePasswordsVisible ? 'text' : 'password'}
              value={secrets.mqttPassword}
              onChange={(event) => updateSecret('mqttPassword', event.target.value)}
              autoComplete="new-password"
            />
            <span className="hint">
              The one you created for <code>{provisioning.deviceId}</code> with{' '}
              <code>mosquitto_passwd</code>. The API does not issue broker
              accounts, so it cannot fill this in for you.
            </span>
          </div>
        </div>

        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={arePasswordsVisible}
            onChange={(event) => setArePasswordsVisible(event.target.checked)}
          />
          <span>Show passwords</span>
        </label>
      </div>

      {/* ================================================================
       * Ack key pair — generate, save, then activate. In that order.
       * ================================================================ */}
      <div className="config-secrets">
        <h4>Delivery ack key</h4>
        <p className="hint">
          With acks on, the tracker only clears a fix from its SD card once this
          API confirms the fix reached the database — a broker acknowledgement
          alone proves only that the broker took the message. The API seals each
          verdict to this device, so the device needs its own private key.
        </p>

        {keyPair === null ? (
          <>
            <p className="hint">
              <strong>Generating a new pair replaces the key this device uses.</strong>{' '}
              The tracker cannot read acks again until you flash it with the new{' '}
              <code>Config.h</code>, and the private key is shown{' '}
              <strong>once</strong> — it is never stored here or on the server, and
              cannot be recovered afterwards.
            </p>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={() => void handleGenerate()}
              disabled={isGenerating || !canGenerate}
            >
              {isGenerating ? 'Generating…' : 'Generate a new ack key pair'}
            </button>
            {!canGenerate ? (
              <p className="hint">
                Key generation needs a secure context. Open the dashboard over
                https (or on localhost), or mint the pair with{' '}
                <code>openssl</code> — the commands are in the file below.
              </p>
            ) : null}
          </>
        ) : activatedFingerprint !== null ? (
          <div className="banner banner--success" role="status">
            The new key is active. The API now seals every delivery ack to it —
            make sure the tracker is flashed with the <code>Config.h</code> you
            just saved, or it will stop confirming deliveries.
          </div>
        ) : (
          <>
            <div className="banner banner--warning" role="status">
              <p>
                <strong>Nothing has been saved yet.</strong> The private key
                below exists only in this page. Two steps remain:
              </p>
              <ol>
                <li>
                  {isFileSaved ? '✓ ' : ''}Download or copy the{' '}
                  <code>Config.h</code> below — it now contains this private key.
                </li>
                <li>
                  Then activate the key here, which tells the API to start using
                  its public half.
                </li>
              </ol>
              <p>
                Leaving now costs nothing: the device keeps its current key and
                the server is untouched.
              </p>
            </div>

            <div className="provisioning-actions">
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => void handleActivate()}
                disabled={!isFileSaved || isActivating}
              >
                {isActivating ? 'Activating…' : 'I have saved the file — activate this key'}
              </button>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => void handleGenerate()}
                disabled={isGenerating}
              >
                Generate a different pair
              </button>
              {!isFileSaved ? (
                <span className="hint">Save the file first.</span>
              ) : null}
            </div>
          </>
        )}

        {keyError ? (
          <div className="banner banner--error" role="alert">{keyError}</div>
        ) : null}
      </div>

      {/* ================================================================
       * The file itself
       * ================================================================ */}
      <div className="provisioning-actions">
        <button type="button" className="btn btn-primary btn-sm" onClick={handleDownload}>
          Download Config.h
        </button>
        <button type="button" className="btn btn-secondary btn-sm" onClick={() => void handleCopy()}>
          {copyState === 'copied' ? '✓ Copied' : 'Copy to clipboard'}
        </button>
        {copyState === 'failed' ? (
          <span className="hint" role="status">
            Copying is not available here — download it, or select the text below.
          </span>
        ) : null}
      </div>

      <pre className="provisioning-snippet" aria-label="Config.h contents">
        {configFile}
      </pre>

      <p className="hint">
        Only public keys appear above. The receiver private key — the one that
        decrypts positions — stays encrypted in the API database and never leaves
        the server, so neither this device nor the broker can read back its own
        positions. The ack private key is the mirror image: it is generated here,
        belongs to the device, and is never sent to the server.
      </p>
    </div>
  )
}
