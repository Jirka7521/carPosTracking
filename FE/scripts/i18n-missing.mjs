// ---------------------------------------------------------------------------
// Lists every string the other languages have not been given yet.
//
// English is the source: it defines what a key IS (see src/i18n/resources.ts,
// which is also where t()'s types come from). Every other language is checked
// against it, and a key counts as missing when it is absent, empty, or still
// identical to the English text.
//
// Plural forms are the reason this is not a plain key-set comparison. English
// has two forms and Czech has four, so "relative.daysAgo_one" existing in cs
// but not in en is correct rather than an error — keys are compared by their
// BASE name, with the plural suffix stripped, and a language is asked only to
// cover the base names English uses.
//
// Exits non-zero when anything is missing, so it can gate a commit or a build.
// ---------------------------------------------------------------------------

import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const LOCALES_DIR = fileURLToPath(new URL('../src/i18n/locales', import.meta.url))
const SOURCE_LANGUAGE = 'en'

// The CLDR plural categories i18next appends. `_0`, `_1`, … are the ordinal /
// explicit-count forms, which the catalogues do not currently use but which
// would otherwise read as unknown keys.
const PLURAL_SUFFIX = /_(zero|one|two|few|many|other|\d+)$/

function baseKey(key) {
  return key.replace(PLURAL_SUFFIX, '')
}

// Flattens nested JSON into "a.b.c" keys, which is the shape t() addresses.
function flatten(value, prefix = '', out = new Map()) {
  for (const [key, entry] of Object.entries(value)) {
    const path = prefix === '' ? key : `${prefix}.${key}`
    if (entry !== null && typeof entry === 'object' && !Array.isArray(entry)) {
      flatten(entry, path, out)
    } else {
      out.set(path, String(entry))
    }
  }
  return out
}

function readCatalogue(language, namespace) {
  const path = join(LOCALES_DIR, language, namespace)
  return flatten(JSON.parse(readFileSync(path, 'utf8')))
}

const languages = readdirSync(LOCALES_DIR, { withFileTypes: true })
  .filter((entry) => entry.isDirectory())
  .map((entry) => entry.name)

const namespaces = readdirSync(join(LOCALES_DIR, SOURCE_LANGUAGE))
  .filter((name) => name.endsWith('.json'))

let problems = 0

for (const language of languages) {
  if (language === SOURCE_LANGUAGE) {
    continue
  }

  for (const namespace of namespaces) {
    const source = readCatalogue(SOURCE_LANGUAGE, namespace)
    let target
    try {
      target = readCatalogue(language, namespace)
    } catch {
      console.error(`${language}/${namespace}: catalogue is missing entirely`)
      problems += 1
      continue
    }

    // Which base keys the language covers, in any plural form.
    const covered = new Set()
    for (const [key, text] of target) {
      if (text.trim() !== '') {
        covered.add(baseKey(key))
      }
    }

    for (const [key, sourceText] of source) {
      const base = baseKey(key)
      if (!covered.has(base)) {
        console.error(`${language}/${namespace}  ${base}  — missing`)
        problems += 1
        continue
      }

      // An exact copy of the English is almost always an untranslated
      // placeholder. Short technical tokens ("TSV", "GNSS", "Broker") are
      // legitimately identical in most languages, so only prose is reported.
      const targetText = target.get(key)
      if (targetText !== undefined && targetText === sourceText && sourceText.includes(' ')) {
        console.warn(`${language}/${namespace}  ${key}  — still the English text`)
      }
    }

    // Keys the language has that English does not: dead weight, or a typo.
    for (const key of target.keys()) {
      const base = baseKey(key)
      const known = [...source.keys()].some((sourceKey) => baseKey(sourceKey) === base)
      if (!known) {
        console.error(`${language}/${namespace}  ${key}  — not in ${SOURCE_LANGUAGE}`)
        problems += 1
      }
    }
  }
}

if (problems > 0) {
  console.error(`\n${problems} problem(s). Fill them in under src/i18n/locales/.`)
  process.exit(1)
}

console.log(`All ${namespaces.length} namespaces are complete in: ${languages.join(', ')}`)
