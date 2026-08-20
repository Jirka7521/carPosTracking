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
// Child tabs access the shared context using:
//   const { device, reloadDevice } = useOutletContext<DevicePageContext>()
// ============================================================

import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useParams } from 'react-router-dom'
import { fetchMyDevices } from '../services/apiClient'
import type { DeviceDto } from '../services/apiTypes'
import { deviceLabel, hasDistinctLabel } from '../utils/devices'
import { BatteryBadge } from '../components/BatteryBadge'
import { describeError } from '../utils/errors'

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
}

export function DevicePage() {
  // :deviceId from the URL — the device's MQTT identity, e.g. "GNSS01".
  const { deviceId } = useParams<{ deviceId: string }>()

  const [device, setDevice] = useState<DeviceDto | null>(null)
  const [isLoading, setIsLoading] = useState<boolean>(true)
  const [errorMessage, setErrorMessage] = useState<string>('')

  // Fetches the full device list and picks out the one matching the URL id.
  // The API already filters to devices the caller has access to, so a
  // missing device means either it was deleted or access was revoked.
  // Nothing is set before the first await: a setState reached synchronously
  // from the effect below would render twice before paint. The "still loading"
  // signal for a *changed* :deviceId is derived instead — see isBusy.
  async function loadDevice(): Promise<void> {
    try {
      const devices = await fetchMyDevices()
      const found = devices.find((d) => d.deviceId === deviceId)
      if (!found) {
        setErrorMessage('Device not found, or you do not have access to it.')
      } else {
        setDevice(found)
        setErrorMessage('')
      }
    } catch (error) {
      setErrorMessage(describeError(error, 'Failed to load device.'))
    } finally {
      setIsLoading(false)
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
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadDevice()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deviceId])

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
                Renders nothing when the device has reported none. */}
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
        } satisfies DevicePageContext} />
      </div>
    </div>
  )
}
