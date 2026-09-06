// ============================================================
// i18next set-up. Imported for its side effect by main.tsx, before <App />.
//
// Two decisions shape this file:
//
// 1. CATALOGUES ARE BUNDLED, NOT FETCHED. There is no i18next-http-backend.
//    This app is served both at the site root and under the /carPosFE tunnel
//    prefix (see FE/README.md § "Served under a path prefix"), so anything
//    fetched at runtime has to have BASE_PATH prepended or it 404s behind the
//    prefix while working perfectly at the root — the exact bug class that
//    section exists to warn about. Two small languages cost a few kB gzipped;
//    that is a much better trade than a locale file that goes missing only in
//    production. It also means i18next is ready synchronously, so there is no
//    loading gate and no flash of untranslated text on first paint.
//
// 2. ADDING A LANGUAGE IS DROPPING IN A FOLDER. Every language except English
//    is discovered from disk by the glob below, so a new one is: copy
//    locales/en to locales/<code>, translate it, and add <code> to
//    SUPPORTED_LANGUAGES. Nothing else in the app changes.
// ============================================================

import i18n from 'i18next'
import type { Resource, ResourceKey, ResourceLanguage } from 'i18next'
import { initReactI18next } from 'react-i18next'
import LanguageDetector from 'i18next-browser-languagedetector'

import { enResources, NAMESPACES, DEFAULT_NAMESPACE } from './resources'

// The languages offered in the picker, in the order they are offered.
//
// nativeName is deliberately NOT translated: a language is always listed in
// its own language, so somebody who has landed in one they cannot read can
// still find their way back out.
export const SUPPORTED_LANGUAGES = [
  { code: 'en', nativeName: 'English' },
  { code: 'cs', nativeName: 'Čeština' },
] as const

export type LanguageCode = (typeof SUPPORTED_LANGUAGES)[number]['code']

// Same `carpos.` namespace the CSV separator preference already uses.
export const LANGUAGE_STORAGE_KEY = 'carpos.language'

// Every catalogue on disk, keyed by "./locales/<lang>/<namespace>.json".
// Vite resolves this at build time, so it is a static import list in disguise
// — the files are in the bundle, nothing is looked up at runtime.
const catalogues = import.meta.glob<ResourceKey>('./locales/*/*.json', {
  eager: true,
  import: 'default',
})

const CATALOGUE_PATH = /\.\/locales\/([^/]+)\/([^/]+)\.json$/

const resources: Resource = {
  // English comes from resources.ts instead of the glob, because that import
  // is also the source of the key types. Same files either way.
  en: { ...enResources },
}

for (const [path, catalogue] of Object.entries(catalogues)) {
  const match: RegExpExecArray | null = CATALOGUE_PATH.exec(path)
  if (match === null) {
    continue
  }

  const language: string = match[1]
  const namespace: string = match[2]
  if (language === 'en') {
    continue
  }

  const bundle: ResourceLanguage = (resources[language] ??= {})
  bundle[namespace] = catalogue
}

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    ns: NAMESPACES,
    defaultNS: DEFAULT_NAMESPACE,

    fallbackLng: 'en',
    supportedLngs: SUPPORTED_LANGUAGES.map((language) => language.code),
    // A browser reporting cs-CZ should get the `cs` catalogue rather than
    // falling straight through to English.
    load: 'languageOnly',
    nonExplicitSupportedLngs: true,

    detection: {
      // The stored choice wins; a first-time visitor gets their browser's
      // language. The detector guards its own localStorage access, so a
      // private window or blocked storage degrades to the browser language
      // rather than throwing.
      order: ['localStorage', 'navigator'],
      caches: ['localStorage'],
      lookupLocalStorage: LANGUAGE_STORAGE_KEY,
    },

    // React escapes everything it renders already.
    interpolation: { escapeValue: false },

    // Resources are in the bundle, so nothing is ever pending and no
    // <Suspense> boundary is needed anywhere in the tree.
    react: { useSuspense: false },

    debug: import.meta.env.DEV,

    // In development an unextracted key should be loud. It is not an error —
    // the fallback text still renders — but it means `npm run i18n:extract`
    // has not been run, and that is worth seeing before the commit.
    saveMissing: import.meta.env.DEV,
    missingKeyHandler: (languages: readonly string[], namespace: string, key: string) => {
      console.warn(
        `[i18n] missing key "${namespace}:${key}" for ${languages.join(', ')} — run "npm run i18n:extract"`,
      )
    },
  })

export default i18n
