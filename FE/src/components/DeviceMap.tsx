// ============================================================
// DeviceMap — Google Maps integration for rendering GPS positions.
//
// Loading strategy:
//   • Inject the Maps JS API script with the marker library preloaded
//     via `&libraries=marker` — this loads everything synchronously so
//     google.maps.Map, google.maps.Marker, etc. are available immediately
//     once the script's `load` event fires.
//   • A module-level singleton promise (googleMapsLoader) prevents the
//     script from being injected more than once across re-mounts / HMR.
//
// Rendering:
//   • Latest position (newest timestamp) gets a blue circular marker
//   • All other positions get red circular markers
//   • A blue polyline connects the positions in chronological order
//   • Clicking a marker opens an info window with coordinates + timestamp
//   • The map frames all positions on the FIRST load that has data and never
//     moves itself again — reloads reconcile the overlays in place, so the
//     user's pan, zoom and open info window survive. `fitToken` is the caller's
//     way to ask for a re-frame ("Fit to positions").
// ============================================================

import { useEffect, useMemo, useRef, useState } from 'react'
import type { PositionDto } from '../services/apiTypes'
import { parseApiTimestamp } from '../utils/dates'

type DeviceMapProps = {
  positions: PositionDto[]
  apiKey: string
  // Bumped by the parent to re-frame the map on all positions. Starts at 0,
  // meaning "never asked" — the initial framing is handled internally.
  fitToken: number
}

// Live Google Maps objects for the current map instance.
type MapState = {
  map:          google.maps.Map | null
  // Markers keyed by fix, so a reload can reuse the ones that are still there
  markersByKey: Map<string, google.maps.Marker>
  polyline:     google.maps.Polyline | null
  infoWindow:   google.maps.InfoWindow | null
  // Which marker's info window is open, so it can be closed if that fix falls
  // out of the range on a later load
  selectedKey:  string | null
  // The newest fix currently drawn — the one wearing the blue pin
  latestKey:    string | null
  // Whether the viewport has already been framed to the data
  didFit:       boolean
}

// ---- Helpers ----

function formatCoordinate(value: number): string {
  return value.toFixed(6)
}

// Treat timestamps without an explicit timezone as UTC (the API convention).
function formatTimestamp(value: string): string {
  const parsed = parseApiTimestamp(value)
  return parsed === null ? value : parsed.toLocaleString(undefined, { hour12: false })
}

// Builds the info-window body as DOM nodes rather than an HTML string.
//
// Two reasons. The window's content is styled through the app's own stylesheet
// (see .map-info-* in App.css) instead of inline style attributes, which the
// page's Content-Security-Policy would otherwise have to permit. And nothing is
// concatenated into markup, so there is no innerHTML sink here at all — the
// values are numbers and dates today, but the next field added might not be.
function buildInfoContent(position: PositionDto): HTMLElement {
  const container = document.createElement('div')
  container.className = 'map-info'

  const title = document.createElement('div')
  title.className = 'map-info-title'
  title.textContent = 'Position'
  container.appendChild(title)

  const appendRow = (label: string, value: string): void => {
    const labelNode = document.createElement('div')
    labelNode.className = 'map-info-label'
    labelNode.textContent = label
    container.appendChild(labelNode)

    const valueNode = document.createElement('div')
    valueNode.className = 'map-info-value'
    valueNode.textContent = value
    container.appendChild(valueNode)
  }

  appendRow('Latitude', formatCoordinate(position.latitude))
  appendRow('Longitude', formatCoordinate(position.longitude))
  appendRow('Speed', `${position.speedKmph.toFixed(1)} km/h`)
  appendRow('Altitude', `${Math.round(position.altitudeMeters)} m`)

  // Battery (0 = charging) and the raw ADXL345 sample — only when the device
  // sent them for this fix, so older fixes show no empty rows.
  if (position.batteryPct !== null) {
    appendRow('Battery', position.batteryPct === 0 ? 'Charging' : `${position.batteryPct}%`)
  }
  if (position.accelXG !== null || position.accelYG !== null || position.accelZG !== null) {
    const axis = (value: number | null): string => (value === null ? '—' : value.toFixed(2))
    appendRow('Accel X/Y/Z (g)', `${axis(position.accelXG)}, ${axis(position.accelYG)}, ${axis(position.accelZG)}`)
  }
  if (position.temperatureC !== null) {
    appendRow('Temperature', `${position.temperatureC.toFixed(1)} °C`)
  }

  const recorded = document.createElement('div')
  recorded.className = 'map-info-footer'
  recorded.textContent = `Recorded: ${formatTimestamp(position.timestamp)}`
  container.appendChild(recorded)

  return container
}

// ---- Script loader (singleton) ----

let googleMapsLoader: Promise<void> | null = null

function loadGoogleMaps(apiKey: string): Promise<void> {
  // Already loaded? Resolve immediately.
  const w = window as unknown as { google?: typeof google }
  if (w.google?.maps?.Map) {
    return Promise.resolve()
  }

  if (googleMapsLoader) {
    return googleMapsLoader
  }

  googleMapsLoader = new Promise<void>((resolve, reject) => {
    // Reuse an existing tag if one was injected in a previous (now-discarded)
    // module instance — avoids duplicate-load warnings during HMR.
    const existing = document.querySelector<HTMLScriptElement>(
      'script[data-google-maps-loader]',
    )

    const onReady = (script: HTMLScriptElement): void => {
      // After the script load event fires we still need to verify the API
      // actually populated `google.maps.Map`. If not, we treat it as a failure.
      const check = (): void => {
        if ((window as unknown as { google?: typeof google }).google?.maps?.Map) {
          resolve()
        } else {
          reject(new Error('Google Maps script loaded but google.maps.Map is missing.'))
        }
      }
      if ((window as unknown as { google?: typeof google }).google?.maps?.Map) {
        check()
      } else {
        script.addEventListener('load', check, { once: true })
      }
    }

    if (existing) {
      onReady(existing)
      return
    }

    const script = document.createElement('script')
    script.dataset.googleMapsLoader = 'true'
    script.async = true
    script.defer = true
    // `libraries=marker` ensures the marker classes ship in the main bundle.
    // No `loading=async` — that flag turns classes into lazy imports, which
    // breaks direct google.maps.X usage.
    script.src = `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(apiKey)}&v=weekly&libraries=marker`
    script.addEventListener('error', () => {
      reject(new Error('Failed to load Google Maps script.'))
    })
    script.addEventListener('load', () => {
      const ok = !!(window as unknown as { google?: typeof google }).google?.maps?.Map
      if (ok) {
        resolve()
      } else {
        reject(new Error('Google Maps script loaded but google.maps.Map is missing.'))
      }
    })
    document.head.appendChild(script)
  })

  return googleMapsLoader
}

// Classic teardrop pin shape — tip anchored at (0,0), body extends upward.
const PIN_PATH =
  'M 0,0 C -2,-20 -10,-22 -10,-30 A 10,10 0 1,1 10,-30 C 10,-22 2,-20 0,0 z'

// The newest fix is blue and slightly larger; everything older is red. Named
// here because a marker's pin is now re-assigned when the newest fix changes,
// not just chosen once when the marker is built.
const LATEST_PIN  = { color: '#0065BD', scale: 1.2 }
const HISTORY_PIN = { color: '#E31E24', scale: 1 }

function createPinSymbol(g: typeof google, color: string, scale: number): google.maps.Symbol {
  return {
    path:         PIN_PATH,
    fillColor:    color,
    fillOpacity:  1,
    strokeColor:  '#ffffff',
    strokeWeight: 2,
    scale,
    anchor: new g.maps.Point(0, 0),
  }
}

// ---- Overlay updater ----

// Identifies one fix. The timestamp plus the coordinates is enough to tell two
// fixes apart and is stable across reloads — which is the whole point: a marker
// whose key is still present in the new data is REUSED rather than destroyed and
// rebuilt, so an open info window survives a refresh.
function positionKey(position: PositionDto): string {
  return `${position.timestamp}|${position.latitude}|${position.longitude}`
}

// Frames every marker currently on the map. Called once on the first load that
// has data, and again whenever the user presses "Fit to positions" — nothing
// else moves the map. Reading the markers rather than a positions array keeps
// this callable from anywhere without threading the data through.
function fitToDrawnPositions(state: MapState): void {
  const g = (window as unknown as { google?: typeof google }).google
  if (!g?.maps || !state.map || state.markersByKey.size === 0) {
    return
  }

  const bounds = new g.maps.LatLngBounds()
  state.markersByKey.forEach((marker) => {
    // getPosition() is null for a marker that has been detached from the map;
    // one can be in the Map for the tick between setMap(null) and delete().
    const markerPosition = marker.getPosition()
    if (markerPosition) {
      bounds.extend(markerPosition)
    }
  })

  state.map.fitBounds(bounds)
  state.didFit = true
}

// Reconciles the markers and the track line against a freshly loaded set of
// positions, WITHOUT touching the viewport.
//
// This used to clear every overlay and call fitBounds on each load, which threw
// away the user's pan and zoom (and closed whatever info window they had open)
// every time the auto-refresh ticked. Now only what actually changed is touched.
function updateMapOverlays(state: MapState, positions: PositionDto[]): void {
  const g = (window as unknown as { google?: typeof google }).google
  if (!g?.maps || !state.map) {
    return
  }

  // Which fix is the newest? That one gets the blue pin.
  const latestKey: string | null =
    positions.length === 0
      ? null
      : positionKey(
          positions.reduce((latest, position) =>
            new Date(position.timestamp) >= new Date(latest.timestamp) ? position : latest,
          ),
        )

  const seen = new Set<string>()

  positions.forEach((position, index) => {
    const key: string = positionKey(position)

    // Two fixes at the same instant and the same spot share one marker
    if (seen.has(key)) {
      return
    }
    seen.add(key)

    const existing = state.markersByKey.get(key)
    if (existing) {
      // Already on the map: refresh the record behind it and leave it alone
      existing.set('carposPosition', position)
      return
    }

    const pin = key === latestKey ? LATEST_PIN : HISTORY_PIN
    const marker = new g.maps.Marker({
      position: { lat: position.latitude, lng: position.longitude },
      map:      state.map,
      title:    `Position ${index + 1}`,
      zIndex:   key === latestKey ? 10 : 1,
      icon:     createPinSymbol(g, pin.color, pin.scale),
    })

    // The listener is attached once, at creation, and reads the position back
    // off the marker — so a marker that outlives several reloads still shows the
    // current record rather than the one it was created with.
    marker.set('carposPosition', position)
    marker.addListener('click', () => {
      if (!state.infoWindow) {
        state.infoWindow = new g.maps.InfoWindow()
      }
      state.selectedKey = key
      state.infoWindow.setContent(buildInfoContent(marker.get('carposPosition')))
      state.infoWindow.open({ map: state.map, anchor: marker })
    })

    state.markersByKey.set(key, marker)
  })

  // Remove markers for fixes that are no longer in the loaded range. Deleting
  // from a Map while iterating it is well defined — an entry removed before it
  // is visited is simply skipped.
  state.markersByKey.forEach((marker, key) => {
    if (seen.has(key)) {
      return
    }

    marker.setMap(null)
    state.markersByKey.delete(key)

    // The open info window was anchored to a marker that is going away
    if (state.selectedKey === key) {
      if (state.infoWindow) {
        state.infoWindow.close()
      }
      state.selectedKey = null
    }
  })

  // Re-paint only the two markers whose highlight actually changed
  if (state.latestKey !== latestKey) {
    const previous = state.latestKey === null ? undefined : state.markersByKey.get(state.latestKey)
    if (previous) {
      previous.setIcon(createPinSymbol(g, HISTORY_PIN.color, HISTORY_PIN.scale))
      previous.setZIndex(1)
    }

    const current = latestKey === null ? undefined : state.markersByKey.get(latestKey)
    if (current) {
      current.setIcon(createPinSymbol(g, LATEST_PIN.color, LATEST_PIN.scale))
      current.setZIndex(10)
    }

    state.latestKey = latestKey
  }

  if (positions.length === 0) {
    if (state.polyline) {
      state.polyline.setMap(null)
      state.polyline = null
    }
    return
  }

  // The track: move the existing line rather than replacing the object, so
  // there is nothing for the map to re-render from scratch.
  const path = positions.map((position) => ({ lat: position.latitude, lng: position.longitude }))
  if (state.polyline) {
    state.polyline.setPath(path)
  } else {
    state.polyline = new g.maps.Polyline({
      map:           state.map,
      path,
      strokeColor:   '#0065BD',
      strokeOpacity: 0.85,
      strokeWeight:  4,
    })
  }

  // First load with data: frame the track once. From then on the viewport
  // belongs to the user.
  if (!state.didFit) {
    fitToDrawnPositions(state)
  }
}

// ---- React component ----

function DeviceMap({ positions, apiKey, fitToken }: DeviceMapProps) {
  const mapContainerRef           = useRef<HTMLDivElement | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const mapState = useMemo<MapState>(
    () => ({
      map:          null,
      markersByKey: new Map<string, google.maps.Marker>(),
      polyline:     null,
      infoWindow:   null,
      selectedKey:  null,
      latestKey:    null,
      didFit:       false,
    }),
    [],
  )

  useEffect(() => {
    if (!apiKey) {
      return
    }

    let canceled = false

    loadGoogleMaps(apiKey)
      .then(() => {
        if (canceled || !mapContainerRef.current) {
          return
        }

        const g = (window as unknown as { google?: typeof google }).google
        if (!g?.maps?.Map) {
          return
        }

        if (!mapState.map) {
          mapState.map = new g.maps.Map(mapContainerRef.current, {
            center:            { lat: 50.0755, lng: 14.4378 }, // Prague — default fallback
            zoom:              10,
            mapTypeControl:    false,
            streetViewControl: false,
            fullscreenControl: true,
          })
        }

        updateMapOverlays(mapState, positions)
      })
      .catch((error: Error) => {
        // Surfaced in the placeholder below — a failed map load is something
        // the user needs told, not something to bury in the console.
        setLoadError(error.message)
      })

    return () => {
      canceled = true
    }
  }, [apiKey, mapState, positions])

  // "Fit to positions" — the only thing that moves the viewport after the first
  // load. fitToken starts at 0, meaning the user has not asked yet. Positions
  // are deliberately not a dependency: a reload must never re-frame the map.
  useEffect(() => {
    if (fitToken > 0) {
      fitToDrawnPositions(mapState)
    }
  }, [fitToken, mapState])

  if (!apiKey) {
    return (
      <div className="map-placeholder">
        <span style={{ fontSize: '2rem' }} aria-hidden="true">🗺</span>
        <p>
          Map not configured.{' '}
          <br />
          Set <code>CARPOS_GOOGLE_MAPS_API_KEY</code> on the frontend container
          to enable it.
        </p>
      </div>
    )
  }

  if (loadError) {
    return (
      <div className="map-placeholder">
        <span style={{ fontSize: '2rem' }} aria-hidden="true">⚠️</span>
        <p>Could not load Google Maps: {loadError}</p>
      </div>
    )
  }

  return (
    <div className="map-canvas" ref={mapContainerRef} aria-label="Device position map" />
  )
}

export default DeviceMap
