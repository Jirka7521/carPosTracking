// ============================================================
// DeviceTripsTab — the "Trips" tab inside DevicePage: the car's trips, newest
// first and grouped by day, and the journeys they are organised into.
//
// Trips are detected by the server from the positions; a trip ends when the
// car stays put long enough (the threshold is an Expert setting per car). Only
// each trip's start is stored — its end, distance, duration and speeds are
// recomputed by the server for every answer — so nothing here caches them for
// longer than the page is open.
//
// Layout: the journey sidebar on the left (on a phone, a button above the
// list), the open view on the right — "All trips", "Not in a journey", or one
// journey with its header (JourneyOverview) and the trips directly in it.
//
// The view lives in the address (?journey=all|none|<id>), not in DevicePage's
// shared state: it is a place, so it should survive a reload, be linkable, and
// be what the browser's Back button and a trip page's "← Trips" return to.
// Unlike the other tabs this one ignores the shared date range — trips are
// paged back through history instead ("Load older trips").
//
// Refreshing: it runs on the device page's one shared timer like every tab.
// A tick re-reads the journeys (counts only) and the NEWEST page of trips,
// spliced over the head of what is loaded (utils/trips.ts mergeNewestPage), so
// paging back a long way is not undone every 30 seconds and the server is not
// asked to recompute old trips that cannot have changed. A change the reader
// makes (a move, a new or edited journey) reloads the list from the top
// instead, keeping the old rows on screen until the new ones arrive.
//
// Rows the server could not finish within one answer come back statsPending;
// they are fetched one at a time and filled in.
//
// Organising — journeys, moves, the selection bar — needs CanModifySettings,
// like the device's other settings. Without it the controls are hidden; the API
// re-checks every request regardless.
// ============================================================

import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useLocation, useNavigate, useOutletContext, useSearchParams } from 'react-router-dom'
import type { To } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { formatInteger } from '../i18n/format'
import type { DevicePageContext } from './DevicePage'
import { assignTrips, deleteJourney, fetchJourneys, fetchTrip, fetchTrips } from '../services/apiClient'
import type { TripListFilter } from '../services/apiClient'
import type { JourneyDto, JourneyListDto, TripSummaryDto } from '../services/apiTypes'
import { RefreshToolbar } from '../components/RefreshToolbar'
import { JourneySidebar } from '../components/JourneySidebar'
import { JourneyOverview } from '../components/JourneyOverview'
import { JourneyDialog } from '../components/JourneyDialog'
import type { JourneyDialogMode } from '../components/JourneyDialog'
import { JourneyDeleteDialog } from '../components/JourneyDeleteDialog'
import { JourneyMoveDialog } from '../components/JourneyMoveDialog'
import { TripDayGroup } from '../components/TripDayGroup'
import { TripListEmpty } from '../components/TripListEmpty'
import { TripRow } from '../components/TripRow'
import { TripSelectionBar } from '../components/TripSelectionBar'
import { describeError } from '../utils/errors'
import type { JourneyTree } from '../utils/journeyTree'
import { buildJourneyTree, canPlaceUnder, childrenOf, journeyPathLabel } from '../utils/journeyTree'
import type { TripDay } from '../utils/trips'
import {
  MAX_TRIPS_PER_ASSIGN,
  TRIP_PAGE_SIZE,
  appendOlderPage,
  groupTripsByDay,
  mergeNewestPage,
  replaceTrip,
} from '../utils/trips'

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

const NO_SELECTION: ReadonlySet<number> = new Set<number>()
const NO_TRIPS: readonly TripSummaryDto[] = []

// The loaded list, tagged with the device and view it belongs to, so a list
// still on screen from the previous view is never mistaken for this one's.
type TripList = {
  key: string
  trips: TripSummaryDto[]
  nextCursor: string | null
  isRecalculating: boolean
}

type DialogState =
  | { kind: 'journey'; mode: JourneyDialogMode }
  | { kind: 'delete'; journey: JourneyDto }
  | { kind: 'move' }
  | null

export function DeviceTripsTab() {
  const { t } = useTranslation(['trips', 'common', 'errors'])
  const { device, autoRefresh: refresh } = useOutletContext<DevicePageContext>()
  const location = useLocation()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()

  const filter: TripListFilter = parseFilter(searchParams.get('journey'))
  const canManage: boolean = device.permissions.canModifySettings
  const listKey: string = `${device.deviceId}|${filter}`

  // Bumped by every change the reader makes; reloads journeys, the list and the
  // open journey's totals. `replaceNextRef` tells the list load to replace
  // rather than merge, because the change may have moved trips out of view.
  const [changeToken, setChangeToken] = useState<number>(0)
  const replaceNextRef = useRef<boolean>(false)

  const [journeyList, setJourneyList] = useState<{ deviceId: string; value: JourneyListDto } | null>(null)
  const [journeysError, setJourneysError] = useState<string>('')

  const [list, setList] = useState<TripList | null>(null)
  const [tripsError, setTripsError] = useState<{ key: string; message: string } | null>(null)
  const [isLoadingTrips, setIsLoadingTrips] = useState<boolean>(false)
  const [isLoadingOlder, setIsLoadingOlder] = useState<boolean>(false)

  const [isSelecting, setIsSelecting] = useState<boolean>(false)
  const [selection, setSelection] = useState<{ key: string; ids: ReadonlySet<number> }>({
    key: '',
    ids: NO_SELECTION,
  })

  const [dialog, setDialog] = useState<DialogState>(null)
  const [isBusy, setIsBusy] = useState<boolean>(false)
  const [actionError, setActionError] = useState<string>('')
  const [notice, setNotice] = useState<string>('')

  // ---- Journeys ----
  useEffect(() => {
    let canceled = false
    fetchJourneys(device.deviceId)
      .then((value) => {
        if (!canceled) {
          setJourneyList({ deviceId: device.deviceId, value })
          setJourneysError('')
        }
      })
      .catch((error: unknown) => {
        if (!canceled) {
          setJourneysError(describeError(error, t('errors:loadJourneysFailed')))
        }
      })
    return () => {
      canceled = true
    }
    // `t` is deliberately absent: a language switch must not refetch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [device.deviceId, refresh.token, changeToken])

  const journeys: JourneyListDto | null =
    journeyList !== null && journeyList.deviceId === device.deviceId ? journeyList.value : null
  const tree: JourneyTree = useMemo(() => buildJourneyTree(journeys?.journeys ?? []), [journeys])
  const openJourney: JourneyDto | null =
    filter !== 'all' && filter !== 'none' ? (tree.byId.get(filter) ?? null) : null
  const isUnknownJourney: boolean =
    journeys !== null && filter !== 'all' && filter !== 'none' && openJourney === null

  // ---- The newest page of trips ----
  // On a new view (or after a change) it replaces the list; on a refresh tick it
  // is spliced over the head of what is already loaded.
  useEffect(() => {
    let canceled = false
    const key: string = listKey

    const load = async (): Promise<void> => {
      setIsLoadingTrips(true)
      try {
        const page = await fetchTrips(device.deviceId, { journey: filter, limit: TRIP_PAGE_SIZE })
        if (canceled) {
          return
        }
        const replace: boolean = replaceNextRef.current
        replaceNextRef.current = false
        setList((current) => {
          if (current === null || current.key !== key || replace) {
            return {
              key,
              trips: page.items,
              nextCursor: page.nextCursor,
              isRecalculating: page.isRecalculating,
            }
          }
          const merged: TripSummaryDto[] = mergeNewestPage(current.trips, page.items, page.nextCursor !== null)
          return {
            key,
            trips: merged,
            // Older rows were kept: the next page starts where they end.
            nextCursor: merged.length > page.items.length ? current.nextCursor : page.nextCursor,
            isRecalculating: page.isRecalculating,
          }
        })
        setTripsError(null)
      } catch (error) {
        if (!canceled) {
          setTripsError({ key, message: describeError(error, t('errors:loadTripsFailed')) })
        }
      } finally {
        if (!canceled) {
          setIsLoadingTrips(false)
        }
      }
    }

    void load()
    return () => {
      canceled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [listKey, refresh.token, changeToken])

  const shown: TripList | null = list !== null && list.key === listKey ? list : null
  const shownTrips: readonly TripSummaryDto[] = shown?.trips ?? NO_TRIPS
  const days: TripDay[] = useMemo(() => groupTripsByDay(shownTrips), [shownTrips])

  // ---- Filling in rows the server left pending ----
  // One at a time, so a page full of pending rows is not a burst of requests.
  // A result is applied only to the list it was asked for.
  const fillingRef = useRef<Set<number>>(new Set<number>())
  useEffect(() => {
    if (shown === null) {
      return
    }
    const pending: TripSummaryDto[] = shown.trips.filter(
      (trip) => trip.statsPending && !fillingRef.current.has(trip.id),
    )
    if (pending.length === 0) {
      return
    }

    let canceled = false
    const key: string = shown.key
    const run = async (): Promise<void> => {
      for (const trip of pending) {
        if (canceled) {
          return
        }
        fillingRef.current.add(trip.id)
        try {
          const fresh: TripSummaryDto = await fetchTrip(device.deviceId, trip.id)
          setList((current) =>
            current === null || current.key !== key ? current : { ...current, trips: replaceTrip(current.trips, fresh) },
          )
        } catch {
          // Left pending; the next refresh tries again.
        } finally {
          fillingRef.current.delete(trip.id)
        }
      }
    }

    void run()
    return () => {
      canceled = true
    }
  }, [shown, device.deviceId])

  // ---- Selection ----
  const selectedIds: ReadonlySet<number> = selection.key === listKey ? selection.ids : NO_SELECTION
  // Only what is still on screen: a trip a refresh merged away cannot be moved.
  const selectedTripIds: number[] = shownTrips.filter((trip) => selectedIds.has(trip.id)).map((trip) => trip.id)
  const allShownSelected: boolean =
    shownTrips.length > 0 && shownTrips.every((trip) => selectedIds.has(trip.id))

  function toggleSelected(tripId: number): void {
    const next = new Set<number>(selectedIds)
    if (next.has(tripId)) {
      next.delete(tripId)
    } else if (next.size < MAX_TRIPS_PER_ASSIGN) {
      next.add(tripId)
    }
    setSelection({ key: listKey, ids: next })
  }

  function selectAllShown(): void {
    const next = new Set<number>(selectedIds)
    for (const trip of shownTrips) {
      if (next.size >= MAX_TRIPS_PER_ASSIGN) {
        break
      }
      next.add(trip.id)
    }
    setSelection({ key: listKey, ids: next })
  }

  function clearSelection(): void {
    setSelection({ key: listKey, ids: NO_SELECTION })
  }

  function stopSelecting(): void {
    clearSelection()
    setIsSelecting(false)
  }

  // ---- Actions ----
  function reloadAfterChange(): void {
    replaceNextRef.current = true
    setChangeToken((current) => current + 1)
  }

  function linkFor(target: TripListFilter): To {
    return { search: target === 'all' ? '' : `?journey=${encodeURIComponent(target)}` }
  }

  function openDialog(next: DialogState): void {
    setActionError('')
    setNotice('')
    setDialog(next)
  }

  function openCreate(withRange: boolean, parentId: string | null): void {
    openDialog({ kind: 'journey', mode: { kind: 'create', parentId, withRange } })
  }

  async function loadOlder(): Promise<void> {
    if (shown === null || shown.nextCursor === null || isLoadingOlder) {
      return
    }
    const key: string = shown.key
    const before: string = shown.nextCursor

    setIsLoadingOlder(true)
    try {
      const page = await fetchTrips(device.deviceId, { journey: filter, before, limit: TRIP_PAGE_SIZE })
      setList((current) =>
        current === null || current.key !== key
          ? current
          : { ...current, trips: appendOlderPage(current.trips, page.items), nextCursor: page.nextCursor },
      )
    } catch (error) {
      setTripsError({ key, message: describeError(error, t('errors:loadMoreTripsFailed')) })
    } finally {
      setIsLoadingOlder(false)
    }
  }

  async function assign(journeyId: string | null, automatic: boolean): Promise<void> {
    if (selectedTripIds.length === 0 || isBusy) {
      return
    }
    setIsBusy(true)
    setActionError('')
    setNotice('')
    try {
      const result = await assignTrips(device.deviceId, {
        tripIds: selectedTripIds,
        journeyId,
        automatic,
      })
      clearSelection()
      setDialog(null)
      setNotice(t('trips:selection.moved', { count: result.moved, value: formatInteger(result.moved) }))
      reloadAfterChange()
    } catch (error) {
      setActionError(describeError(error, t('errors:moveTripsFailed')))
    } finally {
      setIsBusy(false)
    }
  }

  async function confirmDelete(journey: JourneyDto): Promise<void> {
    setIsBusy(true)
    setActionError('')
    try {
      await deleteJourney(device.deviceId, journey.id)
      setDialog(null)
      // The deleted journey's contents went to its parent, so that is where to look.
      void navigate(linkFor(journey.parentId ?? 'all'))
      reloadAfterChange()
    } catch (error) {
      setActionError(describeError(error, t('errors:deleteJourneyFailed')))
    } finally {
      setIsBusy(false)
    }
  }

  function handleJourneySaved(saved: JourneyDto, created: boolean): void {
    setDialog(null)
    reloadAfterChange()
    if (created) {
      void navigate(linkFor(saved.id))
    }
  }

  // ---- Rendering ----
  const currentLabel: string =
    filter === 'all'
      ? t('trips:sidebar.allTrips')
      : filter === 'none'
        ? t('trips:sidebar.noJourney')
        : openJourney !== null
          ? journeyPathLabel(tree, openJourney.id)
          : '…'

  // A new journey made from the list goes inside the open one when it fits.
  const defaultParent: string | null =
    openJourney !== null && canPlaceUnder(tree, null, openJourney.id) ? openJourney.id : null

  const statusMessage: string =
    actionError.length > 0 && dialog === null
      ? actionError
      : notice.length > 0
        ? notice
        : tripsError !== null && tripsError.key === listKey
          ? tripsError.message
          : journeysError

  return (
    <div className="trips-layout">
      <JourneySidebar
        tree={tree}
        allTripCount={journeys?.allTripCount ?? 0}
        unassignedTripCount={journeys?.unassignedTripCount ?? 0}
        filter={filter}
        linkFor={linkFor}
        currentLabel={currentLabel}
        canManage={canManage}
        onNewJourney={() => openCreate(false, null)}
      />

      <section className="trips-main" aria-label={currentLabel}>
        {shown?.isRecalculating ? (
          <div className="banner banner--info trips-banner" role="status">
            <span className="banner-icon" aria-hidden="true">⏳</span>
            <div className="banner-text">
              <p className="banner-title">{t('trips:list.recalculating')}</p>
              <p className="banner-detail">{t('trips:list.recalculatingDetail')}</p>
            </div>
          </div>
        ) : null}

        {openJourney !== null ? (
          <JourneyOverview
            deviceId={device.deviceId}
            journey={openJourney}
            tree={tree}
            reloadToken={changeToken}
            canManage={canManage}
            linkFor={linkFor}
            onEdit={() => openDialog({ kind: 'journey', mode: { kind: 'edit', journey: openJourney } })}
            onDelete={() => openDialog({ kind: 'delete', journey: openJourney })}
            onAddSubJourney={() => openCreate(false, openJourney.id)}
          />
        ) : !isUnknownJourney ? (
          <h3 className="trips-view-title">
            <span aria-hidden="true">{filter === 'none' ? '📥 ' : '🚗 '}</span>
            {currentLabel}
          </h3>
        ) : null}

        {isUnknownJourney ? (
          <div className="empty-state">
            <span className="empty-state-icon" aria-hidden="true">🧭</span>
            <h3>{t('trips:list.unknownJourneyTitle')}</h3>
            <p>{t('trips:list.unknownJourneyBody')}</p>
            <Link to={linkFor('all')} className="btn btn-secondary">
              {t('trips:list.showAll')}
            </Link>
          </div>
        ) : (
          <>
            <div className="trips-toolbar">
              {canManage && shownTrips.length > 0 ? (
                <button
                  type="button"
                  className={`btn btn-sm ${isSelecting ? 'btn-primary' : 'btn-secondary'}`}
                  onClick={() => (isSelecting ? stopSelecting() : setIsSelecting(true))}
                  aria-pressed={isSelecting}
                >
                  {isSelecting ? t('trips:list.doneSelecting') : t('trips:list.select')}
                </button>
              ) : null}
              {canManage ? (
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={() => openCreate(true, defaultParent)}
                >
                  ＋ {t('trips:list.journeyFromDates')}
                </button>
              ) : null}
              <span className="trips-toolbar-spacer" />
              <RefreshToolbar autoRefresh={refresh} isLoading={isLoadingTrips} />
            </div>

            <p className="hint" role="status" style={{ marginBottom: 12 }}>
              {statusMessage}
            </p>

            {shown === null ? (
              tripsError !== null && tripsError.key === listKey ? null : (
                <div className="loading-state">
                  <div className="spinner" />
                  <span>{t('trips:list.loading')}</span>
                </div>
              )
            ) : shownTrips.length === 0 ? (
              <TripListEmpty
                filter={filter}
                hasSubJourneys={openJourney !== null && childrenOf(tree, openJourney.id).length > 0}
              />
            ) : (
              <div className="trip-days">
                {days.map((day) => (
                  <TripDayGroup key={day.key} day={day}>
                    {day.trips.map((trip) => (
                      <TripRow
                        key={trip.id}
                        trip={trip}
                        to={{ pathname: String(trip.id), search: location.search }}
                        selectable={isSelecting && canManage}
                        selected={selectedIds.has(trip.id)}
                        onToggleSelected={toggleSelected}
                        journeyName={
                          filter === 'all' && trip.journeyId !== null
                            ? (tree.byId.get(trip.journeyId)?.name ?? null)
                            : null
                        }
                      />
                    ))}
                  </TripDayGroup>
                ))}
              </div>
            )}

            {shown !== null && shown.nextCursor !== null ? (
              <div className="trips-more">
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => void loadOlder()}
                  disabled={isLoadingOlder}
                >
                  {isLoadingOlder ? t('trips:list.loadingOlder') : t('trips:list.loadOlder')}
                </button>
              </div>
            ) : null}

            {isSelecting && canManage ? (
              <TripSelectionBar
                count={selectedTripIds.length}
                allShownSelected={allShownSelected}
                isBusy={isBusy}
                onMove={() => openDialog({ kind: 'move' })}
                onRemove={() => void assign(null, false)}
                onAutomatic={() => void assign(null, true)}
                onSelectAllShown={selectAllShown}
                onClear={clearSelection}
              />
            ) : null}
          </>
        )}
      </section>

      {dialog?.kind === 'journey' ? (
        <JourneyDialog
          deviceId={device.deviceId}
          tree={tree}
          mode={dialog.mode}
          onSaved={(saved) => handleJourneySaved(saved, dialog.mode.kind === 'create')}
          onCancel={() => setDialog(null)}
        />
      ) : null}

      {dialog?.kind === 'delete' ? (
        <JourneyDeleteDialog
          journey={dialog.journey}
          parentName={
            dialog.journey.parentId === null ? null : (tree.byId.get(dialog.journey.parentId)?.name ?? null)
          }
          subJourneyCount={childrenOf(tree, dialog.journey.id).length}
          isBusy={isBusy}
          errorMessage={actionError}
          onConfirm={() => void confirmDelete(dialog.journey)}
          onCancel={() => setDialog(null)}
        />
      ) : null}

      {dialog?.kind === 'move' ? (
        <JourneyMoveDialog
          tree={tree}
          tripCount={selectedTripIds.length}
          initialJourneyId={openJourney?.id ?? null}
          isBusy={isBusy}
          errorMessage={actionError}
          onMove={(journeyId) => void assign(journeyId, false)}
          onCancel={() => setDialog(null)}
        />
      ) : null}
    </div>
  )
}

// ?journey= as a view: "all" (also when absent or malformed), "none", or a
// journey id. Ids are compared lower-case, which is how the API writes them.
function parseFilter(value: string | null): TripListFilter {
  if (value === 'none') {
    return 'none'
  }
  if (value !== null && UUID_PATTERN.test(value)) {
    return value.toLowerCase()
  }
  return 'all'
}
