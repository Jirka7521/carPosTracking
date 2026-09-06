// Teaches TypeScript what keys exist, so `t()` is checked at compile time.
//
// The resource type comes from the English catalogue (see resources.ts): every
// other language is a translation OF it and never adds keys of its own, so
// English alone defines what a valid key is.

import type { enResources, DEFAULT_NAMESPACE } from './resources'

declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: typeof DEFAULT_NAMESPACE
    resources: typeof enResources
  }
}
