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
import { Trans, useTranslation } from 'react-i18next'
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
  const { t } = useTranslation(['settings', 'common', 'errors'])

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
      setKeyError(describeError(error, t('errors:generateKeyFailed')))
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
      setKeyError(describeError(error, t('errors:storeAckKeyFailed')))
    } finally {
      setIsActivating(false)
    }
  }

  return (
    <div className="add-device-panel" style={{ marginTop: 16 }}>
      <h3>{title ?? t('settings:provisioning.title')}</h3>
      <p>
        <Trans i18nKey="provisioning.intro" ns="settings" components={{ code: <code /> }} />
      </p>

      <dl className="provisioning-facts">
        <dt>{t('settings:provisioning.deviceId')}</dt>
        <dd><code>{provisioning.deviceId}</code></dd>

        <dt>{t('settings:provisioning.telemetryTopic')}</dt>
        <dd><code>{provisioning.telemetryTopic}</code></dd>

        <dt>{t('settings:provisioning.configTopic')}</dt>
        <dd><code>{provisioning.configTopic}</code></dd>

        <dt>{t('settings:provisioning.ackTopic')}</dt>
        <dd><code>{provisioning.ackTopic}</code></dd>

        <dt>{t('settings:provisioning.broker')}</dt>
        <dd><code>{provisioning.brokerUri}</code></dd>

        <dt>{t('settings:provisioning.publicKeyFingerprint')}</dt>
        {/* SHA-256 of the SPKI bytes. Comparing this against what the flashed
            firmware reports confirms the device carries the right key without
            either side handling key material. */}
        <dd><code style={{ wordBreak: 'break-all' }}>{provisioning.publicKeyFingerprint}</code></dd>

        <dt>{t('settings:provisioning.ackKeyFingerprint')}</dt>
        {/* Null until an ack public key is stored. Saying so explicitly matters:
            firmware flashed with acks enabled against a device that has no ack key
            would retry every fix forever, and the cause would be invisible here. */}
        <dd>
          {activatedFingerprint !== null ? (
            <code style={{ wordBreak: 'break-all' }}>{activatedFingerprint}</code>
          ) : provisioning.ackPublicKeyFingerprint === null ? (
            <span className="hint">{t('settings:provisioning.ackNotConfigured')}</span>
          ) : (
            <code style={{ wordBreak: 'break-all' }}>{provisioning.ackPublicKeyFingerprint}</code>
          )}
        </dd>
      </dl>

      {/* ================================================================
       * Secrets — typed here, woven in here, never uploaded
       * ================================================================ */}
      <div className="config-secrets">
        <h4>{t('settings:secrets.title')}</h4>
        <p className="hint">
          <Trans i18nKey="secrets.intro" ns="settings" components={{ strong: <strong /> }} />
        </p>

        <div className="config-grid">
          <div className="form-field">
            <label className="form-label" htmlFor="config-wifi-ssid">{t('settings:secrets.wifiSsid')}</label>
            <input
              id="config-wifi-ssid"
              className="form-input"
              type="text"
              value={secrets.wifiSsid}
              onChange={(event) => updateSecret('wifiSsid', event.target.value)}
              autoComplete="off"
              spellCheck={false}
            />
            {/* Explaining the flip, because it is a change the file makes on
                your behalf and a silent one would be surprising. */}
            <span className="hint">
              <Trans i18nKey="secrets.wifiSsidHint" ns="settings" components={{ code: <code /> }} />
            </span>
          </div>

          <div className="form-field">
            <label className="form-label" htmlFor="config-wifi-password">{t('settings:secrets.wifiPassword')}</label>
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
            <label className="form-label" htmlFor="config-mqtt-password">{t('settings:secrets.mqttPassword')}</label>
            <input
              id="config-mqtt-password"
              className="form-input"
              type={arePasswordsVisible ? 'text' : 'password'}
              value={secrets.mqttPassword}
              onChange={(event) => updateSecret('mqttPassword', event.target.value)}
              autoComplete="new-password"
            />
            <span className="hint">
              <Trans
                i18nKey="secrets.mqttPasswordHint"
                ns="settings"
                values={{ deviceId: provisioning.deviceId }}
                components={{ code: <code /> }}
              />
            </span>
          </div>
        </div>

        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={arePasswordsVisible}
            onChange={(event) => setArePasswordsVisible(event.target.checked)}
          />
          <span>{t('settings:secrets.showPasswords')}</span>
        </label>
      </div>

      {/* ================================================================
       * Ack key pair — generate, save, then activate. In that order.
       * ================================================================ */}
      <div className="config-secrets">
        <h4>{t('settings:ackKey.title')}</h4>
        <p className="hint">{t('settings:ackKey.intro')}</p>

        {keyPair === null ? (
          <>
            <p className="hint">
              <Trans
                i18nKey="ackKey.warning"
                ns="settings"
                components={{ strong: <strong />, code: <code /> }}
              />
            </p>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={() => void handleGenerate()}
              disabled={isGenerating || !canGenerate}
            >
              {isGenerating ? t('settings:ackKey.generating') : t('settings:ackKey.generate')}
            </button>
            {!canGenerate ? (
              <p className="hint">
                <Trans i18nKey="ackKey.insecureContext" ns="settings" components={{ code: <code /> }} />
              </p>
            ) : null}
          </>
        ) : activatedFingerprint !== null ? (
          <div className="banner banner--success" role="status">
            <Trans i18nKey="ackKey.activated" ns="settings" components={{ code: <code /> }} />
          </div>
        ) : (
          <>
            <div className="banner banner--warning" role="status">
              <p>
                <Trans i18nKey="ackKey.unsavedTitle" ns="settings" components={{ strong: <strong /> }} />
              </p>
              <ol>
                <li>
                  {isFileSaved ? '✓ ' : ''}
                  <Trans i18nKey="ackKey.stepSave" ns="settings" components={{ code: <code /> }} />
                </li>
                <li>{t('settings:ackKey.stepActivate')}</li>
              </ol>
              <p>{t('settings:ackKey.leavingIsSafe')}</p>
            </div>

            <div className="provisioning-actions">
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => void handleActivate()}
                disabled={!isFileSaved || isActivating}
              >
                {isActivating ? t('settings:ackKey.activating') : t('settings:ackKey.activate')}
              </button>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => void handleGenerate()}
                disabled={isGenerating}
              >
                {t('settings:ackKey.regenerate')}
              </button>
              {!isFileSaved ? (
                <span className="hint">{t('settings:ackKey.saveFirst')}</span>
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
          {t('settings:provisioning.download')}
        </button>
        <button type="button" className="btn btn-secondary btn-sm" onClick={() => void handleCopy()}>
          {copyState === 'copied'
            ? `✓ ${t('common:actions.copied')}`
            : t('settings:provisioning.copy')}
        </button>
        {copyState === 'failed' ? (
          <span className="hint" role="status">{t('settings:provisioning.copyFailed')}</span>
        ) : null}
      </div>

      <pre className="provisioning-snippet" aria-label={t('settings:provisioning.fileContents')}>
        {configFile}
      </pre>

      <p className="hint">{t('settings:provisioning.keyNote')}</p>
    </div>
  )
}
