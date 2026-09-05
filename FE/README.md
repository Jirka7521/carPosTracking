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

## Languages

The dashboard ships in **English and Czech**, chosen from the 🌐 picker in the header — and, because the sign-in and registration pages render their own shell rather than `AppLayout`'s, from the top-right corner of those two as well. Somebody who cannot read English has to be able to switch *before* they can sign in.

The choice is remembered in `localStorage` under `carpos.language`, the same `carpos.` namespace the CSV separator preference uses. A first-time visitor gets their browser's language (`cs-CZ` resolves to `cs`; anything unsupported falls back to English). It is a **per-browser** preference, not a per-account one — `UserProfileDto` carries no locale and the API is not involved.

### How it is put together

| | |
|---|---|
| [`src/i18n/index.ts`](src/i18n/index.ts) | i18next set-up, `SUPPORTED_LANGUAGES`, locale discovery |
| [`src/i18n/resources.ts`](src/i18n/resources.ts) | the English catalogue, imported statically — it is also the key TYPE |
| [`src/i18n/i18next.d.ts`](src/i18n/i18next.d.ts) | feeds that type to i18next, so `t()` is checked by `tsc` |
| [`src/i18n/format.ts`](src/i18n/format.ts) | the one place `Intl` is configured — dates and numbers |
| `src/i18n/locales/<lang>/*.json` | the catalogues, one file per namespace |

Eight namespaces, following the route areas so a screen's strings sit together: `common` (buttons, units, weekdays, relative times, permissions), `auth`, `home`, `device` (the shell and the map / positions / charts tabs), `settings` (device settings, config panels, firmware table), `schedule`, `profile`, `errors`.

**Catalogues are bundled, not fetched.** There is no `i18next-http-backend` on purpose: anything fetched at runtime would have to have `BASE_PATH` prepended or it 404s behind the `/carPosFE` prefix while working perfectly at the root — see [Referring to an asset](#referring-to-an-asset), which is the same trap. Two small languages cost a few kB gzipped, and i18next is ready synchronously, so there is no loading gate and no flash of untranslated text.

### Adding a string

```tsx
const { t } = useTranslation(['device', 'common'])
…
<h2>{t('device:positions.title')}</h2>
<button>{t('common:actions.saveChanges')}</button>
```

Then `npm run i18n:extract`, and fill in the Czech value. **Forgetting either step fails a build**, from opposite directions: a key that is not in the English JSON is a `tsc -b` error (the types come from that file), and a key the code uses that the catalogues lack fails `npm run i18n:check`.

Counts take `count`, never a hand-written plural — `t('device:positions.loaded', { count: total })`. English has two forms, Czech has four, and `_one`/`_other` is not a shape the others can be squeezed into.

A sentence with markup inside it uses `<Trans>` rather than three separate keys, so the translator gets a whole sentence and can reorder it:

```tsx
<Trans i18nKey="config.updatesHint" ns="settings" components={{ strong: <strong /> }} />
```

### Adding a language

1. Copy `src/i18n/locales/en/` to `src/i18n/locales/<code>/` and translate it.
2. Add `{ code: '<code>', nativeName: '…' }` to `SUPPORTED_LANGUAGES` in [`src/i18n/index.ts`](src/i18n/index.ts).

That is all. Every language except English is discovered from disk by an `import.meta.glob`, so nothing else in the app changes. `nativeName` is deliberately not translated — a language is always listed in its own language, so somebody stranded in one they cannot read can still find the way out.

### Dates, numbers and what stays untranslated

Everything user-visible goes through [`src/i18n/format.ts`](src/i18n/format.ts). This is not cosmetic: `toFixed()` always writes a `.`, while `toLocaleString()` groups thousands the reader's way, so the two used side by side showed a Czech reader `1 234` and `12.3` in the same table row.

Three things deliberately bypass it:

- **`<input type="datetime-local">` values** — the format is fixed by the HTML spec (`formatDateTimeLocal` in [`utils/dates.ts`](src/utils/dates.ts)).
- **The CSV export** — ISO-8601 UTC timestamps, `.` decimals, and the API's own field names as headers, because the file is read back by a machine. The *separator* is the reader's choice and handles the Czech-Excel case on its own.
- **Config.h, MQTT topics and firmware constant names** — they must match what the firmware spells.

### Known gap: API error messages

The .NET API returns its ProblemDetails `detail` text in English, and `describeError()` displays it verbatim. So a few server-side errors — a wrong current password, a duplicate device id — still read English in a Czech UI. Fixing that means `Accept-Language` and resources on the backend; it is out of scope for the frontend and is the one place the translation is knowingly incomplete. Every message the *frontend* generates, including the fallback used when the server sends nothing usable, is translated.

### Label tables and dynamic keys

A number of tables map an enum to a label — `CONFIG_FIELD_LABEL_KEYS`, `SERIES[].labelKey`, `CSV_DELIMITERS`, the weekday names. They hold **translation keys, not text**, and the component resolves them, which is what keeps `utils/` free of any particular language.

Because those call sites read `t(SOME_TABLE[key])`, a source scan cannot see them — and `removeUnusedKeys` defaults to true. Every such family is listed under `preservePatterns` in [`i18next.config.ts`](i18next.config.ts). **If you add another table like this, add its prefix there in the same commit**; `tsc` will not catch the loss, because deleting the English key deletes the type along with it.

---

## Available Scripts

| Script | Description |
|--------|-------------|
| `npm run dev` | Start Vite dev server with hot-module reload |
| `npm run build` | Type-check and produce a production build in `dist/` |
| `npm run preview` | Serve the `dist/` build locally for final checks |
| `npm run lint` | Run ESLint across all source files |
| `npm run i18n:extract` | Add keys the code uses to every catalogue; prune dead ones. Run after adding strings |
| `npm run i18n:check` | Read-only version of the above — **fails when the catalogues are stale**. For CI |
| `npm run i18n:missing` | List keys English has that another language does not, and ones still holding the English text |
| `npm run i18n:lint` | Advisory scan for hardcoded strings. Not part of `npm run lint` — it also reports the decorative `aria-hidden` emoji used as section icons, and there is no way to exclude those without also excluding the `<span>`s carrying real text |

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
│   ├── i18n/                      See "Languages" above
│   │   ├── index.ts               i18next set-up, SUPPORTED_LANGUAGES, locale discovery
│   │   ├── resources.ts           The English catalogue — also the type every t() key is checked against
│   │   ├── i18next.d.ts           Module augmentation that feeds that type to i18next
│   │   ├── format.ts              The only place Intl is configured — dates and numbers
│   │   └── locales/<lang>/*.json  One file per namespace; adding a language is adding a folder
│   ├── components/                Reusable UI components (one file per component)
│   │   ├── AppLayout.tsx          Sticky navigation bar shell
│   │   ├── LanguageMenu.tsx       The 🌐 language picker — header, plus the login and register pages
│   │   ├── SessionLoading.tsx     Spinner shown while the session probe runs
│   │   ├── DeviceMap.tsx          Google Maps component with position markers
│   │   ├── TelemetryChart.tsx     Recharts line chart — one Y axis per unit in the selection
│   │   ├── ProvisioningPanel.tsx  Complete Config.h for a device: secrets typed in here, ack key generated here
│   │   ├── FirmwareParameterTable.tsx  Read-only reference of every firmware parameter
│   │   ├── PermissionBadges.tsx   Badge row showing canRead/canDelete/canShare/canModifySettings
│   │   ├── BatteryBadge.tsx       Battery level pill (⚡ when charging; 0 = charging sentinel)
│   │   ├── DurationField.tsx      Number input plus a seconds/minutes/hours combobox — stores canonical units
│   │   ├── RefreshToolbar.tsx     Auto-refresh toggle, countdown pill and the manual Refresh button
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
│   │   ├── DevicePage.tsx         Device shell — loads device data, owns the page's refresh timer, renders the tab bar
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
│       ├── timeUnits.ts           Seconds ⇄ minutes/hours/days for DurationField; picks the best unit for a value
│       ├── downloadTextFile.ts    Blob download helper (used for Config.h)
│       └── errors.ts              describeError() helper over ApiError
├── Dockerfile                     Multi-stage build → nginx
├── nginx.conf                     SPA serving + /api proxy + caching
├── nginx-security-headers.conf    CSP and friends, included by every location
├── docker-entrypoint.sh           Substitutes BE URL, base path and Maps key into the nginx config
├── scripts/
│   └── i18n-missing.mjs           Reports untranslated keys — see npm run i18n:missing
├── i18next.config.ts              Key extraction; preservePatterns for keys reached through a table
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

## Staying up to date

Nothing here polls the device — the tracker publishes when it publishes. What
the dashboard can do is stop showing an answer it read minutes ago, and that is
what [`useAutoRefresh`](src/hooks/useAutoRefresh.ts) is for: a 30 s countdown
plus a manual **Refresh**, exposed by
[`RefreshToolbar`](src/components/RefreshToolbar.tsx) and rendered inside
[`RangeToolbar`](src/components/RangeToolbar.tsx) on the tabs that also pick a
date range.

The hook never moves the goalposts. It bumps a **token**, which each load effect
carries in its dependency array, so a refresh re-runs the *same* query rather
than rewriting the date range to "now".

**One timer per page.** [`DevicePage`](src/pages/DevicePage.tsx) owns the device
page's, reloads the device on every tick — which is what keeps the header's
battery pill and status badge honest — and hands it to every tab through the
outlet context (`autoRefresh`). The tabs use it instead of starting their own,
so there is one countdown however many controls are on screen, and pressing
Refresh anywhere advances all of it. The Home page runs its own, for the battery
and last-fix on the device cards.

Deliberately **not** refreshed: the firmware-configuration panel (an on-demand
block holding a key, and re-rendering it under the reader would be hostile) and
the access roster (it changes when a person changes it).

### The settings form under a refresh

[`DeviceConfigSection`](src/components/DeviceConfigSection.tsx) is the one place
where a background reload could destroy work, so it is explicit about what a
tick may touch:

- The **state around the form** — the sync badge, the pending-change table, the
  per-field "device still on 60 s" notes, the version history if it is expanded
  — is always replaced. That is the point: "Pending" becomes "In sync" only when
  the *device* reports the new revision back, and there was previously no way to
  learn that short of reloading the page.
- The **form inputs** are re-seeded only when there is nothing unsaved in them.
  With edits in progress they are left exactly as typed and the form says so.
- A tick landing **mid-save** is skipped, and a tick that **fails** leaves the
  panel it has rather than replacing it with an error.

### Durations are typed in the unit you choose

Every duration is stored in one canonical unit — whole seconds for the reporting
interval, the GNSS lock timeout and the settings re-check; whole hours for the
two retry knobs — and that is what goes on the wire, unchanged.
[`DurationField`](src/components/DurationField.tsx) puts a unit combobox at the
end of the input row so six hours can be entered as `6 hours` rather than
`21600`, and opens on whichever unit the current value reads most naturally in
(see [`utils/timeUnits.ts`](src/utils/timeUnits.ts)).

Changing the unit changes nothing but the display — 180 seconds simply re-reads
as 3 minutes. Where the chosen unit is finer than storage (minutes on an
hours-based field) the input's `step` keeps the value landing on something
storable.

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
npm run lint         # ESLint — TypeScript, React Hooks, React Refresh rules
npm run i18n:check   # Fails when the translation catalogues are stale
npm run i18n:missing # Fails when a language is missing a string
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
| A raw key such as `device:positions.title` renders instead of text | That key is missing from the catalogue. In development the console also warns. Run `npm run i18n:extract`, then fill the value in |
| One screen is English while the rest is Czech | Either the Czech value is empty (i18next falls back to English) — `npm run i18n:missing` lists those — or the text came from the API, which is still English by design. See [Known gap](#known-gap-api-error-messages) |
| A whole group of translations vanished after `npm run i18n:extract` | Those keys are reached through a table (`t(SOME_TABLE[key])`), which a source scan cannot see, so `removeUnusedKeys` pruned them. Add the prefix to `preservePatterns` in [`i18next.config.ts`](i18next.config.ts) and restore the values |
| Map not configured **only** in the image you built from a clone, `GET /config.js` 404s at the origin while `<prefix>/config.js` returns 200 | `index.html` asked for `/config.js` instead of `./config.js`, so the request ignored the `<base href>`. Vite silently rewrites a root-absolute public reference to a relative one **only when the file exists in the build context** — and `public/config.js` is git-ignored, so it is present on the machine that created it and absent in a clone. That is why this reproduces in the image and never on the developer's PC. Keep the `./` in `index.html`; `public/config.js` is in `.dockerignore` so both builds behave the same |
| Tunnel URL loads but every API call 404s | nginx is not stripping the prefix from `<prefix>/api/…`. Confirm the request arrives with the prefix you configured — the match is case-sensitive |
| API answers 404 on `/carPosAPI/health` | `Hosting:PathBase` on the **backend** is unset or spelled differently — that route bypasses this nginx entirely |

---

## See Also

- [API README](../API/CarPosAPI/README.md) — backend endpoints, configuration and ops
- [Container/App](../Container/App/docker-compose.yml) — the deployment stack
