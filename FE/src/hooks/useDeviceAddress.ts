// ============================================================
// useDeviceAddress — the address of a device's last fix, for the line under its
// name on the Home card and in the device header.
//
// Google reverse geocoding (Geocoding API v3, through the Maps JS Geocoder) in
// the viewer's browser. It sends Google the fix's exact position, so it runs
// only on a STANDING maps consent — the caller passes `enabled` from
// useAccountMapsConsent().isStanding; there is no map on those pages to agree
// "once" to — and only when a Maps key is configured.
//
// Each lookup is billed, so they are rationed per device:
//   • a lookup is repeated only once the car has moved more than 100 m from the
//     position last looked up, and then no sooner than 60 s after it — the
//     page's refresh ticks and a parked car cost nothing;
//   • a failed or empty answer is retried after 10 minutes, not on every tick;
//   • concurrent requests for one device share one lookup.
//
// The answers live in this module's memory only, for as long as the page is
// open: Google's terms do not allow storing them, so never localStorage, never
// the server. Nothing here logs a coordinate.
// ============================================================

import { useEffect, useState, useSyncExternalStore } from 'react'
import { useTranslation } from 'react-i18next'
import type { DeviceLastPositionDto } from '../services/apiTypes'
import { hasGoogleMapsKey, runtimeConfig } from '../services/runtimeConfig'
import { describeAddress } from '../utils/addressLabel'
import type { DeviceAddressText } from '../utils/addressLabel'
import { loadGeocodingLibrary } from '../utils/googleMapsLoader'

const MIN_MOVE_METERS: number = 100
const MIN_INTERVAL_MS: number = 60_000
const FAILURE_RETRY_MS: number = 10 * 60_000
const EARTH_RADIUS_METERS: number = 6_371_000

type CachedAddress = {
  // The position this answer is for, and when and in which language it was asked.
  latitude: number
  longitude: number
  fetchedAt: number
  language: string
  // Null when Google had no answer or the lookup failed.
  address: DeviceAddressText | null
}

const cache: Map<string, CachedAddress> = new Map<string, CachedAddress>()
const inFlight: Set<string> = new Set<string>()
const listeners: Set<() => void> = new Set<() => void>()

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

function publish(): void {
  listeners.forEach((listener) => listener())
}

export function useDeviceAddress(
  deviceId: string,
  position: DeviceLastPositionDto | null,
  enabled: boolean,
): DeviceAddressText | null {
  const { i18n } = useTranslation()
  const language: string = i18n.resolvedLanguage ?? i18n.language
  const entry: CachedAddress | undefined = useSyncExternalStore(subscribe, () => cache.get(deviceId))
  // Bumped when a deferred lookup comes due, to run the effect again.
  const [recheck, setRecheck] = useState<number>(0)

  const isActive: boolean = enabled && position !== null && hasGoogleMapsKey()
  const latitude: number | null = position?.latitude ?? null
  const longitude: number | null = position?.longitude ?? null

  useEffect(() => {
    if (!isActive || latitude === null || longitude === null) {
      return
    }
    const wait: number = msUntilLookup(entry, latitude, longitude, language, Date.now())
    if (wait === Number.POSITIVE_INFINITY) {
      return
    }
    if (wait > 0) {
      const timer: number = window.setTimeout(() => setRecheck((count) => count + 1), wait)
      return () => window.clearTimeout(timer)
    }
    lookUp(deviceId, latitude, longitude, language)
    return undefined
  }, [isActive, deviceId, latitude, longitude, language, entry, recheck])

  if (!isActive || entry === undefined) {
    return null
  }
  return entry.address
}

// How long until this position may be looked up: 0 for now, Infinity for "the
// answer we have still holds".
function msUntilLookup(
  entry: CachedAddress | undefined,
  latitude: number,
  longitude: number,
  language: string,
  now: number,
): number {
  if (entry === undefined || entry.language !== language) {
    return 0
  }
  const elapsed: number = now - entry.fetchedAt
  if (entry.address === null) {
    return Math.max(0, FAILURE_RETRY_MS - elapsed)
  }
  if (distanceMeters(entry.latitude, entry.longitude, latitude, longitude) <= MIN_MOVE_METERS) {
    return Number.POSITIVE_INFINITY
  }
  return Math.max(0, MIN_INTERVAL_MS - elapsed)
}

function lookUp(deviceId: string, latitude: number, longitude: number, language: string): void {
  if (inFlight.has(deviceId)) {
    return
  }
  inFlight.add(deviceId)

  loadGeocodingLibrary(runtimeConfig.googleMapsApiKey)
    .then((library: google.maps.GeocodingLibrary) =>
      new library.Geocoder().geocode({ location: { lat: latitude, lng: longitude }, language }))
    .then((response: google.maps.GeocoderResponse) => describeAddress(response.results))
    // ZERO_RESULTS rejects too. Either way the line is simply left out.
    .catch(() => null)
    .then((address: DeviceAddressText | null) => {
      cache.set(deviceId, { latitude, longitude, fetchedAt: Date.now(), language, address })
      inFlight.delete(deviceId)
      publish()
    })
}

// Great-circle distance; plenty precise for "has it moved 100 m".
function distanceMeters(fromLatitude: number, fromLongitude: number, toLatitude: number, toLongitude: number): number {
  const toRadians = (degrees: number): number => (degrees * Math.PI) / 180
  const latitudeDelta: number = toRadians(toLatitude - fromLatitude)
  const longitudeDelta: number = toRadians(toLongitude - fromLongitude)
  const a: number = Math.sin(latitudeDelta / 2) ** 2
    + Math.cos(toRadians(fromLatitude)) * Math.cos(toRadians(toLatitude)) * Math.sin(longitudeDelta / 2) ** 2
  return 2 * EARTH_RADIUS_METERS * Math.asin(Math.min(1, Math.sqrt(a)))
}
