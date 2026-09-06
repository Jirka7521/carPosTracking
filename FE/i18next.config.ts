// ---------------------------------------------------------------------------
// i18next-cli — key extraction and catalogue health.
//
// `npm run i18n:extract` scans the source for t('…') and <Trans i18nKey="…">
// calls and adds any key the code uses but the catalogues do not have: English
// gets the key itself as a visible placeholder, every other language gets an
// empty string, which is what makes i18next fall back to English until someone
// fills it in and what `npm run i18n:missing` looks for.
//
// `npm run i18n:check` runs the same scan read-only and FAILS when the
// catalogues are out of date with the code. That is the CI-facing one.
//
// It is not the only guard. src/i18n/i18next.d.ts types t() against the English
// catalogue, so a key that is not in the JSON is already a `tsc -b` error. The
// extractor covers the other direction: keys the code uses that nobody has
// translated, and keys nothing uses any more.
// ---------------------------------------------------------------------------

import { defineConfig } from 'i18next-cli'

export default defineConfig({
  // English first: it is the primary language, so it is the one that gets
  // placeholder defaults while every other locale gets an empty string.
  locales: ['en', 'cs'],

  extract: {
    input: ['src/**/*.{ts,tsx}'],
    output: 'src/i18n/locales/{{language}}/{{namespace}}.json',

    // Must match src/i18n/index.ts: namespaces are separated from the key by a
    // colon, key segments by a dot, and `common` is where an unqualified key
    // belongs.
    defaultNS: 'common',
    nsSeparator: ':',
    keySeparator: '.',

    // Sorted, so a diff on a catalogue is about the strings that changed rather
    // than about where the extractor decided to put them.
    sort: true,

    // KEYS REACHED THROUGH A CONSTANT ARE INVISIBLE TO A SOURCE SCAN, and
    // removeUnusedKeys defaults to true — so without this list, one run of
    // `i18n:extract` would silently delete most of the label tables.
    //
    // Each entry below is a family whose call site is `t(SOME_TABLE[key])` or a
    // template literal rather than a string literal. If you add another such
    // table, add its prefix here in the same commit; `tsc -b` will not catch
    // the loss, because deleting the English key deletes the type too.
    preservePatterns: [
      'config.field.*',        // utils/deviceConfig.ts  CONFIG_FIELD_LABEL_KEYS
      'firmware.origin.*',     // utils/firmwareParameters.ts  ORIGIN_LABEL_KEYS
      'sync.badge.*',          // ConfigSyncIndicator, template literal
      'units.*',               // utils/timeUnits.ts  UNIT_LABEL_KEYS
      'csv.*',                 // utils/csv.ts  CSV_DELIMITERS
      'weekday.*',             // utils/schedule.ts  DAY_SHORT_KEYS / DAY_LONG_KEYS
      'permission.*',          // PermissionBadges  CAPABILITIES
      'charts.series.*',       // utils/telemetry.ts  SERIES labelKey
      'positions.column.*',    // PositionListTab  COLUMNS labelKey
    ],
  },

  // `npm run i18n:lint` looks for hardcoded strings that should have been
  // translated. It is ADVISORY and deliberately not part of `npm run lint`:
  // it also reports the decorative aria-hidden emoji this UI uses as section
  // icons (📡, ⚙️, 🔋 …), and there is no way to exclude those without also
  // excluding the <span>s that carry real text. Run it after adding UI and
  // read past the emoji; it has already caught two whole sentences that were
  // missed by hand.
  lint: {
    // Code samples are not prose — the Config.h snippets and constant names
    // must stay exactly as the firmware spells them.
    ignoredTags: ['code', 'pre'],
  },
})
