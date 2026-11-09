// ============================================================
// TripStatTiles — the row of figures at the top of a trip's page.
//
// Two sources, kept apart on purpose:
//   • distance, duration and the three speeds are the SERVER's, the same
//     numbers the list showed for this trip — so the two pages never disagree;
//   • climb, altitude range and battery come from the fixes loaded for the
//     charts (utils/tripTelemetry.ts), and read "…" until those arrive.
//
// Average speed is distance over the whole duration, stops in traffic
// included; the moving average leaves out the time spent below walking pace.
// Both are shown because each answers a different question ("how long will
// this route take me" against "how fast did I drive").
// ============================================================

import { useTranslation } from 'react-i18next'
import { formatAltitude, formatDistance, formatDuration, formatSpeed } from '../i18n/format'
import type { TripSummaryDto } from '../services/apiTypes'
import type { TripFixSummary } from '../utils/tripTelemetry'
import { TripFigure } from './TripFigure'

export type TripStatTilesProps = {
  trip: TripSummaryDto
  // Null while the trip's positions are still loading.
  fixes: TripFixSummary | null
}

export function TripStatTiles({ trip, fixes }: TripStatTilesProps) {
  const { t } = useTranslation(['trips', 'common'])
  const serverPending: boolean = trip.statsPending
  const fixesPending: boolean = fixes === null

  // 0 is the charging sentinel, never a flat battery.
  const battery = (value: number): string =>
    value === 0 ? `⚡ ${t('common:battery.charging')}` : t('common:battery.percent', { value })

  return (
    <dl className="trip-tiles">
      <div className="trip-tile">
        <dt>{t('trips:detail.tiles.distance')}</dt>
        <dd>
          <TripFigure
            pending={serverPending}
            value={trip.distanceMeters === null ? null : formatDistance(trip.distanceMeters)}
          />
        </dd>
      </div>
      <div className="trip-tile">
        <dt>{t('trips:detail.tiles.duration')}</dt>
        <dd>
          <TripFigure
            pending={serverPending}
            value={trip.durationSeconds === null ? null : formatDuration(trip.durationSeconds)}
            title={t('trips:detail.tiles.durationTitle')}
          />
        </dd>
      </div>
      <div className="trip-tile">
        <dt>{t('trips:detail.tiles.averageSpeed')}</dt>
        <dd>
          <TripFigure
            pending={serverPending}
            value={trip.averageSpeedKmph === null ? null : formatSpeed(trip.averageSpeedKmph)}
            title={t('trips:detail.tiles.averageSpeedTitle')}
          />
        </dd>
      </div>
      <div className="trip-tile">
        <dt>{t('trips:detail.tiles.movingAverage')}</dt>
        <dd>
          <TripFigure
            pending={serverPending}
            value={trip.movingAverageSpeedKmph === null ? null : formatSpeed(trip.movingAverageSpeedKmph)}
            title={t('trips:detail.tiles.movingAverageTitle')}
          />
        </dd>
      </div>
      <div className="trip-tile">
        <dt>{t('trips:detail.tiles.maxSpeed')}</dt>
        <dd>
          <TripFigure
            pending={serverPending}
            value={trip.maxSpeedKmph === null ? null : formatSpeed(trip.maxSpeedKmph)}
          />
        </dd>
      </div>
      <div className="trip-tile">
        <dt>{t('trips:detail.tiles.climb')}</dt>
        <dd>
          <TripFigure
            pending={fixesPending}
            value={fixes?.climbMeters == null ? null : `+${formatAltitude(fixes.climbMeters)}`}
            title={t('trips:detail.tiles.climbTitle')}
          />
        </dd>
      </div>
      <div className="trip-tile">
        <dt>{t('trips:detail.tiles.altitude')}</dt>
        <dd>
          <TripFigure
            pending={fixesPending}
            value={
              fixes?.altitudeMin == null || fixes.altitudeMax == null
                ? null
                : `${formatAltitude(fixes.altitudeMin)} – ${formatAltitude(fixes.altitudeMax)}`
            }
          />
        </dd>
      </div>
      <div className="trip-tile">
        <dt>{t('trips:detail.tiles.battery')}</dt>
        <dd>
          <TripFigure
            pending={fixesPending}
            value={
              fixes?.batteryStart == null || fixes.batteryEnd == null
                ? null
                : `${battery(fixes.batteryStart)} → ${battery(fixes.batteryEnd)}`
            }
          />
        </dd>
      </div>
    </dl>
  )
}
