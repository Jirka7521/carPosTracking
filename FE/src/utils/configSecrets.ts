// ---------------------------------------------------------------------------
// Weaves the operator's secrets into the Config.h the API rendered — in the
// browser, which is the whole point.
//
// The API deliberately emits four constants empty: the two WiFi credentials and
// the broker password (which it never issued and cannot know), and
// kDeviceAckPrivateKeyPem, which is the device's own secret and must never
// reach the server at all. Doing the substitution here means none of the four
// is ever sent anywhere: they are typed into a form, spliced into a string, and
// downloaded to disk without a single request carrying them.
//
// Everything in this module is a pure function of its arguments — no state, no
// API calls, no storage. That is deliberate: a secret that is never written
// down cannot be leaked by a later bug.
//
// The substitutions are anchored regexes on the constant *name*, not on the
// exact line the API happens to emit, so the template's column alignment can
// change without silently breaking this. A constant that does not match is left
// alone rather than throwing: the file is shown to the user in full, so a
// missed value is visible as an empty literal rather than hidden behind an
// error nobody can act on.
// ---------------------------------------------------------------------------

export type ConfigSecrets = {
  wifiSsid: string
  wifiPassword: string
  mqttPassword: string
  // The private half of a freshly generated ack pair, or null when the operator
  // has not generated one — in which case the file keeps the empty literal and
  // kAckEnabled is left exactly as the API rendered it.
  ackPrivateKeyPem: string | null
}

export const EMPTY_SECRETS: ConfigSecrets = {
  wifiSsid: '',
  wifiPassword: '',
  mqttPassword: '',
  ackPrivateKeyPem: null,
}

// Indent of a continued C string literal — matches the firmware's own style and
// the API's ConfigSnippetBuilder, so a key pasted in here is indistinguishable
// from one the server rendered.
const LITERAL_INDENT = '    '

// Returns the file with every supplied secret filled in. Values left blank stay
// blank, so a partially completed form still produces valid C++.
export function fillSecrets(configFile: string, secrets: ConfigSecrets): string {
  let filled = configFile

  filled = setStringConstant(filled, 'kWifiSsid', secrets.wifiSsid)
  filled = setStringConstant(filled, 'kWifiPassword', secrets.wifiPassword)
  filled = setStringConstant(filled, 'kMqttPassword', secrets.mqttPassword)

  // The API renders kWifiEnabled false because it cannot know any credentials.
  // An SSID here is the operator saying otherwise — and without this the station
  // would stay off no matter what they typed, which reads as a bug.
  if (secrets.wifiSsid.trim() !== '') {
    filled = setBoolConstant(filled, 'kWifiEnabled', true)
  }

  if (secrets.ackPrivateKeyPem !== null) {
    filled = setPemConstant(filled, 'kDeviceAckPrivateKeyPem', secrets.ackPrivateKeyPem)
    // A device holding the private key can read acks, so turn them on. The
    // server side of this only becomes true once the key is activated — which
    // is why the panel refuses to activate before the file has been saved.
    filled = setBoolConstant(filled, 'kAckEnabled', true)
  }

  return filled
}

// Renders a PEM as the firmware's multi-line quoted literal: one quoted,
// \n-terminated line per PEM line. Mirrors ConfigSnippetBuilder.BuildPemLiteral
// on the API side — the two must agree, because the firmware parses whatever
// the C compiler reassembles from it.
export function pemToCLiteral(pem: string): string {
  const lines = pem
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line !== '')

  // No escaping pass is needed: a PEM body is base64 and its delimiters are
  // dashes and spaces, so it can contain neither a quote nor a backslash.
  return lines.map((line) => `${LITERAL_INDENT}"${line}\\n"`).join('\n')
}

// Replaces `constexpr char <name>[] = "";` with the same line carrying a value.
function setStringConstant(configFile: string, name: string, value: string): string {
  if (value === '') {
    return configFile
  }

  const pattern = new RegExp(`^(constexpr char ${name}\\[\\]\\s*=\\s*)""`, 'm')

  // A function replacement, not a '$1' string: a value containing '$&' or '$1'
  // would otherwise be interpreted as a backreference — and a WiFi password is
  // exactly the kind of string that contains punctuation nobody thought about.
  return configFile.replace(pattern, (_match, prefix: string) => `${prefix}"${escapeCString(value)}"`)
}

// Replaces `constexpr bool <name> = true|false;` with the given value.
function setBoolConstant(configFile: string, name: string, value: boolean): string {
  const pattern = new RegExp(`^(constexpr bool ${name}\\s*=\\s*)(?:true|false)`, 'm')

  return configFile.replace(pattern, (_match, prefix: string) => `${prefix}${value ? 'true' : 'false'}`)
}

// Replaces an empty `constexpr char <name>[] = "";` with a multi-line PEM
// literal spread over the following lines, the last carrying the semicolon.
function setPemConstant(configFile: string, name: string, pem: string): string {
  const pattern = new RegExp(`^constexpr char ${name}\\[\\]\\s*=\\s*"";`, 'm')

  const literal = `constexpr char ${name}[] =\n${pemToCLiteral(pem)};`

  return configFile.replace(pattern, () => literal)
}

// Escapes the only two characters a C string literal cannot carry raw. Newlines
// are stripped rather than escaped: these values come from single-line inputs,
// so one arriving here means a paste accident, and a literal \n in an SSID would
// be a bug the firmware could not diagnose.
function escapeCString(value: string): string {
  return value
    .replace(/\r?\n/g, '')
    .replace(/\\/g, '\\\\')
    .replace(/"/g, '\\"')
}
