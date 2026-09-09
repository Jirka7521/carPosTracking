# Record of Processing Activities (GDPR Art. 30)

**carPosTracking** — a non-commercial personal test project. Version `2026-09-09`.

> Art. 30(5) exempts organisations under 250 people from keeping this record *unless* the
> processing is "not occasional" or involves data on a large scale. Continuous vehicle
> location tracking is not occasional, so the exemption is assumed **not** to apply and this
> record is kept anyway. It is deliberately short: the processing is small and simple.

---

## 1. Controller

| | |
|---|---|
| Controller | Jiří Majer, natural person, Czech Republic |
| Contact for data-protection matters | `SET-CONTROLLER-CONTACT-EMAIL` |
| Joint controllers | None |
| Representative (Art. 27) | Not applicable — the controller is established in the EU |
| Data Protection Officer | None appointed; not required at this scale |

---

## 2. Processing activities

### A. User accounts and authentication

| | |
|---|---|
| **Purpose** | Let a person create an account, sign in, and be identified to people they share a device with |
| **Data subjects** | Registered users |
| **Categories of data** | Email address, first and last name, salted password hash, account creation time, privacy-policy version and acceptance timestamp |
| **Legal basis** | Art. 6(1)(b) contract; Art. 6(1)(f) legitimate interest for the security measures |
| **Recipients** | Other users the subject shares a device with (name and email only). Cloudflare as TLS terminator. |
| **Third-country transfers** | Cloudflare (USA) — EU–US Data Privacy Framework |
| **Retention** | Until the user deletes their account (`DELETE /api/me`), which erases the row |
| **Security measures** | PBKDF2-HMAC-SHA256 password hashing with per-password salt and rehash-on-login; `HttpOnly`/`Secure`/`SameSite=Strict` session cookie; CSRF double-submit token; sign-in rate limiting (20/min per IP); emails and names never written to logs |

### B. Vehicle position and telemetry

| | |
|---|---|
| **Purpose** | Record and display where a tracker-equipped vehicle has been, and how it was driven |
| **Data subjects** | Users who operate a tracker, and any other person driving or travelling in a tracked vehicle |
| **Controller split** | For the account holder’s own data the operator is controller. For **other drivers and passengers** the *account holder* is the controller and the operator is a **processor** acting on their instructions; the Art. 28 terms are the `yourDevices` section of the terms of use, accepted at registration and re-confirmed per device (`devices.tracking_declaration_accepted_at`). Drivers without an account are told in the privacy policy to write to the controller contact, and the request is passed to the responsible account holder. |
| **Categories of data** | Latitude, longitude, speed, three-axis acceleration, altitude, GNSS fix time, receive time, battery percentage, temperature, device identifier — **precise geolocation and behavioural data** |
| **Special categories (Art. 9)** | None intended. A location history can incidentally reveal e.g. visits to a place of worship or a clinic; the system does not seek or derive such inferences. |
| **Legal basis** | Art. 6(1)(b) contract — storing and displaying a location history is the service the account holder accepted the terms of use to obtain. **Not** consent: consent bundled into a policy acknowledgement is not freely given or specific (Recital 43), and a basis that collapses under scrutiny would make the whole processing unlawful. |
| **Recipients** | Users granted access to that device. Google LLC receives IP, browser data and — through the map viewport — the approximate area, **only after the viewer consents to loading the map.** Cloudflare as TLS terminator. |
| **Third-country transfers** | Google LLC and Cloudflare, Inc. (USA) — EU–US Data Privacy Framework |
| **Retention** | **Indefinite.** No automatic deletion. Erased on user request: per device (`DELETE /api/devices/{id}/positions`) or by account deletion. Retention is bounded by purpose rather than by a timer: reviewing history is the purpose, so it is kept while the account holder wants it, and the erasure controls are immediate and unconditional. Stated in the policy at `/privacy`. |
| **Security measures** | End-to-end encryption device→backend (RSA-3072-OAEP-SHA256 + AES-256-GCM per message); device private keys encrypted at rest under a master key; coordinates never logged; per-request re-authorisation against the caller's access grant; invisible devices answer 404 not 403 |

### C. Device sharing and access control

| | |
|---|---|
| **Purpose** | Let a device owner grant, change and revoke another account's access to a device |
| **Data subjects** | Registered users |
| **Categories of data** | User↔device grant with four capability flags, who granted it, grant time, per-user device nickname |
| **Legal basis** | Art. 6(1)(b) contract |
| **Recipients** | The two users involved |
| **Retention** | Until revoked or the account is deleted. On account deletion the grant rows are deleted and the "granted by" reference on any surviving grant is nulled, so the operational record survives without the personal link. |

### D. Device configuration and tracking schedules

| | |
|---|---|
| **Purpose** | Let a user change how often a tracker samples, and on what weekly schedule; keep an auditable revision history |
| **Data subjects** | Registered users |
| **Categories of data** | Sampling intervals, schedule rules (which reveal *when* a vehicle is tracked closely), the authoring user id, timestamps |
| **Legal basis** | Art. 6(1)(b) contract; Art. 6(1)(f) legitimate interest in an auditable configuration history |
| **Retention** | Revision history indefinite; the authoring user reference is **nulled** when that account is deleted |

### E. Operational logs

| | |
|---|---|
| **Purpose** | Keep the deployment running and diagnosable; resist brute-force sign-in |
| **Data subjects** | Anyone connecting to the deployment |
| **Categories of data** | IP addresses (broker and reverse-proxy access logs), MQTT client identifiers, connection times, user ids and device ids in application logs, request method and path on failures |
| **Not logged** | Coordinates, email addresses, passwords, tokens, keys, request bodies |
| **Legal basis** | Art. 6(1)(f) legitimate interests — security and availability |
| **Retention** | Bounded container logs: 10 MB per file, 3 files per service, oldest discarded |

---

## 3. Processors

| Processor | Role | Safeguard |
|---|---|---|
| Cloudflare, Inc. | Tunnel and TLS termination — sees all traffic in the clear | EU–US Data Privacy Framework; Cloudflare's standard DPA |
| Google LLC | Google Maps JavaScript API, loaded in the viewer's browser only after consent | EU–US Data Privacy Framework |

No other processors. The database, the API and the MQTT broker are self-hosted on hardware in
the controller's possession.

---

## 4. Data protection impact assessment (Art. 35)

Systematic monitoring of location is on the kind of list that normally triggers a DPIA. A full
DPIA has **not** been carried out because this is a personal test project with a handful of
accounts and no commercial purpose. The mitigations that a DPIA would ask for are nonetheless
in place: end-to-end encryption, no coordinate logging, consent before any third-party map
call, per-device access control, self-service export and erasure, and a plainly-stated
retention position. **If this were ever operated at scale or for anyone else's benefit, a DPIA
would have to be done first** — as would appointing a contact, a breach-notification procedure,
and a retention schedule.

---

## 5. Breach procedure (Art. 33/34)

One person maintains this. In the event of a personal-data breach: assess scope from the
container logs and database, notify the Czech DPA (ÚOOÚ) within 72 hours if the breach is
likely to result in a risk to data subjects, and notify affected users by email at the address
on their account if the risk is high. Given the scale, this is a manual procedure and is
recorded here rather than in a separate runbook.
