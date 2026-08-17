// ---------------------------------------------------------------------------
// Every parameter the firmware is built with, for the read-only reference table
// in the Settings tab.
//
// This is a TRANSCRIPTION of the config table in ESP32/README.md (the
// "Configuration" section) and of ESP32/src/config/Config.example.h. It is
// static on purpose: these are compile-time constants, so there is nothing to
// fetch and nothing to change from here — the table exists to answer "what is
// this tracker actually built with?" without opening the firmware repo.
//
// KEEPING IT HONEST: when the firmware gains, loses or re-values a constant,
// this list and the API's Services/Provisioning/ConfigTemplate.h.txt both need
// the same edit. The API side has a test that fails when it drifts; this side
// does not, so the header of that template names this file too.
//
// The `origin` of a row is what a reader actually needs to know about it:
//   'device' — filled in per device; the real value comes from the provisioning
//              payload, so the table shows what THIS tracker uses
//   'secret' — supplied by the operator, never known to the server
//   'remote' — a compile-time DEFAULT that the Reporting & Power settings
//              override at runtime, so the live value lives further up the page
//   'fixed'  — the same for every device unless the firmware is edited
// ---------------------------------------------------------------------------

import type { DeviceProvisioningDto } from '../services/apiTypes'

export type ParameterOrigin = 'device' | 'secret' | 'remote' | 'fixed'

// The provisioning fields a 'device' row can be filled from. Narrowed to the
// ones that are plain strings, so the table can render them without a cast.
export type ProvisioningField =
  | 'deviceId'
  | 'telemetryTopic'
  | 'configTopic'
  | 'ackTopic'
  | 'brokerUri'
  | 'publicKeyFingerprint'
  | 'ackPublicKeyFingerprint'

export type FirmwareParameter = {
  // The C++ constant, exactly as it appears in Config.h.
  name: string
  // What the firmware ships with. For 'device' rows this is the fallback shown
  // when no provisioning payload is loaded; `field` supplies the real one.
  value: string
  meaning: string
  origin: ParameterOrigin
  field?: ProvisioningField
}

export type FirmwareParameterGroup = {
  title: string
  parameters: readonly FirmwareParameter[]
}

export const FIRMWARE_PARAMETER_GROUPS: readonly FirmwareParameterGroup[] = [
  {
    title: 'Modem & UART',
    parameters: [
      { name: 'kModemUartPort', value: 'UART_NUM_1', origin: 'fixed', meaning: 'UART used for the modem (UART0 is the USB console)' },
      { name: 'kModemTxPin / kModemRxPin', value: '27 / 26', origin: 'fixed', meaning: 'T-SIM7000G factory pins' },
      { name: 'kModemBaudRate', value: '9600', origin: 'fixed', meaning: 'SIM7000G default; tried first at start-up' },
      { name: 'kModemBaudCandidates', value: '115200, 9600, 57600, 38400, 19200', origin: 'fixed', meaning: 'Fallback rates probed if the modem stays silent' },
      { name: 'kModemPwrKeyPin', value: '4', origin: 'fixed', meaning: 'PWRKEY — powers the whole modem on and off' },
      { name: 'kModemPwrKeyActiveLow', value: 'true', origin: 'fixed', meaning: 'PWRKEY polarity: idles HIGH, pulses LOW' },
    ],
  },
  {
    title: 'GNSS',
    parameters: [
      { name: 'kEnableGps / Glonass / Beidou / Galileo', value: 'all true', origin: 'fixed', meaning: 'Constellations fused for a faster, more accurate fix' },
      { name: 'kGnssDebug', value: 'true', origin: 'fixed', meaning: 'Print the full decoded fix and per-constellation satellite counts on every read' },
      { name: 'kSatelliteScanMs', value: '3000', origin: 'fixed', meaning: 'How long to listen to NMEA when counting satellites' },
      { name: 'kFixAcquireTimeoutSeconds', value: '180', origin: 'remote', meaning: 'Default for the GNSS lock timeout — give up on a cycle after this long' },
      { name: 'kFixPollStepMs', value: '2000', origin: 'fixed', meaning: 'Gap between AT+CGNSINF polls while acquiring' },
    ],
  },
  {
    title: 'Accelerometer (ADXL345)',
    parameters: [
      { name: 'kAdxlEnabled', value: 'true', origin: 'fixed', meaning: 'Include the accelerometer; when false the driver is compiled out and the fields are absent' },
      { name: 'kI2cSdaPin / kI2cSclPin', value: '21 / 22', origin: 'fixed', meaning: 'I2C data and clock GPIOs' },
      { name: 'kI2cClockHz', value: '400000', origin: 'fixed', meaning: 'I2C bus speed (fast mode)' },
      { name: 'kAdxlI2cAddress', value: '0x53', origin: 'fixed', meaning: 'ADXL345 address (CS→3V3, SDO→GND)' },
      { name: 'kAdxlInt1Pin / kAdxlInt2Pin', value: '32 / 33', origin: 'fixed', meaning: 'INT pins — wired but reserved; interrupts are not used yet' },
    ],
  },
  {
    title: 'Battery & temperature',
    parameters: [
      { name: 'kBatteryEnabled', value: 'true', origin: 'fixed', meaning: 'Report pack charge and modem die temperature (both read over AT)' },
      { name: 'kBatteryChargeSensePin', value: '35', origin: 'fixed', meaning: 'Charge-sense ADC pin; reads ~0 while charging' },
      { name: 'kBatteryChargeAdcThreshold', value: '200', origin: 'fixed', meaning: 'Raw ADC counts below which the device reports the "charging" sentinel (0 %)' },
      { name: 'kBatteryEmptyMv / kBatteryFullMv', value: '3300 / 4200', origin: 'fixed', meaning: 'Ends of the Li-ion state-of-charge curve (≤ empty → 1 %, ≥ full → 100 %)' },
    ],
  },
  {
    title: 'WiFi',
    parameters: [
      { name: 'kWifiEnabled', value: 'false until an SSID is set', origin: 'secret', meaning: 'Rendered off, because a station with no credentials waits out the connect timeout on every boot' },
      { name: 'kWifiSsid / kWifiPassword', value: 'you supply these', origin: 'secret', meaning: 'Your network. Typed into the form above and woven in by your browser' },
      { name: 'kWifiConnectTimeoutMs', value: '15000', origin: 'fixed', meaning: 'Max wait for an IP before giving up an attempt' },
      { name: 'kWifiMaxRetries', value: '5', origin: 'fixed', meaning: 'Fast retries in a burst before the attempt is deemed failed' },
      { name: 'kWifiReconnectIntervalMs', value: '30000', origin: 'fixed', meaning: 'Background reconnect interval after a failed burst' },
    ],
  },
  {
    title: 'MQTT & identity',
    parameters: [
      { name: 'kMqttEnabled', value: 'true', origin: 'fixed', meaning: 'Publish over MQTT; when false the publish path is compiled out entirely' },
      { name: 'kDeviceId', value: '—', origin: 'device', field: 'deviceId', meaning: 'This device’s MQTT identity — also its broker username, client id and the id inside every payload' },
      { name: 'kMqttBrokerUri', value: '—', origin: 'device', field: 'brokerUri', meaning: 'Broker URI; the scheme sets transport and TLS (wss:// = encrypted WebSocket)' },
      { name: 'kMqttUsername', value: '—', origin: 'device', field: 'deviceId', meaning: 'Broker login name — the device id. Not a secret' },
      { name: 'kMqttPassword', value: 'you supply this', origin: 'secret', meaning: 'Broker password. Created by hand on the server with mosquitto_passwd; the API never issues MQTT credentials' },
      { name: 'kMqttClientId', value: '—', origin: 'device', field: 'deviceId', meaning: 'Client id shown in the broker’s logs' },
      { name: 'kTelemetryTopic', value: '—', origin: 'device', field: 'telemetryTopic', meaning: 'Topic each encrypted fix is published to' },
      { name: 'kMqttPublishAckTimeoutMs', value: '8000', origin: 'fixed', meaning: 'How long to wait for the broker’s QoS-2 delivery ack' },
    ],
  },
  {
    title: 'Remote settings',
    parameters: [
      { name: 'kConfigTopic', value: '—', origin: 'device', field: 'configTopic', meaning: 'Topic the retained settings document is read from' },
      { name: 'kConfigFetchTimeoutMs', value: '8000', origin: 'fixed', meaning: 'Wait for the retained config at start-up (covers connect + TLS)' },
      { name: 'kDefaultSendIntervalSeconds', value: '60', origin: 'remote', meaning: 'Default reporting interval, used until the broker says otherwise' },
      { name: 'kDefaultSleepBetweenSends', value: 'false', origin: 'remote', meaning: 'Default deep-sleep flag' },
      { name: 'kDefaultConfigCheckSeconds', value: '3600', origin: 'remote', meaning: 'Default backstop interval for re-asking the broker for the settings' },
      { name: 'kMinSendIntervalSeconds / kMax…', value: '5 / 86400', origin: 'fixed', meaning: 'Clamps on a broker-supplied interval — the same bounds this API rejects outside of' },
      { name: 'kMinFixTimeoutSeconds / kMax…', value: '15 / 900', origin: 'fixed', meaning: 'Clamps on the GNSS lock timeout' },
      { name: 'kMinQueueMaxFixes / kMax…', value: '100 / 100000', origin: 'fixed', meaning: 'Clamps on the undelivered-fix queue cap' },
      { name: 'kMinRetryIntervalHours / kMax…', value: '1 / 720', origin: 'fixed', meaning: 'Clamps on the rejected-fix retry interval' },
      { name: 'kMaxRetryMaxAgeHours', value: '8760', origin: 'fixed', meaning: 'Upper clamp on the give-up age (no floor — 0 means "never")' },
      { name: 'kMinConfigCheckSeconds / kMax…', value: '60 / 86400', origin: 'fixed', meaning: 'Clamps on the settings re-check interval' },
    ],
  },
  {
    title: 'Delivery acks',
    parameters: [
      { name: 'kAckEnabled', value: 'on once an ack key exists', origin: 'device', meaning: 'Only clear a fix from the SD card once the API confirms it was stored — a broker ack alone does not prove that' },
      { name: 'kAckTopic', value: '—', origin: 'device', field: 'ackTopic', meaning: 'Topic the API publishes its encrypted delivery verdicts to' },
      { name: 'kAckTimeoutMs', value: '10000', origin: 'fixed', meaning: 'Wait for the API’s verdict (covers decrypt, validate and the database write)' },
      { name: 'kDeviceAckPrivateKeyPem', value: 'generated in your browser', origin: 'secret', meaning: 'This device’s own private key, which opens the acks. Never sent to the server — the server holds only its public half' },
    ],
  },
  {
    title: 'End-to-end encryption',
    parameters: [
      { name: 'kReceiverPublicKeyPem', value: '—', origin: 'device', field: 'publicKeyFingerprint', meaning: 'Receiver public key that every fix is encrypted to (shown here by fingerprint). Its private half never leaves the API database' },
      { name: 'ack public key on file', value: '—', origin: 'device', field: 'ackPublicKeyFingerprint', meaning: 'Fingerprint of the ack key the server currently seals verdicts with' },
    ],
  },
  {
    title: 'microSD store-and-forward',
    parameters: [
      { name: 'kSdEnabled', value: 'true', origin: 'fixed', meaning: 'Queue undelivered fixes on the card; when false they are simply dropped' },
      { name: 'kSdSpiHost', value: 'SPI2_HOST', origin: 'fixed', meaning: 'SPI peripheral the card is wired to (HSPI)' },
      { name: 'kSdPinMiso / Mosi / Sclk / Cs', value: '2 / 15 / 14 / 13', origin: 'fixed', meaning: 'T-SIM7000G microSD SPI pins' },
      { name: 'kSdMountPoint', value: '/sdcard', origin: 'fixed', meaning: 'Where the FAT filesystem is mounted' },
      { name: 'kSdQueueFilePath', value: '/sdcard/queue.jsonl', origin: 'fixed', meaning: 'Queue file — one encrypted envelope per line, so a stolen card leaks nothing' },
      { name: 'kSdSettingsFilePath', value: '/sdcard/settings.json', origin: 'fixed', meaning: 'Cached copy of the runtime settings (plaintext — it holds no position data)' },
      { name: 'kSdMaxQueuedFixes', value: '20000', origin: 'remote', meaning: 'Default cap on stored fixes; the oldest are dropped past this' },
      { name: 'kSdMaxBurstFixes', value: '40', origin: 'fixed', meaning: 'Max envelopes per burst message — a RAM and MQTT-buffer safety bound' },
      { name: 'kBacklogFlushRetryMs', value: '600000', origin: 'fixed', meaning: 'Pause after a backlog flush that did not fully drain (an MQTT reconnect cancels it)' },
      { name: 'kSdRetryFilePath', value: '/sdcard/retry.jsonl', origin: 'fixed', meaning: 'Fixes the API rejected, held apart from the live queue' },
      { name: 'kRetryIntervalHours', value: '24', origin: 'remote', meaning: 'Default wait between attempts on a rejected fix' },
      { name: 'kRetryMaxAgeHours', value: '168', origin: 'remote', meaning: 'Default give-up age for a still-rejected fix (0 = never)' },
      { name: 'kSdMaxRetryEntries', value: '2000', origin: 'fixed', meaning: 'Cap on the retry file — small, because a healthy system rejects nothing' },
    ],
  },
  {
    title: 'Deep sleep',
    parameters: [
      { name: 'kWakeGpioPin', value: '-1', origin: 'fixed', meaning: 'Extra ext0 wake pin (ignition sense, motion line); -1 = timer only' },
      { name: 'kWakeGpioLevel', value: '1', origin: 'fixed', meaning: 'Pin level that wakes the chip (1 = HIGH)' },
      { name: 'kMinDeepSleepMs', value: '1000', origin: 'fixed', meaning: 'Floor on a sleep duration, so an overrunning cycle pauses rather than rebooting straight back' },
    ],
  },
]

// The value to show for one row: the real per-device value when there is one,
// the firmware default otherwise.
export function resolveParameterValue(
  parameter: FirmwareParameter,
  provisioning: DeviceProvisioningDto | null,
): string {
  if (parameter.field === undefined || provisioning === null) {
    return parameter.value
  }

  // Only ackPublicKeyFingerprint is nullable, and "none imported" is a real
  // state worth naming rather than showing as a dash.
  const value = provisioning[parameter.field]
  if (value === null) {
    return 'not configured'
  }

  return value
}

// Short badge text for the origin column.
export const ORIGIN_LABELS: Record<ParameterOrigin, string> = {
  device: 'this device',
  secret: 'you supply',
  remote: 'default only',
  fixed: 'compile-time',
}
