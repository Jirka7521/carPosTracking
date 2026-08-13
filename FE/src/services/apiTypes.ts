// ---------------------------------------------------------------------------
// Wire-format DTOs that mirror the API response/request shapes. Keeping them
// in their own file (separate from the network code) means UI components can
// import the types without pulling in fetch logic.
//
// Devices are keyed by `deviceId` — the MQTT identity the firmware publishes
// under, e.g. "GNSS01". That string is the device's identity everywhere: in the
// URL, in the broker topic, and in the payloads the tracker encrypts. The API's
// internal row Guid is deliberately not exposed; one thing should have one id.
//
// The permission model is four boolean flags on a single Access row per
// (user, device). There is no separate access-level lookup; granting access
// just sets the flags. The API enforces:
//   * CanRead is always true on any active grant
//   * CanShare implies CanModifySettings
// ---------------------------------------------------------------------------

export type UserProfileDto = {
  id: number
  email: string
  firstName: string
  lastName: string
}

// Response of POST /api/auth/register and /login. There is no token field: the
// session is delivered as an HttpOnly cookie that JavaScript cannot read.
export type AuthResponseDto = {
  user: UserProfileDto
}

export type DevicePermissionsDto = {
  canRead: boolean
  canDelete: boolean
  canShare: boolean
  canModifySettings: boolean
}

export type DeviceDto = {
  // MQTT identity and primary key on the wire.
  deviceId: string
  // The shared, provisioning-time friendly name (visible to everyone with access).
  displayName: string | null
  // The caller's *private* nickname for this device, or null when none is set.
  // Nobody else sees it. Label fallback order: customName → displayName → deviceId.
  customName: string | null
  isActive: boolean
  createdAt: string
  deactivatedAt: string | null
  // When the last accepted fix arrived, or null if the device has never
  // reported. The firmware sends no heartbeat, so this is the only liveness
  // signal that exists.
  lastSeenAt: string | null
  // Battery state of charge from this device's most recent fix (0–100), or null
  // when it has never reported one. The value 0 is the "charging" sentinel — the
  // UI shows it as charging rather than as a flat battery. Lets the device grid
  // display a battery level without loading positions.
  lastBatteryPct: number | null
  // What the authenticated caller can do on this device. The API computes this
  // from the caller's active Access row and the FE uses it to hide / disable
  // controls. Every mutation is still re-authorized server-side, so these flags
  // are UX hints, not security.
  permissions: DevicePermissionsDto
}

export type PositionDto = {
  id: number
  deviceId: string
  // The GNSS fix time — when the vehicle was there.
  timestamp: string
  // When the server stored it. Differs from `timestamp` by hours when a device
  // uploads a backlog after being offline.
  receivedAt: string
  latitude: number
  longitude: number
  speedKmph: number
  altitudeMeters: number
  // Battery state of charge at this fix (0–100), or null when the device sent
  // none. The value 0 is the "charging" sentinel.
  batteryPct: number | null
  // Raw instantaneous ADXL345 acceleration at this fix, in g, or null when the
  // device sent none (accelerometer disabled or older firmware).
  accelXG: number | null
  accelYG: number | null
  accelZG: number | null
  // Modem die temperature at this fix in °C, or null when the device sent none
  // (older firmware, or the SIM7000 AT+CPMUTEMP command unsupported). A proxy for
  // how hot the tracker is running — a hot-car cut-off shows up here.
  temperatureC: number | null
}

// One row in GET /api/access?deviceId=X — the four capability flags a user
// holds on the device. Only active grants are returned.
export type AccessDto = {
  id: number
  userId: number
  deviceId: string
  grantedBy: number
  dateRegistration: string
  canRead: boolean
  canDelete: boolean
  canShare: boolean
  canModifySettings: boolean
}

// POST /api/devices. additionalAccesses is optional; the server automatically
// grants the creator full access. Each entry produces one Access row (the
// server forces CanRead and coerces Share ⇒ Settings), and entries whose email
// matches no account are skipped silently.
export type DeviceCreateRequestDto = {
  deviceId: string
  displayName?: string
  additionalAccesses?: DeviceAccessGrantInput[]
}

export type DeviceAccessGrantInput = {
  userEmail: string
  canDelete: boolean
  canShare: boolean
  canModifySettings: boolean
}

// Everything needed to flash the firmware for a device. Contains the *public*
// key only — the matching private key is encrypted at rest in the API database
// and has no code path out of it, which is what stops the broker (or anyone who
// steals the tracker) from reading positions.
export type DeviceProvisioningDto = {
  deviceId: string
  displayName: string | null
  telemetryTopic: string
  configTopic: string
  brokerUri: string
  publicKeyPem: string
  // SHA-256 of the SPKI bytes, uppercase hex. Lets you confirm the flashed
  // firmware carries the key this device expects without handling key material.
  publicKeyFingerprint: string
  // The above pre-formatted as C++ constexpr lines, ready to paste into
  // ESP32/src/config/Config.h.
  configSnippet: string
}

// 201 response of POST /api/devices — the new device row plus its provisioning
// block, so the dashboard can add the card and show the snippet in one step.
export type DeviceCreatedDto = {
  device: DeviceDto
  provisioning: DeviceProvisioningDto
}

// POST /api/access — share a device with a user. CanRead is implicit on the
// server side; CanShare coerces CanModifySettings on.
export type AccessCreateRequestDto = {
  userId: number
  deviceId: string
  canDelete: boolean
  canShare: boolean
  canModifySettings: boolean
}

// PUT /api/access/{id} — overwrite the capability set on an existing grant.
// A full replacement, not a patch: an omitted flag means "off".
export type AccessUpdateRequestDto = {
  canDelete: boolean
  canShare: boolean
  canModifySettings: boolean
}

// PUT /api/users/{id} — update first/last name. Both fields are optional;
// omitting one leaves it unchanged on the server.
export type UserUpdateRequestDto = {
  firstName?: string
  lastName?: string
}

// PUT /api/users/{id}/password — change the account password. Requires the
// current password as proof of identity; both fields are required.
export type ChangePasswordRequestDto = {
  currentPassword: string
  newPassword: string
}

// PUT /api/me/devices/{deviceId}/alias — set (or clear) a personal display
// name. Sending an empty string removes the alias.
export type DeviceAliasUpdateRequestDto = {
  alias: string
}
