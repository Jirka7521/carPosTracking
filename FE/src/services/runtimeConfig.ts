// ---------------------------------------------------------------------------
// Runtime configuration.
//
// Vite's `import.meta.env` values are inlined at *build* time, which would mean
// one image per environment and a rebuild every time a key rotates. Instead
// index.html loads /config.js before the bundle and everything below reads what
// it left on `window`. In the container nginx answers that request from its own
// site config; in local development it is the git-ignored public/config.js.
//
// The API base URL is deliberately NOT configurable: nginx serves this app and
// proxies /api to the backend, so the two share an origin. That is what lets the
// session live in a same-site HttpOnly cookie — a configurable cross-origin API
// URL would quietly break that guarantee.
// ---------------------------------------------------------------------------

// The path prefix this app is being served under, without a trailing slash:
// "" at the site root, "/carPosFE" behind the Cloudflare tunnel, which forwards
// its route prefix to us verbatim.
//
// It is read from the <base href> that index.html carries and nginx rewrites per
// request (see index.html and nginx.conf) rather than being configured here, so
// the same build works both ways — and works through *any* prefix, not just the
// one this repo happens to deploy under today.
function readBasePath(): string {
  // document.baseURI is the resolved <base href>, absolute, so this is the
  // prefix and nothing else — never the current route.
  const pathname: string = new URL(document.baseURI).pathname

  // "/carPosFE/" -> "/carPosFE", and "/" -> "". Callers append their own
  // leading slash, so carrying a trailing one would produce "//api".
  return pathname.endsWith('/') ? pathname.slice(0, -1) : pathname
}

// Prefix to hand React Router as its basename, so every route it builds or
// matches carries the prefix without a single component having to know about it.
export const BASE_PATH: string = readBasePath()

// Path the API is reachable at. Same origin and same prefix as the app: nginx
// proxies <prefix>/api/ to the backend exactly as it proxies /api/, so the
// session cookie keeps working untouched.
export const API_BASE_PATH: string = `${BASE_PATH}/api`

/**
 * URL for a file served from `public/` — the favicon and anything else that is
 * referenced by name rather than imported.
 *
 * Assets that Vite *bundles* (anything `import`ed from `src/assets/`) need none
 * of this: it fingerprints them, emits a relative URL, and the page's
 * `<base href>` resolves it under whatever prefix we are served from. Files in
 * `public/` are copied through untouched, so a component naming one has to build
 * the path itself — and a leading-slash literal like `/favicon.svg` would ignore
 * the base entirely and 404 behind the tunnel.
 *
 * Returning an absolute path (prefix included) rather than a bare relative one
 * is deliberate: a relative URL would resolve against the `<base href>`, which
 * is correct today but silently becomes route-relative if that tag is ever
 * dropped. This cannot.
 *
 * @param fileName File as it appears in `public/`, e.g. `favicon.svg`.
 * @returns Root-relative URL carrying the current prefix.
 */
export function assetUrl(fileName: string): string {
  return `${BASE_PATH}/${fileName}`
}

type RuntimeConfig = {
  // Google Maps JavaScript API key. Restrict it by HTTP referrer in the Google
  // Cloud console — it is served to the browser and cannot be kept secret.
  googleMapsApiKey: string
}

declare global {
  interface Window {
    __CARPOS_CONFIG__?: Partial<RuntimeConfig>
  }
}

// Read once at module load. A missing config.js is a deployment mistake, not a
// runtime condition to handle over and over.
const raw: Partial<RuntimeConfig> = window.__CARPOS_CONFIG__ ?? {}

export const runtimeConfig: RuntimeConfig = {
  googleMapsApiKey: (raw.googleMapsApiKey ?? '').trim(),
}

// Whether the map can be rendered at all. Callers use this to show an
// explanatory message instead of an empty grey box that looks like a bug.
export function hasGoogleMapsKey(): boolean {
  return runtimeConfig.googleMapsApiKey.length > 0
}
