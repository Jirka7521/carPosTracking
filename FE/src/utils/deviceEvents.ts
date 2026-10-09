// ---------------------------------------------------------------------------
// deviceEvents — what a tracker's connection status MEANS, for the status badge
// and the Events tab.
//
// The API stores what happened (an offline reason, a restart, a wake, a motion
// step) and how much it matters (the severity). Two things are left to the
// dashboard, because only it knows "now": whether the device has come back since
// it went offline, and whether a sleeping device is late. Both are answered here,
// in one place, so the card in the grid and the header on the device page can
// never disagree.
//
// Tables below hold translation KEYS, not text, like every other label table in
// utils/. They are reached through a constant, so their prefixes are listed in
// i18next.config.ts preservePatterns — without that, `npm run i18n:extract` would
// delete them.
// ---------------------------------------------------------------------------

import type {
  DeviceDto,
  DeviceEventReason,
  DeviceEventSeverity,
  DeviceOfflineReason,
} from '../services/apiTypes'
import { parseApiTimestamp } from './dates'

// How late a sleeping device may be before the badge calls it overdue. Waking
// takes a fresh WiFi association, a TLS handshake and an MQTT connect before the
// "online" message can go out, and the sleep itself is timed on an RC oscillator
// that drifts — so a device a minute late is not late. Five minutes is well past
// either and still short next to any reporting interval worth sleeping through.
export const OVERDUE_GRACE_MS: number = 5 * 60 * 1000

// The connection state of one device, as the badge shows it.
//   unknown — nothing to say: the device is retired, or its firmware predates
//             status messages (no offline event and no online time)
//   online  — connected since its last offline event
//   offline — gone, with the reason it gave; for a planned sleep, when it should
//             be back, and whether that time has passed
export type DeviceLinkState =
  | { kind: 'unknown' }
  | { kind: 'online'; since: Date | null }
  | {
      kind: 'offline'
      reason: DeviceOfflineReason
      severity: DeviceEventSeverity
      since: Date
      expectedBackAt: Date | null
      overdue: boolean
    }

// The later of the two "it is alive" signals. lastOnlineAt is set by the
// device's own online message, lastSeenAt by every stored fix; either one being
// newer than the last offline event means the device has been back since.
// Using both means a lost online message cannot leave a reporting device looking
// offline.
function latestSignOfLife(device: DeviceDto): Date | null {
  const online: Date | null = device.lastOnlineAt ? parseApiTimestamp(device.lastOnlineAt) : null
  const seen: Date | null = device.lastSeenAt ? parseApiTimestamp(device.lastSeenAt) : null
  if (online === null) {
    return seen
  }
  if (seen === null) {
    return online
  }
  return online.getTime() >= seen.getTime() ? online : seen
}

export function resolveLinkState(device: DeviceDto, nowMs: number): DeviceLinkState {
  // A retired device is not "offline" in any sense worth a badge; the inactive
  // status pill beside it already says everything.
  if (!device.isActive) {
    return { kind: 'unknown' }
  }

  const alive: Date | null = latestSignOfLife(device)
  const offline = device.lastOfflineEvent
  const offlineAt: Date | null = offline ? parseApiTimestamp(offline.occurredAt) : null

  if (offline === null || offlineAt === null) {
    // Never gone offline on record. Only claim "online" when the device has said
    // so itself — a fix alone from firmware without status messages is what the
    // "last fix" line is for, and claiming more would be a guess.
    return device.lastOnlineAt ? { kind: 'online', since: alive } : { kind: 'unknown' }
  }

  if (alive !== null && alive.getTime() > offlineAt.getTime()) {
    return { kind: 'online', since: alive }
  }

  const expectedBackAt: Date | null =
    offline.sleepSeconds !== null && offline.sleepSeconds > 0
      ? new Date(offlineAt.getTime() + offline.sleepSeconds * 1000)
      : null

  return {
    kind: 'offline',
    reason: offline.reason,
    severity: offline.severity,
    since: offlineAt,
    expectedBackAt,
    overdue: expectedBackAt !== null && nowMs > expectedBackAt.getTime() + OVERDUE_GRACE_MS,
  }
}

// The badge's word for why a device is offline — phrased as a STATE ("Sleeping"),
// since the badge answers "what is it doing now".
// Both sleeps read "Sleeping": the badge says what the device is doing, and why
// it went to sleep is the Events tab's to tell.
export const OFFLINE_BADGE_LABEL_KEYS = {
  sleep: 'common:link.reason.sleep',
  sleepNoMotion: 'common:link.reason.sleep',
  powerOff: 'common:link.reason.powerOff',
  batteryLow: 'common:link.reason.batteryLow',
  error: 'common:link.reason.error',
  connectionLost: 'common:link.reason.connectionLost',
} as const satisfies Record<DeviceOfflineReason, string>

// Decorative icon per offline reason; always rendered aria-hidden beside text.
export const OFFLINE_BADGE_ICONS: Record<DeviceOfflineReason, string> = {
  sleep: '💤',
  sleepNoMotion: '💤',
  powerOff: '⏻',
  batteryLow: '🪫',
  error: '⚠️',
  connectionLost: '📵',
}

// The Events tab's word for what happened — phrased as an EVENT ("Went to
// sleep"), since each row is one moment in the history.
export const EVENT_REASON_LABEL_KEYS = {
  sleep: 'device:events.reason.sleep',
  sleepNoMotion: 'device:events.reason.sleepNoMotion',
  powerOff: 'device:events.reason.powerOff',
  batteryLow: 'device:events.reason.batteryLow',
  error: 'device:events.reason.error',
  connectionLost: 'device:events.reason.connectionLost',
  powerOn: 'device:events.reason.powerOn',
  powerLoss: 'device:events.reason.powerLoss',
  crash: 'device:events.reason.crash',
  timer: 'device:events.reason.timer',
  accelerometer: 'device:events.reason.accelerometer',
  powerSwitch: 'device:events.reason.powerSwitch',
  checking: 'device:events.reason.checking',
  activity: 'device:events.reason.activity',
  motionOn: 'device:events.reason.motionOn',
  moving: 'device:events.reason.moving',
  noMotion: 'device:events.reason.noMotion',
  stopped: 'device:events.reason.stopped',
  motionOff: 'device:events.reason.motionOff',
} as const satisfies Record<DeviceEventReason, string>

export const EVENT_SEVERITY_LABEL_KEYS = {
  normal: 'device:events.severity.normal',
  alert: 'device:events.severity.alert',
  error: 'device:events.severity.error',
} as const satisfies Record<DeviceEventSeverity, string>
