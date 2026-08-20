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
          Dashboard
        </text>
        <text className="config-pipeline-label" x="160" y="44" textAnchor="middle">
          Broker
        </text>
        <text className="config-pipeline-label" x="290" y="44" textAnchor="middle">
          Device
        </text>
      </svg>

      <div className="config-pipeline-status">
        <span className={`config-sync-badge config-sync-badge--${syncState}`}>
          {syncState === 'synced' ? 'In sync' : syncState === 'pending' ? 'Pending' : 'Unknown'}
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
        title="Publish the current settings to the broker again"
      >
        {isRepublishing ? 'Sending…' : 'Re-send to device'}
      </button>
    </div>
  )
}

function describeStatus(state: DeviceConfigStateDto, syncState: ConfigSyncState): string {
  if (syncState === 'synced') {
    return `Device is running v${state.desired.version} · confirmed ${formatRelativeTime(state.appliedAt)}`
  }
  if (syncState === 'pending') {
    return `v${state.desired.version} published · device is running v${state.applied?.version} · last report ${formatRelativeTime(state.lastSeenAt)}`
  }
  return `v${state.desired.version} published · the device has not reported a version yet`
}

function describeExplanation(state: DeviceConfigStateDto, syncState: ConfigSyncState): string {
  if (syncState === 'synced') {
    return 'The settings below are the ones the tracker is using.'
  }

  if (syncState === 'pending') {
    // With deep sleep on, the device is awake for seconds per cycle and only
    // picks the change up on its next wake — so quote that wait rather than
    // letting the reader wonder whether something is stuck.
    const wait: string = state.applied?.values.sleepBetween
      ? ` With deep sleep on it is only online briefly, so this can take up to one reporting interval (${describeSeconds(state.applied.values.intervalSeconds)}).`
      : ''
    return `The broker is holding this change; the device applies it on its next report.${wait}`
  }

  return 'Nothing is wrong with the settings — this device has simply never confirmed which revision it is running. Firmware older than remote settings never will.'
}

function describeDiagram(syncState: ConfigSyncState): string {
  if (syncState === 'synced') {
    return 'Settings have reached the device: dashboard, broker and device all confirmed.'
  }
  if (syncState === 'pending') {
    return 'Settings are published to the broker and waiting for the device to pick them up.'
  }
  return 'Settings are published to the broker; the device has not confirmed a revision.'
}
