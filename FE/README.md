# Frontend — React SPA

Single-page application built with **React 19**, **TypeScript**, and **Vite**, charting with **Recharts**. Displays GNSS positions on an interactive Google Maps view, plots telemetry over time, registers tracker devices, handles user authentication, and manages device sharing.

Served in production by **nginx**, which also proxies `/api` to the backend container — see [Deployment](#deployment).

---

## Prerequisites

- Node.js 22 LTS or newer
- A Google Maps JavaScript API key (with the **Maps JavaScript API** enabled in Google Cloud Console)
- The backend API running (locally or deployed)

---

## How it talks to the API

Two decisions shape almost everything in `src/services/` and `src/auth/`:

**1. Same origin.** The API is always at the relative path `/api` — never an absolute URL, and there is no variable to point it somewhere else. In production nginx serves this app and proxies `/api` to the API container; in development the Vite dev server proxies the same path (see [`vite.config.ts`](vite.config.ts)). One origin means no CORS at all, and it is what makes the next point possible. When the app is served under a path prefix the API moves with it — `/carPosFE/api` — which is [its own section](#served-under-a-path-prefix) below; it is still the same origin and still the same nginx.

**2. The session is an `HttpOnly` cookie**, not a token in `localStorage`. No script can read it, so an XSS bug cannot walk off with a valid session. Three consequences worth knowing:

- **The app cannot tell whether it is signed in** without asking. `AuthProvider` issues one `GET /api/me` on mount, and `status` stays `'loading'` until it answers. Route guards wait rather than redirect — treating "unknown" as "signed out" would flash the login page on every reload and discard the deep link.
- **Every mutation carries a CSRF token.** The API sets a readable `carpos_csrf` cookie beside the session; `apiClient.ts` echoes it in the `X-CSRF-Token` header on every non-GET request. A cross-site attacker can make the browser send cookies but cannot read them, so it cannot produce that header.
- **Logging out is a round-trip.** `POST /api/auth/logout` is what expires the cookies; there is nothing local to clear.

Devices are addressed by their **MQTT device id** (e.g. `GNSS01`) everywhere — in URLs, in API calls, and in the broker topic the firmware publishes to. There is no separate numeric id.

---

## Served under a path prefix

The Cloudflare tunnel publishes the dashboard at `https://jimajer.cz/carPosFE` and forwards that prefix **as part of the path** — a tunnel cannot strip it. So the same build has to work at `/login` and at `/carPosFE/login`, and it does: **both are live at once**, and nothing in the app's own code knows which one it is being reached through.

The chain is three links, each of which just passes the answer along:

1. **nginx decides.** A request that carries the prefix has it stripped (`rewrite … last` in [`nginx.conf`](nginx.conf)), so `/api/`, `/assets/`, `/config.js` and the SPA fallback are each reached by exactly one code path either way. The prefix is remembered from `$request_uri` and, when `index.html` goes out, `sub_filter` rewrites its `<base href="/" />` to `<base href="/carPosFE/" />`.
2. **The browser follows the base.** Every URL in the document is relative (`base: './'` in [`vite.config.ts`](vite.config.ts)), so `./assets/*`, `./config.js` and `./favicon.svg` resolve under the prefix without anything being rewritten in the bundle. Because a `<base>` overrides the *document* URL, this holds on a deep link too: `/carPosFE/device/GNSS01/map` still asks for `/carPosFE/assets/…`, not `/carPosFE/device/GNSS01/assets/…`.
3. **The app reads it back.** [`runtimeConfig.ts`](src/services/runtimeConfig.ts) derives `BASE_PATH` from `document.baseURI`; `main.tsx` hands it to `<BrowserRouter basename>`, `apiClient.ts` prefixes `/api` with it, and `assetUrl()` prefixes files served from `public/`. Routes in [`App.tsx`](src/App.tsx) stay written as plain `/login` and `/device/:deviceId` — the router adds and strips the prefix.

### Referring to an asset

The one rule: **never write a root-absolute path like `/favicon.svg` in a component or a stylesheet.** A leading slash ignores the `<base href>` and resolves against the origin, so it 404s behind the tunnel while working perfectly at the root — which is exactly the kind of bug that only shows up in production.

| Asset | How to refer to it | Why |
|---|---|---|
| Bundled — anything under `src/assets/` | `import hero from '../assets/hero.png'` | Vite fingerprints it and emits a relative URL; nothing else to do. |
| Static — anything in `public/` (`favicon.svg`, `icons.svg`) | `assetUrl('favicon.svg')` from [`runtimeConfig.ts`](src/services/runtimeConfig.ts) | These are copied through untouched, so the prefix has to be added at runtime. |
| In `index.html` | a relative URL (`./config.js`) | Resolved against the `<base href>` that nginx stamps in. |
| External (Google Maps) | the absolute `https://…` URL | Unaffected by the prefix; must be allowed by the CSP. |

`assetUrl()` returns a root-relative path *with* the prefix (`/carPosFE/favicon.svg`) rather than a bare relative one, so it is correct regardless of the current route and does not quietly depend on the `<base>` tag still being there.

The prefix is **not baked into the build**. It comes from `CARPOS_BASE_PATH` on the container (default `/carPosFE`, empty turns the handling off) and is substituted into the nginx config at start-up by [`docker-entrypoint.sh`](docker-entrypoint.sh), so renaming the tunnel route is a restart, not a rebuild.

The API's own tunnel route (`/carPosAPI`, handled by `Hosting:PathBase` on the backend) is **independent of this** — the dashboard reaches the API through its own nginx, so it works whether or not that route exists.

In local development none of this is engaged: Vite serves at the root, `BASE_PATH` is `''`, and the app behaves exactly as it did before.

---

## Environment Variables

The Maps key is **runtime** configuration, not build-time: `index.html` loads `/config.js` before the bundle and [`src/services/runtimeConfig.ts`](src/services/runtimeConfig.ts) reads it from `window`. The same image therefore runs in every environment.

**In the container, `/config.js` is generated by nginx**, not written to disk — the entrypoint substitutes the key into a `location = /config.js` block that returns it directly. It used to be a file, which meant the app's configuration depended on a step that could fail *after* the site config had been rendered; because the stock nginx image starts the server even when a `/docker-entrypoint.d/` script exits non-zero, the result was a container serving the entire app with `/config.js` 404ing and the map silently dead. Generating it removes that state: either the key is substituted or nginx has no config and does not start.

| Variable | Where it is set | Description |
|----------|-----------------|-------------|
| `CARPOS_GOOGLE_MAPS_API_KEY` | the **container's** environment | Google Maps JavaScript API key. Restrict it by HTTP referrer in the Google Cloud console — it is served to the browser and cannot be kept secret. |
| `CARPOS_BE_URL` | the **container's** environment | Where nginx proxies `/api/`. Defaults to `http://api:8080`; no trailing slash and no path (see the comment in [`nginx.conf`](nginx.conf)). |
| `CARPOS_BASE_PATH` | the **container's** environment | Path prefix the app is also served under, on top of the root. Defaults to `/carPosFE` — the tunnel's route. Empty serves at the root only. See [Served under a path prefix](#served-under-a-path-prefix). |

There is no `VITE_BACKEND_API_URL` any more (the API is at `/api` by construction) and no build-time `VITE_GOOGLE_MAPS_API_KEY`.

**In local development** `/config.js` does not exist; the 404 is harmless and the app simply reports that the map is not configured. To work on the map locally, create `public/config.js`:

```js
window.__CARPOS_CONFIG__ = { googleMapsApiKey: "your-key-here" }
```

`public/config.js` is git-ignored, so a real key cannot be committed by accident.

---

## Local Development

```bash
npm install        # install dependencies
npm run dev        # start Vite dev server on http://localhost:61074
```

Start the API first (`dotnet run` in [`../API/CarPosAPI`](../API/CarPosAPI)) — the dev server proxies `/api` to `http://localhost:5135`, matching the `http` launch profile. Development also sets `AuthCookie:SecureCookies=false` on the API, because a `Secure` cookie would never come back over plain HTTP and every request after sign-in would 401.

The dev server uses a fixed port (`61074`). If that port is in use it exits immediately (`strictPort: true`) rather than silently moving elsewhere.

---

## Available Scripts

| Script | Description |
|--------|-------------|
| `npm run dev` | Start Vite dev server with hot-module reload |
| `npm run build` | Type-check and produce a production build in `dist/` |
| `npm run preview` | Serve the `dist/` build locally for final checks |
| `npm run lint` | Run ESLint across all source files |

---

## Project Structure

```
FE/
├── public/                        Copied through verbatim — refer to these with assetUrl()
│   ├── favicon.svg                Browser tab icon, also the in-page logo mark
│   ├── icons.svg                  SVG sprite
│   └── staticwebapp.config.json   Legacy SPA routing fallback (nginx handles this now)
├── src/
│   ├── App.tsx                    Router — public and protected route definitions
│   ├── main.tsx                   Entry point — sets <BrowserRouter basename> from BASE_PATH
│   ├── auth/
│   │   ├── AuthContext.tsx        Session probe, current user, login/logout helpers
│   │   └── RequireAuth.tsx        Route guard — waits for the probe, then redirects
│   ├── components/                Reusable UI components (one file per component)
│   │   ├── AppLayout.tsx          Sticky navigation bar shell
│   │   ├── SessionLoading.tsx     Spinner shown while the session probe runs
│   │   ├── DeviceMap.tsx          Google Maps component with position markers
│   │   ├── TelemetryChart.tsx     Recharts line chart — one Y axis per unit in the selection
│   │   ├── ProvisioningPanel.tsx  Complete Config.h for a device: secrets typed in here, ack key generated here
│   │   ├── FirmwareParameterTable.tsx  Read-only reference of every firmware parameter
│   │   ├── PermissionBadges.tsx   Badge row showing canRead/canDelete/canShare/canModifySettings
│   │   ├── BatteryBadge.tsx       Battery level pill (⚡ when charging; 0 = charging sentinel)
│   │   ├── DeviceCard.tsx         One card in the device grid on the Home page
│   │   ├── SharedUserCard.tsx     One row in the "People with access" list
│   │   ├── CapabilityCheckboxes.tsx  Permission flag checkboxes (Delete / Share / Settings)
│   │   ├── PersonalInfoSection.tsx   Edit first/last name form (used on Profile page)
│   │   └── ChangePasswordSection.tsx Change password form (used on Profile page)
│   ├── pages/                     Full-page route components
│   │   ├── LoginPage.tsx          Sign-in form
│   │   ├── RegisterPage.tsx       Account creation form
│   │   ├── HomePage.tsx           Device list + register-device panel
│   │   ├── ProfilePage.tsx        Edit name and change password
│   │   ├── DevicePage.tsx         Device shell — loads device data and renders the tab bar
│   │   ├── DeviceMapTab.tsx       Map tab — live Google Maps view with auto-refresh
│   │   ├── PositionListTab.tsx    Positions tab — paginated GPS position table
│   │   ├── DeviceChartsTab.tsx    Charts tab — plot selected telemetry series over time
│   │   └── DeviceSettingsTab.tsx  Settings tab — info, alias, firmware config, sharing, delete
│   ├── services/
│   │   ├── apiClient.ts           All fetch calls; cookies + CSRF header
│   │   ├── apiTypes.ts            TypeScript DTOs matching the backend contracts
│   │   └── runtimeConfig.ts       Reads window.__CARPOS_CONFIG__; derives BASE_PATH, API_BASE_PATH, assetUrl()
│   └── utils/
│       ├── dates.ts               Date/time formatting helpers
│       ├── devices.ts             Device label fallback (customName → displayName → deviceId)
│       ├── telemetry.ts           Plottable series table + PositionDto → chart rows
│       ├── configSecrets.ts       Splices your secrets into the rendered Config.h — in the browser
│       ├── ackKeyPair.ts          WebCrypto RSA-3072 ack key generation (private half never uploaded)
│       ├── firmwareParameters.ts  Static table of every Config.h constant, for the reference panel
│       ├── downloadTextFile.ts    Blob download helper (used for Config.h)
│       └── errors.ts              describeError() helper over ApiError
├── Dockerfile                     Multi-stage build → nginx
├── nginx.conf                     SPA serving + /api proxy + caching
├── nginx-security-headers.conf    CSP and friends, included by every location
├── docker-entrypoint.sh           Substitutes BE URL, base path and Maps key into the nginx config
├── vite.config.ts
├── tsconfig.json
└── eslint.config.js
```

---

## Routing

| Path | Access | Description |
|------|--------|-------------|
| `/` | any | Redirects to `/home` (authenticated) or `/login` (guest) |
| `/login` | public | Sign in |
| `/register` | public | Create account |
| `/home` | protected | List of accessible devices + register a new one |
| `/profile` | protected | Edit first/last name and change password |
| `/device/:deviceId/map` | protected | Live map for a device |
| `/device/:deviceId/positions` | protected | Position history table |
| `/device/:deviceId/charts` | protected | Telemetry charts — speed, altitude, battery, temperature, acceleration over time |
| `/device/:deviceId/settings` | protected | Device settings (info, alias, firmware config, sharing, delete) |

`:deviceId` is the tracker's MQTT identity, e.g. `/device/GNSS01/map`.

---

## Registering a device

`POST /api/devices` does more than create a row: the API generates the device's RSA-3072 key pair, stores the private half encrypted at rest, and returns the public half inside a **complete `Config.h`** — the firmware's own template with this device's id, topics, broker URI, key and current settings already substituted in. The Home page shows it in a `ProvisioningPanel` immediately after registration, and the device's Settings tab can re-read it later (`GET /api/devices/{deviceId}/provisioning`, requires `canModifySettings`). Save it as `ESP32/src/config/Config.h` and build; there is nothing left to merge by hand.

**The receiver private key never leaves the server** — not in that response, not on any endpoint. That is what stops the broker, or anyone who steals the tracker, from reading positions.

### Secrets are filled in by the browser, not the server

The API renders four constants empty on purpose, and `ProvisioningPanel` fills them in locally from its own form: `kWifiSsid`, `kWifiPassword`, `kMqttPassword` and `kDeviceAckPrivateKeyPem`. The substitution lives in [`utils/configSecrets.ts`](src/utils/configSecrets.ts) — pure functions over the file text, so **none of those values is ever sent anywhere**, stored, or kept across a reload. Typing an SSID also flips `kWifiEnabled` on, which the API leaves off precisely because a station with no credentials burns a full connect timeout on every boot.

### Rotating the delivery-ack key

Acks invert the key roles: the API encrypts and the *device* decrypts, so the device owns the private half and the server may hold only the public one. [`utils/ackKeyPair.ts`](src/utils/ackKeyPair.ts) generates the pair with WebCrypto (RSA-3072, PKCS#8 + SPKI — byte-compatible with `openssl genpkey` and with what the firmware's mbedTLS parses), which is what keeps that true from a dashboard at all.

The order of the flow is a safety property, not a formality:

1. **Generate** — the pair exists only in the page.
2. **Download or copy** the `Config.h`, which now carries the private key.
3. **Activate** — only then is the public half `POST`ed to `/api/devices/{deviceId}/ack-key`.

The activate button stays disabled until step 2 has happened. If the key were stored first and the file were then lost, the device would be left with a server-side key whose private half exists nowhere, and every fix would sit waiting out the ack timeout with nothing to explain why. Abandoning the flow before step 3 costs one regeneration and changes nothing on the server.

Device ids match `^[A-Za-z0-9_-]{1,64}$`. The frontend checks that as a hint; the server enforces it as a security control, because the id is interpolated into MQTT topics and must not be able to smuggle in a separator or a wildcard.

Registration can optionally share the device with other people straight away, by email address. Addresses that match no account are skipped silently — the API will not confirm who has an account here.

---

## Permission model

Four boolean flags on one access grant per (user, device):

| Flag | Meaning |
|------|---------|
| `canRead` | See the device and its positions. Always true on an active grant. |
| `canDelete` | Soft-delete (deactivate) the device. |
| `canShare` | Grant, change and revoke others' access. Implies `canModifySettings`. |
| `canModifySettings` | Change settings and read the firmware configuration block. |

The flags the UI receives are **hints for hiding controls**. Every mutation is re-authorised server-side against the caller's grant, so a client that lies about its permissions gets a 403, not an effect.

---

## Building for Production

```bash
npm run build
```

Output goes to `dist/`. TypeScript strict mode is on and the build runs `tsc -b` first, so type errors fail it.

---

## Deployment

The app ships as a Docker image that bundles nginx:

```bash
# from the repository root
docker compose -f Container/App/docker-compose.yml up -d --build
```

See [`Container/App/.env.example`](../Container/App/.env.example) for the variables the stack needs. nginx:

- serves the SPA with an `index.html` fallback so deep links and reloads work;
- serves it at the root **and** under `$CARPOS_BASE_PATH` (`/carPosFE`, the tunnel's route), stripping the prefix and stamping it into the page's `<base href>`;
- proxies `/api/` to the `api` container (`http://api:8080`), passing `X-Forwarded-*`;
- does **not** proxy `/health` or `/openapi` — those are for the operator, not the internet;
- sends a strict CSP and the usual security headers on every response;
- caches `/assets/*` for a year (Vite fingerprints the filenames) and never caches `index.html` or `config.js`.

The one deliberate CSP relaxation is `style-src 'unsafe-inline'`, which the Google Maps library requires for its own controls. `script-src` has no such exception.

---

## Code Quality

```bash
npm run lint       # ESLint — TypeScript, React Hooks, React Refresh rules
```

---

## Troubleshooting

| Symptom | Cause and fix |
|---------|---------------|
| **404 on `config.js`** from the container | Should now be impossible — nginx returns it from its own config. If you still see one, the container is running an **older image**: rebuild with `docker compose up -d --build`. A current start logs `Serving /config.js from the site config (Maps key length: N)` |
| Map shows "not configured" | The key reached the container empty. Check the `Maps key length:` line in `docker logs carpos-fe`; if the entrypoint rejected the value it names the offending characters (quotes around the value in `.env` are the usual cause) |
| Map area blank, CSP errors in the console | The Maps API key is restricted to a different referrer, or the CSP is blocking a host Google has newly started using |
| Signed out immediately after signing in | The session cookie is `Secure` but the site is served over plain HTTP. In development set `AuthCookie:SecureCookies=false` on the API (already the default in `appsettings.Development.json`) |
| Every mutation returns 403 "Invalid CSRF token" | The `carpos_csrf` cookie is missing — usually because the app and the API are not actually on the same origin. Check the nginx or Vite proxy |
| `401` on every call right after login | The API and the browser disagree about the cookie's host. Confirm nginx passes `Host` through unchanged |
| Blank page after deploy | The SPA fallback is not in effect — check `try_files` in `nginx.conf` |
| Blank page **only** through the tunnel, `/assets/*` 404s | The `<base href>` was not rewritten. `CARPOS_BASE_PATH` must match the tunnel's route (the entrypoint prints what it settled on), and the `sub_filter` search string in `nginx.conf` must match the `<base href="/" />` tag in `index.html` **byte for byte** |
| One image is broken through the tunnel but the page works | That reference is a root-absolute path. Use `assetUrl()` — see [Referring to an asset](#referring-to-an-asset) |
| Map not configured **only** in the image you built from a clone, `GET /config.js` 404s at the origin while `<prefix>/config.js` returns 200 | `index.html` asked for `/config.js` instead of `./config.js`, so the request ignored the `<base href>`. Vite silently rewrites a root-absolute public reference to a relative one **only when the file exists in the build context** — and `public/config.js` is git-ignored, so it is present on the machine that created it and absent in a clone. That is why this reproduces in the image and never on the developer's PC. Keep the `./` in `index.html`; `public/config.js` is in `.dockerignore` so both builds behave the same |
| Tunnel URL loads but every API call 404s | nginx is not stripping the prefix from `<prefix>/api/…`. Confirm the request arrives with the prefix you configured — the match is case-sensitive |
| API answers 404 on `/carPosAPI/health` | `Hosting:PathBase` on the **backend** is unset or spelled differently — that route bypasses this nginx entirely |

---

## See Also

- [API README](../API/CarPosAPI/README.md) — backend endpoints, configuration and ops
- [Container/App](../Container/App/docker-compose.yml) — the deployment stack
