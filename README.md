# carPosTracking

> ### ⚠️ Non-commercial test project
>
> This is a **personal test project**, built for learning and experimentation. It is
> **not a product, not a service, and not offered to anyone commercially.** There is no
> warranty, no support, and no uptime expectation — the public deployment exists so the
> author can try the thing out, and it can disappear at any time.
>
> Licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE). **Commercial use of
> any kind is not permitted.**
>
> It handles **precise vehicle location data**, which is personal data under the GDPR.
> See the in-app **privacy policy** (`/privacy`) and **terms of use** (`/legal`) for what is
> collected and how to have it
> deleted, and [docs/DATA-INVENTORY.md](docs/DATA-INVENTORY.md) for the field-by-field
> detail.

A GNSS vehicle tracker built end to end: a battery-powered ESP32 in the car takes a GPS
fix, seals it with public-key cryptography, and publishes it over MQTT; a .NET backend
decrypts and stores it; a React dashboard draws it on a map.

The point of the exercise is the **end-to-end encryption**: the position payload is
encrypted on the microcontroller with the server's public key and is only ever opened
inside the API. The broker in the middle, and anyone who can reach it, sees ciphertext.

---

## The four subsystems

| Folder | What it is | Stack |
|---|---|---|
| **[ESP32/](ESP32/)** | Tracker firmware — GNSS fix, accelerometer, battery, SD-card store-and-forward queue, sealed MQTT publish | C++ / PlatformIO / ESP-IDF + Arduino |
| **[API/CarPosAPI/](API/CarPosAPI/)** | Backend — MQTT ingest and decryption, REST API, device provisioning, remote settings and schedules | ASP.NET Core (.NET 10), EF Core, PostgreSQL |
| **[FE/](FE/)** | Dashboard — map, position list, telemetry charts, device settings and sharing | React 19 + Vite + TypeScript, i18next (English / Czech) |
| **[Container/](Container/)** | The self-hosted stack — Mosquitto broker behind nginx, PostgreSQL, Cloudflare tunnel | Docker Compose |

Plus [scripts/](scripts/) — the developer front door (`Dev-QuickMenu.ps1`) and the
deployment publisher, and [Application/](Application/) — a **generated** deployment
bundle; nothing in it is edited by hand.

## How a fix travels

```
ESP32                     Mosquitto                 CarPosAPI              PostgreSQL
  │  GNSS fix                  │                        │                       │
  ├─ seal (RSA-OAEP + AES-GCM) │                        │                       │
  ├──── publish devices/<id> ──▶  (ciphertext only) ─────▶  decrypt, validate ───▶  store
  │                            │                        │                       │
  ◀───── sealed ack ───────────┤◀───────────────────────┤                       │
                                                        │                       │
                            React dashboard  ◀── REST ──┤◀──────────────────────┤
```

If the device cannot reach the broker it queues sealed fixes on its SD card and sends
them when it can, so a drive through a dead zone is not a hole in the history.

## Getting started

Each subsystem has its own README with the real detail:

- **[FE/README.md](FE/README.md)** — dev server, the i18n contract, routing, deployment
- **[API/CarPosAPI/README.md](API/CarPosAPI/README.md)** — configuration, secrets, the full
  REST reference, migrations, ops
- **[ESP32/README.md](ESP32/README.md)** — hardware, wiring, the crypto scheme, flashing
- **[Container/MQTTBroker/README.md](Container/MQTTBroker/README.md)** and
  **[Container/Postgres/README.md](Container/Postgres/README.md)** — the self-hosted stack

The short version, from a clean checkout:

```powershell
cd Container/Postgres; docker compose up -d      # database
cd ../../API/CarPosAPI;  dotnet run              # API on :5135
cd ../../FE;             npm install; npm run dev
```

## Privacy and data protection

Because this project stores where real vehicles have been, it is built to take that
seriously rather than to pretend the data is not sensitive:

- **Positions are end-to-end encrypted** from the device to the API; the broker never
  holds plaintext.
- **Coordinates are never written to a log** — the ingest pipeline logs device ids,
  counts and reasons only.
- **Every user can export everything held about them** (`Profile → Export my data`) and
  **delete their account outright** (`Profile → Delete my account`), which really does
  erase the rows rather than flag them.
- **Position history can be wiped per device** from the device settings tab.
- **Maps are not loaded until you say so** — the dashboard asks before contacting Google.
- **Everyone who runs a tracker accepts terms** (`/legal`) that put the duty to tell the
  car's other drivers on the person who put the box in it, and confirms it again per
  device. Those terms are also where the no-warranty and no-liability position lives —
  accepted, not merely published.
- **Positions are kept indefinitely** unless you delete them. That is a deliberate choice
  for a project whose whole point is looking at history, and it is stated plainly in the
  privacy policy at `/privacy` rather than papered over.

The policy and terms live in the app, at `/privacy` and `/legal` — there is deliberately
no second copy in this repository to drift out of step.

Documents: [Where the policy lives](docs/PRIVACY.md) · [Record of processing](docs/RECORD-OF-PROCESSING.md) ·
[Data inventory](docs/DATA-INVENTORY.md)

## License

[PolyForm Noncommercial License 1.0.0](LICENSE) — free to use, study, modify and share for
any **non-commercial** purpose. Commercial use requires separate permission, which is not
on offer.
