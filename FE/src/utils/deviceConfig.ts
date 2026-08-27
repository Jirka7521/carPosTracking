// ---------------------------------------------------------------------------
// Pure helpers for the remote device-settings panel.
//
// They live here rather than inside the components because the *same* diff has
// to drive three things at once: the sync graphic at the top of the section, the
// pending-change table under it, and the little "device still on 60 s" note
// beside each input. Computing it in one place is what keeps those three from
// ever disagreeing with each other.
//
// Everything here is a pure function of its arguments — no API calls, no state.
// ---------------------------------------------------------------------------

import type { DeviceConfigStateDto, DeviceConfigValuesDto } from '../services/apiTypes'

// 'unknown' is deliberately distinct from 'pending'. Pending means we published
// something the device has not acknowledged yet; unknown means it has never told
// us anything, so we cannot say whether it agrees — an important difference when
// you are deciding whether to go and look at the hardware.
export type ConfigSyncState = 'synced' | 'pending' | 'unknown'

export function resolveSyncState(state: DeviceConfigStateDto): ConfigSyncState {
  if (state.applied === null) {
    return 'unknown'
  }
  return state.isInSync ? 'synced' : 'pending'
}

// The bounds the API enforces with a 400 and the firmware clamps to. Repeated
// here (from ESP32/src/config/Config.h via the API's DeviceConfigRules) because
// the number inputs need them as `min`/`max`, and because a client-side check
// gives a better message than a round trip. The server is still the authority.
export const CONFIG_LIMITS = {
  intervalSeconds: { min: 5, max: 86400 },
  fixTimeoutSeconds: { min: 15, max: 3600 },
  queueMaxFixes: { min: 100, max: 100000 },
  retryIntervalHours: { min: 1, max: 720 },
  retryMaxAgeHours: { min: 0, max: 8760 },
  configCheckSeconds: { min: 60, max: 86400 },
} as const

// Every editable key, in the order the form and the pending table present them.
// Driving both from one array is what stops a newly added setting from showing
// up in the form but silently missing from the diff.
export const CONFIG_FIELD_ORDER: readonly (keyof DeviceConfigValuesDto)[] = [
  'intervalSeconds',
  'sleepBetween',
  'fixTimeoutSeconds',
  'queueMaxFixes',
  'retryIntervalHours',
  'retryMaxAgeHours',
  'configCheckSeconds',
]

// Short labels used wherever a setting is named outside its own form field.
export const CONFIG_FIELD_LABELS: Record<keyof DeviceConfigValuesDto, string> = {
  intervalSeconds: 'Report every',
  sleepBetween: 'Deep-sleep between reports',
  fixTimeoutSeconds: 'GNSS lock timeout',
  queueMaxFixes: 'Undelivered fixes kept',
  retryIntervalHours: 'Retry rejected fixes every',
  retryMaxAgeHours: 'Give up on rejected fixes after',
  configCheckSeconds: 'Re-check for settings every',
}

// Which settings differ between two revisions. Returns keys in CONFIG_FIELD_ORDER
// order so the pending table reads the same way as the form above it.
export function diffConfig(
  left: DeviceConfigValuesDto,
  right: DeviceConfigValuesDto,
): (keyof DeviceConfigValuesDto)[] {
  return CONFIG_FIELD_ORDER.filter((key) => left[key] !== right[key])
}

// Render one setting's value the way a person reads it, units included. Used by
// the pending table and the per-field notes, so a value is never shown as a bare
// number in one place and "5 minutes" in another.
export function formatConfigValue(
  key: keyof DeviceConfigValuesDto,
  values: DeviceConfigValuesDto,
): string {
  switch (key) {
    case 'sleepBetween':
      return values.sleepBetween ? 'on' : 'off'
    case 'intervalSeconds':
      return `${values.intervalSeconds} s`
    case 'fixTimeoutSeconds':
      return `${values.fixTimeoutSeconds} s`
    case 'queueMaxFixes':
      return `${values.queueMaxFixes.toLocaleString()} fixes`
    case 'retryIntervalHours':
      return `${values.retryIntervalHours} h`
    case 'retryMaxAgeHours':
      // 0 is not "zero hours", it is the deliberate "keep retrying forever".
      return values.retryMaxAgeHours === 0 ? 'never' : `${values.retryMaxAgeHours} h`
    case 'configCheckSeconds':
      return `${values.configCheckSeconds} s`
  }
}

// Turn a number of seconds into the phrase a person would use. The form shows
// this live beside the input, so "300" reads as "every 5 minutes" while typing.
export function describeSeconds(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) {
    return ''
  }
  if (seconds < 60) {
    return `${seconds} seconds`
  }
  if (seconds < 3600) {
    return describeRounded(seconds / 60, 'minute')
  }
  if (seconds < 86400) {
    return describeRounded(seconds / 3600, 'hour')
  }
  return describeRounded(seconds / 86400, 'day')
}

// Same idea for a count of hours, used by the two retry fields.
export function describeHours(hours: number): string {
  if (!Number.isFinite(hours) || hours <= 0) {
    return ''
  }
  if (hours < 24) {
    return describeRounded(hours, 'hour')
  }
  return describeRounded(hours / 24, 'day')
}

// How long the SD queue can absorb an outage, given how often fixes go into it.
//
// This is the whole reason the queue cap is exposed as a count rather than a
// duration: one fix is queued per reporting cycle, so entries × interval is the
// span it covers — but the queue file holds bare ciphertext with no timestamps,
// so the device cannot compute this itself. The dashboard does it instead.
export function estimateQueueSpan(maxFixes: number, intervalSeconds: number): string {
  if (!Number.isFinite(maxFixes) || !Number.isFinite(intervalSeconds)) {
    return ''
  }
  if (maxFixes <= 0 || intervalSeconds <= 0) {
    return ''
  }
  return `≈ ${describeSeconds(maxFixes * intervalSeconds)} at the current interval`
}

// One decimal place, but only when it carries information: "5 minutes", not
// "5.0 minutes", and "1 day" rather than "1 days".
function describeRounded(value: number, unit: string): string {
  const rounded: number = Math.round(value * 10) / 10
  const text: string = Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1)
  return `${text} ${unit}${rounded === 1 ? '' : 's'}`
}

// Mirrors the API's [Range] attributes and the firmware's clamps. Returns the
// first problem found, phrased for a person, or null when everything is in range.
//
// Shared by the settings form and the schedule's profile editor: a profile is
// not a lesser kind of configuration, and a value the API would reject on one
// must not become reachable through the other.
export function validateConfigRanges(values: DeviceConfigValuesDto): string | null {
  const checks: { key: keyof typeof CONFIG_LIMITS; value: number }[] = [
    { key: 'intervalSeconds', value: values.intervalSeconds },
    { key: 'fixTimeoutSeconds', value: values.fixTimeoutSeconds },
    { key: 'queueMaxFixes', value: values.queueMaxFixes },
    { key: 'retryIntervalHours', value: values.retryIntervalHours },
    { key: 'retryMaxAgeHours', value: values.retryMaxAgeHours },
    { key: 'configCheckSeconds', value: values.configCheckSeconds },
  ]

  for (const check of checks) {
    const limit = CONFIG_LIMITS[check.key]
    if (!Number.isInteger(check.value) || check.value < limit.min || check.value > limit.max) {
      return `"${CONFIG_FIELD_LABELS[check.key]}" must be a whole number between ${limit.min} and ${limit.max}.`
    }
  }

  return null
}
