// ============================================================
// DeviceLinkBadge — a compact pill saying whether a tracker is connected, and if
// not, why: "Sleeping", "Switched off", "Battery low", "Connection lost",
// "Error" — or "Overdue" when a sleeping device has not come back on time.
//
// The reason comes from the device itself (or from its Last Will, published by
// the broker when it could not say goodbye); the colour comes from the severity
// the API assigned. Whether it has come back since, and whether it is late, is
// worked out in utils/deviceEvents.ts so the card and the device header agree.
//
// Renders nothing when there is nothing to say (a retired device, or firmware
// that predates status messages), so callers drop it in unconditionally — the
// same contract as BatteryBadge, whose shape and classes this mirrors.
//
// State is never shown by colour alone: every variant carries a word, and the
// title spells out the when.
// ============================================================

import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { DeviceDto } from '../services/apiTypes'
import { formatDateTime, formatTime } from '../i18n/format'
import { formatRelativeTime } from '../utils/dates'
import {
  OFFLINE_BADGE_ICONS,
  OFFLINE_BADGE_LABEL_KEYS,
  resolveLinkState,
} from '../utils/deviceEvents'
import type { DeviceLinkState } from '../utils/deviceEvents'

type DeviceLinkBadgeProps = {
  device: DeviceDto
  // Larger rendering for the device page header, like BatteryBadge's.
  large?: boolean
}

export function DeviceLinkBadge({ device, large = false }: DeviceLinkBadgeProps) {
  // The array form: the label table in utils/deviceEvents.ts carries
  // namespace-qualified keys, which the single-namespace form does not accept.
  const { t } = useTranslation(['common'])

  // "Now" is state, not a Date.now() read during render — the same reasoning and
  // the same pattern as ScheduleTimeline. It is sampled whenever the parent hands
  // in a freshly loaded device, which both pages do on their thirty-second
  // refresh, so "Sleeping" turns into "Overdue" on that same cadence. Zero until
  // the first effect runs, which simply means "not overdue" for that one frame.
  const [nowMs, setNowMs] = useState<number>(0)

  // set-state-in-effect is suppressed for the reason ScheduleTimeline gives: the
  // clock is an external system and the device reload is the subscription to it.
  // One number is set per reload, and nothing feeds back into it.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setNowMs(Date.now())
  }, [device])

  const state: DeviceLinkState = resolveLinkState(device, nowMs)
  const sizeClass: string = large ? ' link-badge--lg' : ''

  if (state.kind === 'unknown') {
    return null
  }

  if (state.kind === 'online') {
    return (
      <span
        className={`link-badge link-badge--online${sizeClass}`}
        title={t('link.onlineTitle', {
          when: state.since ? formatRelativeTime(state.since.toISOString()) : t('relative.unknown'),
        })}
      >
        <span aria-hidden="true">📶</span>
        <span>{t('link.online')}</span>
      </span>
    )
  }

  // A sleep that has run past its expected wake (plus grace) is the one case a
  // normal reason turns into a warning: the device said it would be back and is
  // not. Shown as its own word so it is not mistaken for a routine sleep.
  if (state.overdue && state.expectedBackAt !== null) {
    return (
      <span
        className={`link-badge link-badge--alert${sizeClass}`}
        title={t('link.overdueTitle', { time: formatDateTime(state.expectedBackAt) })}
      >
        <span aria-hidden="true">⏰</span>
        <span>{t('link.overdue')}</span>
      </span>
    )
  }

  const title: string =
    state.expectedBackAt !== null
      ? t('link.sleepTitle', {
          since: formatTime(state.since),
          time: formatTime(state.expectedBackAt),
        })
      : t('link.sinceTitle', { when: formatDateTime(state.since) })

  return (
    <span className={`link-badge link-badge--${state.severity}${sizeClass}`} title={title}>
      <span aria-hidden="true">{OFFLINE_BADGE_ICONS[state.reason]}</span>
      <span>{t(OFFLINE_BADGE_LABEL_KEYS[state.reason])}</span>
    </span>
  )
}
