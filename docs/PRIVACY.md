# Privacy Policy — carPosTracking

**Policy version: `2026-09-06`** · Last updated: 6 September 2026

> **This document and the in-app page at `/privacy` carry the same text.** The in-app page
> is the authoritative version for users, because it is the one shown at registration and
> the one available in both English and Czech. If you edit one, edit the other:
> `FE/src/i18n/locales/{en,cs}/legal.json`.

---

## 0. Read this first

**carPosTracking is a non-commercial personal test project.** It is not a company, not a
product, and not a service offered to anyone. It exists so its author can learn by building
a GNSS vehicle tracker end to end. There is no warranty, no support, and no guarantee that
the deployment will still be running tomorrow.

It nevertheless stores **precise vehicle location histories**, which are personal data under
Regulation (EU) 2016/679 (GDPR). This document says exactly what is held, why, who else sees
it, and how to get it back or get rid of it. Nothing here is buried: if you only read one
section, read [§6 Retention](#6-how-long-data-is-kept) and [§7 Your rights](#7-your-rights).

## 1. Who is responsible (the controller)

| | |
|---|---|
| **Controller** | Jiří Majer — a natural person, acting in a personal capacity |
| **Contact for data-protection requests** | `SET-CONTROLLER-CONTACT-EMAIL` |
| **Deployment** | `https://jimajer.cz/carPosFE` (self-hosted, Czech Republic) |
| **Data Protection Officer** | None. A project of this size is not required to appoint one, and has not. |

> WARNING: **Before this deployment is used by anyone other than the author, the contact
> address above must be filled in.** The API refuses to start in Production while it is unset.

## 2. What this system does

A small battery-powered tracker in a vehicle takes a GPS fix, encrypts it, and publishes it
over MQTT. A backend decrypts and stores it. A web dashboard draws it on a map, plots the
telemetry, and lets the account holder share a device with other accounts.

The position payload is **encrypted end to end**: sealed on the tracker with the backend's
public key, opened only inside the backend. The MQTT broker in the middle — and anybody who
can reach it — sees ciphertext and nothing else.

## 3. What personal data is processed

### 3.1 Account data — because you created an account

| Data | Where it comes from |
|---|---|
| Email address | you, at registration; it is your login identity |
| First and last name | you, at registration; shown to people you share a device with |
| Password | you — stored only as a PBKDF2-HMAC-SHA256 salted hash, never in the clear |
| Account creation time | recorded automatically |
| Privacy-policy acknowledgement — the timestamp and the version you accepted | recorded automatically at registration |

### 3.2 Location and vehicle telemetry — because you run a tracker

For every fix a device reports:

| Data | Note |
|---|---|
| **Latitude and longitude** | precise geolocation — the most sensitive data here |
| **Speed** | driving behaviour |
| **Acceleration on three axes** | driving behaviour — braking, cornering |
| Altitude, GNSS fix time, server receive time | |
| Battery percentage, temperature | device health |
| Device identifier (e.g. `GNSS01`) and display name | pseudonymous; you choose the name |

A location history describes where a vehicle — and therefore, usually, a person — has been,
when, and how they drove. It is treated as sensitive throughout: **coordinates are never
written to any log file.**

### 3.3 Device and account relationships

Which accounts may see which device and with what permissions; who granted that permission;
your private nickname for a device; the tracking schedules and sampling intervals configured
for a device (which reveal *when* a vehicle is tracked closely).

### 3.4 Technical data

- **IP addresses** — seen by the web server, the MQTT broker, and Cloudflare in the ordinary
  course of serving a request, and used in memory to rate-limit sign-in attempts. The
  application itself never writes an IP address to its own log.
- **Connection logs of the broker** — timestamps and client identifiers, kept in bounded
  container logs (capped at 30 MB per service, oldest discarded).

### 3.5 What is *not* collected

No analytics. No advertising. No tracking pixels. No third-party scripts other than the map
(see §5). The dashboard does **not** ask for your browser's own location — that permission is
blocked outright by the site's `Permissions-Policy` header. Nothing is sold or shared for
marketing, ever.

## 4. Why, and on what legal basis

| Purpose | Legal basis (Art. 6 GDPR) |
|---|---|
| Running your account, authenticating you, showing your devices' data | **Contract** — Art. 6(1)(b); it is what you signed up for |
| Storing and displaying position history from a tracker you operate | **Consent** — Art. 6(1)(a), given by acknowledging this policy at registration and by choosing to run a device |
| Keeping the service secure — password hashing, CSRF protection, rate-limiting sign-ins | **Legitimate interests** — Art. 6(1)(f): keeping other people out of your data |

Consent can be withdrawn at any time by deleting your account (§7), which erases the data.

> **If you put a tracker in a vehicle that someone else drives, *you* become a controller of
> *their* data**, and it is your responsibility to tell them and to have a lawful basis for
> it. This project gives you the technical means; it cannot give you the legal cover, and
> tracking someone without their knowledge is unlawful in most circumstances.

## 5. Who else sees the data

| Recipient | What they receive | Why |
|---|---|---|
| **Other users you share a device with** | that device's positions and telemetry, plus your name and email address | because you granted them access; revocable at any time in device settings |
| **Google LLC** (Google Maps JavaScript API, USA) | your IP address, browser details, referrer, and — through the map viewport — **the area the tracked vehicle is in** | to draw the map. **This request is not made until you consent to it.** The dashboard shows a placeholder and asks first; declining costs you only the map. The choice is remembered in your browser and revocable under *Profile → Privacy*. Transfers to the USA rely on Google's participation in the EU–US Data Privacy Framework. |
| **Cloudflare, Inc.** | all traffic to the deployment, including sign-in requests and position data, because it terminates TLS for the tunnel | it is how the self-hosted server is reachable from the internet |
| **Nobody else** | | |

The MQTT broker is self-hosted and only ever holds encrypted payloads. There are no other
processors, no subcontractors, and no data sales.

## 6. How long data is kept

**Positions and telemetry are kept indefinitely.** There is no automatic deletion and no
retention limit.

This is a deliberate choice, and it is the one point where this project does not meet the
GDPR's storage-limitation principle (Art. 5(1)(e)) on its own: the whole point of the project
is looking at history, so nothing expires on a timer. Rather than claim a limit that does not
exist, it is stated here plainly — **and the deletion controls in §7 are real, immediate, and
under your sole control.**

| Data | Kept |
|---|---|
| Positions and telemetry | indefinitely, until you delete the device history or your account |
| Account data | until you delete your account |
| Access grants and device nicknames | until revoked, or until you delete your account |
| Configuration revision history | indefinitely — but the link to *who* made a change is erased when that account is deleted |
| Container and broker logs (including IPs) | rolled at 10 MB, three files per service; days to weeks in practice |
| Encrypted fixes queued on a tracker's SD card | up to 20 000 fixes, oldest dropped; the card is physically in your possession |

## 7. Your rights

Under the GDPR you have the rights below. The first three are wired into the application —
no email, no waiting, no identity-verification dance.

| Right | How to exercise it |
|---|---|
| **Access and portability** (Art. 15, 20) | **Profile → Export my data.** Downloads a JSON file containing *everything* held about you: profile, devices, permissions, nicknames, configuration history, and the complete position history of every device you can read. Not truncated. |
| **Erasure** (Art. 17) | **Profile → Delete my account.** Requires your password, then permanently deletes your account, your access grants and nicknames; any device only you could see is deleted along with its entire position history; devices shared with others survive, with your access removed. This is a real `DELETE`, not a hidden flag. |
| **Erasure of location history alone** (Art. 17) | **Device → Settings → Erase position history.** Wipes a device's positions while keeping the device. |
| **Rectification** (Art. 16) | **Profile** — edit your name; change your password. Your email address is your login identity and cannot currently be changed in the app; write to the contact address in §1. |
| **Restriction and objection** (Art. 18, 21) | Power the tracker off — nothing arrives while it is off — or write to the contact address in §1. |
| **Withdraw consent** (Art. 7(3)) | Delete your account, or revoke map consent under *Profile → Privacy*. Withdrawal does not undo processing that already happened lawfully. |
| **Complain** (Art. 77) | Úřad pro ochranu osobních údajů (Czech DPA), Pplk. Sochora 27, 170 00 Praha 7, https://uoou.gov.cz. You may also complain to the authority where you live. |

Requests sent to the contact address are answered within one month (Art. 12(3)).

## 8. Cookies and browser storage

No advertising or analytics cookies — so there is no cookie banner, because there is nothing
to consent to beyond what is strictly necessary and what you choose.

| Name | Kind | Purpose |
|---|---|---|
| `carpos_session` | cookie, `HttpOnly` `Secure` `SameSite=Strict` | your sign-in session; unreadable by any script |
| `carpos_csrf` | cookie, readable by design | cross-site request forgery protection |
| `carpos.language` | localStorage | your chosen interface language |
| `carpos.csvDelimiter` | localStorage | remembers your CSV export preference |
| `carpos.mapsConsent` | localStorage | remembers whether you allowed the Google map to load |

The localStorage entries never leave your browser and are never sent to the server. Clearing
your site data removes all of them.

## 9. Security

- Position payloads are **encrypted end to end** (RSA-3072-OAEP-SHA256 wrapping a per-message
  AES-256-GCM key) from the tracker to the backend.
- Device private keys are **encrypted at rest** under a master key held only by the backend.
- Passwords are salted PBKDF2 hashes, rehashed on sign-in when the parameters improve.
- The session token lives in an `HttpOnly` cookie, so no injected script can read it; every
  mutating request additionally carries a CSRF token.
- **Coordinates are never logged.** The ingest pipeline logs device ids, counts and reasons.
- Sign-in is rate-limited. Every request is re-authorised against the caller's access grant
  rather than trusting anything the browser says.
- A device that is not yours answers `404`, not `403` — so the API cannot be used to discover
  which device identifiers exist.

No system is perfectly secure, and this one is a personal project maintained by one person in
their spare time. Please do not entrust it with anything you could not stand to lose.

## 10. Children

Not directed at children and not knowingly used to process a child's data.

## 11. Changes

The version string at the top changes when this policy does. New accounts acknowledge the
current version at registration, and the version accepted is recorded against the account.
Material changes will be surfaced in the application.

---

*carPosTracking is a non-commercial personal test project, licensed under the
[PolyForm Noncommercial License 1.0.0](../LICENSE). Field-level detail of everything stored is
in [DATA-INVENTORY.md](DATA-INVENTORY.md); the Art. 30 record is in
[RECORD-OF-PROCESSING.md](RECORD-OF-PROCESSING.md).*
