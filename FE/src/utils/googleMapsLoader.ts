// ============================================================
// googleMapsLoader — injects the Google Maps JS API once per page.
//
// Shared by every map in the app (DeviceMap, LocationPickerMap). Call it only
// after the viewer has agreed to load maps (see components/MapsConsentPrompt and
// hooks/useAccountMapsConsent): calling it is what contacts Google.
//
//   • The script is injected with the marker library preloaded via
//     `&libraries=marker`, so google.maps.Map, Marker, Circle, … are available
//     once the API's ready callback fires.
//   • A module-level singleton promise prevents the script from being injected
//     more than once across re-mounts / HMR.
// ============================================================

let googleMapsLoader: Promise<void> | null = null

// The loaded API, or undefined before it has loaded.
export function googleApi(): typeof google | undefined {
  return (window as unknown as { google?: typeof google }).google
}

export function loadGoogleMaps(apiKey: string): Promise<void> {
  // Already loaded? Resolve immediately.
  if (googleApi()?.maps?.Map) {
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
        if (googleApi()?.maps?.Map) {
          resolve()
        } else {
          reject(new Error('Google Maps script loaded but google.maps.Map is missing.'))
        }
      }
      if (googleApi()?.maps?.Map) {
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

    // `loading=async` is what Google asks for, and without it the console warns
    // about suboptimal performance on every load. It does change the contract:
    // the script's own `load` event now fires BEFORE the API has bootstrapped,
    // so google.maps.X is not populated yet at that point. The `callback`
    // parameter is the supported answer — it runs once the API *and* every
    // library named in `libraries=` are ready, so direct `new google.maps.Map()`
    // usage still works. (What genuinely does not survive is the inline
    // bootstrap-loader form, where only importLibrary() is guaranteed.)
    //
    // `libraries=marker` ensures the marker classes ship with that bootstrap.
    const callbackName = '__carposGoogleMapsReady'
    const globalScope = window as unknown as Record<string, unknown>
    globalScope[callbackName] = (): void => {
      delete globalScope[callbackName]
      resolve()
    }

    script.src =
      `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(apiKey)}` +
      `&v=weekly&libraries=marker&loading=async&callback=${callbackName}`

    script.addEventListener('error', () => {
      delete globalScope[callbackName]
      reject(new Error('Failed to load Google Maps script.'))
    })
    // Belt and braces: if the API somehow finished before the callback was
    // wired up, resolving twice is a no-op. Note there is deliberately no
    // "loaded but google.maps.Map is missing" rejection here any more — under
    // loading=async that is the normal state at `load` time, not a failure.
    script.addEventListener('load', () => {
      if (googleApi()?.maps?.Map) {
        resolve()
      }
    })
    document.head.appendChild(script)
  })

  return googleMapsLoader
}
