// ============================================================
// LocationRulesView — the Trips tab's "Place rules" view (?view=rules): every
// place rule of the car, the ones in force first, then the history of ended
// ones, which can be hidden.
//
// Rules are never deleted, so the history is the record of how trips came to
// be where they are: an ended rule still covers the trips that started while
// it was in force.
// ============================================================

import { useState } from 'react'
import type { To } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { formatInteger } from '../i18n/format'
import type { LocationRuleDto } from '../services/apiTypes'
import type { JourneyTree } from '../utils/journeyTree'
import { LocationRuleList } from './LocationRuleList'

export type LocationRulesViewProps = {
  // Null while they load.
  rules: readonly LocationRuleDto[] | null
  tree: JourneyTree
  canManage: boolean
  linkFor: (journeyId: string) => To
  onCreate: () => void
  onEdit: (rule: LocationRuleDto) => void
  onEnd: (rule: LocationRuleDto) => void
}

export function LocationRulesView({ rules, tree, canManage, linkFor, onCreate, onEdit, onEnd }: LocationRulesViewProps) {
  const { t } = useTranslation(['trips', 'common'])
  const [hideEnded, setHideEnded] = useState<boolean>(false)

  const active: LocationRuleDto[] = (rules ?? []).filter((rule) => rule.endedAt === null)
  const ended: LocationRuleDto[] = (rules ?? []).filter((rule) => rule.endedAt !== null)

  return (
    <section className="location-rules-view">
      <div className="location-rules-head">
        <h3 className="trips-view-title">
          <span aria-hidden="true">📍 </span>
          {t('trips:locationRule.viewTitle')}
        </h3>
        {canManage ? (
          <button type="button" className="btn btn-primary btn-sm" onClick={onCreate}>
            ＋ {t('trips:locationRule.add')}
          </button>
        ) : null}
      </div>
      <p className="hint">{t('trips:locationRule.viewHint')}</p>

      {rules === null ? (
        <div className="loading-state">
          <div className="spinner" />
          <span>{t('common:states.loading')}</span>
        </div>
      ) : rules.length === 0 ? (
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">📍</span>
          <p>{t('trips:locationRule.viewEmpty')}</p>
        </div>
      ) : (
        <>
          {active.length > 0 ? (
            <LocationRuleList
              rules={active}
              tree={tree}
              canManage={canManage}
              showJourney
              linkFor={linkFor}
              onEdit={onEdit}
              onEnd={onEnd}
            />
          ) : null}

          {ended.length > 0 ? (
            <>
              <div className="location-rules-history-head">
                <h4 className="location-rules-history-title">
                  {t('trips:locationRule.history', { count: ended.length, value: formatInteger(ended.length) })}
                </h4>
                <label className="checkbox-field">
                  <input type="checkbox" checked={hideEnded} onChange={(e) => setHideEnded(e.target.checked)} />
                  <span>{t('trips:locationRule.hideEnded')}</span>
                </label>
              </div>
              {!hideEnded ? (
                <LocationRuleList
                  rules={ended}
                  tree={tree}
                  canManage={canManage}
                  showJourney
                  linkFor={linkFor}
                  onEdit={onEdit}
                  onEnd={onEnd}
                />
              ) : null}
            </>
          ) : null}
        </>
      )}
    </section>
  )
}
