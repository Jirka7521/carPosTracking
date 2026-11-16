// ============================================================
// DeviceMap — Google Maps integration for rendering GPS positions.
//
// Loading strategy: utils/googleMapsLoader injects the Maps JS API once per
// page, and only after the viewer passed the consent gate
// (components/MapsConsentPrompt).
//
// Rendering:
//   • ONE pin, on the latest position (newest timestamp). It is blue, and red
//     while the caller says it is overdue (`isOverdue`, from hooks/useIsOverdue
//     — the same answer the caller's legend shows) — the tracker should have
//     reported again by then. Older positions get no pin of their own: a day's
//     track used to bury the map under hundreds of them.
//   • A red polyline connects the positions in chronological order — red
//     whatever the pin's colour, so the route always reads as the route
//   • Clicking the pin, or anywhere on the line, opens an info window with the
//     nearest recorded fix's coordinates, readings and timestamp
//   • The map frames all positions on the FIRST load that has data and never
//     moves itself again — reloads reconcile the overlays in place, so the
//     user's pan, zoom and open info window survive. `fitToken` is the caller's
//     way to ask for a re-frame ("Fit to positions").
// ============================================================

import { useEffect, useMemo, useRef, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import i18n from '../i18n'
import { formatCoordinate, formatDateTime, formatInteger, formatNumber } from '../i18n/format'
import { parseApiTimestamp } from '../utils/dates'
import { loadGoogleMaps } from '../utils/googleMapsLoader'
import { MapsConsentPrompt } from './MapsConsentPrompt'

// What this map needs from a fix, which is less than a PositionDto carries.
//
// The narrower type exists so the public share page can use this component
// without either fabricating fields it is not allowed to know or duplicating
// five hundred lines of Google Maps plumbing. A share deliberately withholds
// altitude and the accelerometer — they answer nothing a person asking "where is
// it" is asking — so those are optional here and their rows are simply omitted.
// PositionDto satisfies this shape as it stands.
export type MapPosition = {
  timestamp: string
  latitude: number
  longitude: number
  speedKmph: number | null
  batteryPct: number | null
  altitudeMeters?: number | null
  accelXG?: number | null
  accelYG?: number | null
  accelZG?: number | null
}

type DeviceMapProps = {
  positions: MapPosition[]
  apiKey: string
  // Bumped by the parent to re-frame the map on all positions. Starts at 0,
  // meaning "never asked" — the initial framing is handled internally.
  fitToken: number
  // Whether the latest position is overdue right now, which paints its pin red.
  // Absent keeps the pin blue — a trip's end. The caller works it out with
  // hooks/useIsOverdue, so its legend and this pin always agree.
  isOverdue?: boolean
  // Whether this viewer already said "always". Read once, at mount — so the
  // caller must know the answer before it renders this component.
  hasStandingConsent: boolean
  // "Always load maps" was pressed. The caller remembers it where it belongs:
  // on the account, or in the share page's cookie.
  onGrantStandingConsent: () => void
  // Where "always" is remembered, which decides what the prompt promises about
  // it: every device on the account, or this browser only.
  consentScope: 'account' | 'browser'
}

// Live Google Maps objects for the current map instance.
type MapState = {
  map:          google.maps.Map | null
  // The one pin, on the newest fix. Moved rather than rebuilt when a newer fix
  // arrives, so an info window open on it stays open.
  latestMarker: google.maps.Marker | null
  // The newest fix currently drawn — the one wearing the pin
  latestKey:    string | null
  // Whether the pin is painted red, or null before it is painted at all —
  // so a repaint only touches the marker when the colour actually changes
  pinIsRed:     boolean | null
  // Whether the pin should be red. Mirrors the prop, so code running outside
  // React (a reload's overlay update) can read it.
  isOverdue:    boolean
  polyline:     google.maps.Polyline | null
  // The fixes behind the line, for finding the one nearest a click and for
  // framing the map
  positions:    MapPosition[]
  infoWindow:   google.maps.InfoWindow | null
  // Which fix the open info window shows, so it can be closed if that fix falls
  // out of the range on a later load
  selectedKey:  string | null
  // Whether that window hangs off the pin (and so follows it to a newer fix)
  // rather than off a point on the line
  selectedOnPin: boolean
  // Whether the viewport has already been framed to the data
  didFit:       boolean
}

// ---- Helpers ----

// Treat timestamps without an explicit timezone as UTC (the API convention).
function formatTimestamp(value: string): string {
  const parsed = parseApiTimestamp(value)
  return parsed === null ? value : formatDateTime(parsed)
}

// Builds the info-window body as DOM nodes rather than an HTML string.
//
// It runs outside React — Google Maps owns this subtree — so its strings come
// from the i18next singleton. The window is rebuilt from scratch on every
// marker click, so a language switch is picked up the next time one is opened.
//
// Two reasons. The window's content is styled through the app's own stylesheet
// (see .map-info-* in App.css) instead of inline style attributes, which the
// page's Content-Security-Policy would otherwise have to permit. And nothing is
// concatenated into markup, so there is no innerHTML sink here at all — the
// values are numbers and dates today, but the next field added might not be.
function buildInfoContent(position: MapPosition): HTMLElement {
  const container = document.createElement('div')
  container.className = 'map-info'

  const title = document.createElement('div')
  title.className = 'map-info-title'
  title.textContent = i18n.t('device:map.info.title')
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

  appendRow(i18n.t('device:map.info.latitude'), formatCoordinate(position.latitude))
  appendRow(i18n.t('device:map.info.longitude'), formatCoordinate(position.longitude))
  // Speed and altitude are always present on a device fix and may both be absent
  // on a shared one, where the creator decides what a visitor is told.
  if (position.speedKmph !== null && position.speedKmph !== undefined) {
    appendRow(i18n.t('device:map.info.speed'), `${formatNumber(position.speedKmph, 1)} km/h`)
  }
  if (position.altitudeMeters !== null && position.altitudeMeters !== undefined) {
    appendRow(i18n.t('device:map.info.altitude'), `${formatInteger(Math.round(position.altitudeMeters))} m`)
  }

  // Battery (0 = charging) and the raw ADXL345 sample — only when the device
  // sent them for this fix, so older fixes show no empty rows.
  if (position.batteryPct !== null) {
    appendRow(
      i18n.t('device:map.info.battery'),
      position.batteryPct === 0
        ? i18n.t('common:battery.charging')
        : i18n.t('common:battery.percent', { value: position.batteryPct }),
    )
  }
  if (
    (position.accelXG ?? null) !== null
    || (position.accelYG ?? null) !== null
    || (position.accelZG ?? null) !== null
  ) {
    const axis = (value: number | null | undefined): string =>
      value === null || value === undefined ? i18n.t('common:states.none') : formatNumber(value, 2)
    appendRow(
      i18n.t('device:map.info.accel'),
      `${axis(position.accelXG)}, ${axis(position.accelYG)}, ${axis(position.accelZG)}`,
    )
  }

  const recorded = document.createElement('div')
  recorded.className = 'map-info-footer'
  recorded.textContent = i18n.t('device:map.info.recorded', {
    when: formatTimestamp(position.timestamp),
  })
  container.appendChild(recorded)

  return container
}

// Classic teardrop pin shape — tip anchored at (0,0), body extends upward.
const PIN_PATH =
  'M 0,0 C -2,-20 -10,-22 -10,-30 A 10,10 0 1,1 10,-30 C 10,-22 2,-20 0,0 z'

// The one pin is blue while the latest fix is current and red once it is
// overdue. Slightly larger than Google's own pins so it reads as the subject.
const LATEST_PIN  = { color: '#0065BD', scale: 1.2 }
const OVERDUE_PIN = { color: '#E31E24', scale: 1.2 }

// The route is always red, the same red the older positions' pins used to be.
// An overdue pin shares the colour but stays readable on it: it is larger than
// the line is wide, and outlined in white.
const TRACK_COLOR = '#E31E24'

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
// fixes apart and is stable across reloads — which is what lets an info window
// survive a refresh as long as the fix it shows is still loaded.
function positionKey(position: MapPosition): string {
  return `${position.timestamp}|${position.latitude}|${position.longitude}`
}

// Frames every loaded fix. Called once on the first load that has data, and
// again whenever the user presses "Fit to positions" — nothing else moves the
// map. It reads the fixes kept on the state (not the single pin), so the whole
// track is framed, and it stays callable without threading the data through.
function fitToDrawnPositions(state: MapState): void {
  const g = (window as unknown as { google?: typeof google }).google
  if (!g?.maps || !state.map || state.positions.length === 0) {
    return
  }

  const bounds = new g.maps.LatLngBounds()
  state.positions.forEach((position) => {
    bounds.extend({ lat: position.latitude, lng: position.longitude })
  })

  state.map.fitBounds(bounds)
  state.didFit = true
}

// The fix closest to a clicked point of the line. Flat-earth distance with the
// longitude scaled by cos(latitude) — exact enough at the scale of one track to
// pick the right neighbour, and cheap enough to scan fifty thousand fixes on a
// click.
function nearestPosition(positions: MapPosition[], lat: number, lng: number): MapPosition | null {
  const lngScale: number = Math.cos((lat * Math.PI) / 180)
  let nearest: MapPosition | null = null
  let nearestDistance: number = Number.POSITIVE_INFINITY

  for (const position of positions) {
    const dLat: number = position.latitude - lat
    const dLng: number = (position.longitude - lng) * lngScale
    const distance: number = dLat * dLat + dLng * dLng
    if (distance < nearestDistance) {
      nearest = position
      nearestDistance = distance
    }
  }

  return nearest
}

function closeInfoWindow(state: MapState): void {
  if (state.infoWindow) {
    state.infoWindow.close()
  }
  state.infoWindow = null
  state.selectedKey = null
  state.selectedOnPin = false
}

// Opens the details of one fix — hanging off the pin when it is the latest one,
// otherwise at the fix's own spot on the line. A fresh window each time, because
// one that has been anchored to a marker does not reliably let go of it.
function openInfoWindow(
  g: typeof google,
  state: MapState,
  position: MapPosition,
  anchor: google.maps.Marker | null,
): void {
  closeInfoWindow(state)

  const infoWindow = new g.maps.InfoWindow({ content: buildInfoContent(position) })
  infoWindow.addListener('closeclick', () => {
    // Only if it is still the current one — a replaced window's late event must
    // not clear the selection of the window that replaced it.
    if (state.infoWindow === infoWindow) {
      state.infoWindow = null
      state.selectedKey = null
      state.selectedOnPin = false
    }
  })

  if (anchor) {
    infoWindow.open({ map: state.map, anchor })
  } else {
    infoWindow.setPosition({ lat: position.latitude, lng: position.longitude })
    infoWindow.open({ map: state.map })
  }

  state.infoWindow = infoWindow
  state.selectedKey = positionKey(position)
  state.selectedOnPin = anchor !== null
}

// Paints the pin red while it is overdue, blue otherwise. Called after every
// load and whenever the caller's answer flips.
function paintLatestPin(state: MapState): void {
  const g = (window as unknown as { google?: typeof google }).google
  if (!g?.maps || !state.latestMarker) {
    return
  }

  const isRed: boolean = state.isOverdue
  if (state.pinIsRed === isRed) {
    return
  }

  const pin = isRed ? OVERDUE_PIN : LATEST_PIN
  state.latestMarker.setIcon(createPinSymbol(g, pin.color, pin.scale))
  // The marker lives outside React, so its tooltip comes from the i18next
  // singleton, like the info window's text.
  state.latestMarker.setTitle(isRed ? i18n.t('device:map.overdueTitle') : i18n.t('device:map.latestTitle'))
  state.pinIsRed = isRed
}

// Records the caller's answer on the state and repaints the pin for it. A module
// function rather than inline in the effect, because the state object is
// mutated and the hooks lint rightly refuses that inside a hook's own body.
function setPinOverdue(state: MapState, isOverdue: boolean): void {
  state.isOverdue = isOverdue
  paintLatestPin(state)
}

// Reconciles the pin and the track line against a freshly loaded set of
// positions, WITHOUT touching the viewport.
//
// This used to clear every overlay and call fitBounds on each load, which threw
// away the user's pan and zoom (and closed whatever info window they had open)
// every time the auto-refresh ticked. Now only what actually changed is touched.
function updateMapOverlays(state: MapState, positions: MapPosition[]): void {
  const g = (window as unknown as { google?: typeof google }).google
  if (!g?.maps || !state.map) {
    return
  }

  state.positions = positions

  // Which fix is the newest? That one gets the pin.
  const latest: MapPosition | null =
    positions.length === 0
      ? null
      : positions.reduce((newest, position) =>
          new Date(position.timestamp) >= new Date(newest.timestamp) ? position : newest,
        )
  const latestKey: string | null = latest === null ? null : positionKey(latest)

  // The open info window shows a fix that is no longer in the loaded range
  if (state.selectedKey !== null) {
    const selectedKey: string = state.selectedKey
    if (!positions.some((position) => positionKey(position) === selectedKey)) {
      closeInfoWindow(state)
    }
  }

  if (latest === null) {
    if (state.latestMarker) {
      state.latestMarker.setMap(null)
      state.latestMarker = null
      state.pinIsRed = null
    }
  } else if (state.latestMarker) {
    // A newer fix: move the pin rather than rebuild it. A window open on the pin
    // moves with it and is told about the fix it now stands on.
    state.latestMarker.setPosition({ lat: latest.latitude, lng: latest.longitude })
    state.latestMarker.set('carposPosition', latest)
    if (state.selectedOnPin && state.latestKey !== latestKey && state.infoWindow) {
      state.infoWindow.setContent(buildInfoContent(latest))
      state.selectedKey = latestKey
    }
  } else {
    const marker = new g.maps.Marker({
      position: { lat: latest.latitude, lng: latest.longitude },
      map:      state.map,
      zIndex:   10,
    })

    // The listener is attached once, at creation, and reads the position back
    // off the marker — so a pin that has moved through several reloads still
    // shows the fix it stands on now rather than the one it was created with.
    marker.set('carposPosition', latest)
    marker.addListener('click', () => {
      openInfoWindow(g, state, marker.get('carposPosition'), marker)
    })

    state.latestMarker = marker
    state.pinIsRed = null
  }

  state.latestKey = latestKey
  paintLatestPin(state)

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
    const polyline = new g.maps.Polyline({
      map:           state.map,
      path,
      strokeColor:   TRACK_COLOR,
      strokeOpacity: 0.85,
      strokeWeight:  4,
      clickable:     true,
    })

    // The older fixes have no pins to click any more, so the line stands in for
    // them: a click shows the recorded fix nearest to where it landed. Attached
    // once and reads the fixes off the state, so it always searches the current
    // load.
    polyline.addListener('click', (event: google.maps.PolyMouseEvent) => {
      if (!event.latLng) {
        return
      }
      const nearest = nearestPosition(state.positions, event.latLng.lat(), event.latLng.lng())
      if (nearest === null) {
        return
      }
      const isLatest: boolean = positionKey(nearest) === state.latestKey
      openInfoWindow(g, state, nearest, isLatest ? state.latestMarker : null)
    })

    state.polyline = polyline
  }

  // First load with data: frame the track once. From then on the viewport
  // belongs to the user.
  if (!state.didFit) {
    fitToDrawnPositions(state)
  }
}

// ---- React component ----

function DeviceMap({
  positions,
  apiKey,
  fitToken,
  isOverdue = false,
  hasStandingConsent,
  onGrantStandingConsent,
  consentScope,
}: DeviceMapProps) {
  const { t } = useTranslation('device')

  const mapContainerRef           = useRef<HTMLDivElement | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  // Has this viewer agreed to contact Google at all? Seeded from the standing
  // consent the caller holds; the "just this once" button flips it for this tab
  // only.
  //
  // Everything below hangs off this flag: the loader effect returns early while
  // it is false, so opening a device's map tab makes no request to
  // maps.googleapis.com until somebody chooses. That is the whole gate.
  const [hasConsented, setHasConsented] = useState<boolean>(hasStandingConsent)

  const mapState = useMemo<MapState>(
    () => ({
      map:           null,
      latestMarker:  null,
      latestKey:     null,
      pinIsRed:      null,
      isOverdue:     false,
      polyline:      null,
      positions:     [],
      infoWindow:    null,
      selectedKey:   null,
      selectedOnPin: false,
      didFit:        false,
    }),
    [],
  )

  // Repaints the pin whenever the caller's answer flips — its hook times that, so
  // a tracker that goes quiet turns red without a reload. Declared before the
  // loader so the answer is on the state before the first overlay update reads it.
  useEffect(() => setPinOverdue(mapState, isOverdue), [mapState, isOverdue])

  useEffect(() => {
    if (!apiKey || !hasConsented) {
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
  }, [apiKey, hasConsented, mapState, positions])

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
          <Trans i18nKey="map.notConfigured" ns="device" components={{ br: <br />, code: <code /> }} />
        </p>
      </div>
    )
  }

  // The gate. Nothing has been sent to Google at this point.
  if (!hasConsented) {
    return (
      <MapsConsentPrompt
        onOnce={() => setHasConsented(true)}
        onAlways={() => {
          // The map loads now whatever happens to the save: the consent given
          // in this moment is real, and only remembering it can fail.
          onGrantStandingConsent()
          setHasConsented(true)
        }}
        consentScope={consentScope}
      />
    )
  }

  if (loadError) {
    return (
      <div className="map-placeholder">
        <span style={{ fontSize: '2rem' }} aria-hidden="true">⚠️</span>
        <p>{t('map.loadFailed', { error: loadError })}</p>
      </div>
    )
  }

  return (
    <div className="map-canvas" ref={mapContainerRef} aria-label={t('map.canvasLabel')} />
  )
}

export default DeviceMap
