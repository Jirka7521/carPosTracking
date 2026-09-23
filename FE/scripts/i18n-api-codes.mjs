// ---------------------------------------------------------------------------
// Checks that every error code the API can send has an English translation.
//
// The API names each failure with a stable code (ProblemDetails `code`), all of
// them declared in API/CarPosAPI/Services/Common/ErrorCodes.cs, and
// utils/errors.ts renders errors:api.<code>. A code with no entry there does not
// break anything loudly — the reader just gets the screen's generic fallback —
// so this is the check that notices. Czech is covered by `npm run i18n:missing`,
// which holds every language to the keys English has.
//
// Reads the API's source, so it only works in a full checkout (not in the FE's
// own Docker build context). Exits non-zero when a code is untranslated.
// ---------------------------------------------------------------------------

import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const ERROR_CODES_FILE = fileURLToPath(
  new URL('../../API/CarPosAPI/Services/Common/ErrorCodes.cs', import.meta.url),
)
const CATALOGUE_FILE = fileURLToPath(new URL('../src/i18n/locales/en/errors.json', import.meta.url))

// Codes utils/errors.ts or apiClient.ts produce themselves, never the API.
const CLIENT_ONLY_CODES = new Set(['network'])

const PLURAL_SUFFIX = /_(zero|one|two|few|many|other|\d+)$/

const apiCodes = [...readFileSync(ERROR_CODES_FILE, 'utf8').matchAll(/public const string \w+ = "(\w+)";/g)]
  .map((match) => match[1])

const translated = new Set(
  Object.keys(JSON.parse(readFileSync(CATALOGUE_FILE, 'utf8')).api ?? {})
    .map((key) => key.replace(PLURAL_SUFFIX, '')),
)

const missing = apiCodes.filter((code) => !translated.has(code))
const unused = [...translated].filter((code) => !apiCodes.includes(code) && !CLIENT_ONLY_CODES.has(code))

if (unused.length > 0) {
  console.warn(`errors:api keys no API code uses any more: ${unused.join(', ')}`)
}

if (missing.length > 0) {
  console.error(`API error codes with no errors:api translation: ${missing.join(', ')}`)
  process.exit(1)
}

console.log(`All ${apiCodes.length} API error codes are translated.`)
