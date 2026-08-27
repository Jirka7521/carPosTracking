// ---------------------------------------------------------------------------
// ScheduleTimeline — one week, seven rows, a coloured block per profile.
//
//   Sun ▐███ Night ███▌▐──────── Day ────────▌▐███ Night ███▌
//   Mon ▐███ Night ███▌▐──────── Day ────────▌▐███ Night ███▌
//   ...                        ▲ now
//
// This is the component that makes the feature honest. A list of rules is a
// program, and reading one means simulating it: which rule wins at 23:30 on a
// Saturday, is there an hour nothing covers, did that 22:00 really survive the
// DST change. The strip answers all three at a glance, because a gap looks like
// a gap and an overlap resolves in front of you.
//
// It is drawn in LOCAL time — a row is the reader's Monday, not UTC's — while
// the rules underneath it are UTC. utils/schedule.ts owns that conversion and
// deliberately mirrors the server's evaluator rather than approximating it: a
// timeline that disagreed with what the tracker is actually doing would be worse
// than no timeline at all.
//
// Colour is an index into a fixed palette, never carried alone: every block is
// also labelled with its profile name (elided only when the block is too narrow,
// where the title attribute and the legend carry it instead).
// ---------------------------------------------------------------------------

import { useEffect, useState } from 'react'
import type { DeviceScheduleRuleDto } from '../services/apiTypes'
import {
  DAY_LABELS_LONG,
  MINUTES_PER_DAY,
  buildWeekTimeline,
  formatMinuteOfDay,
  localWeekStart,
} from '../utils/schedule'
import type { TimelineSegment } from '../utils/schedule'

// Distinct hues that keep their separation in both light and dark themes. More
// than the profile cap would need is pointless; twelve profiles is already more
// schedule than anyone can hold in their head.
const PROFILE_COLORS: readonly string[] = [
  '#3b82f6', '#f59e0b', '#10b981', '#8b5cf6',
  '#ef4444', '#06b6d4', '#ec4899', '#84cc16',
]

const DAY_MS = MINUTES_PER_DAY * 60_000

export type ScheduleTimelineProps = {
  rules: readonly DeviceScheduleRuleDto[]
  fallbackProfileId: string | null
  fallbackProfileName: string | null
  // Ids in a stable order, so a profile keeps its colour as rules are edited.
  // Taken from the profile list rather than from the rules, which change often.
  profileOrder: readonly string[]
  // Bumped by the tab's refresh timer. Only used to re-anchor "now" — the strip
  // itself is a pure function of the rules.
  refreshToken: number
}

export function ScheduleTimeline({
  rules,
  fallbackProfileId,
  fallbackProfileName,
  profileOrder,
  refreshToken,
}: ScheduleTimelineProps) {
  // "Now" is state, not a Date.now() read during render. Reading the clock while
  // rendering is impure — two renders in the same commit could disagree about
  // which day is today, and which block the marker sits in — so it is sampled
  // once on mount and then on every tick of the tab's refresh timer, which is
  // exactly the cadence the marker should move at.
  const [nowMs, setNowMs] = useState<number>(0)

  // set-state-in-effect is suppressed rather than satisfied, the same way
  // DevicePage suppresses it. The rule exists to catch state derived from other
  // state; the clock is a genuine external system, and `refreshToken` is the
  // subscription to it. There is no cascade: this sets one number, once per
  // tick, and nothing downstream feeds back into it.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setNowMs(Date.now())
  }, [refreshToken])

  // Zero only between mount and that first effect. Anchoring the week to the
  // epoch would draw a meaningless strip for that one frame.
  if (nowMs === 0) {
    return <p className="hint">Building the week…</p>
  }

  return (
    <TimelineBody
      rules={rules}
      fallbackProfileId={fallbackProfileId}
      fallbackProfileName={fallbackProfileName}
      profileOrder={profileOrder}
      nowMs={nowMs}
    />
  )
}

type TimelineBodyProps = {
  rules: readonly DeviceScheduleRuleDto[]
  fallbackProfileId: string | null
  fallbackProfileName: string | null
  profileOrder: readonly string[]
  nowMs: number
}

function TimelineBody({
  rules,
  fallbackProfileId,
  fallbackProfileName,
  profileOrder,
  nowMs,
}: TimelineBodyProps) {
  // Recomputed on every render rather than memoised: it is a few hundred
  // comparisons over at most 32 rules, and a stale timeline is a worse bug than
  // a redundant loop.
  const weekStart: Date = localWeekStart(new Date(nowMs))
  const segments: TimelineSegment[] = buildWeekTimeline(
    rules,
    fallbackProfileId,
    fallbackProfileName,
    weekStart,
  )

  const weekStartMs: number = weekStart.getTime()

  function colorFor(profileId: string | null): string {
    if (profileId === null) {
      return 'var(--border, #cbd5e1)'
    }
    const index: number = profileOrder.indexOf(profileId)
    return PROFILE_COLORS[(index < 0 ? 0 : index) % PROFILE_COLORS.length]
  }

  return (
    <div className="schedule-timeline">
      <div className="schedule-timeline-scale" aria-hidden="true">
        {[0, 6, 12, 18].map((hour) => (
          <span key={hour} style={{ left: `${(hour / 24) * 100}%` }}>
            {String(hour).padStart(2, '0')}
          </span>
        ))}
        <span style={{ left: '100%' }}>24</span>
      </div>

      {[0, 1, 2, 3, 4, 5, 6].map((dayIndex) => {
        const dayStartMs: number = weekStartMs + dayIndex * DAY_MS
        const dayEndMs: number = dayStartMs + DAY_MS
        const isToday: boolean = nowMs >= dayStartMs && nowMs < dayEndMs

        return (
          <div className={`schedule-timeline-row${isToday ? ' is-today' : ''}`} key={dayIndex}>
            <span className="schedule-timeline-day">{DAY_LABELS_LONG[dayIndex].slice(0, 3)}</span>

            <div className="schedule-timeline-track">
              {segments
                // Clipped per row rather than split up front: a block spanning
                // midnight belongs to two rows, and clipping is how it appears in
                // both without the segment list having to know about days.
                .filter((segment) => segment.endMs > dayStartMs && segment.startMs < dayEndMs)
                .map((segment) => {
                  const fromMs: number = Math.max(segment.startMs, dayStartMs)
                  const toMs: number = Math.min(segment.endMs, dayEndMs)
                  const leftPct: number = ((fromMs - dayStartMs) / DAY_MS) * 100
                  const widthPct: number = ((toMs - fromMs) / DAY_MS) * 100

                  const fromLabel: string = formatMinuteOfDay(
                    new Date(fromMs).getHours() * 60 + new Date(fromMs).getMinutes(),
                  )
                  const toLabel: string = formatMinuteOfDay(
                    new Date(toMs).getHours() * 60 + new Date(toMs).getMinutes(),
                  )
                  const name: string = segment.profileName ?? 'Not covered'

                  return (
                    <div
                      key={`${segment.startMs}-${fromMs}`}
                      className={`schedule-timeline-block${
                        segment.profileId === null ? ' schedule-timeline-block--empty' : ''
                      }`}
                      style={{
                        left: `${leftPct}%`,
                        width: `${widthPct}%`,
                        backgroundColor: colorFor(segment.profileId),
                      }}
                      // Carries the full label for the blocks too narrow to show
                      // one, so no block is ever colour-only.
                      title={`${name} · ${fromLabel}–${toLabel}`}
                    >
                      <span className="schedule-timeline-block-label">{name}</span>
                    </div>
                  )
                })}

              {isToday ? (
                <div
                  className="schedule-timeline-now"
                  style={{ left: `${((nowMs - dayStartMs) / DAY_MS) * 100}%` }}
                  title="Now"
                  aria-label="Current time"
                />
              ) : null}
            </div>
          </div>
        )
      })}

      <p className="hint schedule-timeline-note">
        Shown in your local time ({Intl.DateTimeFormat().resolvedOptions().timeZone}).
      </p>
    </div>
  )
}
