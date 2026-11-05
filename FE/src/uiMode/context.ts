// ---------------------------------------------------------------------------
// uiModeContext — the context object and its value type for the Normal / Expert
// switch, split out from UiModeProvider.tsx for the same Fast Refresh reason
// as auth/context.ts: a module that exports anything but components forces a
// full reload on every edit. Three files: the context here, the provider in
// UiModeProvider.tsx, and the useUiMode() hook in useUiMode.ts.
//
// NORMAL is the default and shows what a driver uses day to day — map,
// positions, charts and the few settings that matter (how often to report,
// whether to wake on motion, how long to wait for movement). EXPERT is the
// whole dashboard: the events history, every firmware setting, the firmware
// configuration panel and the schedule's rule priorities. The mode only HIDES
// controls; a value tuned in Expert mode is kept, and saved unchanged, when it
// is edited in Normal mode.
// ---------------------------------------------------------------------------

import { createContext } from 'react'

export type UiMode = 'normal' | 'expert'

// Same `carpos.` prefix as the CSV separator and the language choice: a
// per-browser preference, not an account setting.
export const UI_MODE_STORAGE_KEY = 'carpos.uiMode'

export type UiModeContextValue = {
  mode: UiMode
  isExpert: boolean
  setMode: (mode: UiMode) => void
}

export const UiModeContext = createContext<UiModeContextValue | null>(null)
