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
//
// Unlike the Positions tab there is no chunked paging: the API caps one answer
// at EVENTS_LIMIT rows, and a range holding more than that says so and asks the
// reader to narrow it. An event list long enough to need paging is better read
// filtered than scrolled.
// ============================================================

import { useEffect, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import i18n from '../i18n'
import { formatDateTime, formatInteger, formatTime } from '../i18n/format'
import RangeToolbar from '../components/RangeToolbar'
import type { DevicePageContext } from './DevicePage'
import { fetchDeviceEvents } from '../services/apiClient'
import type { DeviceEventDto, DeviceEventSeverity } from '../services/apiTypes'
import { datetimeLocalToIso, parseApiTimestamp } from '../utils/dates'
import { describeSeconds } from '../utils/deviceConfig'
import { EVENT_REASON_LABEL_KEYS, EVENT_SEVERITY_LABEL_KEYS } from '../utils/deviceEvents'
import { describeError } from '../utils/errors'

// Rows asked for per load — the API's own ceiling. A range that fills it is
// reported as truncated rather than paged.
const EVENTS_LIMIT = 1000

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
