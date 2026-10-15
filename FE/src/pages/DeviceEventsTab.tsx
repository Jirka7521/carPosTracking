// ============================================================
// DeviceEventsTab — the "Events" tab inside DevicePage: the tracker's history,
// newest first.
//
// Every row is one moment something changed:
//   • the device went off the air, and why — a regular sleep, a sleep because
//     the car was found parked, the power switch, a flat battery, a fault it
//     caught, or a connection that simply died (its Last Will, published by the
//     broker);
//   • a restart worth knowing about — a crash, a power loss, a cold power-on;
//   • a wake from deep sleep — the regular timer, the accelerometer, the switch;
//   • a step of motion wake — checking for movement, moving, parked, stopped.
// Rows are placed by when they HAPPENED (occurredAt): events the device kept on
// its SD card while out of range arrive in a burst later, but slot in where
// they belong.
//
// Features:
//   • The same date range as the Map, Positions and Charts tabs — one range,
//     held by DevicePage, opening on today and moved only by the reader
//   • A severity filter, "All events" or "Alerts and errors" — a device that
//     sleeps between reports logs several routine events per report, so the
//     problems are easy to lose without it. The filter runs in the API, not here.
//   • Refreshes on the device page's one shared timer, keeping the rows on screen
//     while it does
//   • "Export CSV" writes the range to a file, asking for the column separator
//     first — and, when the range holds more than the table shows, whether the
//     file should hold all of it
//
// Unlike the Positions tab there is no chunked paging ON SCREEN: the API caps one
// answer at EVENTS_LIMIT rows, and a range holding more than that says so and
// asks the reader to narrow it. An event list long enough to need paging is
// better read filtered than scrolled.
//
// The FILE is another matter, because it is read somewhere that can sort and
// filter it. A range that fits in one answer is exported from the rows already on
// screen. One that does not raises a popup — everything, or just the newest
// EVENTS_LIMIT the table shows — and "everything" walks the range back a thousand
// events at a time (services/eventPager.ts), the way the Positions export pulls
// in the rest of its window. The table itself stays as it was.
// ============================================================

import { useEffect, useRef, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import i18n from '../i18n'
import { formatDateTime, formatInteger, formatTime } from '../i18n/format'
import { CsvExportMenu } from '../components/CsvExportMenu'
import { ExportLengthDialog } from '../components/ExportLengthDialog'
import RangeToolbar from '../components/RangeToolbar'
import type { DevicePageContext } from './DevicePage'
import { fetchDeviceEvents } from '../services/apiClient'
import type { DeviceEventDto, DeviceEventSeverity } from '../services/apiTypes'
import { EVENTS_LIMIT, fetchAllDeviceEvents } from '../services/eventPager'
import type { CsvDelimiter } from '../utils/csv'
import { buildCsv, csvFileName } from '../utils/csv'
import { datetimeLocalToIso, parseApiTimestamp } from '../utils/dates'
import { describeSeconds } from '../utils/deviceConfig'
import { EVENT_REASON_LABEL_KEYS, EVENT_SEVERITY_LABEL_KEYS } from '../utils/deviceEvents'
import { downloadTextFile } from '../utils/downloadTextFile'
import { describeError } from '../utils/errors'

// Ceiling on how far an export of the whole range will go, matching the
// Positions tab's. Fifty sequential requests is already a long wait; a file of
// more events than this is better asked for in slices of the range.
const MAX_EXPORT_ROWS = 50_000

// The two filter choices. 'problems' is everything at alert severity and above.
export type SeverityFilter = 'all' | 'problems'

const MIN_SEVERITY: Record<SeverityFilter, DeviceEventSeverity | undefined> = {
  all: undefined,
  problems: 'alert',
}

export function DeviceEventsTab() {
  const { t } = useTranslation(['device', 'common', 'errors'])

  // The device page's timer and device, shared with every other tab — see
  // DevicePage for why there is exactly one. The range and the filter are its
  // too, so they match the other tabs and are still set when the reader returns.
  const {
    device,
    autoRefresh: refresh,
    dateRange,
    setDateRange,
    eventFilter: filter,
    setEventFilter: setFilter,
  } = useOutletContext<DevicePageContext>()

  const [events, setEvents]               = useState<DeviceEventDto[]>([])
  const [isLoading, setIsLoading]         = useState<boolean>(false)
  const [hasLoaded, setHasLoaded]         = useState<boolean>(false)
  const [statusMessage, setStatusMessage] = useState<string>('')

  // The export. `pendingDelimiter` is the separator the reader picked while the
  // "more than the table shows" popup is open — null when no popup is, so it is
  // also the popup's open flag.
  const [pendingDelimiter, setPendingDelimiter] = useState<CsvDelimiter | null>(null)
  const [isExporting, setIsExporting]           = useState<boolean>(false)

  // Bumped when the query an export was started for stops being the one on screen
  // — a new range, filter or device, or leaving the tab. A walk still in flight
  // compares it to the value it started with: a file of events for a range the
  // reader has since moved away from would be worse than no file.
  const exportGenerationRef = useRef<number>(0)
  useEffect(() => {
    return () => {
      exportGenerationRef.current += 1
    }
  }, [device.deviceId, dateRange.from, dateRange.to, filter])

  // Load on mount, on a range or filter change, and on every refresh tick. A
  // failed load keeps the rows already on screen and says so in the status line
  // — a momentary network blip must not empty the table being read.
  useEffect(() => {
    let canceled = false

    const load = async (): Promise<void> => {
      setIsLoading(true)
      try {
        const rows: DeviceEventDto[] = await fetchDeviceEvents(device.deviceId, {
          from: datetimeLocalToIso(dateRange.from),
          to: datetimeLocalToIso(dateRange.to),
          minSeverity: MIN_SEVERITY[filter],
          limit: EVENTS_LIMIT,
        })
        if (canceled) {
          return
        }
        setEvents(rows)
        setHasLoaded(true)
        setStatusMessage(
          rows.length >= EVENTS_LIMIT
            ? t('device:events.truncated', { value: formatInteger(EVENTS_LIMIT) })
            : rows.length === 0
              ? ''
              : t('device:events.loadedCount', { count: rows.length, value: formatInteger(rows.length) }),
        )
      } catch (error) {
        if (!canceled) {
          setStatusMessage(describeError(error, t('errors:loadEventsFailed')))
        }
      } finally {
        if (!canceled) {
          setIsLoading(false)
        }
      }
    }

    void load()
    return () => {
      canceled = true
    }
    // `t` is deliberately absent: a language switch re-renders the rows already,
    // and re-fetching the range for it would be a request for nothing.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [device.deviceId, dateRange.from, dateRange.to, filter, refresh.token])

  function downloadEvents(rows: readonly DeviceEventDto[], delimiter: CsvDelimiter): void {
    downloadTextFile(
      csvFileName('events', device.deviceId),
      buildCsv(CSV_HEADERS, rows.map(eventToCsvRow), delimiter),
      // Named rather than left as text/plain: spreadsheets and "open with"
      // rules key off the MIME type as much as off the extension.
      'text/csv;charset=utf-8',
    )
  }

  // Picking a separator in the menu IS the export request. A range that fitted in
  // one answer is complete on screen, so it is written out as it stands; one that
  // filled the answer may hold more, which is the reader's call — hence the popup.
  function handleExportPick(delimiter: CsvDelimiter): void {
    if (events.length >= EVENTS_LIMIT) {
      setPendingDelimiter(delimiter)
      return
    }

    downloadEvents(events, delimiter)
    setStatusMessage(
      t('device:events.exported', { count: events.length, value: formatInteger(events.length) }),
    )
  }

  // The popup's "just the newest" answer: what the table shows, and nothing else.
  function handleExportShown(delimiter: CsvDelimiter): void {
    setPendingDelimiter(null)
    downloadEvents(events, delimiter)
    setStatusMessage(t('device:events.exportedNewest', { value: formatInteger(events.length) }))
  }

  // The popup's "everything" answer: walks the range back until it runs out or
  // MAX_EXPORT_ROWS is reached, then writes the file. The table is left alone — it
  // keeps showing the newest EVENTS_LIMIT, and the extra rows live only in the file.
  async function handleExportAll(delimiter: CsvDelimiter): Promise<void> {
    setPendingDelimiter(null)
    if (isExporting) {
      return
    }

    // The query is read NOW, not when each hop lands: a range edited mid-walk
    // must not quietly change what an export already under way is asking for.
    const generation: number = exportGenerationRef.current
    const isCurrent = (): boolean => exportGenerationRef.current === generation

    setIsExporting(true)
    setStatusMessage(t('device:events.loadingForExport'))

    try {
      const { events: rows, reachedCap } = await fetchAllDeviceEvents(device.deviceId, {
        fromIso: datetimeLocalToIso(dateRange.from),
        toIso: datetimeLocalToIso(dateRange.to),
        minSeverity: MIN_SEVERITY[filter],
        maxRows: MAX_EXPORT_ROWS,
        isCanceled: () => !isCurrent(),
        onProgress: (loaded) => {
          if (isCurrent()) {
            setStatusMessage(
              t('device:events.exportProgress', { count: loaded, value: formatInteger(loaded) }),
            )
          }
        },
      })

      if (!isCurrent()) {
        return
      }

      if (rows.length === 0) {
        setStatusMessage(t('device:events.nothingToExport'))
        return
      }

      downloadEvents(rows, delimiter)
      setStatusMessage(
        reachedCap
          ? t('device:events.exportedCapped', {
              value: formatInteger(rows.length),
              limit: formatInteger(MAX_EXPORT_ROWS),
            })
          : t('device:events.exported', { count: rows.length, value: formatInteger(rows.length) }),
      )
    } catch (error) {
      // A failed hop means a file with a hole in the middle of the range, so
      // there is no file at all.
      if (isCurrent()) {
        setStatusMessage(describeError(error, t('errors:exportEventsFailed')))
      }
    } finally {
      setIsExporting(false)
    }
  }

  return (
    <div>
      <RangeToolbar
        range={dateRange}
        onRangeChange={setDateRange}
        autoRefresh={refresh}
        isLoading={isLoading}
        idPrefix="events"
        className="position-list-controls"
        extra={
          <>
            <div className="form-field">
              <label className="form-label" htmlFor="events-filter">
                {t('device:events.filter.label')}
              </label>
              <select
                id="events-filter"
                className="form-input"
                value={filter}
                onChange={(e) => setFilter(e.target.value === 'problems' ? 'problems' : 'all')}
                style={{ width: 'auto' }}
              >
                <option value="all">{t('device:events.filter.all')}</option>
                <option value="problems">{t('device:events.filter.problems')}</option>
              </select>
            </div>

            {/* A quiet text button, like the Positions tab's: exporting is
                something the reader does occasionally, and the toolbar's weight
                belongs to the range pickers. Blocked while a walk is running — a
                second one would only race the first. */}
            <CsvExportMenu
              onExport={handleExportPick}
              disabled={isExporting || events.length === 0}
              isExporting={isExporting}
            />
          </>
        }
      />

      {/* Status line */}
      <p className="hint" role="status" style={{ marginBottom: 12 }}>
        {statusMessage}
      </p>

      {/* Spinner only before the first answer: a refresh must not replace the
          table being read. */}
      {isLoading && !hasLoaded ? (
        <div className="loading-state">
          <div className="spinner" />
          <span>{t('device:events.loading')}</span>
        </div>
      ) : events.length === 0 ? (
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">📡</span>
          <h3>{t('device:events.emptyTitle')}</h3>
          <p>
            {filter === 'problems'
              ? t('device:events.emptyProblemsBody')
              : t('device:events.emptyBody')}
          </p>
        </div>
      ) : (
        <div className="position-table-wrapper">
          <table className="position-table">
            <thead>
              <tr>
                <th scope="col">{t('device:events.column.time')}</th>
                <th scope="col">{t('device:events.column.severity')}</th>
                <th scope="col">{t('device:events.column.event')}</th>
                <th scope="col">{t('device:events.column.details')}</th>
              </tr>
            </thead>
            <tbody>
              {events.map((event) => (
                <tr key={event.id}>
                  <td style={{ whiteSpace: 'nowrap' }}>{formatTimestamp(event.occurredAt)}</td>
                  <td>
                    <span className={`event-severity event-severity--${event.severity}`}>
                      {t(EVENT_SEVERITY_LABEL_KEYS[event.severity])}
                    </span>
                  </td>
                  <td>{t(EVENT_REASON_LABEL_KEYS[event.reason])}</td>
                  <td className="hint">{describeDetails(event)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Raised by "Export CSV" when the range filled the answer and so may hold
          more than the table shows. */}
      {pendingDelimiter !== null ? (
        <ExportLengthDialog
          title={t('device:events.exportDialog.title')}
          intro={t('device:events.exportDialog.intro', { limit: formatInteger(EVENTS_LIMIT) })}
          allNote={t('device:events.exportDialog.allNote', { max: formatInteger(MAX_EXPORT_ROWS) })}
          allLabel={t('device:events.exportDialog.all')}
          shownLabel={t('device:events.exportDialog.newestOnly', { limit: formatInteger(EVENTS_LIMIT) })}
          onExportAll={() => void handleExportAll(pendingDelimiter)}
          onExportShown={() => handleExportShown(pendingDelimiter)}
          onCancel={() => setPendingDelimiter(null)}
        />
      ) : null}
    </div>
  )
}

// The secondary facts of one event, joined into one line: how long a sleep was
// meant to last and when the device was due back, the battery it reported, and
// an error's machine code. Absent facts are simply left out.
function describeDetails(event: DeviceEventDto): string {
  const parts: string[] = []

  if (event.sleepSeconds !== null) {
    const occurredAt: Date | null = parseApiTimestamp(event.occurredAt)
    const backAt: Date | null =
      occurredAt === null ? null : new Date(occurredAt.getTime() + event.sleepSeconds * 1000)
    parts.push(
      backAt === null
        ? i18n.t('device:events.detail.sleepFor', { duration: describeSeconds(event.sleepSeconds) })
        : i18n.t('device:events.detail.sleepUntil', {
            duration: describeSeconds(event.sleepSeconds),
            time: formatTime(backAt),
          }),
    )
  }

  if (event.batteryPct !== null) {
    // 0 is the agreed "charging" sentinel, never a flat pack.
    parts.push(
      event.batteryPct === 0
        ? i18n.t('device:events.detail.charging')
        : i18n.t('device:events.detail.battery', { value: event.batteryPct }),
    )
  }

  if (event.detail !== null) {
    parts.push(i18n.t('device:events.detail.code', { code: event.detail }))
  }

  return parts.length === 0 ? '—' : parts.join(' · ')
}

function formatTimestamp(value: string): string {
  const parsed: Date | null = parseApiTimestamp(value)
  return parsed === null ? '—' : formatDateTime(parsed)
}

// ---- CSV export ----

// The header row, and with it the column order of the file. The names are the
// API's own field names, as in the Positions export; "charging" is the one column
// with no field of its own — see eventToCsvRow.
const CSV_HEADERS: readonly string[] = [
  'occurredAt',
  'receivedAt',
  'deviceTime',
  'kind',
  'reason',
  'severity',
  'batteryPct',
  'charging',
  'sleepSeconds',
  'detail',
]

// A timestamp cell: ISO-8601 UTC, or empty when there is none.
function csvTimestamp(value: string | null): string {
  if (value === null) {
    return ''
  }
  return parseApiTimestamp(value)?.toISOString() ?? value
}

// One event's cells, in CSV_HEADERS order.
//
// Deliberately NOT the strings the table shows. A cell here is meant to be read
// back by a machine, and it must not depend on the language the dashboard was in
// when the file was written: timestamps in ISO-8601 UTC rather than in the
// browser's locale, `reason`, `kind` and `severity` as the API's own keys
// ("sleepNoMotion") rather than a translated sentence, plain numbers, and an
// EMPTY cell — not an em dash — where the device sent nothing.
//
// `detail` needs no escaping against a spreadsheet running it as a formula: the
// API only stores a device's code when it matches ^[a-z0-9_]{1,32}$
// (DeviceStatusValidator), so it can never start with "=", "+", "-" or "@".
function eventToCsvRow(event: DeviceEventDto): string[] {
  return [
    csvTimestamp(event.occurredAt),
    csvTimestamp(event.receivedAt),
    csvTimestamp(event.deviceTime),
    event.kind,
    event.reason,
    event.severity,
    // 0 is the agreed "charging" sentinel, not a percentage — a 0 left in a
    // numeric column would read as a flat pack to anything that imports it. The
    // reading is split in two instead, as the Positions export does.
    event.batteryPct === null || event.batteryPct === 0 ? '' : String(event.batteryPct),
    // Empty rather than "false" when there was no reading: the device did not say
    // it was not charging, it said nothing at all.
    event.batteryPct === null ? '' : String(event.batteryPct === 0),
    event.sleepSeconds === null ? '' : String(event.sleepSeconds),
    event.detail ?? '',
  ]
}
