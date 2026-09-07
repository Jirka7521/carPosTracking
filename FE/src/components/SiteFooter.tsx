// ---------------------------------------------------------------------------
// SiteFooter — the standing reminder of what this thing is.
//
// It says three things, on every page, signed in or out: this is a
// non-commercial test project rather than a service, here is the privacy
// policy, and here is the legal notice. That is the whole component.
//
// It is mounted in three places rather than one because the app has two shells:
// AppLayout wraps every authenticated page, and the login and registration
// pages render their own. A footer only inside AppLayout would be invisible to
// exactly the people seeing the project for the first time.
// ---------------------------------------------------------------------------

import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

export function SiteFooter() {
  const { t } = useTranslation('common')

  return (
    <footer className="site-footer">
      <span className="site-footer-notice">{t('footer.testProject')}</span>

      <nav className="site-footer-links" aria-label={t('footer.legal')}>
        <Link to="/privacy" title={t('nav.privacy')}>
          {t('footer.privacy')}
        </Link>
        <Link to="/legal" title={t('nav.legal')}>
          {t('footer.legal')}
        </Link>
      </nav>
    </footer>
  )
}
