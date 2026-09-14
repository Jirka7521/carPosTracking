// ============================================================
// AccessCountBadge — a compact pill answering "who can see this vehicle?".
//
// One badge, two segments, because the two kinds of reach are not the same
// thing and a single total would hide the difference at exactly the moment
// somebody is trying to understand their own exposure:
//
//   👥 n   accounts holding an active grant — one per account whatever its
//          capabilities, and including the viewer, so it is never below 1.
//   🔗 n   share links that are live right now — one per link regardless of how
//          many times it has been opened. Revoked, expired and not-yet-started
//          links are not counted; the server decides this (see
//          DeviceAccessCountsDto) so the pill cannot disagree with the API.
//
// The links segment is dropped entirely when there are none, following
// BatteryBadge's rule that a device with nothing to report shows no empty
// placeholder rather than a zero.
//
// It is a <span>, not a link, on purpose: on the Home page the whole card is
// already a <Link>, and nesting anchors is invalid HTML. Managing who has
// access lives on the device's Settings tab.
//
// CSS classes are in App.css under "access-badge", mirroring the
// status-badge / battery-badge pill idiom.
// ============================================================

import { useTranslation } from 'react-i18next'
import type { DeviceAccessCountsDto } from '../services/apiTypes'

type AccessCountBadgeProps = {
  // Straight from DeviceDto.accessCounts.
  counts: DeviceAccessCountsDto | null | undefined
  // Larger, standalone rendering for the device page header (vs. the small pill
  // on the cards). Purely visual — adds the `access-badge--lg` modifier.
  large?: boolean
}

export function AccessCountBadge({ counts, large = false }: AccessCountBadgeProps) {
  const { t } = useTranslation('common')

  // Defensive: an older API build, or a response cached from before this field
  // existed, would leave this undefined. Rendering nothing beats rendering NaN.
  if (counts === null || counts === undefined) {
    return null
  }

  const sizeClass = large ? ' access-badge--lg' : ''
  const showLinks = counts.activeLinks > 0

  // One sentence covering both figures. It is both the tooltip and the badge's
  // accessible name, because two pictograms beside two bare numerals tell a
  // screen-reader user nothing — they would hear "3 2".
  //
  // Built from two separately-pluralised fragments rather than one template with
  // two numbers in it, because i18next pluralises on a single `count` and Czech
  // needs three forms of each noun. Composing the fragments is what lets
  // "1 osoba a 2 aktivní odkazy" and "5 osob a 1 aktivní odkaz" both come out
  // right; a single string with two counts in it could only ever be right once.
  const peopleLabel = t('access.people', { count: counts.people })
  const linksLabel = t('access.links', { count: counts.activeLinks })

  const description = showLinks
    ? t('access.summaryWithLinks', { people: peopleLabel, links: linksLabel })
    : t('access.summary', { count: counts.people })

  return (
    /*
     * role="img" with a label is what makes the whole pill announce as that one
     * sentence: it tells assistive tech to treat the segments as the picture
     * they are rather than reading the digits out of context.
     */
    <span
      className={`access-badge${sizeClass}`}
      role="img"
      aria-label={description}
      title={description}
    >
      <span className="access-badge-part">
        <span aria-hidden="true">👥</span>
        <span>{counts.people}</span>
      </span>

      {showLinks ? (
        <>
          {/* A hairline rather than a character, so the two counts read as one
              badge with two parts instead of two badges crowded together. */}
          <span className="access-badge-sep" aria-hidden="true" />
          <span className="access-badge-part">
            <span aria-hidden="true">🔗</span>
            <span>{counts.activeLinks}</span>
          </span>
        </>
      ) : null}
    </span>
  )
}
