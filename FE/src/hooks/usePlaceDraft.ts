// ============================================================
// usePlaceDraft — the place of a location rule as the reader is editing it:
// the raw inputs (typed coordinates, radius, which trips, valid-from day) and
// what they mean. Owned by the dialog that saves it (LocationRuleDialog, or
// JourneyDialog for a journey's one place) and rendered by
// LocationPlaceFields, which also edits it from the map.
// ============================================================

import { useMemo, useState } from 'react'
import type { PickedPoint } from '../components/LocationPickerMap'
import type { LocationRuleDto } from '../services/apiTypes'
import { LOCATION_RULE_RADIUS, dateInputToRangeStart, rangeStartToDateInput, todayDateInput } from '../utils/trips'

// Where a draft starts.
export type PlaceDraftInitial = {
  name: string
  center: PickedPoint | null
  radiusMeters: number
  matchStart: boolean
  matchEnd: boolean
  // yyyy-mm-dd, a local day.
  validDay: string
}

// A rule's values as a draft's starting point; a new place when there is none.
export function placeDraftInitial(rule: LocationRuleDto | null, center: PickedPoint | null = null): PlaceDraftInitial {
  if (rule === null) {
    return {
      name: '',
      center,
      radiusMeters: LOCATION_RULE_RADIUS.defaultValue,
      matchStart: true,
      matchEnd: true,
      validDay: todayDateInput(),
    }
  }
  return {
    name: rule.name ?? '',
    center: { latitude: rule.latitude, longitude: rule.longitude },
    radiusMeters: rule.radiusMeters,
    matchStart: rule.matchStart,
    matchEnd: rule.matchEnd,
    validDay: rangeStartToDateInput(rule.validFrom),
  }
}

// The place as the reader is editing it: the raw inputs and what they mean.
export type PlaceDraft = {
  name: string
  setName: (value: string) => void
  latitudeText: string
  setLatitudeText: (value: string) => void
  longitudeText: string
  setLongitudeText: (value: string) => void
  radiusText: string
  setRadiusText: (value: string) => void
  matchStart: boolean
  setMatchStart: (value: boolean) => void
  matchEnd: boolean
  setMatchEnd: (value: boolean) => void
  validDay: string
  setValidDay: (value: string) => void
  latitude: number
  longitude: number
  radius: number
  // Something was typed into either coordinate.
  hasPlace: boolean
  placeValid: boolean
  radiusValid: boolean
  matchValid: boolean
  validFromIso: string | undefined
  // The picked point, one object per place so the map is only re-synced when it moves.
  center: PickedPoint | null
  // Everything a save needs is there and valid.
  isComplete: boolean
  // Every value the preview is asked for; '' while the place is incomplete.
  previewKey: string
}

// Only `initial` of the first render is used, like useState's argument.
export function usePlaceDraft(initial: PlaceDraftInitial): PlaceDraft {
  const [name, setName] = useState<string>(initial.name)
  const [latitudeText, setLatitudeText] = useState<string>(
    initial.center === null ? '' : String(initial.center.latitude),
  )
  const [longitudeText, setLongitudeText] = useState<string>(
    initial.center === null ? '' : String(initial.center.longitude),
  )
  const [radiusText, setRadiusText] = useState<string>(String(initial.radiusMeters))
  const [matchStart, setMatchStart] = useState<boolean>(initial.matchStart)
  const [matchEnd, setMatchEnd] = useState<boolean>(initial.matchEnd)
  const [validDay, setValidDay] = useState<string>(initial.validDay)

  const latitude: number = parseCoordinate(latitudeText)
  const longitude: number = parseCoordinate(longitudeText)
  const radius: number = parseWhole(radiusText)
  const hasPlace: boolean = latitudeText.trim().length > 0 || longitudeText.trim().length > 0
  const placeValid: boolean =
    Number.isFinite(latitude) && Math.abs(latitude) <= 90 && Number.isFinite(longitude) && Math.abs(longitude) <= 180
  const radiusValid: boolean =
    Number.isFinite(radius) && radius >= LOCATION_RULE_RADIUS.min && radius <= LOCATION_RULE_RADIUS.max
  const matchValid: boolean = matchStart || matchEnd
  const validFromIso: string | undefined = dateInputToRangeStart(validDay)
  const center: PickedPoint | null = useMemo(
    () => (placeValid ? { latitude, longitude } : null),
    [placeValid, latitude, longitude],
  )
  const isComplete: boolean = placeValid && radiusValid && matchValid && validFromIso !== undefined
  const previewKey: string = isComplete
    ? `${latitude}|${longitude}|${radius}|${matchStart}|${matchEnd}|${validFromIso}`
    : ''

  return {
    name,
    setName,
    latitudeText,
    setLatitudeText,
    longitudeText,
    setLongitudeText,
    radiusText,
    setRadiusText,
    matchStart,
    setMatchStart,
    matchEnd,
    setMatchEnd,
    validDay,
    setValidDay,
    latitude,
    longitude,
    radius,
    hasPlace,
    placeValid,
    radiusValid,
    matchValid,
    validFromIso,
    center,
    isComplete,
    previewKey,
  }
}

// What a complete draft says, in the shape both save requests share.
export type PlaceValues = {
  name: string | null
  latitude: number
  longitude: number
  radiusMeters: number
  matchStart: boolean
  matchEnd: boolean
  validFrom: string
}

// The draft's values for a request; null while it is incomplete.
export function placeValues(draft: PlaceDraft): PlaceValues | null {
  if (!draft.isComplete || draft.validFromIso === undefined) {
    return null
  }
  const trimmedName: string = draft.name.trim()
  return {
    name: trimmedName.length > 0 ? trimmedName : null,
    latitude: draft.latitude,
    longitude: draft.longitude,
    radiusMeters: draft.radius,
    matchStart: draft.matchStart,
    matchEnd: draft.matchEnd,
    validFrom: draft.validFromIso,
  }
}

// True when the values say something other than the rule does now.
export function placeDiffersFrom(values: PlaceValues, rule: LocationRuleDto): boolean {
  return (
    values.name !== (rule.name ?? null)
    || values.latitude !== rule.latitude
    || values.longitude !== rule.longitude
    || values.radiusMeters !== rule.radiusMeters
    || values.matchStart !== rule.matchStart
    || values.matchEnd !== rule.matchEnd
    || Date.parse(values.validFrom) !== Date.parse(rule.validFrom)
  )
}

// A typed coordinate, accepting a decimal comma; NaN when it is not a number.
function parseCoordinate(text: string): number {
  const trimmed: string = text.trim().replace(',', '.')
  return trimmed.length === 0 || !/^-?\d+(\.\d+)?$/.test(trimmed) ? Number.NaN : Number(trimmed)
}

// A typed whole number; NaN when it is not one.
function parseWhole(text: string): number {
  const trimmed: string = text.trim()
  return /^\d+$/.test(trimmed) ? Number(trimmed) : Number.NaN
}
