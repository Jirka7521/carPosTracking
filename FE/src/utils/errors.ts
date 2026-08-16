// Helper for turning any thrown value into a single string the UI can render.
// We try ApiError first (it carries a server-provided message), then any
// generic Error, then fall back to a safe default.

import { ApiError } from '../services/apiClient'

export function describeError(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    return error.message
  }
  if (error instanceof Error && error.message.length > 0) {
    return error.message
  }
  return fallback
}
