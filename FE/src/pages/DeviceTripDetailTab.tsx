// ============================================================
// DeviceTripDetailTab — one trip's page, at /device/:deviceId/trips/:tripId:
// when it ran, its figures, its route on the map and every reading the tracker
// took on the way, charted against one shared time axis.
//
// Three loads, each for its own reason:
//   • the trip itself — its end and figures are computed by the server on
//     request, so it is re-read on every refresh tick (cheap: one trip), which
//     is what keeps a trip still being driven up to date;
//   • the journeys — only for the journey's name and the "Move…" dialog;
//   • the trip's positions — from the departure to the end, both bounds
//     inclusive and passed to the API exactly as it sent them (never through a
//     Date, which would drop the microseconds and could miss the edge fixes).
//     A finished trip's fixes cannot change, so they are walked once; a trip in
//     progress has no end yet and is topped up with the newest fixes on each
//     tick, the way the Charts tab does it. If late data moves the trip's start
//     or end, the range changes and the walk starts over.
//
// "← Trips" goes back to the list with the same ?journey= the reader came from;
// "Show on the Map tab" sets the device page's shared date range to the trip,
// widened to whole minutes because the range pickers have minute resolution
// and the API's bounds are inclusive.
//
// Organising (name, journey) needs CanModifySettings, as on the list.
// ============================================================

import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useLocation, useNavigate, useOutletContext, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { formatInteger, formatTime, formatWeekdayDate } from '../i18n/format'
import type { DevicePageContext } from './DevicePage'
import { ApiError, assignTrips, fetchJourneys, fetchTrip } from '../services/apiClient'
import type { JourneyListDto, PositionDto, TripSummaryDto } from '../services/apiTypes'
import { fetchAllPositions, fetchPositionChunk, mergeNewest } from '../services/positionPager'
import { RefreshToolbar } from '../components/RefreshToolbar'
import { JourneyMoveDialog } from '../components/JourneyMoveDialog'
import { TripCharts } from '../components/TripCharts'
import { TripNameEditor } from '../components/TripNameEditor'
import { TripRouteMap } from '../components/TripRouteMap'
import { TripStatTiles } from '../components/TripStatTiles'
import { RANGE_FUTURE_HOURS, formatDateTimeLocal, parseApiTimestamp } from '../utils/dates'
import { describeError } from '../utils/errors'
import type { JourneyTree } from '../utils/journeyTree'
import { buildJourneyTree, journeyPathLabel } from '../utils/journeyTree'
import type { TripFixSummary } from '../utils/tripTelemetry'
import { summarizeTripFixes } from '../utils/tripTelemetry'
import { describeTripTimes, tripShownStart } from '../utils/trips'

// Ceiling on one trip's positions, as on the Charts tab: fifty requests at most.
const MAX_TRIP_ROWS: number = 50_000

// Gaps listed by name in the banner; any more are only counted.
const MAX_LISTED_GAPS: number = 5

const MINUTE_MS: number = 60_000

type LoadedPositions = {
  key: string
  rows: PositionDto[]
  reachedCap: boolean
}

export function DeviceTripDetailTab() {
  const { t } = useTranslation(['trips', 'common', 'errors'])
  const { device, autoRefresh: refresh, setDateRange } = useOutletContext<DevicePageContext>()
  const { tripId: tripIdParam } = useParams<{ tripId: string }>()
  const location = useLocation()
  const navigate = useNavigate()

  const tripId: number | null = parseTripId(tripIdParam)
  const canManage: boolean = device.permissions.canModifySettings
  const tripKey: string = `${device.deviceId}|${tripId ?? ''}`

  // Bumped by a change made here (a move); re-reads the trip and the journeys.
  const [changeToken, setChangeToken] = useState<number>(0)

  const [trip, setTrip] = useState<{ key: string; value: TripSummaryDto } | null>(null)
  const [tripError, setTripError] = useState<{ key: string; message: string; notFound: boolean } | null>(null)
  const [isLoadingTrip, setIsLoadingTrip] = useState<boolean>(false)

  const [journeyList, setJourneyList] = useState<{ deviceId: string; value: JourneyListDto } | null>(null)

  const [positions, setPositions] = useState<LoadedPositions | null>(null)
  const positionsRef = useRef<LoadedPositions | null>(null)
  const [positionsStatus, setPositionsStatus] = useState<string>('')

  const [isMoveOpen, setIsMoveOpen] = useState<boolean>(false)
  const [isBusy, setIsBusy] = useState<boolean>(false)
  const [actionError, setActionError] = useState<string>('')
  const [notice, setNotice] = useState<string>('')

  // ---- The trip ----
  useEffect(() => {
    if (tripId === null) {
      return
    }
    let canceled = false
    const key: string = tripKey

    const load = async (): Promise<void> => {
      setIsLoadingTrip(true)
      try {
        const value: TripSummaryDto = await fetchTrip(device.deviceId, tripId)
        if (!canceled) {
          setTrip({ key, value })
          setTripError(null)
        }
      } catch (error) {
        if (!canceled) {
          setTripError({
            key,
            message: describeError(error, t('errors:loadTripFailed')),
            notFound: error instanceof ApiError && error.status === 404,
          })
        }
      } finally {
        if (!canceled) {
          setIsLoadingTrip(false)
        }
      }
    }

    void load()
    return () => {
      canceled = true
    }
    // `t` is deliberately absent: a language switch must not refetch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tripKey, refresh.token, changeToken])

  // ---- The journeys (names and the move dialog) ----
  useEffect(() => {
    let canceled = false
    fetchJourneys(device.deviceId)
      .then((value) => {
        if (!canceled) {
          setJourneyList({ deviceId: device.deviceId, value })
        }
      })
      .catch(() => {
        // Only names are missing then; the page still works without them.
      })
    return () => {
      canceled = true
    }
  }, [device.deviceId, changeToken])

  const shownTrip: TripSummaryDto | null = trip !== null && trip.key === tripKey ? trip.value : null
  const shownError = tripError !== null && tripError.key === tripKey ? tripError : null

  const journeys: JourneyListDto | null =
    journeyList !== null && journeyList.deviceId === device.deviceId ? journeyList.value : null
  const tree: JourneyTree = useMemo(() => buildJourneyTree(journeys?.journeys ?? []), [journeys])

  // ---- The trip's positions ----
  const fromIso: string | undefined =
    shownTrip === null ? undefined : (shownTrip.departure ?? shownTrip.startTime)
  const toIso: string | undefined =
    shownTrip === null || shownTrip.inProgress ? undefined : (shownTrip.endTime ?? undefined)
  const inProgress: boolean = shownTrip?.inProgress ?? false
  const positionsKey: string = fromIso === undefined ? '' : `${device.deviceId}|${fromIso}|${toIso ?? ''}`

  useEffect(() => {
    if (positionsKey === '' || fromIso === undefined) {
      return
    }
    const held: LoadedPositions | null = positionsRef.current
    const isTopUp: boolean = held !== null && held.key === positionsKey
    // A finished trip's fixes do not change; nothing to do on a tick.
    if (isTopUp && !inProgress) {
      return
    }

    let canceled = false
    const apply = (next: LoadedPositions): void => {
      positionsRef.current = next
      setPositions(next)
    }

    const load = async (): Promise<void> => {
      try {
        if (isTopUp && held !== null) {
          const chunk = await fetchPositionChunk(device.deviceId, fromIso, toIso, new Set<number>())
          if (!canceled) {
            apply({ ...held, rows: mergeNewest(held.rows, chunk.rows) })
          }
          return
        }

        setPositionsStatus(t('trips:detail.loadingPositions'))
        const result = await fetchAllPositions(device.deviceId, fromIso, toIso, {
          maxRows: MAX_TRIP_ROWS,
          isCanceled: () => canceled,
          onProgress: (loaded) => {
            if (!canceled) {
              setPositionsStatus(
                t('trips:detail.loadingProgress', { count: loaded, value: formatInteger(loaded) }),
              )
            }
          },
        })
        if (canceled) {
          return
        }
        apply({ key: positionsKey, rows: result.positions, reachedCap: result.reachedCap })
        setPositionsStatus('')
      } catch (error) {
        if (!canceled) {
          setPositionsStatus(describeError(error, t('errors:loadPositionsFailed')))
        }
      }
    }

    void load()
    return () => {
      canceled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [positionsKey, inProgress, refresh.token])

  const shownPositions: PositionDto[] | null =
    positions !== null && positions.key === positionsKey ? positions.rows : null
  const fixSummary: TripFixSummary | null = useMemo(
    () => (shownPositions === null ? null : summarizeTripFixes(shownPositions)),
    [shownPositions],
  )

  // ---- Actions ----
  async function assign(journeyId: string | null, automatic: boolean): Promise<void> {
    if (tripId === null || isBusy) {
      return
    }
    setIsBusy(true)
    setActionError('')
    setNotice('')
    try {
      await assignTrips(device.deviceId, { tripIds: [tripId], journeyId, automatic })
      setIsMoveOpen(false)
      setNotice(t('trips:detail.moved'))
      setChangeToken((current) => current + 1)
    } catch (error) {
      setActionError(describeError(error, t('errors:moveTripsFailed')))
    } finally {
      setIsBusy(false)
    }
  }

  function showOnMap(): void {
    if (shownTrip === null) {
      return
    }
    const start: Date | null = tripShownStart(shownTrip)
    const end: Date | null =
      shownTrip.inProgress || shownTrip.endTime === null ? null : parseApiTimestamp(shownTrip.endTime)
    if (start === null) {
      return
    }
    // Down to the minute at the start; up past the end — the +1 ms keeps a fix
    // a few microseconds after a whole minute (which a Date cannot hold) inside.
    const from = new Date(Math.floor(start.getTime() / MINUTE_MS) * MINUTE_MS)
    const to: Date =
      end === null
        ? new Date(Date.now() + RANGE_FUTURE_HOURS * 60 * MINUTE_MS)
        : new Date(Math.ceil((end.getTime() + 1) / MINUTE_MS) * MINUTE_MS)
    setDateRange({ from: formatDateTimeLocal(from), to: formatDateTimeLocal(to) })
    void navigate('../../map', { relative: 'path' })
  }

  // ---- Rendering ----
  const backLink = (
    <Link to={{ pathname: '..', search: location.search }} relative="path" className="trip-detail-back">
      ← {t('trips:detail.back')}
    </Link>
  )

  if (tripId === null || (shownTrip === null && shownError?.notFound)) {
    return (
      <div>
        {backLink}
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">🚗</span>
          <h3>{t('trips:detail.notFoundTitle')}</h3>
          <p>{t('trips:detail.notFoundBody')}</p>
        </div>
      </div>
    )
  }

  if (shownTrip === null) {
    return (
      <div>
        {backLink}
        {shownError !== null ? (
          <div className="error-state">
            <p>{shownError.message}</p>
            <button type="button" className="btn btn-secondary" onClick={refresh.refreshNow}>
              {t('common:actions.retry')}
            </button>
          </div>
        ) : (
          <div className="loading-state">
            <div className="spinner" />
            <span>{t('trips:detail.loading')}</span>
          </div>
        )}
      </div>
    )
  }

  const start: Date | null = tripShownStart(shownTrip)
  const journeyId: string | null = shownTrip.journeyId
  const journeyLabel: string | null =
    journeyId !== null && tree.byId.has(journeyId) ? journeyPathLabel(tree, journeyId) : null

  const gaps = fixSummary?.gaps ?? []
  const gapText: string = gaps
    .slice(0, MAX_LISTED_GAPS)
    .map((gap) => `${formatTime(gap.from)}–${formatTime(gap.to)}`)
    .join(', ')

  const statusMessage: string =
    actionError.length > 0 && !isMoveOpen
      ? actionError
      : notice.length > 0
        ? notice
        : shownError !== null
          ? shownError.message
          : positionsStatus

  return (
    <div className="trip-detail">
      <div className="trip-detail-toolbar">
        {backLink}
        <span className="trips-toolbar-spacer" />
        <RefreshToolbar autoRefresh={refresh} isLoading={isLoadingTrip} />
      </div>

      <header className="trip-detail-head">
        <div className="trip-detail-heading">
          <h3 className="trip-detail-title">
            {start === null ? '' : `${formatWeekdayDate(start)} · `}
            {describeTripTimes(shownTrip)}
            {shownTrip.inProgress ? (
              <span className="trip-badge trip-badge--live">{t('trips:row.inProgress')}</span>
            ) : null}
          </h3>
          <TripNameEditor
            key={`${shownTrip.id}:${shownTrip.name ?? ''}`}
            deviceId={device.deviceId}
            tripId={shownTrip.id}
            name={shownTrip.name}
            canEdit={canManage}
            onRenamed={(name) =>
              setTrip((current) =>
                current === null || current.key !== tripKey ? current : { ...current, value: { ...current.value, name } },
              )
            }
          />
        </div>
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={showOnMap}
          title={t('trips:detail.showOnMapTitle')}
        >
          🗺 {t('trips:detail.showOnMap')}
        </button>
      </header>

      <div className="trip-detail-journey">
        <span className="trip-detail-journey-name">
          <span aria-hidden="true">🧭 </span>
          {journeyId === null ? (
            t('trips:detail.notInJourney')
          ) : (
            <Link to={{ pathname: '..', search: `?journey=${encodeURIComponent(journeyId)}` }} relative="path">
              {journeyLabel ?? '…'}
            </Link>
          )}
          {shownTrip.journeyPinned ? (
            <span
              className="trip-row-marker"
              role="img"
              title={journeyId === null ? t('trips:row.pinnedOutTitle') : t('trips:row.pinnedTitle')}
              aria-label={journeyId === null ? t('trips:row.pinnedOutTitle') : t('trips:row.pinnedTitle')}
            >
              {' '}📌
            </span>
          ) : null}
        </span>
        {canManage ? (
          <span className="trip-detail-journey-actions">
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={() => {
                setActionError('')
                setIsMoveOpen(true)
              }}
              disabled={isBusy}
            >
              {t('trips:detail.move')}
            </button>
            {journeyId !== null ? (
              <button
                type="button"
                className="btn btn-quiet btn-sm"
                onClick={() => void assign(null, false)}
                disabled={isBusy}
              >
                {t('trips:selection.remove')}
              </button>
            ) : null}
            {shownTrip.journeyPinned ? (
              <button
                type="button"
                className="btn btn-quiet btn-sm"
                onClick={() => void assign(null, true)}
                disabled={isBusy}
                title={t('trips:selection.automaticTitle')}
              >
                {t('trips:selection.automatic')}
              </button>
            ) : null}
          </span>
        ) : null}
      </div>

      <p className="hint" role="status" style={{ margin: '0 0 12px' }}>
        {statusMessage}
      </p>

      {shownTrip.inProgress ? (
        <div className="banner banner--info trips-banner" role="status">
          <span className="banner-icon" aria-hidden="true">🟢</span>
          <div className="banner-text">
            <p className="banner-title">{t('trips:detail.inProgressTitle')}</p>
            <p className="banner-detail">{t('trips:detail.inProgressBody')}</p>
          </div>
        </div>
      ) : null}

      {shownTrip.hasGap && gaps.length > 0 ? (
        <div className="banner banner--warning trips-banner" role="status">
          <span className="banner-icon" aria-hidden="true">⚠</span>
          <div className="banner-text">
            <p className="banner-title">{t('trips:detail.gapTitle')}</p>
            <p className="banner-detail">
              {t('trips:detail.gapList', { ranges: gapText })}
              {gaps.length > MAX_LISTED_GAPS
                ? ` ${t('trips:detail.gapMore', { count: gaps.length - MAX_LISTED_GAPS })}`
                : ''}
            </p>
          </div>
        </div>
      ) : null}

      {shownTrip.endKind === 'gap' ? (
        <div className="banner banner--warning trips-banner" role="status">
          <span className="banner-icon" aria-hidden="true">⚠</span>
          <div className="banner-text">
            <p className="banner-title">{t('trips:detail.endGapTitle')}</p>
            <p className="banner-detail">{t('trips:detail.endGapBody')}</p>
          </div>
        </div>
      ) : null}

      <TripStatTiles trip={shownTrip} fixes={fixSummary} />

      {positions !== null && positions.key === positionsKey && positions.reachedCap ? (
        <div className="banner banner--warning trips-banner" role="status">
          <span className="banner-icon" aria-hidden="true">⚠</span>
          <span className="banner-text">
            {t('trips:detail.truncated', { limit: formatInteger(MAX_TRIP_ROWS) })}
          </span>
        </div>
      ) : null}

      {shownPositions !== null && shownPositions.length === 0 ? (
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">📍</span>
          <p>{t('trips:detail.noPositions')}</p>
        </div>
      ) : (
        <>
          <TripRouteMap tripId={shownTrip.id} positions={shownPositions ?? []} />
          {shownPositions === null ? (
            <div className="loading-state">
              <div className="spinner" />
              <span>{t('trips:detail.loadingPositions')}</span>
            </div>
          ) : (
            <TripCharts positions={shownPositions} averageSpeedKmph={shownTrip.averageSpeedKmph} />
          )}
        </>
      )}

      {isMoveOpen ? (
        <JourneyMoveDialog
          tree={tree}
          tripCount={1}
          initialJourneyId={journeyId}
          isBusy={isBusy}
          errorMessage={actionError}
          onMove={(target) => void assign(target, false)}
          onCancel={() => setIsMoveOpen(false)}
        />
      ) : null}
    </div>
  )
}

// The :tripId segment as a trip id, or null when it is not a positive integer.
function parseTripId(value: string | undefined): number | null {
  if (value === undefined || !/^\d{1,15}$/.test(value)) {
    return null
  }
  const id: number = Number(value)
  return id > 0 ? id : null
}
