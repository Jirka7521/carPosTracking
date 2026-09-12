// ---------------------------------------------------------------------------
// PrivacyPage — the privacy policy, and the authoritative copy of it.
//
// docs/PRIVACY.md carries the same text for the repository; THIS is the version
// users actually see, in their own language, and the one the registration form
// links to. If you edit one, edit the other.
//
// The sections are written out with literal t() keys rather than looped over a
// table. That is not an oversight: i18next-cli's extractor only sees keys it can
// read as strings, and a table it cannot resolve makes it write a junk "…" key
// into the catalogue — and, worse, treat every real key here as unused and
// delete it on the next `npm run i18n:extract`. Literal keys keep both the
// extractor and `tsc -b` checking this file, which for a document with legal
// weight is worth the repetition.
// ---------------------------------------------------------------------------

import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { LegalPageShell } from '../components/LegalPageShell'
import { fetchPrivacyPolicy } from '../services/apiClient'
import type { PrivacyPolicyDto } from '../services/apiTypes'

export function PrivacyPage() {
  const { t } = useTranslation('legal')

  // The version and the controller's details come from the server, so the page
  // and the consent recorded at registration can never disagree about which
  // policy is in force.
  const [policy, setPolicy] = useState<PrivacyPolicyDto | null>(null)

  useEffect(() => {
    let cancelled: boolean = false

    fetchPrivacyPolicy()
      .then((loaded) => {
        if (!cancelled) {
          setPolicy(loaded)
        }
      })
      // A policy whose header failed to load is still a readable policy. The
      // body below is the part that matters, and it is bundled, not fetched.
      .catch(() => undefined)

    return () => {
      cancelled = true
    }
  }, [])

  return (
    <LegalPageShell
      title={t('privacy.title')}
      subtitle={t('privacy.subtitle')}
      meta={<PolicyMeta policy={policy} />}
    >
      <section className="legal-section">
        <h2>{t('privacy.sections.testProject.title')}</h2>
        <p>{t('privacy.sections.testProject.p1')}</p>
        <p>{t('privacy.sections.testProject.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.controller.title')}</h2>
        <p>{t('privacy.sections.controller.p1')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.whatWeDo.title')}</h2>
        <p>{t('privacy.sections.whatWeDo.p1')}</p>
        <p>{t('privacy.sections.whatWeDo.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.accountData.title')}</h2>
        <p>{t('privacy.sections.accountData.p1')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.locationData.title')}</h2>
        <p>{t('privacy.sections.locationData.p1')}</p>
        <p>{t('privacy.sections.locationData.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.sharingData.title')}</h2>
        <p>{t('privacy.sections.sharingData.p1')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.shareLinks.title')}</h2>
        <p>{t('privacy.sections.shareLinks.p1')}</p>
        <p>{t('privacy.sections.shareLinks.p2')}</p>
        <p>{t('privacy.sections.shareLinks.p3')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.technicalData.title')}</h2>
        <p>{t('privacy.sections.technicalData.p1')}</p>
        <p>{t('privacy.sections.technicalData.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.notCollected.title')}</h2>
        <p>{t('privacy.sections.notCollected.p1')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.legalBasis.title')}</h2>
        <p>{t('privacy.sections.legalBasis.p1')}</p>
        <p>{t('privacy.sections.legalBasis.p2')}</p>
        <p>{t('privacy.sections.legalBasis.p3')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.otherDrivers.title')}</h2>
        <p>{t('privacy.sections.otherDrivers.p1')}</p>
        <p>{t('privacy.sections.otherDrivers.p2')}</p>
        {/* Addressed to the driver, not the account holder: they are named as a
            data subject in the Art. 30 record and, without this, have no route to
            exercise a single right — every control on this page is inside an
            account they do not have. */}
        <p>{t('privacy.sections.otherDrivers.p3')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.recipients.title')}</h2>
        <p>{t('privacy.sections.recipients.p1')}</p>
        <p>{t('privacy.sections.recipients.p2')}</p>
        <p>{t('privacy.sections.recipients.p3')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.retention.title')}</h2>
        <p>{t('privacy.sections.retention.p1')}</p>
        <p>{t('privacy.sections.retention.p2')}</p>
        <p>{t('privacy.sections.retention.p3')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.rights.title')}</h2>
        <p>{t('privacy.sections.rights.p1')}</p>
        <p>{t('privacy.sections.rights.p2')}</p>
        <p>{t('privacy.sections.rights.p3')}</p>
        <p>{t('privacy.sections.rights.p4')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.cookies.title')}</h2>
        <p>{t('privacy.sections.cookies.p1')}</p>
        <p>{t('privacy.sections.cookies.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.security.title')}</h2>
        <p>{t('privacy.sections.security.p1')}</p>
        <p>{t('privacy.sections.security.p2')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.children.title')}</h2>
        <p>{t('privacy.sections.children.p1')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.changes.title')}</h2>
        <p>{t('privacy.sections.changes.p1')}</p>
      </section>

      <section className="legal-section">
        <h2>{t('privacy.sections.complaints.title')}</h2>
        <p>{t('privacy.sections.complaints.p1')}</p>
      </section>
    </LegalPageShell>
  )
}

// The header block: which version this is, who is answerable for it, and where
// to write. Rendered as a definition list because that is what it is.
function PolicyMeta({ policy }: { policy: PrivacyPolicyDto | null }) {
  const { t } = useTranslation('legal')

  if (!policy) {
    return null
  }

  return (
    <div className="legal-meta">
      <p className="legal-version">{t('privacy.versionLabel', { version: policy.version })}</p>

      <dl>
        <dt>{t('privacy.controllerLabel')}</dt>
        <dd>{policy.controllerName}</dd>

        <dt>{t('privacy.contactLabel')}</dt>
        <dd>
          {policy.controllerContactEmail.includes('@')
            ? <a href={`mailto:${policy.controllerContactEmail}`}>{policy.controllerContactEmail}</a>
            : t('privacy.contactUnset')}
        </dd>
      </dl>
    </div>
  )
}
