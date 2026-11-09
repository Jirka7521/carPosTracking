# Data inventory — every place personal data lives

**carPosTracking**, version `2026-10-09`. Companion to the policy served at `/privacy` and
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
| `maps_consent_version`, `maps_consent_granted_at` | p | the account's standing consent to load the Google map: which prompt wording, and when. Null when none; **withdrawal nulls both**, no history kept | deleted |

### `positions` — the sensitive table

| Column | | Note | On account deletion |
|---|---|---|---|
| `device_id` | p | links to a device and thence to a person | — |
| `latitude`, `longitude` | **P** | **precise geolocation** | deleted with the device if solely owned |
| `speed_kmph`, `accel_x/y/z_g` | **P** | driving behaviour | as above |
| `fix_time`, `received_at` | **P** | when the vehicle was where | as above |
| `altitude_m` | **P** | | as above |
| `battery_pct` | · | device health | as above |

Bounded on read at 1000 rows per query (`PositionQueryService.MaxPositionsPerQuery`); the data
export deliberately bypasses that cap so portability is complete. **Never auto-deleted** — see
the retention section of the policy at `/privacy`. Erasable via
`DELETE /api/devices/{deviceId}/positions` or by deleting the account.

### `device_events` — the device history

| Column | | Note | On account deletion |
|---|---|---|---|
| `device_id` | p | links to a device and thence to a person | — |
| `occurred_at` | **P** | when it happened — the tracker went offline, restarted, woke, or started / stopped moving — i.e. when the vehicle was parked, driven or left alone | deleted with the device if solely owned |
| `received_at` | **P** | when the server received it; later than `occurred_at` for an event the tracker kept on its SD card while out of range | as above |
| `device_time` | **P** | the tracker's own clock at that moment, when it trusted one | as above |
| `kind`, `reason`, `severity` | p | offline (sleep / sleep because parked / switched off / battery low / error / connection lost), restart (power on / power loss / crash), wake (timer / accelerometer / power switch) or motion step (checking / jolt / moving / no motion / stopped / motion wake on or off), and how serious | as above |
| `battery_pct`, `sleep_seconds`, `detail` | · | device health; expected time away; an error code such as `gnss_init` | as above |

No location — but a detailed timetable of when the vehicle was in use: the motion steps say when
it started moving and when it was parked, to the minute, so it is treated exactly like `positions`:
several rows per sleep cycle on a sleeping device, **never auto-deleted**, erased over the same
range by `DELETE /api/devices/{deviceId}/positions` and with the device on account deletion, and
included in the data export. Reads are bounded at 1000 rows
(`GET /api/devices/{deviceId}/events`). Written only by the status ingest from the tracker's own
sealed messages and its MQTT Last Will. On the tracker, events said out of range wait on its SD
card (`events.jsonl`) as **ciphertext only**, like queued fixes, until the broker acknowledges
them.

### `trips` — derived trip starts

| Column | | Note | On account deletion |
|---|---|---|---|
| `device_id` | p | links to a device and thence to a person | — |
| `start_time` | **P** | when a trip began — derived from `positions` by the trip worker | deleted with the device if solely owned |
| `journey_id`, `journey_pinned` | p | a user's manual placement of the trip in a journey | as above |
| `name` | **P** | free text a user gave the trip | as above |
| `computed_at` | · | when the worker last wrote the row | as above |

**Deliberately minimal:** only each trip's start is stored. Its end, distance, duration and
speeds are recomputed from `positions` on every read and never kept, so erasing positions
erases them too. No location is stored here — but, like `device_events`, a timetable of when
the vehicle was driven, so it is treated like `positions`: **never auto-deleted**, trips
starting inside a range erased by `DELETE /api/devices/{deviceId}/positions` go with it (the
worker then rebuilds whatever the remaining fixes support), and they are deleted with the
device on account deletion.

### `journeys` and `device_trip_settings`

| Column | | Note | On account deletion |
|---|---|---|---|
| `journeys.name` | **P** | free text — "Croatia 2026", "Mum's" — visible to everyone with access to the device | deleted with the device if solely owned |
| `journeys.range_from`, `range_to` | p | the dates a user grouped as one journey — often a holiday | as above |
| `journeys.parent_id`, `created_at`, `updated_at` | · | nesting and bookkeeping | as above |
| `device_trip_settings.*` | · | the device's trip detection thresholds | as above |

`trip_detection_state` holds per-device worker bookkeeping only (a fix time to recompute
from, a counter, a timestamp) and is deleted with the device; `trip_detection_cursor` is a
single row holding a position id, nobody's data.

### `devices`

| Column | | Note |
|---|---|---|
| `device_id` | p | the MQTT identity, e.g. `GNSS01`; also the topic segment and broker username |
| `display_name` | **P** | free text chosen by a user — often a person's or a car's name |
| `last_seen_at` | **P** | a presence and activity signal |
| `last_online_at` | **P** | when the tracker last announced a broker connection — a presence signal |
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

### `share_links`

`device_id` p, `created_by_user_id` **P**, `label` **P** (free text, chosen by the creator and
shown to the recipient), `valid_from` / `valid_until` p, `scope` ·, `include_speed` ·,
`include_battery` ·, `revoked_at` p, `created_at` p, and three usage counters —
`successful_redeems` ·, `last_accessed_at` p, `failed_attempts` ·, `locked_until` ·.

Three columns are **credentials, never personal data to export**: `selector`,
`verifier_hash` and `passphrase_hash` (the link is `selector.verifier` in the URL; the
passphrase is the visitor's code). Since the 2026-11 security review **only SHA-256 digests**
of the verifier and of the normalised code are stored (migration `HashShareSecrets` converted
existing rows in place, so live links kept working): the link and code are shown once, when a
link is created or reissued, and a copy of the database no longer contains working links. A
creator who lost a link reissues it.

(Until then all three were stored in the clear, from 2026-09-12, so a creator could look a link
up again; the accepted risk was that a leaked dump or backup was live access to every link still
inside its window. That trade is reversed.)

**Nothing about the recipient is stored.** No IP, no user agent, no identifier of any kind —
the three counters above are the entire record that a link was used. That is deliberate: a
share visitor is a third party who agreed to nothing, and logging them would be collecting
personal data with no basis in order to answer a question the counters already answer.

On account deletion the creator's links are **deleted outright**, not anonymised. A grant
handed to another account survives erasure because that person still needs it; a share link is
the opposite case on both counts — a live credential held by somebody with no account, which
nobody would remain to revoke.

### `access_invitations`

`device_id` p, `created_by_user_id` **P**, the offered capabilities `can_delete` /
`can_share` / `can_modify_settings` ·, `created_at` / `expires_at` p, and the link as
`selector` · plus `verifier_hash` · (SHA-256 only — the link itself is shown once and never
stored). **No recipient is recorded**: an invitation is addressed to nobody until someone
accepts it, and then the row is deleted and the grant it became is what remains. Also deleted
when declined or withdrawn, when the creator loses the right to share or is erased, and when
the device is deleted.

### `device_aliases`

`user_id` **P**, `device_id` p, `alias` **P** (free text, frequently a person's name),
`updated_at` p. Deleted with the account.

### `device_config_profiles`, `device_config_schedule_rules`, `device_config_versions`

`name` **P** (free text), `created_by_user_id` **P**, timestamps p, and the sampling policy
itself — p, because it reveals *when* a vehicle is tracked closely. The revision history is
append-only and never pruned; `created_by_user_id` is set to `NULL` on account deletion.

### `registration_invites` — no personal data

`code_hash` (SHA-256 of a single-use sign-up code; the code itself is never stored),
`created_at`, `expires_at`, `used_at`. Deliberately records **neither who a code was
for nor who used it**, so the table holds nothing about anyone, needs no export or
erasure path, and reveals no way in if it leaks.

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
| Tracker → broker | **ciphertext only**; the topic name (`devices/<id>`, and `devices/<id>/status` for the status messages — connection, wake, motion — and the Last Will) and the client IP are visible — so the broker can see *that* a tracker reported or sent a status, never what it said |
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
| Frontend nginx `access_log` | client IPs; **`/share/...` URIs are redacted to `/share/[redacted]`** by the `carpos` log format, because a share URL is itself a credential | bounded container log: 10 MB × 3 |
| Cloudflare tunnel | request URIs, **including share-link secrets** — outside this repo's control. Known and accepted residual exposure | per Cloudflare |
| ESP32 serial console | **prints coordinates in the clear** (`ESP32/src/gnss/GnssModule.cpp`) | volatile; requires physical UART access |

---

## 5. Browser

`carpos_session` (`HttpOnly`, unreadable by script), `carpos_csrf`, `carpos_share` (`HttpOnly`;
only on a browser that has opened a share link, and only for that link's remaining window),
`carpos_maps_consent` (set by the dashboard's script, only when a share-link visitor chooses
"Always load maps"; holds the prompt version and nothing else; `Path=<base>/share`, so it never
travels with an `/api` request; 180 days; removed by the share page's "Stop loading maps
automatically"), and three localStorage preferences: `carpos.language`, `carpos.csvDelimiter`,
`carpos.uiMode` (Normal or Expert dashboard view).
None of the localStorage values is ever sent to the server. The old `carpos.mapsConsent`
localStorage key is deleted on start-up — a signed-in user's map consent now lives in `users`.

---

## 6. What the data export contains

`GET /api/me/export` streams a JSON document with: the profile (including the map consent's
`mapsConsentVersion` and `mapsConsentGrantedAtUtc`), every access grant held and
granted, device nicknames, the temporary share links and open invitations this account
created (`invitationsMade`, without any part of the link), metadata for every
readable device, authored configuration profiles/rules/revisions, and **the complete position
history and device history (`statusEvents`: offline, restarts, wakes, motion steps) of every
readable device** — uncapped. Each readable device also carries its `tripDetectionSettings`
(null while it uses the defaults), its `journeys`, and its `trips` — each trip as stored:
start time, name, and the journey it was placed in by hand. Trip ends and figures are not
stored, so they are not in the file; they follow from the positions beside them.

It must never contain `password_hash`, `private_key_ciphertext`, JWT signing material, the
device-key master key, or any of a share link's three secret columns. `DataExportShapeTests` pins that shape down against the serialised
output of every projection record, which is the test to keep passing when this shape
changes.
