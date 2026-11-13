// ============================================================
// LocationPickerMap — pick a point and a radius on Google Maps, for a
// location rule ("trips that start or end here join this journey").
//
//   • Clicking the map puts the circle's centre there.
//   • The circle is editable: drag its centre or the handle on its edge.
//   • `points` — the trip starts and ends the rule would catch, from the
//     server's preview — are drawn as small dots, so the radius can be tuned
//     by eye. Green dots are starts, blue ones ends.
//
// Behind the same consent gate as every other map (MapsConsentPrompt): nothing
// is requested from Google until this viewer has agreed. The parent must not
// mount it before the account's standing answer is known (see
// hooks/useAccountMapsConsent), because it reads that answer once, at mount.
//
// Controlled: the centre and radius live in the parent (which also offers
// latitude/longitude/radius inputs, so the rule can be made without a map).
// The map only moves its viewport by itself the first time a centre exists, and
// when the centre is changed from outside the map (typed in the inputs).
// ============================================================

import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { LocationRulePreviewPointDto } from '../services/apiTypes'
import { googleApi, loadGoogleMaps } from '../utils/googleMapsLoader'
import { MapsConsentPrompt } from './MapsConsentPrompt'

export type PickedPoint = {
  latitude: number
  longitude: number
}

export type LocationPickerMapProps = {
  apiKey: string
  // The circle's centre, or null before one has been picked.
  center: PickedPoint | null
  radiusMeters: number
  minRadiusMeters: number
  maxRadiusMeters: number
  // Where to look before a centre is picked — the car's latest fix, say.
  initialView: PickedPoint | null
  points: readonly LocationRulePreviewPointDto[]
  onChange: (center: PickedPoint, radiusMeters: number) => void
  hasStandingConsent: boolean
  onGrantStandingConsent: () => void
}

// Prague, the app's default when nothing better is known (as in DeviceMap).
const DEFAULT_VIEW: PickedPoint = { latitude: 50.0755, longitude: 14.4378 }
const DEFAULT_ZOOM = 12

const CIRCLE_COLOR = '#0065BD'
const START_DOT_COLOR = '#1E8E3E'
const END_DOT_COLOR = '#0065BD'

// Six decimals is about 10 cm — finer than any GNSS fix, coarser than float noise.
const COORDINATE_DECIMALS = 6

type PickerState = {
  map: google.maps.Map | null
  circle: google.maps.Circle | null
  dots: google.maps.Marker[]
  // Set while the component itself moves the circle, so the circle's own
  // change events are not reported back as the user's edit.
  isSyncing: boolean
  // The centre this map last reported, to tell its own edits from outside ones.
  lastReported: PickedPoint | null
  radiusMeters: number
  minRadiusMeters: number
  maxRadiusMeters: number
  onChange: (center: PickedPoint, radiusMeters: number) => void
}

function roundCoordinate(value: number): number {
  const factor: number = 10 ** COORDINATE_DECIMALS
  return Math.round(value * factor) / factor
}

function samePoint(left: PickedPoint | null, right: PickedPoint | null): boolean {
  return left !== null
    && right !== null
    && roundCoordinate(left.latitude) === roundCoordinate(right.latitude)
    && roundCoordinate(left.longitude) === roundCoordinate(right.longitude)
}

// Reports a centre and radius the user set on the map, clamped and rounded the
// way the form keeps them.
function report(state: PickerState, latLng: google.maps.LatLng, radiusMeters: number): void {
  const center: PickedPoint = {
    latitude: roundCoordinate(latLng.lat()),
    longitude: roundCoordinate(latLng.lng()),
  }
  const radius: number = Math.min(
    state.maxRadiusMeters,
    Math.max(state.minRadiusMeters, Math.round(radiusMeters)),
  )
  state.lastReported = center
  state.radiusMeters = radius
  state.onChange(center, radius)
}

// Creates the circle on first use and keeps it in step with the props.
function syncCircle(state: PickerState, center: PickedPoint | null, radiusMeters: number): void {
  const g = googleApi()
  if (!g?.maps || !state.map) {
    return
  }

  if (center === null) {
    state.circle?.setMap(null)
    state.circle = null
    return
  }

  const position = { lat: center.latitude, lng: center.longitude }
  const isOutsideChange: boolean = !samePoint(center, state.lastReported)

  state.isSyncing = true
  try {
    if (state.circle === null) {
      const circle = new g.maps.Circle({
        map: state.map,
        center: position,
        radius: radiusMeters,
        editable: true,
        draggable: true,
        // Clicks inside the circle fall through to the map, which moves it there.
        clickable: false,
        strokeColor: CIRCLE_COLOR,
        strokeOpacity: 0.9,
        strokeWeight: 2,
        fillColor: CIRCLE_COLOR,
        fillOpacity: 0.12,
      })

      circle.addListener('center_changed', () => {
        const moved = circle.getCenter()
        if (!state.isSyncing && moved) {
          report(state, moved, circle.getRadius())
        }
      })
      circle.addListener('radius_changed', () => {
        const centre = circle.getCenter()
        if (!state.isSyncing && centre) {
          report(state, centre, circle.getRadius())
        }
      })

      state.circle = circle
      const bounds = circle.getBounds()
      if (bounds) {
        state.map.fitBounds(bounds)
      }
    } else {
      const current = state.circle.getCenter()
      if (!current || !samePoint({ latitude: current.lat(), longitude: current.lng() }, center)) {
        state.circle.setCenter(position)
      }
      if (Math.round(state.circle.getRadius()) !== radiusMeters) {
        state.circle.setRadius(radiusMeters)
      }
      // A centre typed into the form: bring it into view.
      if (isOutsideChange) {
        const bounds = state.circle.getBounds()
        if (bounds) {
          state.map.fitBounds(bounds)
        }
      }
    }
  } finally {
    state.isSyncing = false
  }

  state.lastReported = center
  state.radiusMeters = radiusMeters
}

// Replaces the dots of matched trip starts and ends.
function syncDots(state: PickerState, points: readonly LocationRulePreviewPointDto[], titles: { start: string; end: string }): void {
  const g = googleApi()
  if (!g?.maps || !state.map) {
    return
  }

  state.dots.forEach((dot) => dot.setMap(null))
  state.dots = points.map((point) => new g.maps.Marker({
    map: state.map,
    position: { lat: point.latitude, lng: point.longitude },
    clickable: false,
    title: point.kind === 'start' ? titles.start : titles.end,
    zIndex: point.kind === 'end' ? 2 : 1,
    icon: {
      path: g.maps.SymbolPath.CIRCLE,
      scale: 4,
      fillColor: point.kind === 'start' ? START_DOT_COLOR : END_DOT_COLOR,
      fillOpacity: 0.9,
      strokeColor: '#ffffff',
      strokeWeight: 1,
    },
  }))
}

export function LocationPickerMap({
  apiKey,
  center,
  radiusMeters,
  minRadiusMeters,
  maxRadiusMeters,
  initialView,
  points,
  onChange,
  hasStandingConsent,
  onGrantStandingConsent,
}: LocationPickerMapProps) {
  const { t } = useTranslation(['trips', 'device'])
  const containerRef = useRef<HTMLDivElement | null>(null)
  const [hasConsented, setHasConsented] = useState<boolean>(hasStandingConsent)
  const [isReady, setIsReady] = useState<boolean>(false)
  const [loadError, setLoadError] = useState<string | null>(null)

  const state = useMemo<PickerState>(
    () => ({
      map: null,
      circle: null,
      dots: [],
      isSyncing: false,
      lastReported: null,
      radiusMeters,
      minRadiusMeters,
      maxRadiusMeters,
      onChange,
    }),
    // Created once; the effect below keeps the mutable fields current.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  )

  // The latest callback and limits, for listeners that live outside React.
  useEffect(() => {
    updateCallbacks(state, onChange, minRadiusMeters, maxRadiusMeters)
  }, [state, onChange, minRadiusMeters, maxRadiusMeters])

  // Load the API and create the map, once consent is given.
  useEffect(() => {
    if (!apiKey || !hasConsented) {
      return
    }

    let canceled = false

    loadGoogleMaps(apiKey)
      .then(() => {
        const g = googleApi()
        if (canceled || !containerRef.current || !g?.maps?.Map) {
          return
        }

        if (!state.map) {
          const view: PickedPoint = center ?? initialView ?? DEFAULT_VIEW
          const map = new g.maps.Map(containerRef.current, {
            center: { lat: view.latitude, lng: view.longitude },
            zoom: DEFAULT_ZOOM,
            mapTypeControl: false,
            streetViewControl: false,
            fullscreenControl: true,
            clickableIcons: false,
            draggableCursor: 'crosshair',
          })
          map.addListener('click', (event: google.maps.MapMouseEvent) => {
            if (event.latLng) {
              report(state, event.latLng, state.radiusMeters)
            }
          })
          setMap(state, map)
        }

        setIsReady(true)
      })
      .catch((error: Error) => {
        setLoadError(error.message)
      })

    return () => {
      canceled = true
    }
    // The initial view is read once, when the map is first created.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiKey, hasConsented, state])

  // Keep the circle in step with the form.
  useEffect(() => {
    if (isReady) {
      syncCircle(state, center, radiusMeters)
    }
  }, [isReady, state, center, radiusMeters])

  // Redraw the matched trips' dots whenever a fresh preview arrives.
  const startTitle: string = t('trips:locationRule.map.startDot')
  const endTitle: string = t('trips:locationRule.map.endDot')
  useEffect(() => {
    if (isReady) {
      syncDots(state, points, { start: startTitle, end: endTitle })
    }
  }, [isReady, state, points, startTitle, endTitle])

  // Tear the overlays down with the component.
  useEffect(() => () => clearOverlays(state), [state])

  if (!hasConsented) {
    return (
      <MapsConsentPrompt
        onOnce={() => setHasConsented(true)}
        onAlways={() => {
          // As on the Map tab: the map loads now whatever happens to the save.
          onGrantStandingConsent()
          setHasConsented(true)
        }}
        consentScope="account"
      />
    )
  }

  if (loadError) {
    return (
      <div className="map-placeholder">
        <span style={{ fontSize: '2rem' }} aria-hidden="true">⚠️</span>
        <p>{t('device:map.loadFailed', { error: loadError })}</p>
      </div>
    )
  }

  return (
    <div
      className="map-canvas location-picker-map"
      ref={containerRef}
      aria-label={t('trips:locationRule.map.label')}
    />
  )
}

// Module functions rather than inline in the effects: the state object is
// mutated, and the hooks lint rightly refuses that inside a hook's own body.
function updateCallbacks(
  state: PickerState,
  onChange: (center: PickedPoint, radiusMeters: number) => void,
  minRadiusMeters: number,
  maxRadiusMeters: number,
): void {
  state.onChange = onChange
  state.minRadiusMeters = minRadiusMeters
  state.maxRadiusMeters = maxRadiusMeters
}

function setMap(state: PickerState, map: google.maps.Map): void {
  state.map = map
}

function clearOverlays(state: PickerState): void {
  state.dots.forEach((dot) => dot.setMap(null))
  state.dots = []
  state.circle?.setMap(null)
  state.circle = null
}
