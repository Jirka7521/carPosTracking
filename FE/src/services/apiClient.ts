// ---------------------------------------------------------------------------
// API client — every call to the backend goes through this file.
//
// Responsibilities:
//   * Build request URLs under the same-origin /api path.
//   * Send the session cookie and the CSRF token that guards it.
//   * Parse JSON responses and turn HTTP errors into ApiError so the UI can
//     surface them as user-friendly messages.
//
// What this file deliberately does NOT do any more: store a token. The session
// is an HttpOnly cookie set by the API, unreadable from JavaScript, so there is
// nothing here for an XSS bug to steal. The trade-off is that the browser sends
// it automatically — including on a request some other site triggered — which is
// what the CSRF token below exists to prevent.
//
// All response/request types live in `./apiTypes.ts` so they can be imported
// from anywhere without dragging the network code in too.
// ---------------------------------------------------------------------------

import type {
  AccessDto,
  AccessCreateRequestDto,
  AccessUpdateRequestDto,
  AckKeyImportedDto,
  AuthResponseDto,
  ChangePasswordRequestDto,
  DeviceAliasUpdateRequestDto,
  DeviceConfigStateDto,
  DeviceConfigUpdateRequestDto,
  DeviceConfigVersionDto,
  DeviceCreateRequestDto,
  DeviceCreatedDto,
  DeviceDto,
  DeviceProvisioningDto,
  ImportAckKeyRequestDto,
  PositionDto,
  UserProfileDto,
  UserUpdateRequestDto,
} from './apiTypes'
import { API_BASE_PATH } from './runtimeConfig'

// Must match AuthCookieOptions on the server. This cookie is readable on purpose
// (it is not a credential); the session cookie beside it is not.
const CSRF_COOKIE_NAME = 'carpos_csrf'
const CSRF_HEADER_NAME = 'X-CSRF-Token'

// Dispatched on `window` whenever the API answers 401. AuthContext listens for
// it and drops the cached user, so an expired session logs the UI out instead of
// leaving it stuck on a page that cannot load anything.
export const SESSION_EXPIRED_EVENT = 'carpos:session-expired'

// Typed error thrown by every API call so callers can branch on `status`
// (e.g. 403 -> "not allowed", 409 -> "already exists") instead of parsing
// message strings.
export class ApiError extends Error {
  public readonly status: number
  public readonly body: unknown

  public constructor(status: number, message: string, body: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

// =============================================================================
// Internal request helpers
// =============================================================================

function buildUrl(path: string, query?: Record<string, string | number | boolean | undefined>): string {
  // Relative to the document origin — the app and the API are the same origin by
  // construction, so there is no base URL to configure or get wrong.
  const url = new URL(`${API_BASE_PATH}${path}`, window.location.origin)

  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value === undefined || value === null) {
        continue
      }
      const stringValue = String(value)
      if (stringValue.length === 0) {
        continue
      }
      url.searchParams.set(key, stringValue)
    }
  }

  return url.toString()
}

// Path segments carry device ids and user ids, so they are escaped rather than
// interpolated raw — the server validates them, but a stray slash here would
// silently address a different endpoint.
function segment(value: string | number): string {
  return encodeURIComponent(String(value))
}

function readCsrfToken(): string | null {
  const match = document.cookie.match(new RegExp(`(?:^|;\\s*)${CSRF_COOKIE_NAME}=([^;]*)`))
  return match ? match[1] : null
}

function buildHeaders(includeJson: boolean, isMutation: boolean): HeadersInit {
  const headers: Record<string, string> = {}

  if (includeJson) {
    headers['Content-Type'] = 'application/json'
  }

  if (isMutation) {
    // Double-submit: echo the readable CSRF cookie back in a header. A
    // cross-site attacker can make the browser send our cookies but cannot read
    // them, so it cannot produce this value.
    const token = readCsrfToken()
    if (token) {
      headers[CSRF_HEADER_NAME] = token
    }
  }

  return headers
}

// Reads the response body trying JSON first, falling back to text. We have to
// peek the content-type because empty bodies (204 No Content) would otherwise
// throw on response.json().
async function readBody(response: Response): Promise<unknown> {
  if (response.status === 204) {
    return null
  }

  const contentType: string = response.headers.get('content-type') ?? ''
  // Errors come back as application/problem+json, successes as application/json,
  // so match on the suffix rather than an exact type.
  if (contentType.includes('json')) {
    try {
      return await response.json()
    } catch {
      return null
    }
  }

  try {
    return await response.text()
  } catch {
    return null
  }
}

// Picks a user-friendly error message out of whatever the server returned. The
// API answers every failure with RFC 7807 ProblemDetails, where `detail` is the
// message written for the end user and `title` is the category.
function extractErrorMessage(status: number, body: unknown): string {
  if (typeof body === 'string' && body.trim().length > 0) {
    return body
  }
  if (body && typeof body === 'object') {
    const maybe = body as { detail?: unknown; title?: unknown; errors?: unknown }

    if (typeof maybe.detail === 'string' && maybe.detail.length > 0) {
      return maybe.detail
    }

    // ValidationProblemDetails from [ApiController]: { errors: { Field: [msg] } }.
    // Showing the first message beats "Invalid request" with no clue which field
    // was wrong.
    if (maybe.errors && typeof maybe.errors === 'object') {
      const messages = Object.values(maybe.errors as Record<string, unknown>)
        .flatMap((value) => (Array.isArray(value) ? value : []))
        .filter((value): value is string => typeof value === 'string')
      if (messages.length > 0) {
        return messages[0]
      }
    }

    if (typeof maybe.title === 'string' && maybe.title.length > 0) {
      return maybe.title
    }
  }
  if (status === 401) {
    return 'Your session has expired. Please sign in again.'
  }
  if (status === 403) {
    return "You don't have permission to do that."
  }
  if (status === 404) {
    return 'The requested item was not found.'
  }
  if (status === 429) {
    return 'Too many attempts. Please wait a moment and try again.'
  }
  if (status >= 500) {
    return 'The server encountered an error. Please try again later.'
  }
  return `Request failed with status ${status}.`
}

async function request<T>(method: string, path: string, options: {
  body?: unknown
  query?: Record<string, string | number | boolean | undefined>
} = {}): Promise<T> {
  const isMutation: boolean = method !== 'GET' && method !== 'HEAD'

  let response: Response
  try {
    response = await fetch(buildUrl(path, options.query), {
      method: method,
      headers: buildHeaders(options.body !== undefined, isMutation),
      // Same-origin by construction; being explicit documents that this API is
      // cookie-authenticated and must never be pointed at another origin.
      credentials: 'same-origin',
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    })
  } catch (networkError) {
    // fetch only rejects on network errors (DNS, offline, etc.). Translate
    // these into ApiError(0, …) so callers can treat them uniformly.
    const message: string = networkError instanceof Error
      ? networkError.message
      : 'Network error.'
    throw new ApiError(0, `Could not reach the server: ${message}`, networkError)
  }

  const body: unknown = await readBody(response)

  if (!response.ok) {
    if (response.status === 401) {
      // Tell the app once, centrally, rather than making every caller recognise
      // an expired session for itself.
      window.dispatchEvent(new CustomEvent(SESSION_EXPIRED_EVENT))
    }
    throw new ApiError(response.status, extractErrorMessage(response.status, body), body)
  }

  return body as T
}

// =============================================================================
// Public API methods
// =============================================================================

// ----- Authentication -----
//
// register and login return the profile only; the session arrives as cookies on
// the same response and is invisible to this code by design.

export async function registerUser(
  email: string,
  password: string,
  firstName: string,
  lastName: string,
): Promise<AuthResponseDto> {
  return request<AuthResponseDto>('POST', '/auth/register', {
    body: { email, password, firstName, lastName },
  })
}

export async function loginUser(email: string, password: string): Promise<AuthResponseDto> {
  return request<AuthResponseDto>('POST', '/auth/login', {
    body: { email, password },
  })
}

// Expires the session cookies server-side. There is nothing to clear locally.
export async function logoutUser(): Promise<void> {
  await request<null>('POST', '/auth/logout')
}

// The session probe: an HttpOnly cookie cannot be inspected from JavaScript, so
// "am I signed in?" is a question only the server can answer.
export async function fetchMyProfile(): Promise<UserProfileDto> {
  return request<UserProfileDto>('GET', '/me')
}

// ----- Users -----

export async function fetchUsers(email: string, exactMatch: boolean = true): Promise<UserProfileDto[]> {
  return request<UserProfileDto[]>('GET', '/users', {
    query: { email, exactMatch },
  })
}

export async function fetchUserById(userId: number): Promise<UserProfileDto> {
  return request<UserProfileDto>('GET', `/users/${segment(userId)}`)
}

// Update the authenticated user's own first/last name. Both fields are
// optional; omit a field to leave it unchanged.
export async function updateUserProfile(userId: number, payload: UserUpdateRequestDto): Promise<UserProfileDto> {
  return request<UserProfileDto>('PUT', `/users/${segment(userId)}`, { body: payload })
}

// Change the authenticated user's password. Requires the current password as
// proof of identity. The server returns 204 No Content on success.
export async function changePassword(userId: number, payload: ChangePasswordRequestDto): Promise<void> {
  await request<null>('PUT', `/users/${segment(userId)}/password`, { body: payload })
}

// ----- Devices -----
//
// Devices are addressed by their MQTT device id (e.g. "GNSS01") — the same
// string the firmware publishes under. fetchMyDevices is the API-level filter
// that decides which devices the current user can see; there is no client-side
// filtering to bypass.

export async function fetchMyDevices(): Promise<DeviceDto[]> {
  return request<DeviceDto[]>('GET', '/me/devices')
}

// Registers a device and returns it together with the provisioning block
// (public key, fingerprint, Config.h snippet) needed to flash the firmware.
export async function createDevice(payload: DeviceCreateRequestDto): Promise<DeviceCreatedDto> {
  return request<DeviceCreatedDto>('POST', '/devices', { body: payload })
}

// Soft-delete (deactivate) a device. The API enforces CanDelete on the caller;
// the FE additionally hides the button when the permission is missing.
export async function deleteDevice(deviceId: string): Promise<void> {
  await request<null>('DELETE', `/devices/${segment(deviceId)}`)
}

// Re-reads the firmware configuration for an already-registered device — a
// complete Config.h with the secrets left blank. Requires CanModifySettings.
// Contains the receiver public key only; its private half never leaves the
// server, and the device's ack private key never reaches it in the first place.
export async function fetchDeviceProvisioning(deviceId: string): Promise<DeviceProvisioningDto> {
  return request<DeviceProvisioningDto>('GET', `/devices/${segment(deviceId)}/provisioning`)
}

// Stores the PUBLIC half of an ack key pair generated in this browser, replacing
// whatever the device had before. Requires CanModifySettings.
//
// Call this ONLY after the operator has saved the Config.h carrying the matching
// private key: from the moment it lands the API seals every delivery ack to this
// key, so a device still running the old one stops confirming deliveries until it
// is re-flashed. The API cannot enforce that ordering — it has no way to know
// whether the file was kept — so the panel does.
export async function importDeviceAckKey(
  deviceId: string,
  ackPublicKeyPem: string,
): Promise<AckKeyImportedDto> {
  const payload: ImportAckKeyRequestDto = { ackPublicKeyPem }
  return request<AckKeyImportedDto>('POST', `/devices/${segment(deviceId)}/ack-key`, {
    body: payload,
  })
}

// Set or clear the caller's personal display name for a device. Any user with
// read access may call this — the alias is private to them. Passing an empty
// string removes it.
export async function updateDeviceAlias(deviceId: string, alias: string): Promise<void> {
  const payload: DeviceAliasUpdateRequestDto = { alias }
  await request<null>('PUT', `/me/devices/${segment(deviceId)}/alias`, { body: payload })
}

// ----- Device config -----
//
// The remote settings a device runs on. All four require CanModifySettings —
// including the reads, because the panel exposes operational tuning a read-only
// viewer has no use for (the same rule as the provisioning block above).
//
// The API is the source of truth: saving publishes the new revision to the
// broker retained, and the device adopts it on its next connect. Nothing here
// talks to a device directly.

export async function fetchDeviceConfig(deviceId: string): Promise<DeviceConfigStateDto> {
  return request<DeviceConfigStateDto>('GET', `/devices/${segment(deviceId)}/config`)
}

// Past revisions, newest first. The server clamps `limit` to its own ceiling, so
// an over-large value is answered rather than rejected.
export async function fetchDeviceConfigHistory(
  deviceId: string,
  limit?: number,
): Promise<DeviceConfigVersionDto[]> {
  return request<DeviceConfigVersionDto[]>('GET', `/devices/${segment(deviceId)}/config/history`, {
    query: { limit: limit },
  })
}

// A full replacement, not a patch — every field must be sent. Returns the new
// state (including the bumped version), so the caller does not need to re-read.
// Submitting the values already in force creates no revision and returns the
// existing state unchanged.
export async function updateDeviceConfig(
  deviceId: string,
  payload: DeviceConfigUpdateRequestDto,
): Promise<DeviceConfigStateDto> {
  return request<DeviceConfigStateDto>('PUT', `/devices/${segment(deviceId)}/config`, {
    body: payload,
  })
}

// Re-publish the current revision without creating a new one. Answers 503 when
// the API cannot reach the broker, which surfaces as an ApiError like any other
// failure — the stored settings are untouched either way.
export async function republishDeviceConfig(deviceId: string): Promise<void> {
  await request<null>('POST', `/devices/${segment(deviceId)}/config/republish`)
}

// ----- Positions -----

export async function fetchPositions(
  deviceId: string,
  from?: string,
  to?: string,
): Promise<PositionDto[]> {
  return request<PositionDto[]>('GET', '/positions', {
    query: { deviceId, from, to },
  })
}

// ----- Access grants -----

export async function fetchAccessGrantsForDevice(deviceId: string): Promise<AccessDto[]> {
  return request<AccessDto[]>('GET', '/access', { query: { deviceId } })
}

export async function createAccessGrant(payload: AccessCreateRequestDto): Promise<AccessDto> {
  return request<AccessDto>('POST', '/access', { body: payload })
}

export async function updateAccessGrant(
  accessId: number,
  payload: AccessUpdateRequestDto,
): Promise<AccessDto> {
  return request<AccessDto>('PUT', `/access/${segment(accessId)}`, { body: payload })
}

export async function revokeAccessGrant(accessId: number): Promise<void> {
  await request<null>('DELETE', `/access/${segment(accessId)}`)
}
