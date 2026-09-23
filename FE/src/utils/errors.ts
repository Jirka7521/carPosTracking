// Helper for turning any thrown value into a single string the UI can render.
//
// The API writes its error messages in English (ProblemDetails `detail`), and
// so does the browser for a failed fetch. Showing those verbatim put English
// sentences in the middle of an otherwise Czech page. So every message the API
// is known to send is matched here against a translation key, and rendered in
// the reader's language instead.
//
// Matching is on the exact English text, because that is all the API sends —
// there is no error code in the response. The table below therefore has to
// follow the server: a message that is reworded there stops matching here, and
// the reader then gets the caller's own (translated) fallback rather than
// untranslated text. English readers still see the server's message as-is.

import i18n from 'i18next'

type Translated = {
  // A `errors:` key. Kept as a plain string so the table stays one flat list;
  // every key it names is in errors.json, and `server.*` / `http.*` are in the
  // extractor's preservePatterns so they survive `npm run i18n:extract`.
  key: string
  // Values captured from the message and handed to the translation.
  params?: (match: RegExpExecArray) => Record<string, string | number>
}

type KnownMessage = Translated & { pattern: string | RegExp }

// Every user-facing message the API and apiClient.ts can produce. Strings match
// exactly; the few messages that carry a value are regular expressions.
const KNOWN_MESSAGES: readonly KnownMessage[] = [
  // ----- Accounts -----
  { pattern: 'Incorrect email or password.', key: 'errors:server.invalidCredentials' },
  { pattern: 'An account with that email address already exists.', key: 'errors:server.emailTaken' },
  { pattern: 'The privacy policy has changed since this page was opened. Please reload and read it again before registering.', key: 'errors:server.privacyPolicyChanged' },
  { pattern: 'No such user.', key: 'errors:server.noSuchUser' },
  { pattern: 'Your current password is not correct.', key: 'errors:server.wrongCurrentPassword' },
  { pattern: 'Your password is not correct.', key: 'errors:server.wrongPassword' },

  // ----- Devices -----
  { pattern: 'No such device.', key: 'errors:server.noSuchDevice' },
  { pattern: 'DeviceId may contain only letters, digits, hyphens and underscores.', key: 'errors:server.deviceIdFormat' },
  {
    pattern: /^A device with id '(.*)' is already registered\. Device ids are permanent\.$/,
    key: 'errors:server.deviceIdTaken',
    params: (match) => ({ deviceId: match[1] }),
  },
  { pattern: 'You must confirm that you are entitled to track this vehicle and will tell the people who drive it before a device can be registered.', key: 'errors:server.trackingDeclarationRequired' },
  { pattern: 'You do not have permission to delete this device.', key: 'errors:server.noPermissionDeleteDevice' },
  { pattern: "You do not have permission to delete this device's data.", key: 'errors:server.noPermissionDeleteData' },
  { pattern: "You do not have permission to view this device's firmware configuration.", key: 'errors:server.noPermissionViewFirmware' },
  { pattern: "You do not have permission to change this device's firmware configuration.", key: 'errors:server.noPermissionChangeFirmware' },
  { pattern: 'This device has no stored public key, so no firmware configuration can be rendered.', key: 'errors:server.noStoredPublicKey' },
  { pattern: 'No key was supplied.', key: 'errors:server.ackKeyMissing' },
  { pattern: /^That is a PRIVATE key\./, key: 'errors:server.ackKeyIsPrivate' },
  { pattern: 'That is not a valid PEM public key.', key: 'errors:server.ackKeyInvalid' },
  {
    pattern: /^The ack key is (\d+) bits; this system uses RSA-(\d+)\.$/,
    key: 'errors:server.ackKeyWrongSize',
    params: (match) => ({ bits: match[1], expected: match[2] }),
  },

  // ----- Device settings -----
  { pattern: "You do not have permission to view this device's settings.", key: 'errors:server.noPermissionViewSettings' },
  { pattern: "You do not have permission to change this device's settings.", key: 'errors:server.noPermissionChangeSettings' },
  { pattern: 'This device has been deleted, so its settings can no longer be changed.', key: 'errors:server.settingsDeviceDeleted' },
  { pattern: 'This device is on a schedule, so saving settings by hand only holds until the next scheduled switch. Confirm that you understand this, edit the profile the schedule uses, or turn the schedule off.', key: 'errors:server.scheduleOverrideNeedsConfirm' },
  { pattern: "This device's schedule never switches profiles, so a temporary change has nothing to expire at. Edit the profile it uses, or turn the schedule off.", key: 'errors:server.scheduleNeverSwitches' },
  { pattern: 'This device has no stored configuration to publish.', key: 'errors:server.noStoredConfigToPublish' },
  { pattern: 'This device has no stored configuration.', key: 'errors:server.noStoredConfig' },
  { pattern: /^The settings are saved, but the broker could not be reached/, key: 'errors:server.brokerUnavailable' },

  // ----- Schedule -----
  { pattern: /^You do not have permission to \w+ this device's schedule\.$/, key: 'errors:server.noPermissionSchedule' },
  { pattern: 'This device has been deleted, so its schedule can no longer be changed.', key: 'errors:server.scheduleDeviceDeleted' },
  { pattern: 'This device has no schedule to resume.', key: 'errors:server.noScheduleToResume' },
  { pattern: 'No such profile.', key: 'errors:server.noSuchProfile' },
  { pattern: 'No such rule.', key: 'errors:server.noSuchRule' },
  { pattern: 'That profile does not belong to this device.', key: 'errors:server.profileNotOwned' },
  { pattern: 'The fallback profile does not belong to this device.', key: 'errors:server.fallbackProfileNotOwned' },
  { pattern: 'Choose a fallback profile before enabling the schedule. It is what the device runs at any time no rule covers.', key: 'errors:server.fallbackProfileRequired' },
  {
    pattern: /^This device already has the maximum of (\d+) profiles\.$/,
    key: 'errors:server.tooManyProfiles',
    params: (match) => ({ max: match[1] }),
  },
  {
    pattern: /^This device already has the maximum of (\d+) rules\.$/,
    key: 'errors:server.tooManyRules',
    params: (match) => ({ max: match[1] }),
  },
  {
    pattern: /^This device already has a profile called "(.*)"\.$/,
    key: 'errors:server.duplicateProfileName',
    params: (match) => ({ name: match[1] }),
  },
  {
    pattern: /^"(.*)" is used by (\d+) rule\(s\)\. Delete or repoint them first\.$/,
    key: 'errors:server.profileInUse',
    params: (match) => ({ name: match[1], count: Number(match[2]) }),
  },
  {
    pattern: /^"(.*)" is this schedule's fallback profile\. Choose a different one first\.$/,
    key: 'errors:server.profileIsFallback',
    params: (match) => ({ name: match[1] }),
  },

  // ----- Sharing with people -----
  { pattern: 'You do not have permission to share this device.', key: 'errors:server.noPermissionShare' },
  { pattern: 'You do not have permission to manage sharing for this device.', key: 'errors:server.noPermissionManageSharing' },
  { pattern: 'That user already has access to this device. Edit their existing access instead.', key: 'errors:server.accessExists' },
  { pattern: 'This is the only account that can share this device. Give someone else sharing rights first.', key: 'errors:server.lastSharer' },

  // ----- Share links -----
  { pattern: 'Choose whether the link shows the current position only or the whole track.', key: 'errors:server.shareScopeRequired' },
  { pattern: 'This device already has as many active share links as are allowed. Revoke one before creating another.', key: 'errors:server.tooManyShareLinks' },
  { pattern: 'This link has been revoked and cannot be changed. Create a new one instead.', key: 'errors:server.linkRevokedImmutable' },
  { pattern: 'This link has been revoked. Create a new one instead.', key: 'errors:server.linkRevoked' },
  { pattern: 'The end of the sharing window must be after its start.', key: 'errors:server.windowEndBeforeStart' },
  { pattern: 'That sharing window has already passed.', key: 'errors:server.windowPassed' },
  { pattern: 'That sharing window is longer than a share link is allowed to cover.', key: 'errors:server.windowTooLong' },
  { pattern: 'This link has been withdrawn by the person who shared it.', key: 'errors:server.linkWithdrawn' },
  { pattern: 'This link has expired. Ask for a new one if you still need access.', key: 'errors:server.linkExpired' },
  { pattern: 'This link is not active yet. It starts working at the time it was shared for.', key: 'errors:server.linkNotActiveYet' },
  { pattern: 'That code is not right. Check it and try again.', key: 'errors:server.wrongShareCode' },
  { pattern: 'This link is not valid. Check that you opened the whole address you were sent.', key: 'errors:server.linkInvalid' },
  { pattern: 'This link is no longer available.', key: 'errors:server.linkUnavailable' },

  // ----- Request-level failures (middleware and apiClient.ts) -----
  { pattern: 'The request could not be verified. Reload the page and try again.', key: 'errors:http.csrf' },
  { pattern: 'The server encountered an error. Please try again later.', key: 'errors:http.serverError' },
  { pattern: 'The request body is too large.', key: 'errors:http.bodyTooLarge' },
  { pattern: 'The request headers are too large.', key: 'errors:http.headersTooLarge' },
  { pattern: 'The request could not be read.', key: 'errors:http.unreadable' },
  { pattern: 'Your session has expired. Please sign in again.', key: 'errors:http.sessionExpired' },
  { pattern: "You don't have permission to do that.", key: 'errors:http.forbidden' },
  { pattern: 'The requested item was not found.', key: 'errors:http.notFound' },
  { pattern: 'Too many attempts. Please wait a moment and try again.', key: 'errors:http.tooManyRequests' },
  { pattern: /^Could not reach the server/, key: 'errors:http.network' },
  {
    pattern: /^Request failed with status (\d+)\.$/,
    key: 'errors:http.requestFailed',
    params: (match) => ({ status: match[1] }),
  },
]

type Found = Translated & { match?: RegExpExecArray }

function findTranslation(message: string): Found | null {
  for (const known of KNOWN_MESSAGES) {
    if (typeof known.pattern === 'string') {
      if (known.pattern === message) {
        return known
      }
      continue
    }
    const match: RegExpExecArray | null = known.pattern.exec(message)
    if (match !== null) {
      return { ...known, match }
    }
  }
  return null
}

// The reader's language, as the catalogue resolved it (never "cs-CZ").
function isEnglish(): boolean {
  return (i18n.resolvedLanguage ?? i18n.language ?? 'en') === 'en'
}

// Renders a message in the reader's language when it is one we know; otherwise
// returns null so the caller can decide what to show instead.
export function translateErrorMessage(message: string): string | null {
  const found: Found | null = findTranslation(message.trim())
  if (found === null) {
    return null
  }
  const params = found.params && found.match ? found.params(found.match) : {}
  // The key is data from the table above, so it is not one of the literal keys
  // t() is typed against — hence the widening.
  return (i18n.t as (key: string, options?: Record<string, unknown>) => string)(found.key, params)
}

// Any Error's message (an ApiError carries the server's), translated when it is
// a known one; otherwise the caller's (already translated) default.
export function describeError(error: unknown, fallback: string): string {
  const message: string | null =
    error instanceof Error && error.message.length > 0 ? error.message : null

  if (message === null) {
    return fallback
  }

  const translated: string | null = translateErrorMessage(message)
  if (translated !== null) {
    return translated
  }

  // An unknown message is still the most specific thing we have for an English
  // reader. For anyone else it would be English text in a translated page, so
  // the caller's own translated message wins.
  return isEnglish() ? message : fallback
}
