// ---------------------------------------------------------------------------
// TermsPage — the agreement itself: what this is, who runs it, what you may do
// with a tracker, and on what (absent) warranty.
//
// Public, and deliberately blunt. Somebody arriving at a deployed web app with
// a login form is entitled to know within one click whether they are looking at
// a service or at one person's experiment.
//
// This page is *accepted*, not merely published: RegisterPage requires a tick
// against it before an account can be created, and the accepted version is
// stamped on the user row. That is what gives the warranty and liability
// sections contractual force — the PolyForm licence in the repository binds
// people who copy the source, not people who sign up here, so without an
// acceptance step the disclaimer would bind nobody. Keep the route at /legal:
// SiteFooter links it from four places.
//
// The sections are written out with literal t() keys rather than looped over a
// table. That is not an oversight: i18next-cli's extractor only sees keys it can
// read as strings, and a table it cannot resolve makes it write a junk "…" key
// into the catalogue — and, worse, treat every real key here as unused and
// delete it on the next `npm run i18n:extract`. Literal keys keep both the
// extractor and `tsc -b` checking this file.
//
// The text lives in FE/src/i18n/locales/{en,cs}/legal.json and nowhere else.
// docs/ carries a pointer, not a second copy.
// ---------------------------------------------------------------------------

import { useTranslation } from 'react-i18next'
import { LegalPageShell } from '../components/LegalPageShell'

export function TermsPage() {
  const { t } = useTranslation('legal')

  return (
    <LegalPageShell title={t('legalNotice.title')} subtitle={t('legalNotice.subtitle')}>
      <section className="legal-section">
        <h2>{t('legalNotice.sections.nature.title')}</h2>
        <p>{t('legalNotice.sections.nature.p1')}</p>
        <p>{t('legalNotice.sections.nature.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.acceptance.title')}</h2>
        <p>{t('legalNotice.sections.acceptance.p1')}</p>
        <p>{t('legalNotice.sections.acceptance.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.operator.title')}</h2>
        <p>{t('legalNotice.sections.operator.p1')}</p>
      </section>

      {/* The load-bearing section: shared cars are the realistic exposure, and
          this is where the duty to tell the driver is placed on the person who
          put the tracker in the vehicle. */}
      <section className="legal-section">
        <h2>{t('legalNotice.sections.yourDevices.title')}</h2>
        <p>{t('legalNotice.sections.yourDevices.p1')}</p>
        <p>{t('legalNotice.sections.yourDevices.p2')}</p>
        <p>{t('legalNotice.sections.yourDevices.p3')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.warranty.title')}</h2>
        <p>{t('legalNotice.sections.warranty.p1')}</p>
        <p>{t('legalNotice.sections.warranty.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.liability.title')}</h2>
        <p>{t('legalNotice.sections.liability.p1')}</p>
        <p>{t('legalNotice.sections.liability.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.yourResponsibility.title')}</h2>
        <p>{t('legalNotice.sections.yourResponsibility.p1')}</p>
        <p>{t('legalNotice.sections.yourResponsibility.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.termination.title')}</h2>
        <p>{t('legalNotice.sections.termination.p1')}</p>
        <p>{t('legalNotice.sections.termination.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.licence.title')}</h2>
        <p>{t('legalNotice.sections.licence.p1')}</p>
        <p>{t('legalNotice.sections.licence.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.thirdParty.title')}</h2>
        <p>{t('legalNotice.sections.thirdParty.p1')}</p>
        <p>{t('legalNotice.sections.thirdParty.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.privacy.title')}</h2>
        <p>{t('legalNotice.sections.privacy.p1')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.changes.title')}</h2>
        <p>{t('legalNotice.sections.changes.p1')}</p>
        <p>{t('legalNotice.sections.changes.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.law.title')}</h2>
        <p>{t('legalNotice.sections.law.p1')}</p>
      </section>
    </LegalPageShell>
  )
}
