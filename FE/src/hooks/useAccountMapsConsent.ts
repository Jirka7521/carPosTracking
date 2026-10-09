// ============================================================
// useAccountMapsConsent — the signed-in user's standing agreement to load the
// Google map, as stored on their account.
//
// Read once on mount from GET /api/me/maps-consent. Until that answers, `status`
// is 'loading' and the caller must not render the map gate: DeviceMap seeds its
// state from `isStanding` when it mounts, so mounting it early would show the
// prompt to somebody who already said "always" — or worse, the other way round.
//
// Fails closed. If the read fails for any reason the hook reports "ready, not
// granted": the cost is a prompt the user did not need, never a request to
// Google that nobody agreed to.
//
// Share-link visitors have no account; their answer is a cookie, see
// utils/mapsConsent.ts.
// ============================================================

import { useCallback, useEffect, useState } from 'react'
import { fetchMapsConsent, grantMapsConsent, revokeMapsConsent } from '../services/apiClient'
import { MAPS_CONSENT_VERSION, isCurrentMapsConsentVersion } from '../utils/mapsConsent'

export type AccountMapsConsent = {
  status: 'loading' | 'ready'
  // True only for an agreement to the prompt wording currently shown
  isStanding: boolean
  // Record "always" on the account. Throws ApiError when the save fails.
  grant: () => Promise<void>
  // Withdraw it (GDPR Art. 7(3)). Throws ApiError when the save fails.
  revoke: () => Promise<void>
}

export function useAccountMapsConsent(): AccountMapsConsent {
  const [status, setStatus] = useState<'loading' | 'ready'>('loading')
  const [isStanding, setIsStanding] = useState<boolean>(false)

  useEffect(() => {
    let canceled = false

    const load = async (): Promise<void> => {
      try {
        const consent = await fetchMapsConsent()
        if (!canceled) {
          setIsStanding(isCurrentMapsConsentVersion(consent.version))
        }
      } catch {
        if (!canceled) {
          setIsStanding(false)
        }
      } finally {
        if (!canceled) {
          setStatus('ready')
        }
      }
    }

    void load()

    return () => {
      canceled = true
    }
  }, [])

  const grant = useCallback(async (): Promise<void> => {
    const consent = await grantMapsConsent(MAPS_CONSENT_VERSION)
    setIsStanding(isCurrentMapsConsentVersion(consent.version))
  }, [])

  const revoke = useCallback(async (): Promise<void> => {
    await revokeMapsConsent()
    setIsStanding(false)
  }, [])

  return { status, isStanding, grant, revoke }
}
