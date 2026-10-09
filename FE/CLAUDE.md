# CLAUDE.md — FE (React dashboard)

Frontend specifics. The shared rules — plan first, minimal diffs, git workflow,
subagents, and the cross-subsystem contracts — are in [../CLAUDE.md](../CLAUDE.md).

## What this is

The dashboard: device list, map (Google Maps), position list, telemetry charts
(Recharts), device events, settings and schedules, sharing and temporary share links,
profile and GDPR self-service. English and Czech via i18next.

React 19 + Vite + TypeScript + react-router, Node 22+. [README.md](README.md) explains
routing, the i18n contract, the path-prefix setup and deployment — read the relevant
section before changing any of them.

## Commands

```powershell
npm install
npm run dev            # http://localhost:61074, proxies /api to the API on :5135
npm run build          # tsc -b && vite build
npm run lint           # ESLint
npm run i18n:extract   # sync the translation catalogues with the code
npm run i18n:check     # fails when the catalogues are stale
npm run i18n:missing   # fails when a language is missing a key
npm run i18n:api-codes # fails when an API error code has no translation
```

- Verify before "done": `npm run build && npm run lint && npm run i18n:check && npm run i18n:missing`
  (plus `i18n:api-codes` when the API's `ErrorCodes.cs` changed). There are no unit tests.
- The VS Code task **"Run App (API + FE)"** starts the API and the dev server together.

## Layout

```
src/
├── App.tsx, main.tsx   routes; <BrowserRouter basename> from BASE_PATH
├── auth/               AuthProvider (session probe via GET /api/me), RequireAuth guard
├── pages/              one route component per file (DevicePage + its *Tab pages)
├── components/         reusable UI, one component per file
├── hooks/              custom hooks (useAutoRefresh)
├── services/           apiClient.ts (all HTTP), apiTypes.ts (wire DTOs), runtimeConfig.ts
├── utils/              pure, language-free logic (dates, telemetry, schedule, errors, …)
├── i18n/               set-up, format.ts (the only Intl config), locales/<lang>/<ns>.json
└── App.css, index.css  global styles
```

## Conventions (match the existing code)

- Every file opens with a banner comment: what it is for and why it is built that way.
- One component per file; `export function Name(…)` named exports (a few legacy
  defaults exist — leave them).
- 2-space indent, single quotes, no semicolons; `import type` for type-only imports.
- **All HTTP goes through `services/apiClient.ts`**; request/response types live in
  `services/apiTypes.ts` and mirror the API's DTOs (camelCase). A change there is an API
  contract change — see the root contracts table.
- `utils/` stays free of UI and language: tables that map an enum to a label hold
  **translation keys**, and the component calls `t()`.

## Hard rules

- **The API is always the relative `/api`** — no absolute URLs, no env var for it, no
  CORS. Same origin is what makes the cookie session work.
- **The session is an HttpOnly cookie.** Never store a token in `localStorage` or state.
  `apiClient` adds the `X-CSRF-Token` header to every mutation — don't bypass it.
- **Never write a root-absolute asset path** (`/favicon.svg`) in a component or a
  stylesheet: production is served under `/carPosFE`, so it 404s there while working
  locally. Import bundled assets; use `assetUrl()` from `services/runtimeConfig.ts` for
  files in `public/`; route paths stay plain (`/login`) — the router adds the prefix.
- **Every user-visible string goes through `t()`.** Add the English key, run
  `npm run i18n:extract`, then fill in the Czech value. Plurals use `count` (Czech has
  four forms), and a sentence with markup inside uses `<Trans>`, not split keys.
- **A new label table** (keys looked up dynamically, `t(TABLE[key])`) needs its prefix
  in `preservePatterns` in [i18next.config.ts](i18next.config.ts) in the same commit, or
  the extractor deletes its keys.
- Permission flags (`canRead`, `canDelete`, `canShare`, `canModifySettings`) only hide
  controls; the API re-authorises everything.
- Devices are addressed by their string `deviceId` (e.g. `GNSS01`) everywhere — URLs,
  API calls, topics.

## Gotchas

- A key used in code but missing from the English JSON is a **`tsc -b` error** (the
  types come from that file); a key missing from the catalogues fails `i18n:check`.
- Google Maps loads only after the user consents (`utils/mapsConsent.ts`) — keep it that
  way. A new external host must also be allowed by the CSP in
  [nginx-security-headers.conf](nginx-security-headers.conf).
- API error `detail` text is English; translated messages come from the error `code` via
  `utils/errors.ts` and `errors.json`.
- `dist/` and `node_modules/` are generated — never edit them.
