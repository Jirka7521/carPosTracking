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
//   • The map auto-fits its bounds to show all positions
// ============================================================

import { useEffect, useMemo, useRef, useState } from 'react'
import type { PositionDto } from '../services/apiTypes'
import { parseApiTimestamp } from '../utils/dates'

type DeviceMapProps = {
  positions: PositionDto[]
  apiKey: string
}

// Live Google Maps objects for the current map instance.
type MapState = {
  map:        any | null
  markers:    any[]
  polyline:   any | null
  infoWindow: any | null
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
  const w = window as { google?: any }
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
        if ((window as { google?: any }).google?.maps?.Map) {
          resolve()
        } else {
          reject(new Error('Google Maps script loaded but google.maps.Map is missing.'))
        }
      }
      if ((window as { google?: any }).google?.maps?.Map) {
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
      const ok = !!(window as { google?: any }).google?.maps?.Map
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

function createPinSymbol(g: any, color: string, scale: number) {
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

function updateMapOverlays(state: MapState, positions: PositionDto[]): void {
  const g = (window as { google?: any }).google
  if (!g?.maps || !state.map) {
    return
  }

  state.markers.forEach((m) => m.setMap(null))
  state.markers = []

  if (state.polyline) {
    state.polyline.setMap(null)
    state.polyline = null
  }

  if (state.infoWindow) {
    state.infoWindow.close()
  }

  if (positions.length === 0) {
    return
  }

  if (!state.infoWindow) {
    state.infoWindow = new g.maps.InfoWindow()
  }

  const bounds = new g.maps.LatLngBounds()

  // Find the index of the position with the latest timestamp
  const latestIndex = positions.reduce(
    (maxIdx, p, i) =>
      new Date(p.timestamp) >= new Date(positions[maxIdx].timestamp) ? i : maxIdx,
    0,
  )

  state.markers = positions.map((position, index) => {
    const point    = { lat: position.latitude, lng: position.longitude }
    const isLatest = index === latestIndex
    bounds.extend(point)

    const marker = new g.maps.Marker({
      position: point,
      map:      state.map,
      title:    `Position ${index + 1}`,
      zIndex:   isLatest ? 10 : 1,
      icon: createPinSymbol(g, isLatest ? '#0065BD' : '#E31E24', isLatest ? 1.2 : 1),
    })

    marker.addListener('click', () => {
      if (!state.infoWindow) {
        return
      }
      state.infoWindow.setContent(buildInfoContent(position))
      state.infoWindow.open({ map: state.map, anchor: marker })
    })

    return marker
  })

  state.polyline = new g.maps.Polyline({
    map:           state.map,
    path:          positions.map((p) => ({ lat: p.latitude, lng: p.longitude })),
    strokeColor:   '#0065BD',
    strokeOpacity: 0.85,
    strokeWeight:  4,
  })

  state.map.fitBounds(bounds)
}

// ---- React component ----

function DeviceMap({ positions, apiKey }: DeviceMapProps) {
  const mapContainerRef           = useRef<HTMLDivElement | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const mapState = useMemo<MapState>(
    () => ({ map: null, markers: [], polyline: null, infoWindow: null }),
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

        const g = (window as { google?: any }).google
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
