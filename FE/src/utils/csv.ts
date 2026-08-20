// ---------------------------------------------------------------------------
// A minimal RFC 4180 CSV writer.
//
// Hand-rolled rather than pulled in as a dependency: the whole format is two
// rules — quote a field that would otherwise break the parse, and double any
// quote inside it — and a library for that would be more bytes than the table
// it writes.
//
// The delimiter is a parameter because "CSV" is not one format. A spreadsheet
// opened on a Czech or German machine splits on a SEMICOLON and treats a comma
// as a decimal point, so a comma-separated file lands there as a single column
// of text. Letting the reader pick is the only thing that works everywhere.
// ---------------------------------------------------------------------------

// The separators offered in the UI, in the order they are listed.
//
// `sample` is the character itself, shown beside the name because "semicolon"
// is a word and ";" is what actually ends up between the columns. `hint` names
// the case each one exists for — nobody picking a separator knows off-hand which
// one their spreadsheet wants, but everybody knows which spreadsheet they have.
export const CSV_DELIMITERS = [
  { value: ',',  label: 'Comma',     sample: ',', hint: 'Standard CSV' },
  { value: ';',  label: 'Semicolon', sample: ';', hint: 'Excel in CZ / DE' },
  { value: '\t', label: 'Tab',       sample: '⇥', hint: 'TSV' },
] as const

export type CsvDelimiter = (typeof CSV_DELIMITERS)[number]['value']

// Narrows an arbitrary string — one read back out of localStorage, say — to a
// delimiter this module actually offers.
export function isCsvDelimiter(value: string): value is CsvDelimiter {
  return CSV_DELIMITERS.some((option) => option.value === value)
}

// Rows are joined with CRLF, not LF: RFC 4180 says so, and it is what keeps the
// last column of every row from growing a stray character in Excel.
const ROW_SEPARATOR: string = '\r\n'

// Excel does not sniff UTF-8. Without a byte-order mark it decodes the file in
// the machine's ANSI codepage, which turns "°C" and any accented device name
// into mojibake. Every other reader tolerates the mark, so it always goes on.
const BOM: string = '﻿'

// Quotes a field only when leaving it bare would break the parse — a field
// holding the delimiter, a quote, or a line break of its own. Anything else is
// written through untouched, which keeps the file readable in a text editor.
function escapeField(value: string, delimiter: CsvDelimiter): string {
  const needsQuotes: boolean =
    value.includes(delimiter) ||
    value.includes('"') ||
    value.includes('\n') ||
    value.includes('\r')

  if (!needsQuotes) {
    return value
  }

  // The escape for a quote is a second quote, not a backslash.
  return `"${value.replaceAll('"', '""')}"`
}

// Builds a complete CSV document: the header row, then one row per record.
//
// Every cell arrives already stringified. That is deliberate — how a null or a
// sentinel should read is a decision about the DATA, and it belongs with the
// code that knows what the column means, not here.
export function buildCsv(
  headers: readonly string[],
  rows: readonly (readonly string[])[],
  delimiter: CsvDelimiter,
): string {
  const lines: string[] = [
    headers.map((header) => escapeField(header, delimiter)).join(delimiter),
  ]

  for (const row of rows) {
    lines.push(row.map((cell) => escapeField(cell, delimiter)).join(delimiter))
  }

  // A trailing separator so the file ends on a newline, as a text file should.
  return BOM + lines.join(ROW_SEPARATOR) + ROW_SEPARATOR
}
