# Data inventory — every place personal data lives

**carPosTracking**, version `2026-09-09`. Companion to the policy served at `/privacy` and
[RECORD-OF-PROCESSING.md](RECORD-OF-PROCESSING.md). This is the engineering-level list:
every column, file and log line that holds personal data, and what happens to it when a user
exercises their rights.

Legend — **P** = directly personal, **p** = pseudonymous or indirectly identifying,
· = not personal.

---

## 1. PostgreSQL

### `users`

| Column | | Note | On account deletion |
|---|---|---|---|
| `id` | p | | deleted |
| `email` | **P** | login identity, lower-cased, unique | deleted |
| `password_hash` | **P** | PBKDF2-HMAC-SHA256, salted — **never exported, never logged** | deleted |
| `first_name`, `last_name` | **P** | visible to users you share a device with | deleted |
| `created_at` | p | | deleted |
| `privacy_policy_version`, `privacy_policy_accepted_at` | p | which version of the terms of use **and** privacy policy this account accepted, and when (column names are historical) | deleted |

### `positions` — the sensitive table

| Column | | Note | On account deletion |
|---|---|---|---|
| `device_id` | p | links to a device and thence to a person | — |
| `latitude`, `longitude` | **P** | **precise geolocation** | deleted with the device if solely owned |
| `speed_kmph`, `accel_x/y/z_g` | **P** | driving behaviour | as above |
| `fix_time`, `received_at` | **P** | when the vehicle was where | as above |
| `altitude_m` | **P** | | as above |
| `battery_pct`, `temperature_c` | · | device health | as above |

Bounded on read at 1000 rows per query (`PositionQueryService.MaxPositionsPerQuery`); the data
export deliberately bypasses that cap so portability is complete. **Never auto-deleted** — see
the retention section of the policy at `/privacy`. Erasable via
`DELETE /api/devices/{deviceId}/positions` or by deleting the account.

### `devices`

| Column | | Note |
|---|---|---|
| `device_id` | p | the MQTT identity, e.g. `GNSS01`; also the topic segment and broker username |
| `display_name` | **P** | free text chosen by a user — often a person's or a car's name |
| `last_seen_at` | **P** | a presence and activity signal |
| `private_key_ciphertext` | · | **secret** — AES-256-GCM sealed; never selected into a DTO, never exported, never logged |
| `public_key_pem`, `ack_public_key_pem` | · | public halves |
| `config_*`, `reported_*`, `schedule_bundle_version` | p | how closely and when the vehicle is tracked |
| `tracking_declaration_accepted_at` | p | when whoever registered the device confirmed they were entitled to track the vehicle and would tell its drivers; evidence only, nothing reads it |
| `is_active`, `deactivated_at`, `created_at` | · | |

Deleted outright when the erased account was its only remaining accessor; otherwise untouched.

### `accesses`

`user_id` **P**, `device_id` p, `granted_by` **P**, four capability flags ·, `is_active` ·,
`date_registration` p. The social graph: who may watch whose vehicle.

On account deletion the user's own rows are deleted; `granted_by` on *surviving* rows is set to
`NULL`, keeping the operational record without the personal link.

### `device_aliases`

`user_id` **P**, `device_id` p, `alias` **P** (free text, frequently a person's name),
`updated_at` p. Deleted with the account.

### `device_config_profiles`, `device_config_schedule_rules`, `device_config_versions`

`name` **P** (free text), `created_by_user_id` **P**, timestamps p, and the sampling policy
itself — p, because it reveals *when* a vehicle is tracked closely. The revision history is
append-only and never pruned; `created_by_user_id` is set to `NULL` on account deletion.

---

## 2. The tracker's microSD card

Physically in the vehicle, in the operator's possession. **Nothing on the card reacts to a
server-side erasure** — deleting an account does not reach into the device. Wipe the card, or
reflash, if that matters to you.

| File | Contents | Encrypted | Bound |
|---|---|---|---|
| `queue.jsonl` | undelivered position fixes, one sealed envelope per line | **yes** | 20 000 fixes, oldest dropped |
| `queue.jsonl.idx` | head offset and live count | n/a | — |
| `retry.jsonl` | API-rejected fixes; the envelope is encrypted, but `first`/`next` **timestamps are plaintext** | partly | 2000 entries, aged out by `retry_max_age_h` |
| `settings.json` | cached sampling settings — **plaintext**, no position data | no | overwritten |
| `schedule.json` | the weekly tracking schedule — **plaintext**; reveals the tracking pattern | no | overwritten |
| `boot.log` | one line per boot: reset reason, boot counter | no | 200 lines (a ring buffer) |

Flashed into the firmware image and recoverable from a physically stolen device: WiFi SSID and
password, MQTT device credentials, the device's ack private key, the backend's public key.

---

## 3. In transit

| Hop | What is visible |
|---|---|
| Tracker → broker | **ciphertext only**; the topic name (`devices/<id>`) and the client IP are visible |
| Broker → API | same ciphertext; the API connects as the `dashboard` broker account |
| API → browser | plaintext JSON over TLS — but Cloudflare terminates that TLS and therefore sees it |
| Browser → Google | on map load only, after consent: IP, user-agent, referrer, and the viewport (hence the vehicle's area) |

The broker keeps **retained** `devices/<id>/config` and `/schedule` messages, so its
`mosquitto_data` volume persistently holds each device's tracking policy. Account deletion
publishes an empty retained payload on both topics to clear them.

---

## 4. Logs

| Source | Holds | Retention |
|---|---|---|
| API application log (stdout) | device ids, user ids, counts, reasons; method and path on failure | bounded container log: 10 MB × 3 |
| **Never in the API log** | coordinates, email addresses, passwords, tokens, keys, request bodies, IP addresses | — |
| Mosquitto | `New client connected from <IP> as <client-id>` — an IP↔device correlation | bounded container log: 10 MB × 3 |
| Broker nginx `access_log` | client IPs, combined format | bounded container log: 10 MB × 3 |
| ESP32 serial console | **prints coordinates in the clear** (`ESP32/src/gnss/GnssModule.cpp`) | volatile; requires physical UART access |

---

## 5. Browser

`carpos_session` (`HttpOnly`, unreadable by script), `carpos_csrf`, and three localStorage
preferences: `carpos.language`, `carpos.csvDelimiter`, `carpos.mapsConsent`. None of the
localStorage values is ever sent to the server.

---

## 6. What the data export contains

`GET /api/me/export` streams a JSON document with: the profile, every access grant held and
granted, device nicknames, metadata for every readable device, authored configuration
profiles/rules/revisions, and **the complete position history of every readable device** —
uncapped.

It must never contain `password_hash`, `private_key_ciphertext`, JWT signing material or the
device-key master key. `DataExportShapeTests` pins that shape down against the serialised
output of every projection record, which is the test to keep passing when this shape
changes.
