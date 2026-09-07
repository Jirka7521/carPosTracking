// ---------------------------------------------------------------------------
// Whether this browser has agreed to load the Google map.
//
// Loading the Maps JavaScript API tells Google LLC the viewer's IP address,
// browser details and referrer, and — because the viewport is centred on the
// fixes — roughly where the tracked vehicle is. That is a transfer of personal
// data to a third country, and it used to happen the instant a device's map tab
// was opened, with nothing asked and nothing said.
//
// So the choice is stored here, per browser, alongside the language and CSV
// preferences that already live under the `carpos.` prefix. It is deliberately
// NOT stored on the server: it is a preference of the person looking at the
// screen, not a property of the account, and syncing it would mean one device's
// answer silently speaking for another.
//
// Three states, and the middle one matters: "always" loads without asking,
// "never asked" shows the gate, and a session-only agreement (the "just this
// once" button) lives in memory and dies with the tab.
// ---------------------------------------------------------------------------

// Same `carpos.` namespace as carpos.language and carpos.csvDelimiter.
const MAPS_CONSENT_STORAGE_KEY = 'carpos.mapsConsent'

// The only value that counts as standing consent. Anything else — absent,
// corrupted, left over from an older format — means "ask".
const CONSENT_GRANTED = 'always'

export function hasStandingMapsConsent(): boolean {
  try {
    return window.localStorage.getItem(MAPS_CONSENT_STORAGE_KEY) === CONSENT_GRANTED
  } catch {
    // Private mode, or storage blocked. Failing closed is the right way round
    // here: the cost of being asked again is a click, the cost of assuming
    // consent nobody gave is a transfer that should not have happened.
    return false
  }
}

export function grantStandingMapsConsent(): void {
  try {
    window.localStorage.setItem(MAPS_CONSENT_STORAGE_KEY, CONSENT_GRANTED)
  } catch {
    // The map still loads for this session; only the memory of the choice is
    // lost, which is a worse experience rather than a privacy problem.
  }
}

export function revokeMapsConsent(): void {
  try {
    window.localStorage.removeItem(MAPS_CONSENT_STORAGE_KEY)
  } catch {
    // Nothing to remove if storage is unavailable — it was never written.
  }
}
