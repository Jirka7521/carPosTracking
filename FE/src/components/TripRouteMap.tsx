// ============================================================
// TripRouteMap — the trip's route on Google Maps, on the trip's page.
//
// The same DeviceMap the Map tab uses, behind the same consent gate (see
// DeviceMapTab): nothing loads from Google until this account has agreed to
// it, and the map is not mounted at all until the account's standing answer is
// known, because DeviceMap reads it once at mount and would otherwise flash the
// prompt at someone who already said "always".
//
// Keyed by trip, so moving to another trip starts a fresh map that frames the
// new route instead of staying parked over the previous one. The newest fix
// wears the pin — on a finished trip, that is where it ended — and the rest is
// the line. The pin is never shown as overdue here: it marks the end of a trip,
// not where the car is now, and on a finished trip it would always be "late".
// ============================================================

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import DeviceMap from './DeviceMap'
import { useAccountMapsConsent } from '../hooks/useAccountMapsConsent'
import type { PositionDto } from '../services/apiTypes'
import { hasGoogleMapsKey, runtimeConfig } from '../services/runtimeConfig'

export type TripRouteMapProps = {
  tripId: number
  positions: PositionDto[]
}

export function TripRouteMap({ tripId, positions }: TripRouteMapProps) {
  const { t } = useTranslation(['trips', 'device', 'common'])
  const mapsConsent = useAccountMapsConsent()
  const [fitToken, setFitToken] = useState<number>(0)

  return (
    <section className="trip-section trip-map">
      <div className="trip-section-head">
        <h4 className="trip-section-title">{t('trips:detail.map.title')}</h4>
        <button
          type="button"
          className="btn btn-quiet btn-sm"
          onClick={() => setFitToken((current) => current + 1)}
          disabled={positions.length === 0}
        >
          {t('device:map.fit')}
        </button>
      </div>

      <div className="map-frame">
        {!hasGoogleMapsKey() ? (
          <div className="error-state">
            <p>{t('device:map.noApiKey')}</p>
          </div>
        ) : mapsConsent.status === 'loading' ? (
          <div className="map-placeholder">
            <p>{t('common:states.loading')}</p>
          </div>
        ) : (
          <DeviceMap
            key={tripId}
            positions={positions}
            apiKey={runtimeConfig.googleMapsApiKey}
            fitToken={fitToken}
            hasStandingConsent={mapsConsent.isStanding}
            onGrantStandingConsent={() => {
              // As on the Map tab: a failed save only means the prompt comes
              // back next time; the map is already loading.
              mapsConsent.grant().catch(() => undefined)
            }}
            consentScope="account"
          />
        )}
      </div>

      <div className="map-legend">
        <div className="map-legend-item">
          <span className="legend-dot legend-dot--latest" aria-hidden="true" />
          {t('trips:detail.map.legendEnd')}
        </div>
        <div className="map-legend-item">
          <span className="legend-line" aria-hidden="true" />
          {t('trips:detail.map.legendRoute')}
        </div>
      </div>
    </section>
  )
}
