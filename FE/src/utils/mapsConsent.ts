// ---------------------------------------------------------------------------
// Whether this viewer has agreed to load the Google map — the version of the
// prompt, and the share-visitor half of where the answer is kept.
//
// Loading the Maps JavaScript API tells Google LLC the viewer's IP address,
// browser details and referrer, and — because the viewport is centred on the
// fixes — roughly where the tracked vehicle is. That is a transfer of personal
// data to a third country, so nothing reaches Google until somebody chooses.
//
// "Always" is remembered in one of two places, depending on who is looking:
//
//   * A signed-in user — on the ACCOUNT (PUT /api/me/maps-consent, read through
//     hooks/useAccountMapsConsent.ts). The person who agreed is the account
//     holder, the prompt tells them the answer covers every device they sign in
//     on, and so asking again on each new browser would be asking a question
//     they have already answered. It also leaves a server-side record of when
//     they agreed and to which wording, which is what GDPR Art. 7(1) asks for.
//
//   * A share-link visitor — in a COOKIE on this browser, written below. They
//     have no account to hang it on, and the server deliberately stores nothing
//     about them. The cookie holds only the prompt version, is scoped to the
//     share page's path so it never travels with an /api request, and lapses
//     after 180 days. Storing a choice the visitor explicitly made is a
//     user-requested preference under ePrivacy Art. 5(3), so it needs no banner
//     of its own — but withdrawing it must be as easy as giving it (Art. 7(3)),
//     which is why the share page offers that next to the map.
//
// The "just this once" button stores nothing anywhere: it lives in component
// state and dies with the tab.
//
// MAPS_CONSENT_VERSION versions the prompt's wording. Bump it when the text
// changes materially: a stored answer to an older version counts as no answer,
// on the account and in the cookie alike, so everybody is asked again.
// ---------------------------------------------------------------------------

import { BASE_PATH } from '../services/runtimeConfig'

export const MAPS_CONSENT_VERSION = '2026-10-09'

// Google's own terms for people who use a map embedded through the Maps
// Platform. The Platform terms require that end users are bound by the first and
// pointed to the second; share visitors never accept this site's terms of use,
// so the prompt itself is where they meet them.
export const GOOGLE_MAPS_TERMS_URL = 'https://maps.google.com/help/terms_maps/'
export const GOOGLE_PRIVACY_POLICY_URL = 'https://policies.google.com/privacy'

// Same `carpos_` prefix as carpos_session, carpos_csrf and carpos_share.
const MAPS_CONSENT_COOKIE_NAME = 'carpos_maps_consent'

// Six months. Long enough that somebody sent links by the same person over a
// season is not asked every time; short enough that an old "yes" does not
// outlive the visitor's memory of having given it.
const MAPS_CONSENT_COOKIE_MAX_AGE_SEC = 180 * 24 * 60 * 60

// Where the answer used to live for everybody, per browser. Not carried over:
// it was given for one browser, and recording it as an account-wide agreement
// would put words in somebody's mouth.
const LEGACY_STORAGE_KEY = 'carpos.mapsConsent'

export function isCurrentMapsConsentVersion(version: string | null | undefined): boolean {
  return version === MAPS_CONSENT_VERSION
}

export function hasBrowserMapsConsent(): boolean {
  try {
    const match = document.cookie.match(new RegExp(`(?:^|;\\s*)${MAPS_CONSENT_COOKIE_NAME}=([^;]*)`))
    return match !== null && isCurrentMapsConsentVersion(decodeURIComponent(match[1]))
  } catch {
    // Cookies blocked. Failing closed is the right way round here: the cost of
    // being asked again is a click, the cost of assuming consent nobody gave is
    // a transfer that should not have happened.
    return false
  }
}

export function grantBrowserMapsConsent(): void {
  writeCookie(encodeURIComponent(MAPS_CONSENT_VERSION), MAPS_CONSENT_COOKIE_MAX_AGE_SEC)
}

export function revokeBrowserMapsConsent(): void {
  writeCookie('', 0)
}

// Drops the old per-browser answer. Called once at start-up.
export function forgetLegacyMapsConsent(): void {
  try {
    window.localStorage.removeItem(LEGACY_STORAGE_KEY)
  } catch {
    // Nothing to remove if storage is unavailable — it was never written.
  }
}

function writeCookie(value: string, maxAgeSec: number): void {
  // Secure only over https: the dev server is plain http on localhost, and a
  // Secure cookie there would silently not be stored.
  const secure: string = window.location.protocol === 'https:' ? '; Secure' : ''

  try {
    document.cookie =
      `${MAPS_CONSENT_COOKIE_NAME}=${value}; Path=${BASE_PATH}/share; Max-Age=${maxAgeSec}; SameSite=Strict${secure}`
  } catch {
    // The map still loads for this visit; only the memory of the choice is
    // lost, which is a worse experience rather than a privacy problem.
  }
}
