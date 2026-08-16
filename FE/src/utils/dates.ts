// Date helpers shared by multiple pages. Kept here so the formatting logic
// for `<input type="datetime-local">` is in one obvious place.

export function formatDateTimeLocal(value: Date): string {
  const year: number = value.getFullYear()
  const month: string = String(value.getMonth() + 1).padStart(2, '0')
  const day: string = String(value.getDate()).padStart(2, '0')
  const hour: string = String(value.getHours()).padStart(2, '0')
  const minute: string = String(value.getMinutes()).padStart(2, '0')
  return `${year}-${month}-${day}T${hour}:${minute}`
}

// Parse a timestamp coming from the API. Values without an explicit timezone
// are treated as UTC, which is the API's convention — reading them as local
// time would shift every position by the viewer's offset.
export function parseApiTimestamp(value: string): Date | null {
  const hasTimezone: boolean = /[zZ]|[+-]\d{2}:?\d{2}$/.test(value)
  const parsed: Date = new Date(hasTimezone ? value : `${value}Z`)
  return Number.isNaN(parsed.getTime()) ? null : parsed
}

// A short "how long ago" label for the device liveness indicator. The firmware
// sends no heartbeat, so `lastSeenAt` only advances when a fix actually
// arrives — this is the only signal that a tracker is alive at all.
export function formatRelativeTime(value: string | null): string {
  if (!value) {
    return 'never reported'
  }

  const parsed: Date | null = parseApiTimestamp(value)
  if (parsed === null) {
    return 'unknown'
  }

  const seconds: number = Math.round((Date.now() - parsed.getTime()) / 1000)

  // Clock skew, or a fix whose GNSS time is slightly ahead of the server's.
  if (seconds < 60) {
    return 'just now'
  }

  const minutes: number = Math.round(seconds / 60)
  if (minutes < 60) {
    return `${minutes} min ago`
  }

  const hours: number = Math.round(minutes / 60)
  if (hours < 24) {
    return `${hours} h ago`
  }

  const days: number = Math.round(hours / 24)
  if (days < 30) {
    return `${days} d ago`
  }

  // Past a month, a date is more useful than a count of days.
  return parsed.toLocaleDateString()
}

// Convert a `<input type="datetime-local">` string into a UTC ISO timestamp,
// or undefined when the input is empty / invalid.
export function datetimeLocalToIso(value: string): string | undefined {
  if (!value) {
    return undefined
  }
  const parsed: Date = new Date(value)
  if (Number.isNaN(parsed.getTime())) {
    return undefined
  }
  return parsed.toISOString()
}
