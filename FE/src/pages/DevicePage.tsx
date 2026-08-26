// ============================================================
// DevicePage — the layout shell for a single device.
//
// This component is mounted at /device/:deviceId — where :deviceId is the
// tracker's MQTT identity, e.g. "GNSS01" — and renders:
//   1. A breadcrumb "← Devices / <device name>"
//   2. A four-tab bar: Map · Positions · Charts · Settings
//   3. An <Outlet /> where the active tab component is rendered
//
// It loads the device from the API on mount and passes the result
// to child tabs via React Router's outlet context so each tab does
// not have to make its own /api/me/devices call.
//
// If the device cannot be found (deleted, access revoked, wrong ID)
// an error state is shown with a "Back to devices" link.
//
// It also owns the device page's ONE auto-refresh timer, and reloads the device
// on every tick. The header shows a battery level and a status badge, both of
// which used to freeze at whatever they were when the page opened; now they
// follow the tracker.
//
// The timer is handed to the child tabs through the context, and every tab uses
// it rather than starting one of its own. That is deliberate: each tab already
// renders a refresh control, so a private timer per tab would put two pills on
// screen ticking out of step, and pressing one tab's Refresh would leave the
// header beside it stale. There is exactly one countdown per device page, and
// whichever control you press advances all of it.
//
// Child tabs access the shared context using:
//   const { device, reloadDevice } = useOutletContext<DevicePageContext>()
// ============================================================

import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useParams } from 'react-router-dom'
import { fetchMyDevices } from '../services/apiClient'
import type { DeviceDto } from '../services/apiTypes'
import { deviceLabel, hasDistinctLabel } from '../utils/devices'
import { BatteryBadge } from '../components/BatteryBadge'
import { useAutoRefresh } from '../hooks/useAutoRefresh'
import type { AutoRefresh } from '../hooks/useAutoRefresh'
import { describeError } from '../utils/errors'

// The cadence every load on this page runs at — the device here, and the
// positions each tab fetches off the same token.
const AUTO_REFRESH_SEC = 30

// ---- Shared context type ----
// Exported so child tab components can import the type and call
// useOutletContext<DevicePageContext>() safely.
export type DevicePageContext = {
  // The loaded device object (guaranteed non-null when context is available)
  device: DeviceDto

  // Lets a tab (e.g. settings) trigger a fresh device load — for example
  // after the user deletes the device we want to update the status badge.
  reloadDevice: () => Promise<void>

  // Apply a partial update to the device in the parent state so the
  // breadcrumb, heading, and all tabs reflect the change immediately
  // without a round-trip to the server.
  updateDevice: (patch: Partial<DeviceDto>) => void

  // This page's shared refresh timer. A tab that wants to reload something of
  // its own puts `autoRefresh.token` in its load effect's deps and renders a
  // RefreshToolbar bound to it; there is one countdown, wherever it is shown.
  autoRefresh: AutoRefresh

  // True while a background reload of the device is in flight — for a tab that
  // renders its own refresh button and wants the spinner to agree.
  isRefreshingDevice: boolean
}

export function DevicePage() {
  // :deviceId from the URL — the device's MQTT identity, e.g. "GNSS01".
  const { deviceId } = useParams<{ deviceId: string }>()

  const [device, setDevice] = useState<DeviceDto | null>(null)
  const [isLoading, setIsLoading] = useState<boolean>(true)
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false)
  const [errorMessage, setErrorMessage] = useState<string>('')

  const refresh = useAutoRefresh(AUTO_REFRESH_SEC)

  // Which device is actually on screen. It is what separates a first load —
  // which may show the spinner and may blank the page with an error — from a
  // refresh tick, which must do neither.
  const loadedDeviceIdRef = useRef<string | null>(null)

  // Fetches the full device list and picks out the one matching the URL id.
  // The API already filters to devices the caller has access to, so a
  // missing device means either it was deleted or access was revoked.
  // Nothing is set before the first await: a setState reached synchronously
  // from the effect below would render twice before paint. The "still loading"
  // signal for a *changed* :deviceId is derived instead — see isBusy.
  async function loadDevice(): Promise<void> {
    const isInitial: boolean = loadedDeviceIdRef.current !== deviceId
    if (!isInitial) {
      setIsRefreshing(true)
    }

    try {
      const devices = await fetchMyDevices()
      const found = devices.find((d) => d.deviceId === deviceId)
      if (!found) {
        setErrorMessage('Device not found, or you do not have access to it.')
      } else {
        loadedDeviceIdRef.current = deviceId ?? null
        setDevice(found)
        setErrorMessage('')
      }
    } catch (error) {
      // A failed refresh tick keeps the page it already has. Replacing a
      // working device view with "Failed to load device." because one poll hit
      // a flaky connection would be worse than the stale battery reading it is
      // trying to correct.
      if (isInitial) {
        setErrorMessage(describeError(error, 'Failed to load device.'))
      }
    } finally {
      setIsLoading(false)
      setIsRefreshing(false)
    }
  }

  // Load once on mount (and whenever the URL :deviceId changes).
  //
  // react-hooks/set-state-in-effect is suppressed rather than satisfied: the
  // rule flags any effect that calls a function setting state ANYWHERE in its
  // body — it does not model await boundaries. Every setState in loadDevice()
  // runs after `await fetchMyDevices()`, so there is no synchronous cascading
  // render, which is the thing the rule exists to prevent. Nor can the shape be
  // changed to please it: loadDevice is also handed to the child tabs as
  // reloadDevice, so it cannot move inside this effect (and the rule flags that
  // shape too).
  //
  // refresh.token is in the deps as well, which is what makes the header's
  // battery and last-fix follow the tracker: every tick and every press of
  // "Refresh" bumps it, and loadDevice knows to reload quietly.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadDevice()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deviceId, refresh.token])

  // A loaded device whose id no longer matches the URL means :deviceId changed
  // and the fetch for the new one is still in flight. Deriving that beats
  // setting isLoading back to true from the effect, and it renders the spinner
  // rather than one frame of the previously-viewed device.
  const isBusy = isLoading || (device !== null && device.deviceId !== deviceId)

  // ---- Loading state ----
  if (isBusy) {
    return (
      <div className="loading-state" style={{ flex: 1 }}>
        <div className="spinner" aria-hidden="true" />
        <span>Loading device…</span>
      </div>
    )
  }

  // ---- Error / not-found state ----
  if (errorMessage || device === null) {
    return (
      <div className="error-state" style={{ flex: 1 }}>
        <p>{errorMessage || 'Device not found.'}</p>
        <Link to="/home" className="btn btn-secondary">
          ← Back to devices
        </Link>
      </div>
    )
  }

  // ---- Normal render: breadcrumb + tabs + tab content ----
  return (
    <div className="device-page">
      {/* Breadcrumb navigation */}
      <div className="device-page-header">
        <nav className="breadcrumb" aria-label="Breadcrumb">
          <Link to="/home">Devices</Link>
          <span className="breadcrumb-sep" aria-hidden="true">›</span>
          {/* Show the user's custom name in the breadcrumb when set */}
          <span>{deviceLabel(device)}</span>
        </nav>

        {/* Device title + active/inactive badge */}
        <div className="device-page-title">
          <div>
            <h2>{deviceLabel(device)}</h2>
            {/* Show the MQTT device id beneath the name so the canonical
                identifier — the one the firmware and broker use — is always
                findable. */}
            {hasDistinctLabel(device) ? (
              <p
                style={{
                  margin: '2px 0 0',
                  fontSize: '0.75rem',
                  fontFamily: 'monospace',
                  opacity: 0.65,
                }}
              >
                {device.deviceId}
              </p>
            ) : null}
          </div>
          <div className="device-card-badges">
            {/* Battery from the device's most recent fix (⚡ while charging).
                Renders nothing when the device has reported none. Kept current
                by the refresh control beside it. */}
            <BatteryBadge value={device.lastBatteryPct} large />

            <span
              className={`status-badge ${device.isActive ? 'status-badge--active' : 'status-badge--inactive'}`}
            >
              {device.isActive ? 'Active' : 'Inactive'}
            </span>
          </div>
        </div>
      </div>

      {/*
       * Tab navigation bar.
       * NavLink automatically adds the "active" class when the current URL
       * matches the tab's path — we use `end` to prevent partial matches.
       */}
      <nav className="device-tab-bar" aria-label="Device sections">
        <NavLink
          to="map"
          className={({ isActive }) => `device-tab${isActive ? ' active' : ''}`}
        >
          🗺 Map
        </NavLink>

        <NavLink
          to="positions"
          className={({ isActive }) => `device-tab${isActive ? ' active' : ''}`}
        >
          📋 Positions
        </NavLink>

        <NavLink
          to="charts"
          className={({ isActive }) => `device-tab${isActive ? ' active' : ''}`}
        >
          📈 Charts
        </NavLink>

        <NavLink
          to="settings"
          className={({ isActive }) => `device-tab${isActive ? ' active' : ''}`}
        >
          ⚙ Settings
        </NavLink>
      </nav>

      {/*
       * Tab content: React Router renders the matching child route here.
       * The context object is available to all child tabs via
       * useOutletContext<DevicePageContext>().
       */}
      <div className="device-tab-content">
        <Outlet context={{
          device,
          reloadDevice: loadDevice,
          updateDevice: (patch) => setDevice((d) => d ? { ...d, ...patch } : d),
          autoRefresh: refresh,
          isRefreshingDevice: isRefreshing,
        } satisfies DevicePageContext} />
      </div>
    </div>
  )
}
