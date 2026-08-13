// Helpers for presenting a device consistently across the app.

import type { DeviceDto } from '../services/apiTypes'

// The label to show for a device, in order of how personal it is:
//   1. customName  — the caller's own nickname, private to them
//   2. displayName — the shared name set when the device was registered
//   3. deviceId    — the MQTT identity, which always exists
//
// Kept in one place so the breadcrumb, the card and the sharing list cannot
// drift apart and show three different names for the same tracker.
export function deviceLabel(device: DeviceDto): string {
  const custom = device.customName?.trim()
  if (custom) {
    return custom
  }

  const display = device.displayName?.trim()
  if (display) {
    return display
  }

  return device.deviceId
}

// True when the label above is something other than the device id, i.e. when
// the id is worth showing separately as the canonical identifier.
export function hasDistinctLabel(device: DeviceDto): boolean {
  return deviceLabel(device) !== device.deviceId
}
