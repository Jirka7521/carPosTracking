// ============================================================
// PositionListTab — the "Positions" tab inside DevicePage.
//
// Displays a paginated table of GPS positions, newest-first by default
// so the most recent fix is at the top until the reader sorts otherwise.
//
// Features:
//   • Date range pickers matching those on the Map tab — chosen once on mount
//     and changed only by the user; a refresh re-runs the same query
//   • "Refresh" button and an auto-refresh toggle, neither of which resets the
//     page the user is on
//   • Click any column header to sort by it; click again to reverse
//   • Zebra-striped table with blue header row
//   • The newest row is highlighted with a light-blue tint and a
//     "Latest" badge in the timestamp column, whichever way the table is sorted
//   • Coordinates displayed in monospace for alignment
//   • Pagination: N rows per page with Prev / Next controls
//
// The API hands out at most 1000 fixes per request, so a wide range holds more
// than one answer's worth. Rather than fetching all of them up front, the table
// pulls another chunk only when the reader pages past what is loaded — see
// services/positionPager.ts for how the window is walked. The one thing that
// cannot be done a chunk at a time is SORTING by anything other than the API's
// own newest-first order, so that pulls the rest of the range in first.
// ============================================================

import { useEffect, useMemo, useRef, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import RangeToolbar from '../components/RangeToolbar'
import type { DevicePageContext } from './DevicePage'
import { useAutoRefresh } from '../hooks/useAutoRefresh'
import type { PositionDto } from '../services/apiTypes'
import { fetchPositionChunk, mergeNewest } from '../services/positionPager'
import type { DateRange } from '../utils/dates'
import { datetimeLocalToIso, getDefaultDateRange, parseApiTimestamp } from '../utils/dates'
import { describeError } from '../utils/errors'

const PAGE_SIZE_OPTIONS = [25, 50, 100] as const
type PageSize = (typeof PAGE_SIZE_OPTIONS)[number]

// How many seconds between automatic refreshes when the toggle is on
const AUTO_REFRESH_SEC = 30

// Ceiling on how far a "load the rest so I can sort it" walk will go, matching
// the Charts and Map tabs. Fifty sequential requests is already a long wait; a
// table sorted across more rows than this is not what anyone is asking for.
const MAX_TABLE_ROWS = 50_000

// ---- Sorting ----

type SortKey =
  | 'timestamp'
  | 'latitude'
  | 'longitude'
  | 'speedKmph'
  | 'altitudeMeters'
  | 'batteryPct'
  | 'accelXG'
  | 'accelYG'
  | 'accelZG'
  | 'temperatureC'

type SortDir = 'asc' | 'desc'

type Sort = { key: SortKey; dir: SortDir }

// Whether a sort happens to be the order the API already returns rows in.
//
// This is the hinge the whole lazy-loading design turns on. Newest-first by fix
// time means page 1 is the first thousand rows fetched and page 40 is the next
// thousand, so chunks can be pulled as the reader walks forward. ANY other
// order — including timestamp ASCENDING, whose very first row is the oldest fix
// in the window — can only be produced from the complete set.
function isApiOrder(sort: Sort): boolean {
  return sort.key === 'timestamp' && sort.dir === 'desc'
}

// The sortable columns, in display order. The leading "#" column is deliberately
// absent: it numbers the current sort order, so it has nothing of its own to sort
// by. Header row and comparator both read this, so a column can only ever be
// added in one place.
const COLUMNS: ReadonlyArray<{ key: SortKey; label: string }> = [
  { key: 'timestamp',      label: 'Timestamp' },
  { key: 'latitude',       label: 'Latitude' },
  { key: 'longitude',      label: 'Longitude' },
  { key: 'speedKmph',      label: 'Speed' },
  { key: 'altitudeMeters', label: 'Altitude' },
  { key: 'batteryPct',     label: 'Battery' },
  { key: 'accelXG',        label: 'Accel X (g)' },
  { key: 'accelYG',        label: 'Accel Y (g)' },
  { key: 'accelZG',        label: 'Accel Z (g)' },
  { key: 'temperatureC',   label: 'Temp' },
]

// The single number a row is ranked by, or null when it has none to rank —
// null rows always sink to the bottom, whichever direction is active (see below).
function sortValue(position: PositionDto, key: SortKey): number | null {
  if (key === 'timestamp') {
    return parseApiTimestamp(position.timestamp)?.getTime() ?? null
  }

  // 0 is the agreed "charging" SENTINEL, not a flat pack (see formatBattery), so
  // it is not a percentage that can be ranked. Group it with the em-dash rows at
  // the bottom rather than ranking a charging device below a 1% one.
  if (key === 'batteryPct') {
    return position.batteryPct === 0 ? null : position.batteryPct
  }

  return position[key]
}

// Format one accelerometer axis (in g) for the table, or an em dash when the
// device sent no reading (accelerometer disabled or firmware predating it).
function formatAccel(value: number | null): string {
  return value === null ? '—' : value.toFixed(2)
}

// Battery as the device reports it: 0 is the agreed "charging" SENTINEL (see
// BatteryBadge), not a flat pack; null means the device sent no reading at all.
// Rendered as plain text rather than a <BatteryBadge> so the column stays as
// dense and as aligned as the numeric ones beside it.
function formatBattery(value: number | null): string {
  if (value === null) {
    return '—'
  }
  return value === 0 ? 'Charging' : `${value}%`
}

// Format the modem die temperature (°C) for the table, or an em dash when the
// device sent no reading (older firmware or the sensor unsupported).
function formatTemperature(value: number | null): string {
  return value === null ? '—' : `${value.toFixed(1)} °C`
}

// Formats an API timestamp to a readable local date/time string.
function formatTimestamp(value: string): string {
  const parsed = parseApiTimestamp(value)
  return parsed === null ? value : parsed.toLocaleString(undefined, { hour12: false })
}

export function PositionListTab() {
  const { device } = useOutletContext<DevicePageContext>()

  // Every position fetched SO FAR, newest-first — not necessarily the whole
  // range. More chunks are appended to the old end as the reader needs them.
  const [positions, setPositions]         = useState<PositionDto[]>([])
  const [isLoading, setIsLoading]         = useState<boolean>(false)
  const [statusMessage, setStatusMessage] = useState<string>('')

  // The `to` bound the next chunk reaches back from, or null once the window is
  // exhausted — so `cursor !== null` is exactly "there may be older rows".
  const [cursor, setCursor] = useState<string | null>(null)

  // A chunk fetch triggered by paging or sorting, as opposed to the initial
  // load. Keeps the table on screen while it runs.
  const [isLoadingMore, setIsLoadingMore] = useState<boolean>(false)

  // Date range controls. Computed once, on mount — from here on only the two
  // inputs change it, so a reload can never move the window under the user.
  const [dateRange, setDateRange] = useState<DateRange>(getDefaultDateRange)

  // Auto-refresh: bumps a token to re-run the query, never the date range
  const refresh = useAutoRefresh(AUTO_REFRESH_SEC)

  // Current page index (0-based)
  const [page, setPage]         = useState<number>(0)
  const [pageSize, setPageSize] = useState<PageSize>(25)

  // Which column the table is ordered by. Seeded to the order the API already
  // returns, so the first render is exactly what it was before sorting existed.
  // This is a view preference, so it deliberately survives a refresh tick and a
  // switch to another device — only the reader changes it.
  const [sort, setSort] = useState<Sort>({
    key: 'timestamp',
    dir: 'desc',
  })

  // ---- Loading bookkeeping ----
  // Chunk loading is driven by button clicks as well as by the effect, and both
  // paths need the CURRENT rows / cursor mid-walk rather than the ones captured
  // when the handler was created. Refs mirror the state for exactly that.
  const positionsRef    = useRef<PositionDto[]>([])
  const cursorRef       = useRef<string | null>(null)
  const seenIdsRef      = useRef<Set<number>>(new Set<number>())
  const isLoadingMoreRef = useRef<boolean>(false)

  // Which query the loaded rows belong to. A refresh tick re-runs the effect
  // with this unchanged, which is how a top-up is told apart from a fresh load.
  const loadedQueryKey = useRef<string>('')

  // Bumped whenever a fresh load starts. A chunk walk still in flight from the
  // previous device or range compares against it after every hop and drops what
  // it fetched — the alternative is rows from two different windows interleaved
  // in one table.
  const generationRef = useRef<number>(0)

  // The loader effect must not re-run when the sort changes (that would refetch
  // the range from scratch), but it does need to KNOW the sort: a range change
  // while a non-API order is active has to load everything, not just chunk one.
  // Mirrored in an effect rather than during render, which is not a safe place
  // to write a ref.
  const sortRef = useRef<Sort>(sort)
  useEffect(() => {
    sortRef.current = sort
  }, [sort])

  function applyPositions(next: PositionDto[]): void {
    positionsRef.current = next
    setPositions(next)
  }

  function applyCursor(next: string | null): void {
    cursorRef.current = next
    setCursor(next)
  }

  // Is there anything older still to fetch?
  const hasMore: boolean = cursor !== null

  // Back to the first page when the data SOURCE changes — a different device or
  // a different window. Deliberately NOT on every load: a refresh has to leave
  // the reader where they were.
  //
  // The device arrives as a prop and this component stays mounted when the route
  // switches to another device, so that case is adjusted during render — React's
  // documented alternative to a reset-in-an-effect, which would render the wrong
  // page once before correcting itself.
  const [lastDeviceId, setLastDeviceId] = useState<string>(device.deviceId)
  if (lastDeviceId !== device.deviceId) {
    setLastDeviceId(device.deviceId)
    setPage(0)
  }

  // A range change comes from the toolbar, so it can be handled directly
  function handleRangeChange(next: DateRange): void {
    setDateRange(next)
    setPage(0)
  }

  // Walks further back through the window until at least `targetRows` rows are
  // loaded, or the window runs out. Every hop is one request, and they cannot be
  // issued in parallel — each cursor comes out of the previous answer.
  //
  // Returns rather than throwing on failure: the rows already on screen stay,
  // the status line says what happened, and the cursor is untouched so the next
  // click retries from the same place.
  async function loadMoreChunks(targetRows: number): Promise<void> {
    if (cursorRef.current === null || isLoadingMoreRef.current) {
      return
    }

    const generation: number = generationRef.current
    const fromIso: string | undefined = datetimeLocalToIso(dateRange.from)

    isLoadingMoreRef.current = true
    setIsLoadingMore(true)

    try {
      while (cursorRef.current !== null && positionsRef.current.length < targetRows) {
        const chunk = await fetchPositionChunk(
          device.deviceId, fromIso, cursorRef.current, seenIdsRef.current,
        )

        // The device or the range moved while this was in flight; the rows just
        // fetched belong to a window nobody is looking at.
        if (generationRef.current !== generation) {
          return
        }

        // The walk only ever moves backwards in time, so the new rows are older
        // than everything already held — appending keeps the newest-first order
        // the "Latest" badge and the cursor both rely on.
        applyPositions([...positionsRef.current, ...chunk.rows])
        applyCursor(chunk.nextCursor)
      }

      // The status line was last written by the initial load, or by the sort
      // that started this walk — either way it is now out of date.
      const total: number = positionsRef.current.length
      setStatusMessage(
        cursorRef.current === null
          ? `${total.toLocaleString()} position${total === 1 ? '' : 's'} loaded — the whole range.`
          : `${total.toLocaleString()} position${total === 1 ? '' : 's'} loaded so far.`,
      )
    } catch (error) {
      if (generationRef.current === generation) {
        setStatusMessage(describeError(error, 'Failed to load more positions.'))
      }
    } finally {
      isLoadingMoreRef.current = false
      setIsLoadingMore(false)
    }
  }

  // Clicking the active column reverses it; clicking any other one switches to it
  // descending — largest / newest first, matching the default view. Either way the
  // page the reader was on no longer means anything, so go back to the first.
  //
  // Any order other than the API's own is a claim about the WHOLE range —
  // "the fastest fix" is a lie if it is only the fastest of the first chunk — so
  // the rest of the window is pulled in behind it. The sort applies immediately
  // to what is loaded and widens as the walk lands, rather than leaving the
  // reader looking at an unchanged table until it finishes.
  function handleSortClick(key: SortKey): void {
    const next: Sort =
      sort.key === key
        ? { key, dir: sort.dir === 'asc' ? 'desc' : 'asc' }
        : { key, dir: 'desc' }

    setSort(next)
    setPage(0)

    if (!isApiOrder(next) && cursorRef.current !== null) {
      setStatusMessage('Loading the rest of the range so the sort covers all of it…')
      void loadMoreChunks(MAX_TABLE_ROWS)
    }
  }

  // Load positions on mount, on a range change, and on every refresh tick.
  //
  // Three different loads share this effect:
  //
  //   • a new device or range fetches the FIRST chunk only, and the reader pulls
  //     the rest by paging — unless a non-API sort is active, which needs the
  //     whole window before it means anything;
  //   • a refresh tick fetches the newest chunk and merges it, leaving the
  //     chunks already walked in place and the reader on their page.
  useEffect(() => {
    let canceled = false

    const fromIso = datetimeLocalToIso(dateRange.from)
    const toIso   = datetimeLocalToIso(dateRange.to)

    const queryKey = `${device.deviceId}|${fromIso ?? ''}|${toIso ?? ''}`
    const isTopUp  = loadedQueryKey.current === queryKey

    const load = async (): Promise<void> => {
      setIsLoading(true)

      if (!isTopUp) {
        // Abandon any chunk walk still running for the previous window, and
        // start the accumulation over.
        generationRef.current += 1
        seenIdsRef.current = new Set<number>()
        applyPositions([])
        applyCursor(null)
      }

      const generation: number = generationRef.current

      try {
        if (isTopUp) {
          // One request for the newest chunk. Its ids join `seenIds` so a later
          // chunk walk does not hand back rows the merge already placed.
          const chunk = await fetchPositionChunk(
            device.deviceId, fromIso, toIso, new Set<number>(),
          )
          if (canceled || generationRef.current !== generation) {
            return
          }
          for (const position of chunk.rows) {
            seenIdsRef.current.add(position.id)
          }
          applyPositions(mergeNewest(positionsRef.current, chunk.rows))
        } else {
          const chunk = await fetchPositionChunk(
            device.deviceId, fromIso, toIso, seenIdsRef.current,
          )
          if (canceled || generationRef.current !== generation) {
            return
          }
          applyPositions(chunk.rows)
          applyCursor(chunk.nextCursor)
          loadedQueryKey.current = queryKey
        }

        const total = positionsRef.current.length
        setStatusMessage(
          total === 0
            ? 'No positions found for this time range.'
            : `${total.toLocaleString()} position${total === 1 ? '' : 's'} loaded.`,
        )
      } catch (error) {
        if (canceled) {
          return
        }
        // Keep the rows already on screen — a momentary network blip should
        // report itself in the status line, not empty the table.
        setStatusMessage(describeError(error, 'Failed to load positions.'))
      } finally {
        if (!canceled) {
          setIsLoading(false)
        }
      }

      // A sort the reader had already chosen outlives a range change, and it
      // still needs the whole window to mean anything.
      if (!canceled && !isTopUp && !isApiOrder(sortRef.current)) {
        await loadMoreChunks(MAX_TABLE_ROWS)
      }
    }

    void load()
    return () => {
      canceled = true
    }
    // loadMoreChunks is deliberately absent: it is re-created on every render,
    // so listing it would re-fetch the range on every keystroke. Everything it
    // actually reads — the device, the range, the cursor — is either a
    // dependency here already or held in a ref precisely so it stays current.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [device.deviceId, dateRange.from, dateRange.to, refresh.token])

  // ---- Sorting ----
  // Sorts a COPY: `positions` holds the API's newest-first order, which is what
  // latestId and the chunk cursor both rely on. Sorting is stable, so rows that
  // tie on the chosen column keep that newest-first order relative to each other.
  //
  // Sorts what is LOADED. For any order other than the API's own, handleSortClick
  // has already started pulling the rest of the window in, and this re-runs as
  // each chunk lands — so the ranking widens to the whole range as it arrives
  // rather than being wrong about it permanently.
  const sortedPositions = useMemo(() => {
    return [...positions].sort((a, b) => {
      const left  = sortValue(a, sort.key)
      const right = sortValue(b, sort.key)

      // Rows with no reading sink to the bottom in BOTH directions — an em dash
      // has no rank, so it should never displace real readings at the top.
      if (left === null || right === null) {
        return left === right ? 0 : left === null ? 1 : -1
      }

      return sort.dir === 'asc' ? left - right : right - left
    })
  }, [positions, sort.key, sort.dir])

  // The newest fix, wherever the current sort puts it. `positions` is the API's
  // newest-first order, so that is simply its first row.
  const latestId = positions.length === 0 ? null : positions[0].id

  // ---- Pagination calculations ----
  // "Total" here means total LOADED. While `hasMore` is true there are older
  // rows the reader has not asked for yet, so every count below is a floor
  // rather than a total, and the footer says so.
  const totalPages    = Math.max(1, Math.ceil(positions.length / pageSize))
  const currentPage   = Math.min(page, totalPages - 1) // clamp after data reload
  const pageStart     = currentPage * pageSize
  const pageEnd       = Math.min(pageStart + pageSize, positions.length)
  const pageRows      = sortedPositions.slice(pageStart, pageEnd)

  // Paging forward past the loaded rows is what triggers the next request. The
  // page only advances once the rows behind it exist, so the reader never lands
  // on a blank table.
  //
  // One page beyond the last loaded row is enough to ask for: the chunk that
  // arrives holds a thousand of them, which is ten more pages at the largest
  // page size.
  async function handleNextPage(): Promise<void> {
    const nextPage: number = currentPage + 1
    const rowsNeeded: number = (nextPage + 1) * pageSize

    if (positions.length < rowsNeeded && cursorRef.current !== null) {
      await loadMoreChunks(rowsNeeded)
    }

    // Clamped on render, so a window that ran out mid-page simply stays put.
    setPage(nextPage)
  }

  // Forward is blocked only when there is neither a loaded page nor an
  // unfetched chunk left to go to.
  const canGoNext: boolean = (currentPage < totalPages - 1 || hasMore) && !isLoadingMore

  return (
    <div>
      {/* ---- Controls: date pickers + refresh options ---- */}
      <RangeToolbar
        range={dateRange}
        onRangeChange={handleRangeChange}
        autoRefresh={refresh}
        isLoading={isLoading}
        idPrefix="pos"
        className="position-list-controls"
      />

      {/* Status line */}
      <p className="hint" role="status" style={{ marginBottom: 12 }}>
        {statusMessage}
      </p>

      {/* ---- Loading state ----
           Only while there is nothing to show yet. A refresh must not replace
           the table the user is reading with a spinner. */}
      {isLoading && positions.length === 0 ? (
        <div className="loading-state">
          <div className="spinner" />
          <span>Loading positions…</span>
        </div>
      ) : positions.length === 0 ? (
        /* ---- Empty state ---- */
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">📍</span>
          <h3>No positions</h3>
          <p>No GPS fixes were recorded in the selected time range.</p>
        </div>
      ) : (
        /* ---- Position table ---- */
        <>
          <div className="position-table-wrapper">
            <table className="position-table">
              <thead>
                <tr>
                  <th scope="col">#</th>

                  {/* Each sortable header is a <button> rather than a click
                      handler on the <th>: that way it is reachable by Tab and
                      announced as a control without any extra ARIA. */}
                  {COLUMNS.map((column) => {
                    const isActive = sort.key === column.key

                    return (
                      <th
                        key={column.key}
                        scope="col"
                        aria-sort={
                          isActive
                            ? sort.dir === 'asc'
                              ? 'ascending'
                              : 'descending'
                            : 'none'
                        }
                      >
                        <button
                          type="button"
                          className="th-sort"
                          onClick={() => handleSortClick(column.key)}
                          /* A second sort while the first is still pulling
                             chunks would start a walk the guard in
                             loadMoreChunks silently drops, leaving the new
                             column ranked over a partial range. */
                          disabled={isLoadingMore}
                        >
                          {column.label}
                          <span
                            className={`th-sort-icon${isActive ? ' th-sort-icon--active' : ''}`}
                            aria-hidden="true"
                          >
                            {isActive ? (sort.dir === 'asc' ? '▲' : '▼') : '↕'}
                          </span>
                        </button>
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody>
                {pageRows.map((position, rowIndex) => {
                  // Follows the newest fix wherever the sort puts it — and is
                  // simply off-screen when that row is on another page.
                  const isLatest = position.id === latestId

                  return (
                    <tr
                      key={position.id}
                      className={isLatest ? 'position-row--latest' : ''}
                    >
                      {/* Position in the whole sorted list, not page-relative */}
                      <td style={{ color: 'var(--text-muted)', width: '4rem' }}>
                        {pageStart + rowIndex + 1}
                      </td>

                      {/* Timestamp with optional "Latest" badge */}
                      <td>
                        {formatTimestamp(position.timestamp)}
                        {isLatest ? (
                          <span className="latest-badge" aria-label="Latest position">
                            Latest
                          </span>
                        ) : null}
                      </td>

                      {/* Coordinates in monospace */}
                      <td className="position-coord">{position.latitude.toFixed(6)}</td>
                      <td className="position-coord">{position.longitude.toFixed(6)}</td>

                      {/* As reported by the receiver — not derived from the
                          track, so a stationary vehicle may still show a small
                          non-zero speed. */}
                      <td className="position-coord">{position.speedKmph.toFixed(1)} km/h</td>
                      <td className="position-coord">{Math.round(position.altitudeMeters)} m</td>

                      {/* "Charging" rather than 0% — see formatBattery. */}
                      <td className="position-coord">{formatBattery(position.batteryPct)}</td>

                      {/* Raw instantaneous ADXL345 sample; em dash when the
                          device sent none for this fix. */}
                      <td className="position-coord">{formatAccel(position.accelXG)}</td>
                      <td className="position-coord">{formatAccel(position.accelYG)}</td>
                      <td className="position-coord">{formatAccel(position.accelZG)}</td>
                      <td className="position-coord">{formatTemperature(position.temperatureC)}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          {/* ---- Pagination bar ---- */}
          <div className="pagination">
            <span>
              Showing {(pageStart + 1).toLocaleString()}–{pageEnd.toLocaleString()} of{' '}
              {positions.length.toLocaleString()} positions
              {hasMore ? (
                <span style={{ color: 'var(--text-muted)' }}> loaded — more further back</span>
              ) : null}
            </span>

            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', fontSize: '0.875rem' }}>
              <label htmlFor="page-size-select" style={{ color: 'var(--text-muted)' }}>
                Rows per page:
              </label>
              <select
                id="page-size-select"
                className="form-input"
                style={{ width: 'auto', padding: '2px 6px' }}
                value={pageSize}
                onChange={(e) => {
                  setPageSize(Number(e.target.value) as PageSize)
                  setPage(0)
                }}
              >
                {PAGE_SIZE_OPTIONS.map((n) => (
                  <option key={n} value={n}>{n}</option>
                ))}
              </select>
            </div>

            <div className="pagination-buttons">
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => setPage((p) => Math.max(0, p - 1))}
                disabled={currentPage === 0}
                aria-label="Previous page"
              >
                ← Prev
              </button>

              {/* "40+" rather than "40": the page count is of what is loaded,
                  and there is a chunk behind it that has not been asked for. */}
              <span style={{ alignSelf: 'center', padding: '0 4px', fontSize: '0.875rem' }}>
                Page {currentPage + 1} / {totalPages}{hasMore ? '+' : ''}
              </span>

              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => void handleNextPage()}
                disabled={!canGoNext}
                aria-label="Next page"
              >
                {isLoadingMore ? 'Loading…' : 'Next →'}
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  )
}
