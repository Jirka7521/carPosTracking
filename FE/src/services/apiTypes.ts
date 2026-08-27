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
  // Topic the API confirms stored fixes on. The firmware only clears a fix from
  // its SD queue once it is named here, so a fix the API rejects is no longer
  // lost to a broker-level ack that proved nothing.
  ackTopic: string
  brokerUri: string
  publicKeyPem: string
  // SHA-256 of the SPKI bytes, uppercase hex. Lets you confirm the flashed
  // firmware carries the key this device expects without handling key material.
  publicKeyFingerprint: string
  // Fingerprint of the device's *ack* public key, or null when none has been
  // imported — in which case the API sends this device no delivery acks.
  // Note the key roles invert for acks: the device holds that private key, so it
  // is generated off-server and never travels in this payload.
  ackPublicKeyFingerprint: string | null
  // A COMPLETE Config.h for this device — the firmware's own template with this
  // device's id, topics, broker URI, receiver public key and current setting
  // defaults filled in. Save it as ESP32/src/config/Config.h and build.
  //
  // Four constants arrive deliberately empty (kWifiSsid, kWifiPassword,
  // kMqttPassword, kDeviceAckPrivateKeyPem): they are secrets the server does
  // not have and, in the ack key's case, must never have. The dashboard fills
  // them in locally — see utils/configSecrets.ts.
  configSnippet: string
}

// POST /api/devices/{deviceId}/ack-key — stores the PUBLIC half of an ack key
// pair the browser has just generated. The private half is never in this
// payload: for acks the API encrypts and the device decrypts, so the device
// owns that half and the server may only ever hold the public one.
export type ImportAckKeyRequestDto = {
  ackPublicKeyPem: string
}

export type AckKeyImportedDto = {
  // SHA-256 of the SPKI bytes, uppercase hex — compare it against the key that
  // went into the device's Config.h to confirm the two are a pair.
  ackPublicKeyFingerprint: string
}

// 201 response of POST /api/devices — the new device row plus its provisioning
// block, so the dashboard can add the card and show the snippet in one step.
export type DeviceCreatedDto = {
  device: DeviceDto
  provisioning: DeviceProvisioningDto
}

// ---------------------------------------------------------------------------
// Remote device settings.
//
// These six values are the document the API publishes — retained — to
// devices/<id>/config, and the firmware caches on its SD card. Every save
// creates a new immutable *revision*; nothing is ever edited in place. The
// device echoes the revision number back in each position report, which is how
// the dashboard can tell "published" from "actually running".
//
// The min/max noted on each field mirror the firmware's clamps in
// ESP32/src/config/Config.h and the API's [Range] attributes. The API rejects
// out-of-range values with a 400; the device, having nobody to ask, clamps.
// ---------------------------------------------------------------------------

export type DeviceConfigValuesDto = {
  // Seconds between position reports. 5 … 86400.
  intervalSeconds: number
  // Power the modem down and deep-sleep between reports. Large battery saving
  // above a few minutes, at the cost of a cold GNSS fix every cycle.
  sleepBetween: boolean
  // How long to chase a GNSS lock before giving up on a cycle. 15 … 900.
  fixTimeoutSeconds: number
  // How many undelivered fixes the SD queue may hold before the oldest are
  // dropped. 100 … 100000. A count, not a duration: a queued line is bare
  // ciphertext with no timestamp to age it by. One fix is queued per reporting
  // cycle, so the UI turns it into an approximate duration for the reader.
  queueMaxFixes: number
  // Hours between attempts on a fix the API rejected. 1 … 720.
  retryIntervalHours: number
  // Hours after which a still-rejected fix is abandoned. 0 … 8760, where 0
  // means "never give up".
  retryMaxAgeHours: number
  // How often an *awake* device asks the broker to re-send its configuration.
  // 60 … 86400. Only a backstop: a saved change normally reaches the device by
  // push within a second, because it holds an open subscription. It has no
  // effect at all while sleepBetween is on — a sleeping device re-reads its
  // configuration on every wake anyway.
  configCheckSeconds: number
}

// One revision, as returned by the state and history endpoints.
export type DeviceConfigVersionDto = {
  // Unique and increasing per device, starting at 1.
  version: number
  values: DeviceConfigValuesDto
  createdAt: string
  // Display name of whoever saved it, or null for a revision with no human
  // author — the one created with the device, the one seeded for devices that
  // predate remote settings, and every revision the scheduler wrote.
  createdBy: string | null
  // What produced it. Without this a scheduled revision is indistinguishable
  // from the two authorless rows above, and the history cannot answer the one
  // question it is opened to answer: why did this tracker change at 22:00?
  source: 'manual' | 'schedule'
  // Name of the profile a scheduled revision came from, or null. Goes null once
  // that profile is deleted — the revision keeps its values regardless, because
  // this is a label, not a lookup.
  sourceProfileName: string | null
}

// GET /api/devices/{deviceId}/config — what the device should be running and
// what it last confirmed it is running, both with full values. Having both is
// what lets the UI show "reporting every 60 s, will become every 300 s" while a
// change is pending, instead of two bare version numbers.
export type DeviceConfigStateDto = {
  desired: DeviceConfigVersionDto
  // Null when the device has never reported a revision — a device that has not
  // checked in yet, or firmware older than the settings-version protocol.
  applied: DeviceConfigVersionDto | null
  appliedAt: string | null
  // True when the device has confirmed the desired revision. False is normal,
  // not an error: the change is published and waiting to be picked up.
  isInSync: boolean
  lastSeenAt: string | null
}

// PUT /api/devices/{deviceId}/config — a full replacement, not a patch. Sending
// the values already in force is a no-op that adds no revision.
//
// `acknowledgeOverride` only matters on a device whose schedule is ENABLED,
// where a manual save is temporary: it holds until the next scheduled switch and
// is then reasserted. The API refuses such a save without it, so the surprise
// cannot happen silently. On a device with no schedule — which is every device
// until someone sets one up — it is ignored.
export type DeviceConfigUpdateRequestDto = DeviceConfigValuesDto & {
  acknowledgeOverride?: boolean
}

// ---------------------------------------------------------------------------
// Settings schedules
//
// A schedule switches a device between named PROFILES on a weekly timetable.
// The API evaluates it and pushes the result the same way a manual save does —
// the firmware never learns a schedule exists.
//
// EVERY TIME BELOW IS UTC. The API has no notion of a local time and never
// converts one; `utils/schedule.ts` owns both directions, because the browser is
// the only party that knows the reader's offset. The trade-off that comes with
// that: a window entered as 22:00 in winter is stored as 21:00Z and renders as
// 23:00 local after the spring DST change — the stored instant did not move, the
// local clock did. Every rule shows both times so this is visible, and
// re-entering the time is the fix.
// ---------------------------------------------------------------------------

// A named set of the seven settings. `values` is the same shape a revision
// carries, so the profile editor reuses the settings form's controls wholesale.
export type DeviceConfigProfileDto = {
  id: string
  name: string
  values: DeviceConfigValuesDto
  createdAt: string
  updatedAt: string
  createdBy: string | null
}

// One weekly window that selects a profile.
export type DeviceScheduleRuleDto = {
  id: string
  profileId: string
  // Resolved server-side so no rendering has to look it up and risk a bare id.
  profileName: string
  // 7-bit mask of the UTC weekdays the window opens on; bit 0 is Sunday.
  daysMaskUtc: number
  // Minutes past UTC midnight; 0 … 1439.
  startMinuteUtc: number
  // 1 … 1440, end exclusive. A duration rather than an end time: it needs no
  // midnight-wrap convention and survives timezone conversion untouched.
  durationMinutes: number
  // Lower wins where windows overlap.
  priority: number
  isEnabled: boolean
}

// What the schedule resolves to now, computed by the server — never by the
// client, even though it could. The server is what ACTS on this answer, and a
// dashboard with a second opinion would be the one believed, being on screen.
export type DeviceScheduleStatusDto = {
  activeProfileId: string | null
  activeProfileName: string | null
  // Null when the fallback won rather than a rule.
  activeRuleId: string | null
  // Null when the schedule resolves the same way all week, so there is no
  // meaningful "since" to show.
  activeSince: string | null
  // Null when the profile never changes.
  nextChangeAt: string | null
  nextProfileId: string | null
  nextProfileName: string | null
}

// A manual change holding the schedule off. Present only while live — branch on
// its presence rather than comparing `until` to the browser's clock, which is
// not the server's and would disagree near the boundary.
export type DeviceScheduleOverrideDto = {
  until: string
  resumingProfileId: string | null
  resumingProfileName: string | null
}

// GET /api/devices/{deviceId}/schedule, and the answer to every mutation below.
export type DeviceScheduleStateDto = {
  enabled: boolean
  fallbackProfileId: string | null
  profiles: DeviceConfigProfileDto[]
  rules: DeviceScheduleRuleDto[]
  // Null while `enabled` is false: nothing is acting on the rules, so a
  // confident "in force now" would be fiction.
  status: DeviceScheduleStatusDto | null
  override: DeviceScheduleOverrideDto | null
  // When the worker last completed a pass. Null on an enabled schedule means it
  // never has — "waiting for the scheduler", not an answer already acted on.
  evaluatedAt: string | null
}

// PUT /api/devices/{deviceId}/schedule. Enabling without a fallback is refused:
// it would leave every uncovered hour undefined.
export type UpdateDeviceScheduleRequestDto = {
  enabled: boolean
  fallbackProfileId: string | null
}

// POST/PUT .../schedule/profiles — name plus the seven values, flattened the way
// the API's request record spells them.
export type SaveConfigProfileRequestDto = DeviceConfigValuesDto & {
  name: string
}

// POST/PUT .../schedule/rules — a full replacement, in UTC minutes.
export type SaveScheduleRuleRequestDto = {
  profileId: string
  daysMaskUtc: number
  startMinuteUtc: number
  durationMinutes: number
  priority: number
  isEnabled: boolean
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
