# CarPosAPI

The web backend of **carPosTracking**. Two things live in one process:

1. **An MQTT ingest pipeline** — it subscribes to the broker, decrypts the
   ESP32's end-to-end-encrypted GNSS fixes, validates them and stores them in
   PostgreSQL.
2. **A REST API for the dashboard** — accounts and sessions, devices and their
   provisioning, positions, device sharing, and each device's **remote settings**.

Remote settings are the one place the API talks *to* a device rather than about
it: every saved revision is published retained to `devices/<id>/config`, and the
device reports back which revision it is running. See
[Remote device settings](#remote-device-settings) below.

## How it works

```
ESP32 ──(WSS, QoS 2)──► Mosquitto ──(WSS, QoS 2)──► MqttIngestService
   devices/GNSS01              devices/+                    │
   JSON array of encrypted                                  ▼
   envelopes (RSA-3072 OAEP-SHA256                    IngestPipeline
   wrapped AES-256-GCM)                 topic guard → device lookup → decode
                                        → decrypt → validate → batch insert
                                                            │
                                                            ▼
                                        PostgreSQL (devices, positions)
                                        INSERT … ON CONFLICT DO NOTHING
                                                            │
  ESP32 ◄──(encrypted ack)── Mosquitto ◄─────────── MqttAckPublisher
        devices/GNSS01/ack             stored[] / rejected[] per envelope id
```

Key properties:

- **At-least-once, deduplicated.** MQTT QoS 2 + a persistent broker session
  (clean_session=false, stable client id `carpos-api`) mean nothing is lost
  while the API is down; the unique index on `(device_id, fix_time)` plus
  `ON CONFLICT DO NOTHING` make redelivery and backlog replays harmless.
- **Poison-safe.** Malformed, undecryptable or invalid envelopes are logged
  (aggregated, without coordinates — location data is personal data) and
  consumed; only database outages trigger redelivery (unacknowledged message +
  reconnect after a pause).
- **Delivery is confirmed, not assumed.** After a successful write the API
  publishes an encrypted ack to `devices/<id>/ack` naming, per envelope id, what
  was `stored` and what was `rejected` (with the reason). The firmware clears a
  fix from its SD card only on that ack — a broker QoS-2 ack alone proves only
  that Mosquitto took the message. The ack is published **only** on the success
  path: a database outage stays unacknowledged so the broker redelivers.
- **Keys encrypted at rest.** Device RSA-3072 private keys are stored in
  `devices.private_key_ciphertext`, AES-256-GCM-encrypted under a 32-byte
  master key with the device id as associated data. They are never logged and
  must never be exposed by any endpoint.
- **The ack key runs the other way, and never touches this server.** An ack is
  sealed *to* the device, so the device holds the private half and
  `devices.ack_public_key_pem` holds only the public one. That key pair is
  generated off-server — with `openssl` and `--ack-public-pem`, or in the
  operator's browser from the dashboard, which posts back only the public half to
  `POST /api/devices/{deviceId}/ack-key`. No endpoint, DTO or rendered `Config.h`
  ever carries a device private key, and the import path rejects a PEM that
  contains one.
- **Fail-fast startup.** Missing connection string, MQTT password, or a
  missing/short master key aborts startup, as does a broker URI that is not
  `ws`/`wss`/`mqtt`/`mqtts` — so a typo like `http://` fails at boot rather than
  surfacing as a warning inside the reconnect loop. Plaintext schemes *are*
  allowed: the deployed API reaches Mosquitto over the host's container network,
  and the telemetry is end-to-end encrypted either way (see
  [`Options/MqttOptions.cs`](Options/MqttOptions.cs)).
- **Single instance only.** The persistent session is keyed by the MQTT client
  id — two instances would kick each other off the broker.

## Configuration

[`appsettings.json`](appsettings.json) is committed and **secret-free**, and
doubles as the **example**: every key the API needs is listed there, with the
secret ones left empty. Ingest limits have code defaults in
[`Options/IngestOptions.cs`](Options/IngestOptions.cs).

These four keys are secrets and are blank in the committed file:

| Key | Meaning |
|---|---|
| `ConnectionStrings:CarPos` | Npgsql connection string (BE role, TLS — see below) |
| `Mqtt:Password` | Broker password for the `carpos-api` account |
| `DeviceKeyProtection:MasterKeyBase64` | Base64 of exactly 32 random bytes |
| `Jwt:SigningKey` | HMAC-SHA256 key for session tokens, **≥ 32 bytes** |

Delivery acks are configured under the same `Mqtt` section, all non-secret:

| Key | Default | Meaning |
|---|---|---|
| `Mqtt:AckEnabled` | `true` | Master switch. `false` restores the pre-ack behaviour (devices never hear back and fall back to their own timeout) — a useful kill switch if the broker ACL is wrong. |
| `Mqtt:AckQos` | `1` | At-least-once is right: verdicts are keyed by envelope id, so a duplicate ack is idempotent. |
| `Mqtt:AckPublishTimeoutSeconds` | `5` | Bounds the publish. The ack is sent from inside the message handler MQTTnet awaits, so an unbounded wait for a PUBACK could stall ingest behind its own reply. |

`Jwt:SigningKey` has no default and no fallback: anyone holding it can mint a
session for any account, so a deployment without a real one refuses to start.
Rotating it invalidates every active session, which is the intended effect.
Generate one with `openssl rand -base64 48`.

The `AuthCookie` section controls how the session is carried. The defaults are
the production values; the only one normally worth changing is
`AuthCookie:SecureCookies`, which [`appsettings.Development.json`](appsettings.Development.json)
sets to `false` — over plain HTTP a `Secure` cookie is never sent back, so
sign-in would appear to succeed and every following request would 401.

Leaving them empty is deliberate — startup validation rejects blank secrets, so
a missing local file fails fast with a clear message instead of a confusing
error on the first message.

### `Hosting:PathBase` — published under a path prefix

The Cloudflare tunnel in front of this deployment routes
`https://jimajer.cz/carPosAPI/*` here and forwards the prefix **as part of the
path** — a tunnel has no way to strip it. `Hosting:PathBase` (default
`/carPosAPI`) is what the API strips back off, via `UsePathBase` as the very
first step of the pipeline in [`Program.cs`](Program.cs).

It strips the prefix only from requests that actually carry it, so **both
spellings work at once** and nothing that addressed the API before has to change:

| Request | Reaches |
|---|---|
| `GET /health` | the healthcheck, from inside the compose network |
| `GET /carPosAPI/health` | the same endpoint, through the tunnel |
| `GET /api/me` | the frontend's nginx proxy, unchanged |
| `GET /carPosAPI/api/me` | the same endpoint, through the tunnel |

Matching is case-insensitive, and generated URLs (a `201`'s `Location` header)
get the prefix back automatically, because ASP.NET Core keeps it in
`HttpRequest.PathBase`. Set the key to an empty string to serve at the root only;
the value must be absolute and carry no trailing slash, which is validated at
startup.

The frontend does not use this: its nginx proxies `<prefix>/api/` to the API as
`/api/`, so the dashboard works whether or not the API's own tunnel route exists.

### `Mqtt:BrokerUri` — one address, two consumers

There is a single broker address, and it does two jobs: it is what this API
dials, **and** what device provisioning writes into the firmware snippet as
`kMqttBrokerUri`. In the deployed stack it points at Mosquitto on the shared
container network, so no MQTT traffic leaves the host:

```jsonc
"Mqtt": { "BrokerUri": "ws://mqtt.local:9001/" }
```

`mqtt.local` is the network alias the broker stack gives the Mosquitto service
and `9001` is its WebSocket listener. Development keeps whatever
`appsettings.Local.json` sets (normally the public `wss://` URI), because outside
Docker that alias does not resolve and 9001 is not published to the host.

**Plaintext schemes are accepted** (`ws`, `mqtt`) alongside the TLS ones. TLS is
not required because the deployed hop stays on the host's container network, and
telemetry is already end-to-end encrypted by the firmware — so the transport
carries ciphertext either way. What a plaintext hop does expose is the broker
password, to anything able to read traffic on that network. Startup still rejects
anything outside `ws`/`wss`/`mqtt`/`mqtts`, so a typo fails fast.

> ⚠️ **Provisioning caveat.** Because devices are handed this same value, an
> address that only resolves inside the container network is **not reachable by a
> device**. With `ws://mqtt.local:9001/` configured, the `kMqttBrokerUri` line in
> a generated snippet must be replaced by hand with something the device can
> actually reach before flashing it.

### Development — `appsettings.Local.json`

Real values go in **`appsettings.Local.json`** next to `appsettings.json`. The
file is **git-ignored** (root [`.gitignore`](../../.gitignore)), optional, and
loaded last in [`Program.cs`](Program.cs) — so it overrides everything below it,
including user-secrets and environment variables. It is also excluded from
`dotnet publish` output, so it can never ride along into a deployment.

```jsonc
{
  "ConnectionStrings": {
    "CarPos": "Host=jimajer.cz;Port=5432;Database=carpos;Username=BE;Password=<BE password>;SSL Mode=VerifyFull;Root Certificate=<path to ca.crt>"
  },
  "Mqtt": { "Password": "<carpos-api broker password>" },
  "DeviceKeyProtection": { "MasterKeyBase64": "<base64 of exactly 32 random bytes>" },
  "Jwt": { "SigningKey": "<at least 32 bytes of random text>" }
}
```

Generate the master key once and keep it safe — losing it makes stored device
keys undecryptable; changing it requires re-importing every device key:

```powershell
$bytes = [byte[]]::new(32)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[Convert]::ToBase64String($bytes)
```

**user-secrets still work** and remain a fine alternative if you would rather
keep secrets outside the repo folder entirely — `appsettings.Local.json` simply
wins when both define the same key:

```powershell
dotnet user-secrets set "Mqtt:Password" "<carpos-api broker password>"
dotnet user-secrets set "ConnectionStrings:CarPos" "Host=…;Username=BE;Password=<BE password>;SSL Mode=VerifyFull;Root Certificate=<path to ca.crt>"
```

### Production — environment variables

No `appsettings.Local.json` exists on a server. The same keys use `__`
separators: `ConnectionStrings__CarPos`, `Mqtt__BrokerUri`, `Mqtt__Password`,
`DeviceKeyProtection__MasterKeyBase64`, `Jwt__SigningKey`, `Hosting__PathBase`
(from the optional `API_PATH_BASE`). They are supplied by the deployment
bundle's git-ignored `.env`, which is generated by
[`../../scripts/Publish-Deployment.ps1`](../../scripts/Publish-Deployment.ps1)
(Ctrl+Shift+M) from the template in
[`../../scripts/deploy-template/`](../../scripts/deploy-template/).

## Database

PostgreSQL 18 + PostGIS 3.6 from [`../../Container/Postgres/`](../../Container/Postgres/)
— **always runs on the server**. Two roles exist: `admin` (owner; migrations)
and `BE` (runtime; SELECT/INSERT/UPDATE/DELETE only, granted automatically on
future tables).

Schema (migrations `InitialCreate`, `AddUsersAccessesAndDeviceAliases`,
`AddBatteryAndAccel`, `AddTemperature`, `AddDeviceAckPublicKey`):

- **devices** — `id` (uuid PK), `device_id` (unique MQTT identity, e.g.
  `GNSS01`), `display_name`, `public_key_pem`, `private_key_ciphertext`
  (encrypted at rest), `ack_public_key_pem` (nullable — the device's *public*
  ack key, the mirror image of `private_key_ciphertext`: the server seals acks
  with it and only the firmware can open them, so the private half never
  reaches this database; null means acks are not configured for that device and
  its fixes are stored but never confirmed), `last_seen_at` (only "device
  alive" signal — the firmware sends no heartbeat), `is_active`/`deactivated_at`
  (soft delete), `created_at`.
- **positions** — `id` (bigint identity PK), `device_id` (FK, delete
  restricted), `fix_time` (GNSS time), `received_at`, `latitude`, `longitude`,
  `speed_kmph`, `altitude_m` (all CHECK-constrained), the optional sensor columns
  `battery_pct` (nullable, 0–100 with `0` = *charging*), `accel_x_g`/`accel_y_g`/
  `accel_z_g` (nullable, ±16 g — the raw ADXL345 sample) and `temperature_c`
  (nullable, °C from the modem's `AT+CPMUTEMP`, [-40, 125] — the sensor that
  explains a hot-car cut-off; all sensor columns CHECK-constrained),
  **UNIQUE (device_id, fix_time)** (the dedupe key), and a database-generated
  `location geography(Point,4326)` column + GIST index (derived from lat/lon —
  the app needs no spatial dependency). The sensor columns are nullable because
  older firmware and sensor-disabled devices omit them.
- **users** — `id` (int identity PK), `email` (unique, stored lower-cased),
  `password_hash` (PBKDF2 via ASP.NET Core's `PasswordHasher`), `first_name`,
  `last_name`, `created_at`.
- **accesses** — the entire authorisation model: one row per (user, device)
  with `can_read`/`can_delete`/`can_share`/`can_modify_settings`, `granted_by`
  for audit, and `is_active` for soft revocation. The unique index is
  **partial** (`WHERE is_active`), so a revoked grant stays for the audit trail
  and does not block re-granting later.
- **device_aliases** — a user's private nickname for a device, unique per
  (user, device). Separate from `devices.display_name` because it is per-user:
  a read-only viewer may set their own without touching what anyone else sees.
- **device_config_versions** — one **immutable** row per revision of a device's
  remote settings: `version` (unique per device, increasing from 1), the six
  settings, `created_by_user_id` (null for the factory defaults), `created_at`.
  Every numeric column carries a CHECK constraint mirroring
  [`Dtos/DeviceConfigRules.cs`](Dtos/DeviceConfigRules.cs). Rows are **never
  updated** — a change is an insert, which is what lets the dashboard resolve the
  revision a device is *still running* back to real values while a newer one
  waits. It doubles as the audit trail.

  `devices` carries only pointers into it: `config_version` (what the device
  should run), `config_applied_version` and `config_applied_at` (what it last
  confirmed). Keeping the values in exactly one place is why the two can never
  disagree.

Apply migrations manually **as `admin`** (never auto-migrate):

```powershell
dotnet ef database update --connection "Host=jimajer.cz;Port=5432;Database=carpos;Username=admin;Password=<admin password>;SSL Mode=VerifyFull;Root Certificate=<path to ca.crt>"
```

## REST API

Every endpoint requires a session except `POST /api/auth/register`,
`POST /api/auth/login`, `POST /api/auth/logout` and `GET /health`.

| Method & route | Purpose |
|---|---|
| `POST /api/auth/register`, `POST /api/auth/login` | returns `{ user }`, sets the session cookies |
| `POST /api/auth/logout` | expires them |
| `GET /api/me` | the caller's profile — also the frontend's session probe |
| `GET /api/me/devices` | the caller's devices, each with `customName` + `permissions` |
| `PUT /api/me/devices/{deviceId}/alias` | set/clear the caller's private device name (204) |
| `GET /api/users?email=&exactMatch=`, `GET /api/users/{id}` | search / fetch users, for sharing |
| `PUT /api/users/{id}`, `PUT /api/users/{id}/password` | update own names; change own password |
| `POST /api/devices` | register a device + provision its key pair (201) |
| `DELETE /api/devices/{deviceId}` | **soft**-delete (204) |
| `GET /api/devices/{deviceId}/provisioning` | re-render the device's complete `Config.h` |
| `POST /api/devices/{deviceId}/ack-key` | store a rotated ack **public** key; returns its fingerprint |
| `GET /api/devices/{deviceId}/config` | desired + running settings, both with values |
| `GET /api/devices/{deviceId}/config/history?limit=` | past revisions, newest first (default 20, max 100) |
| `PUT /api/devices/{deviceId}/config` | replace the settings — new revision, published retained |
| `POST /api/devices/{deviceId}/config/republish` | re-send the current revision (204; 503 if the broker is down) |
| `GET /api/positions?deviceId=&from=&to=` | positions, newest first, **max 1000** |
| `GET /api/access?deviceId=`, `POST /api/access`, `PUT /api/access/{id}`, `DELETE /api/access/{id}` | sharing grants |
| `GET /health` | liveness (unauthenticated; database + MQTT state) |

Failures return **`ProblemDetails`** (`application/problem+json`), with `detail`
written for the end user — never an exception message, SQL or a stack trace.

### Sessions

The JWT is delivered in an **`HttpOnly`, `Secure`, `SameSite=Strict` cookie**
(`carpos_session`), not in the response body and not in an `Authorization`
header. The frontend is served from the same origin (nginx proxies `/api` to
this API), so the browser attaches it automatically and no script can read it —
which means an XSS bug cannot steal a session.

Both cookies expire by **`Max-Age`**, not `Expires`
([`Services/Auth/SessionCookieWriter.cs`](Services/Auth/SessionCookieWriter.cs)):
a duration the browser resolves against its own clock, so a server whose clock
has drifted still issues usable sessions. An absolute `Expires` computed here
would be judged there — and a server running more than `Jwt:LifetimeHours`
behind would hand out cookies that are already expired on arrival, making every
sign-in look successful and every request after it 401.

The cost of cookies is CSRF, so every mutating request must also echo the
readable `carpos_csrf` cookie in an **`X-CSRF-Token`** header
([`Middleware/CsrfProtectionMiddleware.cs`](Middleware/CsrfProtectionMiddleware.cs)).
The check only applies when a session cookie is present — without one there is
no ambient authority to abuse, and requiring a token would break sign-in itself.

`POST /api/auth/*` is rate-limited per client address (20/minute); it is the
only place an attacker gets unlimited free guesses.

### Authorisation

There is no ownership column anywhere. What a user may do is entirely decided by
their active row in `accesses`, resolved on **every** request by
[`Services/Authorization/DeviceAccessAuthorizer.cs`](Services/Authorization/DeviceAccessAuthorizer.cs).
Reading it from the database each time (rather than baking it into the token) is
what makes a revoked share stop working immediately.

Two invariants are enforced server-side and never taken from the client:

- **`CanRead` is always true** on an active grant.
- **`CanShare` coerces `CanModifySettings` on** — being able to hand out a right
  you do not hold makes no sense. See
  [`Services/Authorization/CapabilitySet.cs`](Services/Authorization/CapabilitySet.cs).

A device you cannot see answers **404**, not 403 — a 403 would confirm it
exists. The last account able to share a device cannot be revoked or demoted:
since devices are only soft-deleted, that state would be permanent.

## Provisioning a device

Two routes: the endpoint generates a key pair for a brand-new device, the CLI
imports one you already have (and is how you rotate a key).

### `POST /api/devices` — generate and register (recommended)

```jsonc
// Request — authenticated (session cookie + CSRF header)
{
  "deviceId": "GNSS02",
  "displayName": "Test car",
  // Optional. Each entry becomes one access grant; an email that matches no
  // account is skipped silently, so this cannot be used to test who has one.
  "additionalAccesses": [
    { "userEmail": "colleague@example.com", "canDelete": false, "canShare": false, "canModifySettings": true }
  ]
}
```

Generates an RSA-3072 key pair, stores the private half encrypted under the
master key, creates the device row **and the caller's full access grant in one
transaction**, and returns **201** with the public half plus a paste-ready block
of `constexpr` lines for
[`../../ESP32/src/config/Config.h`](../../ESP32/src/config/Config.h):

```jsonc
{
  "device": {
    "deviceId": "GNSS02",
    "displayName": "Test car",
    "customName": null,
    "isActive": true,
    "createdAt": "2026-07-22T12:00:00Z",
    "deactivatedAt": null,
    "lastSeenAt": null,
    "lastBatteryPct": null,
    "permissions": { "canRead": true, "canDelete": true, "canShare": true, "canModifySettings": true }
  },
  "provisioning": {
    "deviceId": "GNSS02",
    "displayName": "Test car",
    "telemetryTopic": "devices/GNSS02",
    "configTopic": "devices/GNSS02/config",
    "brokerUri": "wss://jimajer.cz:443/mqttBroker",
    "publicKeyPem": "-----BEGIN PUBLIC KEY-----\n…",
    "publicKeyFingerprint": "9F1C…",   // SPKI-SHA256, uppercase hex
    "configSnippet": "// --- carPosTracking device \"GNSS02\" …"
  }
}
```

The `provisioning` half can be read again later with
`GET /api/devices/{deviceId}/provisioning` (requires `canModifySettings`). That
re-renders the **stored public key** rather than generating a new pair, so it is
always safe to call — a device already in the field keeps working.

`configSnippet` is a **complete `Config.h`**, not a block to merge: save it as
`ESP32/src/config/Config.h` and run `pio run`. It is the firmware's own
`Config.example.h` with this device's values substituted in:

| `Config.h` constant | Source |
|---|---|
| `kReceiverPublicKeyPem` | generated — public half of the new key pair |
| `kDeviceId`, `kMqttClientId`, `kMqttUsername` | the requested `deviceId` |
| `kTelemetryTopic` | `devices/<deviceId>` |
| `kConfigTopic` | `devices/<deviceId>/config` |
| `kAckTopic` | `devices/<deviceId>/ack` |
| `kAckEnabled` | `true` only when an ack public key is on file, otherwise `false` |
| `kMqttBrokerUri` | `Mqtt:BrokerUri` — **replace by hand** if that address is container-internal |
| `kDefault*`, `kFixAcquireTimeoutSeconds`, `kSdMaxQueuedFixes`, `kRetry*Hours` | the device's **current** settings revision, so a freshly flashed tracker is already right before the broker replays its config |
| `kMin*` / `kMax*` clamps | `DeviceConfigRules` — the same numbers this API validates against |
| `kMqttPassword`, `kWifiSsid`, `kWifiPassword`, `kDeviceAckPrivateKeyPem` | **emitted empty — see below** |
| everything else (pins, timings, feature flags) | copied verbatim from the template |

> **The four blanks are deliberate and are filled in by the dashboard, in the
> browser.** Three are simply not the server's to know (your WiFi credentials, and
> a broker password the API never issued). The fourth is stronger than that:
> `kDeviceAckPrivateKeyPem` is the *device's own* secret, and the ack direction
> only works because this server never holds it — see
> [`--ack-public-pem`](#--ack-public-pem--enable-delivery-acks-for-a-device) below.
> The dashboard generates that pair with WebCrypto and posts back only the public
> half, so none of the four ever travels through this API.

**Where the template lives.** `Services/Provisioning/ConfigTemplate.h.txt`, an
embedded resource. It is a *copy* of `ESP32/src/config/Config.example.h`, because
the API image is built with `../../API` as its Docker context and the firmware
tree does not exist where this code runs. **When the firmware gains or loses a
constant, that template needs the same edit** —
`ConfigSnippetBuilderTests.RendersACompleteCompilableFile` fails the build when it
drifts, and the dashboard's `FE/src/utils/firmwareParameters.ts` lists the same
constants for its reference table.

> **The broker account is still a manual step.** The API does not manage MQTT
> credentials or ACLs: create the account on the server (`mosquitto_passwd`) and
> grant it `write devices/<id>` plus `read devices/<id>/config`, then paste the
> password into `Config.h` yourself. Note that Mosquitto needs that explicit read
> rule before it will *deliver* the retained config — without it the subscription
> is ACK'd and every message is silently dropped.

Status codes: **201** created, **409** the device id is already taken (ids are
permanent — use the CLI to rotate a key), **400** validation failure. Device ids
are limited to `[A-Za-z0-9_-]{1,64}`, matching the ingest's topic guard, and are
stored case-sensitively because MQTT topics are.

**No restart is needed** — `DeviceRegistry` loads unknown devices on demand. The
one exception: if something published to that topic *before* provisioning, the
rejection is negatively cached for `Ingest:UnknownDeviceNegativeCacheMinutes`
(default 5).

> **Authentication required.** This endpoint creates a device the ingest will
> trust, so it sits behind `[Authorize]` like every other endpoint here. It used
> to be gated to Development for want of anything else to guard it with; that
> gate is gone now that sessions exist.

### `import-device-key` — import an existing key (and rotate)

The ingest decrypts with the receiver private key that pairs with the
`kReceiverPublicKeyPem` flashed into the device. Import it once (and after any
rotation):

```powershell
dotnet run -- import-device-key --device GNSS01 --pem receiver_private.pem --public-pem receiver_public.pem --name "GNSS01"
```

The key is validated (RSA-3072; public/private pairing when `--public-pem` is
given), encrypted under the master key and upserted. Only a SPKI-SHA256
fingerprint is printed. **Restart the API afterwards** — keys are cached (the
cache also refreshes itself every 60 minutes).

### `--ack-public-pem` — enable delivery acks for a device

Acks are sealed **to** the device, so the key roles invert: the device holds the
private half and this server needs only the public one.

> **From the dashboard instead:** the device's Settings tab can mint the pair with
> WebCrypto in your browser, splice the private half into the `Config.h` it hands
> you, and store only the public half through
> `POST /api/devices/{deviceId}/ack-key`. Same rule, same validation
> (`AckPublicKeyValidator`, shared by both paths) — it just saves a shell session.
> It deliberately stores the key **only after** you confirm you have saved the
> file: doing it the other way round would leave the device with a server-side key
> whose private half was never written down anywhere.

To do it by hand, generate the pair yourself — **the private key must never reach
this server** — and import only the public half:

```powershell
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out GNSS01_ack_private.pem
openssl rsa -in GNSS01_ack_private.pem -pubout -out GNSS01_ack_public.pem

dotnet run -- import-device-key --device GNSS01 --ack-public-pem GNSS01_ack_public.pem
```

Then paste the **private** PEM into `kDeviceAckPrivateKeyPem` in the firmware's
git-ignored `Config.h`, set `kAckEnabled = true`, and delete the loose `.pem`.

`--ack-public-pem` may be given on its own — adding acks to an existing device
must not require handling its receiver private key. The command refuses a file
containing a private key, and there is deliberately no pairing round-trip: we do
not have the private half and would not want it. Until a device has an ack key it
is simply not sent acks; its fixes are still ingested normally.

**The broker ACL must also allow it**, or Mosquitto ACKs the subscription and
silently drops every ack:

```
user GNSS01
topic read devices/GNSS01/ack

user dashboard
topic write devices/+/ack
```

## Remote device settings

Seven knobs on each tracker are server-owned and editable from the dashboard, with
no reflash and without the device being online at the time:

| Field | Meaning | Range | Default |
|---|---|---|---|
| `intervalSeconds` | seconds between position reports | 5 – 86400 | 60 |
| `sleepBetween` | deep-sleep + modem power-down between reports | — | false |
| `fixTimeoutSeconds` | how long to chase a GNSS lock before giving up on a cycle | 15 – 900 | 180 |
| `queueMaxFixes` | undelivered fixes the SD queue may hold | 100 – 100000 | 20000 |
| `retryIntervalHours` | hours between attempts on a rejected fix | 1 – 720 | 24 |
| `retryMaxAgeHours` | abandon a still-rejected fix after this long; `0` = never | 0 – 8760 | 168 |
| `configCheckSeconds` | how often an **awake** device re-asks for this document | 60 – 86400 | 900 |

The bounds live in [`Dtos/DeviceConfigRules.cs`](Dtos/DeviceConfigRules.cs) and
**mirror the firmware's clamps** in `ESP32/src/config/Config.h`. The two sides
enforce them differently on purpose: this API answers **400**, because a person at
a dashboard can be told to fix their input, while the device *clamps*, because a
tracker in a field has nobody to ask. Change one side and you must change the other.

### The flow

```
PUT /api/devices/GNSS01/config
      │
      ├─ insert device_config_versions row (version = previous + 1)   ┐ one
      └─ devices.config_version → that row                            ┘ transaction
      │
      ▼  (after commit)
MqttConfigPublisher ──retained, QoS 1──► devices/GNSS01/config
                                               │
                                               ▼  broker replays on subscribe
                                          ESP32 adopts + caches to SD
                                               │
                                               ▼  settings_version in every fix
PositionWriter ──► devices.config_applied_version / config_applied_at
```

**Retain is the whole mechanism.** A device with `sleepBetween` on is awake for a
few seconds per cycle and would essentially never catch a live publish; the broker
holds the document and replays it the instant the device subscribes, which it does
on every wake. `MqttIngestService` also calls `RepublishAllAsync` after each
successful subscribe, so a broker restarted without persistence heals itself
rather than leaving a fleet on stale settings.

**An awake device applies a change in under a second.** It holds an open
subscription, so the publish above reaches it unasked — no polling from either
side. Its application task blocks on a FreeRTOS event group that the arriving
message signals, which costs exactly what the plain delay it replaced did. Three
paths deliver a document, all converging on the same handler: push (this one), the
retained replay on every reconnect, and the device's own periodic re-check
(`configCheckSeconds`), which exists only to recover from a connection that looks
alive but delivers nothing. See *Staying in step* in
[`../../ESP32/README.md`](../../ESP32/README.md).

**Publishing happens after the commit**, deliberately: if the broker is down the
save still stands and the reconnect sweep delivers it, whereas publishing first
could hand a device a revision a failed commit then erased.

**The applied version only ever moves forward.** A backlog drained from an SD card
carries the revisions its fixes were *taken* under — possibly days and several
revisions old — so `PositionWriter` takes the version from the newest fix in the
batch and updates only when it is higher than what is stored.

**Saving unchanged values is a no-op.** No revision is created, so a double-click
or a form that re-submits cannot inflate the history.

### Broker ACL

The API publishes to `devices/+/config`, which needs a write grant for whichever
account `Mqtt:Username` resolves to (`dashboard` in the deployed stack), and the
device needs the matching read:

```
user dashboard
topic write devices/+/config

user GNSS01
topic read devices/GNSS01/config
```

Without the write rule the publish is refused; without the read rule Mosquitto
ACKs the subscription and silently drops the document. Both are in
[`../../Container/MQTTBroker/mosquitto/acl`](../../Container/MQTTBroker/mosquitto/acl).

## Build, test, run

```powershell
dotnet build                       # must be clean
dotnet test ..\CarPosAPI.Tests     # crypto/codec/validator unit tests
dotnet run                         # http://localhost:5135 (https://localhost:7032)
dotnet format                      # before finishing a change
```

`GET /health` (unauthenticated liveness) reports the database check and the
MQTT link (Degraded while reconnecting) plus ingest counters. OpenAPI is mapped
in Development only. Neither is proxied to the public internet — the frontend's
nginx serves `/api/` and nothing else.

To run the whole stack in containers (API + frontend + nginx), see
[`../../Container/App/docker-compose.yml`](../../Container/App/docker-compose.yml).
Migrations are still applied by hand as `admin` — the container never migrates
on start-up.

### Working on the frontend at the same time

`dotnet run` on the `http` profile and `npm run dev` in [`../../FE`](../../FE)
work together: the Vite dev server proxies `/api` to `localhost:5135`, which
reproduces the container's single-origin setup, cookies and all.

## One-time server setup (ops)

These are applied manually on the server (nothing here edits
[`../../Container/`](../../Container/)):

### Mosquitto — ingest account + ACL + persistence

```conf
# mosquitto.conf
persistence true
persistence_location /mosquitto/data/
autosave_interval 300
# A full SD-backlog drain is ~500 QoS 2 messages; the default queue cap (1000)
# silently drops beyond it — raise it.
max_queued_messages 5000
```

```bash
mosquitto_passwd -b /mosquitto/config/passwords carpos-api '<password>'
# ACL for the ingest account: read telemetry, write delivery acks.
#   user carpos-api
#   topic read devices/+
#   topic write devices/+/ack
# ...and the device must be allowed to READ its ack topic, or the broker ACKs the
# subscription and silently drops every ack:
#   user GNSS01
#   topic read devices/GNSS01/ack
```

**Verify actual delivery, not just the SUBACK** — this broker once granted a
subscription while silently filtering delivery (2026-07-03):

```bash
mosquitto_sub -h jimajer.cz -p 443 --capath /etc/ssl/certs -u carpos-api -P '<password>' -t 'devices/+' -q 2 -v &
mosquitto_pub -h jimajer.cz -p 443 --capath /etc/ssl/certs -u GNSS01 -P '<device password>' -t 'devices/GNSS01' -q 2 -m '[]'
# The subscriber must print the message. (An empty array is consumed and logged
# as invalid by the API — harmless as a probe.)
```

### PostgreSQL — TLS for remote connections

The API always reaches the database over the network, so the server must offer
TLS. On the server, mount a certificate and enable SSL (compose override):

```yaml
services:
  postgres:
    command: >-
      -c ssl=on
      -c ssl_cert_file=/certs/server.crt
      -c ssl_key_file=/certs/server.key
    volumes:
      - ./certs:/certs:ro
```

The key file must be owned by the container's postgres user (uid 999) with mode
`600`. Prefer `hostssl … scram-sha-256` entries (no plain `host` lines for
remote ranges) in `pg_hba.conf`. The client connection string then uses
`SSL Mode=VerifyFull;Root Certificate=<ca.crt>`.

Stricter alternative: keep 5432 bound to `127.0.0.1` on the server and reach it
through an SSH tunnel from the dev machine
(`ssh -L 5432:localhost:5432 user@jimajer.cz`, then `Host=localhost` in the
connection string).

## Verification checklist (end to end)

1. Startup without secrets fails fast with a clear message; with secrets it
   logs "Connected to MQTT broker … Subscribed to devices/+ at QoS 2".
2. `\d positions` (psql) shows the CHECKs, the unique index and the generated
   `location` column + GIST index; the `BE` role can DML but not DDL.
3. Publish a captured device envelope to `devices/GNSS01` → one row appears
   with the correct UTC `fix_time`; publish the same message again → log shows
   `inserted 0, duplicates 1`.
4. Malformed junk on the topic → aggregated warning, service keeps running, no
   coordinates or key material anywhere in the logs.
5. Stop the API, publish, start it → the queued message arrives (persistent
   session). Stop PostgreSQL mid-flow → bounded retries, reconnect cycle,
   message lands after PostgreSQL returns.
6. Live device test including a forced SD backlog (multi-envelope arrays).
7. `POST /api/devices` (Development) returns 201; repeating it returns 409 and a
   `deviceId` containing `/` returns 400. Neither the response body nor the log
   contains `PRIVATE KEY`, and in psql
   `SELECT device_id, public_key_pem IS NOT NULL, private_key_ciphertext IS NOT NULL
   FROM devices WHERE device_id = 'GNSS02';` is true for both.
8. Save the returned `configSnippet` **as** `ESP32/src/config/Config.h` (back the
   existing one up first — it holds real secrets) and `pio run` compiles it
   untouched. This is the check that matters: the C string literal escaping is the
   part most likely to be subtly wrong, and a comment line ending in `\` silently
   swallows the next one under `-Werror=comment`.
9. Repeat with the dashboard's fields filled in and a generated ack key — that is
   the file an operator actually flashes, and it exercises the browser-side
   substitution as well as this renderer.

## Roadmap (later phases)

Done: the ingest pipeline, API-driven provisioning, the REST endpoints per the
contract in [`CLAUDE.md`](CLAUDE.md), cookie sessions, device sharing, versioned
remote device settings, and the container deployment in
[`../../Container/App/`](../../Container/App/).

Still to do:

1. **Position retention/pruning job** — `positions` grows without bound; every
   read is capped today, but nothing deletes old rows yet.
2. **Session revocation** — tokens carry a `jti` but there is no deny-list, so
   signing out on one device does not invalidate a session already issued to
   another. Changing `Jwt:SigningKey` is the only blunt instrument today.
