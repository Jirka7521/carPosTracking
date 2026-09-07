// ---------------------------------------------------------------------------
// LegalNoticePage — the imprint: what this is, who runs it, on what licence,
// and with what warranty (none).
//
// Public, and deliberately blunt. Somebody arriving at a deployed web app with
// a login form is entitled to know within one click whether they are looking at
// a service or at one person's experiment.
//
// The sections are written out with literal t() keys rather than looped over a
// table. That is not an oversight: i18next-cli's extractor only sees keys it can
// read as strings, and a table it cannot resolve makes it write a junk "…" key
// into the catalogue — and, worse, treat every real key here as unused and
// delete it on the next `npm run i18n:extract`. Literal keys keep both the
// extractor and `tsc -b` checking this file.
//
// The text is the same as docs/PRIVACY.md's sibling notice in the repository;
// this is the copy users actually read.
// ---------------------------------------------------------------------------

import { useTranslation } from 'react-i18next'
import { LegalPageShell } from '../components/LegalPageShell'

export function LegalNoticePage() {
  const { t } = useTranslation('legal')

  return (
    <LegalPageShell title={t('legalNotice.title')} subtitle={t('legalNotice.subtitle')}>
      <section className="legal-section">
        <h2>{t('legalNotice.sections.nature.title')}</h2>
        <p>{t('legalNotice.sections.nature.p1')}</p>
        <p>{t('legalNotice.sections.nature.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.operator.title')}</h2>
        <p>{t('legalNotice.sections.operator.p1')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.licence.title')}</h2>
        <p>{t('legalNotice.sections.licence.p1')}</p>
        <p>{t('legalNotice.sections.licence.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('legalNotice.sections.warranty.title')}</h2>
        <p>{t('legalNotice.sections.warranty.p1')}</p>
        <p>{t('legalNotice.sections.warranty.p2')}</p>
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
    </LegalPageShell>
  )
}
