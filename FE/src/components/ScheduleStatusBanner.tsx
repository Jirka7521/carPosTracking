// ---------------------------------------------------------------------------
// ScheduleStatusBanner — what is in force, why, and what happens next.
//
//    Now: Night · since Thu 22:00 → switches to Day at Fri 06:00 (in 3 h 12 m)
//
// One line, always true, always answerable without reading the rules. It has two
// forms:
//
//   NORMAL   green — the schedule is in charge and this is what it decided.
//   OVERRIDE amber — somebody saved settings by hand and they hold until the
//                    stated moment, with a button to end that early.
//
// The amber form is the one that earns the component. Without it, a device
// quietly ignoring its own schedule for the next six hours looks exactly like
// one obeying it, and the only way to find out is to remember having saved.
//
// Every instant comes from the SERVER's evaluation. The browser's clock is used
// only for the "(in 3 h 12 m)" gloss, which is why that is the one part phrased
// loosely enough to be harmless if the two disagree by a minute.
// ---------------------------------------------------------------------------

import { Trans, useTranslation } from 'react-i18next'
import type { DeviceScheduleOverrideDto, DeviceScheduleStatusDto } from '../services/apiTypes'
import { parseApiTimestamp } from '../utils/dates'
import { describeTimeUntil, formatLocalDayTime } from '../utils/schedule'

export type ScheduleStatusBannerProps = {
  status: DeviceScheduleStatusDto
  // Present only while a manual save is holding the schedule off.
  override: DeviceScheduleOverrideDto | null
  // Null until the worker has completed a pass over this device.
  evaluatedAt: string | null
  isResuming: boolean
  onResume: () => void
}

export function ScheduleStatusBanner({
  status,
  override,
  evaluatedAt,
  isResuming,
  onResume,
}: ScheduleStatusBannerProps) {
  const { t } = useTranslation('schedule')

  if (override !== null) {
    return <OverrideBanner override={override} isResuming={isResuming} onResume={onResume} />
  }

  const since: Date | null = status.activeSince ? parseApiTimestamp(status.activeSince) : null
  const next: Date | null = status.nextChangeAt ? parseApiTimestamp(status.nextChangeAt) : null

  return (
    <div className="schedule-status schedule-status--active" role="status">
      <div className="schedule-status-main">
        <span className="schedule-status-label">{t('status.now')}</span>
        <span className="schedule-status-profile">
          {status.activeProfileName ?? t('status.noProfile')}
        </span>
        {status.activeRuleId === null ? (
          // Naming the fallback explicitly matters: "why is it on Day at
          // midnight?" is answered by "no rule covers this hour", and that is not
          // guessable from the profile name alone.
          <span className="schedule-status-tag">{t('status.fallback')}</span>
        ) : null}
        {since !== null ? (
          <span className="hint">{t('status.since', { when: formatLocalDayTime(since) })}</span>
        ) : null}
      </div>

      <p className="schedule-status-next">
        {next === null || status.nextProfileName === null ? (
          // A schedule that resolves the same way all week. Saying so is better
          // than an empty space the reader reads as "still loading".
          <>{t('status.neverSwitches')}</>
        ) : (
          // <Trans> because the two emphasised values sit inside one sentence
          // whose word order differs between languages.
          <Trans
            i18nKey="status.switchesTo"
            ns="schedule"
            values={{
              profile: status.nextProfileName,
              when: formatLocalDayTime(next),
              until: describeTimeUntil(next),
            }}
            components={{ strong: <strong />, quiet: <span className="hint" /> }}
          />
        )}
      </p>

      {evaluatedAt === null ? (
        // An enabled schedule the worker has not reached yet. Distinguishing
        // "computed and acted on" from "computed for display only" is the whole
        // reason the API returns this timestamp.
        <p className="hint">{t('status.awaitingFirstPass')}</p>
      ) : null}
    </div>
  )
}

function OverrideBanner({
  override,
  isResuming,
  onResume,
}: {
  override: DeviceScheduleOverrideDto
  isResuming: boolean
  onResume: () => void
}) {
  const { t } = useTranslation('schedule')

  const until: Date | null = parseApiTimestamp(override.until)

  return (
    <div className="schedule-status schedule-status--override" role="status">
      <div className="schedule-status-main">
        <span className="schedule-status-label">{t('override.label')}</span>
        <span className="schedule-status-profile">{t('override.manual')}</span>
      </div>

      <p className="schedule-status-next">
        {until === null ? (
          <>{t('override.resumesNextSwitch')}</>
        ) : (
          <Trans
            i18nKey="override.returnsAt"
            ns="schedule"
            values={{
              profile: override.resumingProfileName ?? t('override.scheduledProfile'),
              when: formatLocalDayTime(until),
              until: describeTimeUntil(until),
            }}
            components={{ strong: <strong />, quiet: <span className="hint" /> }}
          />
        )}
      </p>

      <button
        type="button"
        className="btn btn-secondary btn-sm"
        onClick={onResume}
        disabled={isResuming}
        title={t('override.resumeHint')}
      >
        {isResuming ? t('override.resuming') : t('override.resume')}
      </button>
    </div>
  )
}
