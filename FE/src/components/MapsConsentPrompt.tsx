// ============================================================
// MapsConsentPrompt — the gate every Google map sits behind.
//
// Nothing has been sent to Google while this is shown, and the text says
// exactly what pressing either button will send, whose terms the map comes
// under, and how far "always" reaches. Shared by DeviceMap and
// LocationPickerMap so every map asks the same question in the same words.
// ============================================================

import { Trans, useTranslation } from 'react-i18next'
import { GOOGLE_MAPS_TERMS_URL, GOOGLE_PRIVACY_POLICY_URL } from '../utils/mapsConsent'

export type MapsConsentPromptProps = {
  // "Just this once": load the map in this tab only.
  onOnce: () => void
  // "Always": load it now and remember the answer where the caller keeps it.
  onAlways: () => void
  // Where "always" is remembered, which decides what the prompt promises about
  // it: every device on the account, or this browser only.
  consentScope: 'account' | 'browser'
}

export function MapsConsentPrompt({ onOnce, onAlways, consentScope }: MapsConsentPromptProps) {
  const { t } = useTranslation('device')

  return (
    <div className="map-placeholder map-consent">
      <span style={{ fontSize: '2rem' }} aria-hidden="true">🗺</span>
      <h3 className="map-consent-title">{t('mapsConsent.title')}</h3>
      <p>{t('mapsConsent.body')}</p>
      <p>
        <Trans
          i18nKey="mapsConsent.googleTerms"
          ns="device"
          components={{
            terms: <a href={GOOGLE_MAPS_TERMS_URL} target="_blank" rel="noopener noreferrer" />,
            privacy: <a href={GOOGLE_PRIVACY_POLICY_URL} target="_blank" rel="noopener noreferrer" />,
          }}
        />
      </p>

      <div className="map-consent-actions">
        <button type="button" className="btn btn-secondary btn-sm" onClick={onOnce}>
          {t('mapsConsent.once')}
        </button>
        <button type="button" className="btn btn-primary btn-sm" onClick={onAlways}>
          {t('mapsConsent.always')}
        </button>
      </div>

      <p className="map-consent-hint">
        {consentScope === 'account' ? t('mapsConsent.hintAccount') : t('mapsConsent.hintBrowser')}
      </p>
    </div>
  )
}
