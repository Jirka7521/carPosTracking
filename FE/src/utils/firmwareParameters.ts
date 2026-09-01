// ---------------------------------------------------------------------------
// The shape of the firmware reference table, and the one thing about it that
// cannot be read out of the config file itself.
//
// The rows used to live here too — a hand transcription of ESP32/README.md and
// Config.example.h, 231 lines of it, missing fifteen constants by the time it
// was replaced. They are parsed out of the rendered Config.h now; see
// parseFirmwareConfig.ts for why and how.
//
// What remains is `origin`: whether a value is filled in for this device, is a
// secret the operator supplies, or is only a default that the Reporting & Power
// settings override at runtime. Nothing in the file says which — a constant
// looks the same either way — and it is exactly what a reader needs in order to
// know whether a number in front of them is the one actually in force.
// ---------------------------------------------------------------------------

export type ParameterOrigin = 'device' | 'secret' | 'remote' | 'fixed'

export type FirmwareParameter = {
  // The C++ constant, exactly as it appears in Config.h.
  name: string
  // Its value in this device's file, as rendered.
  value: string
  meaning: string
  origin: ParameterOrigin
}

export type FirmwareParameterGroup = {
  title: string
  parameters: readonly FirmwareParameter[]
}

// The constants whose origin is not simply 'fixed'.
//
// Kept deliberately small: every entry is a claim about the system that has to
// stay true, so anything that can default to 'fixed' does. The three groups
// mirror what the API's ConfigSnippetBuilder does with each constant — 'device'
// is what it fills in per device, 'secret' is what it deliberately leaves blank
// for the browser, and 'remote' is a compile-time default that the retained
// settings document replaces at runtime.
export const PARAMETER_ORIGINS: Readonly<Record<string, ParameterOrigin>> = {
  // Filled in for this tracker by the provisioning endpoint.
  kDeviceId: 'device',
  kTelemetryTopic: 'device',
  kConfigTopic: 'device',
  kAckTopic: 'device',
  kMqttBrokerUri: 'device',
  kMqttUsername: 'device',
  kMqttClientId: 'device',
  kReceiverPublicKeyPem: 'device',
  // Rendered from whether an ack key has been imported for this device.
  kAckEnabled: 'device',

  // Never known to the server; woven in by the browser.
  kWifiSsid: 'secret',
  kWifiPassword: 'secret',
  kMqttPassword: 'secret',
  kDeviceAckPrivateKeyPem: 'secret',
  // Rendered off, and flipped on by the browser when an SSID is typed in.
  kWifiEnabled: 'secret',

  // Compile-time defaults only — Reporting & Power above is what sets these on
  // a running tracker, so the value shown here is what it falls back to.
  kDefaultSendIntervalSeconds: 'remote',
  kDefaultSleepBetweenSends: 'remote',
  kDefaultConfigCheckSeconds: 'remote',
  kFixAcquireTimeoutSeconds: 'remote',
  kSdMaxQueuedFixes: 'remote',
  kRetryIntervalHours: 'remote',
  kRetryMaxAgeHours: 'remote',
}

// Headings the config file does not supply itself.
//
// The table's groups come from the firmware's own `// ----` section banners, and
// they are right everywhere but one place: the reporting defaults and their
// clamps sit physically after the delivery-ack block with no banner of their own,
// so without this they file themselves under "Delivery acknowledgements", which
// they have nothing to do with.
//
// This is a heading override, NOT a row list — a constant added to Config.h still
// appears with no edit here. Keep it to genuine mis-filings; a heading that is
// merely terse is the file's own voice and better left alone.
export const GROUP_TITLE_BEFORE: Readonly<Record<string, string>> = {
  kDefaultSendIntervalSeconds: 'Reporting defaults & accepted ranges',
}

// Short badge text for the origin column.
export const ORIGIN_LABELS: Record<ParameterOrigin, string> = {
  device: 'this device',
  secret: 'you supply',
  remote: 'default only',
  fixed: 'compile-time',
}
