// ---------------------------------------------------------------------------
// ConfigSyncIndicator — the three hops a settings change actually travels.
//
//    Dashboard  ──▶  Broker (retained)  ─ ─▶  Device
//
// Why a picture at all: "pending" on its own invites the reading that something
// failed. Showing the path makes it obvious that the change is *parked at the
// broker on purpose*, waiting for a device that is asleep — which is the normal,
// designed behaviour, not a fault. The third node is the only one that is ever
// hollow, because the first two hops are synchronous and already done by the
// time this renders.
//
// Inline SVG rather than a library: the project's only chart dependency is
// recharts, and this is six shapes. It is drawn in a viewBox and scaled by CSS,
// so it stays crisp and reflows with the section.
//
// Accessibility: state is never carried by colour alone — each state also has
// its own node shape, its own label under the diagram, and a sentence of prose.
// The pulse is disabled under prefers-reduced-motion (see App.css).
// ---------------------------------------------------------------------------

import { useTranslation } from 'react-i18next'
import i18n from '../i18n'
import type { DeviceConfigStateDto } from '../services/apiTypes'
import { formatRelativeTime } from '../utils/dates'
import { describeSeconds, resolveSyncState } from '../utils/deviceConfig'
import type { ConfigSyncState } from '../utils/deviceConfig'

export type ConfigSyncIndicatorProps = {
  state: DeviceConfigStateDto
  // True while a re-publish is in flight, so the button can disable itself.
  isRepublishing: boolean
  onRepublish: () => void
}

export function ConfigSyncIndicator({
  state,
  isRepublishing,
  onRepublish,
}: ConfigSyncIndicatorProps) {
  const { t } = useTranslation('settings')

  const syncState: ConfigSyncState = resolveSyncState(state)
  const deviceReached: boolean = syncState === 'synced'

  return (
    <div className={`config-pipeline config-pipeline--${syncState}`}>
      <svg
        className="config-pipeline-diagram"
        viewBox="0 0 320 56"
        role="img"
        aria-label={describeDiagram(syncState)}
      >
        {/* Dashboard → Broker: solid, because saving and publishing both
            completed before this component ever rendered. */}
        <line className="config-pipeline-link" x1="46" y1="20" x2="114" y2="20" />
        {/* Broker → Device: dashed while the device has not confirmed, solid
            once it has. The dash is what reads as "in transit". */}
        <line
          className={`config-pipeline-link${deviceReached ? '' : ' config-pipeline-link--waiting'}`}
          x1="206"
          y1="20"
          x2="274"
          y2="20"
        />

        <circle className="config-pipeline-node config-pipeline-node--done" cx="30" cy="20" r="8" />
        <circle className="config-pipeline-node config-pipeline-node--done" cx="160" cy="20" r="8" />
        {deviceReached ? (
          <circle className="config-pipeline-node config-pipeline-node--done" cx="290" cy="20" r="8" />
        ) : (
          // Hollow, and pulsing while pending — a second, non-colour cue that
          // this is the hop that has not happened.
          <circle
            className={`config-pipeline-node config-pipeline-node--open${
              syncState === 'pending' ? ' config-pipeline-node--waiting' : ''
            }`}
            cx="290"
            cy="20"
            r="8"
          />
        )}

        <text className="config-pipeline-label" x="30" y="44" textAnchor="middle">
          {t('sync.node.dashboard')}
        </text>
        <text className="config-pipeline-label" x="160" y="44" textAnchor="middle">
          {t('sync.node.broker')}
        </text>
        <text className="config-pipeline-label" x="290" y="44" textAnchor="middle">
          {t('sync.node.device')}
        </text>
      </svg>

      <div className="config-pipeline-status">
        <span className={`config-sync-badge config-sync-badge--${syncState}`}>
          {t(`sync.badge.${syncState}`)}
        </span>
        <p className="config-pipeline-summary">{describeStatus(state, syncState)}</p>
        <p className="hint">{describeExplanation(state, syncState)}</p>
      </div>

      <button
        type="button"
        className="btn btn-secondary btn-sm"
        onClick={onRepublish}
        disabled={isRepublishing}
        // The retained message normally survives on the broker without help.
        // This is for the case where it did not — a broker restarted without
        // persistence — which is invisible from here, so the button exists
        // rather than the UI trying to guess.
        title={t('sync.republishHint')}
      >
        {isRepublishing ? t('sync.republishing') : t('sync.republish')}
      </button>
    </div>
  )
}

// The three prose helpers below are module-level rather than inline, so they
// take their strings from the i18next singleton. The component itself
// subscribes with useTranslation(), which is what re-renders them on a
// language change.
function describeStatus(state: DeviceConfigStateDto, syncState: ConfigSyncState): string {
  if (syncState === 'synced') {
    return i18n.t('settings:sync.status.synced', {
      version: state.desired.version,
      confirmed: formatRelativeTime(state.appliedAt),
    })
  }
  if (syncState === 'pending') {
    return i18n.t('settings:sync.status.pending', {
      version: state.desired.version,
      appliedVersion: state.applied?.version,
      lastReport: formatRelativeTime(state.lastSeenAt),
    })
  }
  return i18n.t('settings:sync.status.unknown', { version: state.desired.version })
}

function describeExplanation(state: DeviceConfigStateDto, syncState: ConfigSyncState): string {
  if (syncState === 'synced') {
    return i18n.t('settings:sync.explain.synced')
  }

  if (syncState === 'pending') {
    // With deep sleep on, the device is awake for seconds per cycle and only
    // picks the change up on its next wake — so quote that wait rather than
    // letting the reader wonder whether something is stuck.
    const wait: string = state.applied?.values.sleepBetween
      ? ` ${i18n.t('settings:sync.explain.deepSleepWait', {
          interval: describeSeconds(state.applied.values.intervalSeconds),
        })}`
      : ''
    return `${i18n.t('settings:sync.explain.pending')}${wait}`
  }

  return i18n.t('settings:sync.explain.unknown')
}

function describeDiagram(syncState: ConfigSyncState): string {
  if (syncState === 'synced') {
    return i18n.t('settings:sync.diagram.synced')
  }
  if (syncState === 'pending') {
    return i18n.t('settings:sync.diagram.pending')
  }
  return i18n.t('settings:sync.diagram.unknown')
}
