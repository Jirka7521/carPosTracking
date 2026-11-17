// ============================================================
// addressLabel — turns a Google reverse-geocoding answer into the short line
// shown under a device ("📍 D1 · Průhonice", "📍 Chlumecká 765/6, Praha 9").
//
// Google orders the results from the most precise down, so the first one says
// what kind of place the fix is at:
//   • a building (street_address, premise) → "street number, locality";
//     a village without street names gives "Průhonice 123" alone;
//   • a road outside a building's reach (route) → "road · nearest locality",
//     with the road's short name, so a motorway reads "D1", not "Dálnice D1";
//   • anything coarser → the locality, or failing that the district (okres).
// The full formatted address goes into the tooltip.
// ============================================================

export type DeviceAddressText = {
  // The short line under the device name.
  label: string
  // Google's full address for the same result.
  full: string
}

const STREET_TYPES: readonly string[] = ['street_address', 'premise', 'subpremise']

export function describeAddress(results: readonly google.maps.GeocoderResult[]): DeviceAddressText | null {
  // A plus code ("9F2P+3V Praha") is a grid cell, not a place anybody knows.
  const best: google.maps.GeocoderResult | undefined = results.find(
    (result) => !result.types.includes('plus_code'),
  )
  if (best === undefined) {
    return null
  }

  const locality: string | null = localityOf(best)
    ?? firstMatch(results, (result) => localityOf(result))

  let label: string | null
  if (best.types.some((type) => STREET_TYPES.includes(type))) {
    label = streetLabel(best, locality)
  } else if (best.types.includes('route')) {
    const road: string | null = component(best, 'route', 'short')
    label = road === null ? locality : locality === null ? road : `${road} · ${locality}`
  } else {
    label = locality ?? component(best, 'administrative_area_level_2', 'long')
  }

  if (label === null || label.trim() === '') {
    return null
  }
  return { label, full: best.formatted_address }
}

function streetLabel(result: google.maps.GeocoderResult, locality: string | null): string | null {
  // Czech addresses carry two numbers, "765/6": the building's own (premise)
  // and its number in the street (street_number).
  const premise: string | null = component(result, 'premise', 'long')
  const streetNumber: string | null = component(result, 'street_number', 'long')
  const number: string | null = premise !== null && streetNumber !== null && premise !== streetNumber
    ? `${premise}/${streetNumber}`
    : premise ?? streetNumber

  const route: string | null = component(result, 'route', 'long')
  if (route === null) {
    // A village numbers its houses without streets: "Průhonice 123".
    return locality === null ? number : number === null ? locality : `${locality} ${number}`
  }
  const street: string = number === null ? route : `${route} ${number}`
  return locality === null ? street : `${street}, ${locality}`
}

// "Praha 9" rather than "Praha" when Google knows the district, since a city's
// name alone says little about where in it the car is.
function localityOf(result: google.maps.GeocoderResult): string | null {
  return component(result, 'sublocality_level_1', 'long')
    ?? component(result, 'locality', 'long')
    ?? component(result, 'postal_town', 'long')
}

function component(result: google.maps.GeocoderResult, type: string, form: 'long' | 'short'): string | null {
  const found: google.maps.GeocoderAddressComponent | undefined = result.address_components.find(
    (candidate) => candidate.types.includes(type),
  )
  if (found === undefined) {
    return null
  }
  return form === 'long' ? found.long_name : found.short_name
}

function firstMatch(
  results: readonly google.maps.GeocoderResult[],
  pick: (result: google.maps.GeocoderResult) => string | null,
): string | null {
  for (const result of results) {
    const value: string | null = pick(result)
    if (value !== null) {
      return value
    }
  }
  return null
}
