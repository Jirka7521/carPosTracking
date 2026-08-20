// ---------------------------------------------------------------------------
// capabilityFlags — the permission-flag shape and its empty value, split out
// from CapabilityCheckboxes.tsx so that file exports only its component.
//
// Vite's Fast Refresh can only hot-swap a module whose exports are all
// components; a constant alongside the component forces a full page reload on
// every edit.
// ---------------------------------------------------------------------------

// The three capability flags that can be toggled.
// Read is always on and is therefore not included here.
export type CapabilityFlags = {
  canDelete:         boolean
  canShare:          boolean
  canModifySettings: boolean
}

export const EMPTY_FLAGS: CapabilityFlags = {
  canDelete:         false,
  canShare:          false,
  canModifySettings: false,
}
