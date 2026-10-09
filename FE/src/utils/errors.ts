// Helper for turning any thrown value into a single string the UI can render.
//
// The API writes its `detail` messages in English, so showing them verbatim put
// English sentences in the middle of an otherwise Czech page. Every API error
// therefore also carries a stable `code` (and, when the message mentions values,
// `params`), and that is what gets translated: errors:api.<code> in errors.json.
// The codes are defined in API/CarPosAPI/Services/Common/ErrorCodes.cs — a new
// one there needs its English and Czech text here in the same change.
//
// A code with no translation (a newer API than this bundle, say) falls back to
// the server's English sentence for English readers and to the caller's own
// translated message for everyone else, so nobody gets text in the wrong
// language.

import i18n from 'i18next'
import { ApiError } from '../services/apiClient'

// The reader's language, as the catalogue resolved it (never "cs-CZ").
function isEnglish(): boolean {
  return (i18n.resolvedLanguage ?? i18n.language ?? 'en') === 'en'
}

// The translated message for an API error code, or null when there is none.
function translateCode(code: string, params: Record<string, unknown>): string | null {
  // Built at runtime from what the server sent, so it is not one of the literal
  // keys t() is typed against — hence the widening. The api.* family is in the
  // extractor's preservePatterns for the same reason.
  const key: string = `errors:api.${code}`
  // With the params, so a key that only exists in plural forms (profileInUse_one,
  // profileInUse_other…) is found through its `count`.
  if (!i18n.exists(key, params)) {
    return null
  }
  return (i18n.t as (key: string, options?: Record<string, unknown>) => string)(key, params)
}

// The API's translated message when it sent a known code; otherwise any Error's
// own message for an English reader, or the caller's (already translated)
// default for everyone else.
export function describeError(error: unknown, fallback: string): string {
  if (error instanceof ApiError && error.code !== null) {
    const translated: string | null = translateCode(error.code, error.params)
    if (translated !== null) {
      return translated
    }
  }

  if (error instanceof Error && error.message.length > 0 && isEnglish()) {
    return error.message
  }
  return fallback
}
