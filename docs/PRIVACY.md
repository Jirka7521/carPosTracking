# Privacy policy — where it lives

**The privacy policy and the terms of use are served by the app, not by this
repository.** Read them at `/privacy` and `/legal` on any deployment, or, for the
public one, at <https://jimajer.cz/carPosFE/privacy> and
<https://jimajer.cz/carPosFE/legal>.

This file used to carry a second, hand-synchronised copy of the policy text. It no
longer does. Two copies of a legal document drift, and these two already had: the
EU–US Data Privacy Framework safeguard was in the markdown and missing from the page
users actually read, which is exactly the wrong way round for an Art. 13(1)(f)
disclosure. There is now one copy.

## Where to edit it

| What | Where |
|---|---|
| The policy and terms text, English and Czech | `FE/src/i18n/locales/{en,cs}/legal.json` |
| Which sections appear, and in what order | `FE/src/pages/PrivacyPage.tsx`, `FE/src/pages/TermsPage.tsx` |
| Version string in force | `Privacy:PolicyVersion` (`API/CarPosAPI/appsettings.json`, `Options/PrivacyOptions.cs`) |
| Controller name and contact address | `Privacy:ControllerName`, `Privacy:ControllerContactEmail` |

Both locales must keep identical key sets — `npm run i18n:extract` deletes keys it
cannot see, so add new sections as literal `t()` calls, never through a loop.

Bump `Privacy:PolicyVersion` whenever either document changes materially. New accounts
accept the version in force at registration and it is stamped on the user row; a bump
does not re-prompt existing accounts, and the terms say so plainly (material changes
are announced, continued use is acceptance).

## The other documents here

- [`RECORD-OF-PROCESSING.md`](RECORD-OF-PROCESSING.md) — the Art. 30 record. Internal;
  this is the one to hand a supervisory authority who asks.
- [`DATA-INVENTORY.md`](DATA-INVENTORY.md) — the engineering view: every table, every
  column, what is personal, and what happens to it on erasure.
