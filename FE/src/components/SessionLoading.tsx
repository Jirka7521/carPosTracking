// ============================================================
// SessionLoading — what the app shows while it is asking the
// server whether the visitor has a session.
//
// This exists because the session cookie is HttpOnly: the app
// genuinely cannot know whether it is signed in until GET /api/me
// answers. Rendering the login page during that window would flash
// it on every reload and throw away the deep link; rendering a
// blank page would look broken. So: a spinner, briefly.
// ============================================================

import { useTranslation } from 'react-i18next'

export function SessionLoading() {
  const { t } = useTranslation('common')

  return (
    <div className="loading-state" style={{ minHeight: '60vh' }} role="status" aria-live="polite">
      <div className="spinner" aria-hidden="true" />
      <span>{t('states.loading')}</span>
    </div>
  )
}
