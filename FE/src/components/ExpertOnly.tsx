// Route guard: renders its children only in Expert mode, and otherwise sends
// the reader to the device's map. Used on the device tabs that Normal mode
// hides (the events history).
//
// Hiding the tab alone is not enough: switching to Normal while on that tab,
// or opening an old bookmark to it, would leave a page with no highlighted tab
// and no way back to it. Redirecting puts the reader somewhere Normal mode
// actually shows. It is relative, so it stays inside the current device.

import { Navigate } from 'react-router-dom'
import type { ReactElement } from 'react'
import { useUiMode } from '../uiMode/useUiMode'

export function ExpertOnly({ children }: { children: ReactElement }): ReactElement {
  const { isExpert } = useUiMode()

  if (!isExpert) {
    return <Navigate to="../map" replace />
  }

  return children
}
