// ============================================================
// The English catalogue, imported file by file.
//
// This module exists so that `en` is a STATIC, statically-typed import.
// Its literal shape is what src/i18n/i18next.d.ts feeds to i18next's
// CustomTypeOptions, which is what turns every namespaced t() call in the app
// into something `tsc -b` checks. A key that does not exist here is a
// build failure rather than a raw key string rendered to a user.
//
// Every OTHER language is discovered from disk at build time — see
// src/i18n/index.ts. English is the one that cannot be, because a glob's
// result has no useful type.
// ============================================================

import common from './locales/en/common.json'
import auth from './locales/en/auth.json'
import home from './locales/en/home.json'
import device from './locales/en/device.json'
import settings from './locales/en/settings.json'
import schedule from './locales/en/schedule.json'
import profile from './locales/en/profile.json'
import errors from './locales/en/errors.json'

export const enResources = {
  common,
  auth,
  home,
  device,
  settings,
  schedule,
  profile,
  errors,
} as const

// The namespace names, derived from the catalogue rather than repeated.
export const NAMESPACES = Object.keys(enResources) as (keyof typeof enResources)[]

// The namespace a t() call gets when it names none.
export const DEFAULT_NAMESPACE = 'common'
