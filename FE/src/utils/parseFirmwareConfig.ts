// ---------------------------------------------------------------------------
// Reads the Config.h the API rendered and turns it into the grouped reference
// table under the firmware panel.
//
// This used to be a 231-line hand transcription of ESP32/src/config/Config.h,
// and it drifted exactly as you would expect: by the time it was replaced it
// was missing fifteen constants, three of which the firmware had gained months
// earlier. Nobody had done anything wrong — keeping a prose copy of a 700-line
// C++ file in step by hand is simply not a thing that works.
//
// So the table is now derived from the same string the copy-paste panel shows.
// The API embeds the firmware's own Config.example.h verbatim and rewrites the
// per-device constants in it, which means the text below is the firmware's
// current reality by construction. A constant added to Config.example.h shows
// up here with no edit anywhere.
//
// What is NOT derived is `origin` — whether a value is this device's, a secret
// the operator supplies, or merely a default the Reporting & Power section
// overrides. That is a judgement about the system, not a fact in the file, so
// it lives as a small override map below. Anything unlisted is 'fixed', which
// is the safe answer: a new constant shows up as "compile-time" until someone
// decides otherwise.
//
// ⚠ Parse the RAW provisioning.configSnippet, never the copy the panel has
// woven the operator's secrets into — that one has a WiFi password in it.
// ---------------------------------------------------------------------------

import type {
  FirmwareParameter,
  FirmwareParameterGroup,
  ParameterOrigin,
} from './firmwareParameters'
import { GROUP_TITLE_BEFORE, PARAMETER_ORIGINS } from './firmwareParameters'

// A banner rule: the ---- or ==== line the firmware fences its sections with.
const RULE = /^\/\/\s*[-=]{5,}\s*$/

// A comment line, captured without its leading slashes.
const COMMENT = /^\/\/ ?(.*)$/

// The start of a declaration: `constexpr <type> <name>` or `constexpr <type> <name>[]`.
// The value is read separately because it may run over several lines.
const DECLARATION = /^constexpr\s+[A-Za-z_]\w*\s+(k[A-Za-z0-9_]+)(\[\])?\s*=\s*(.*)$/

// Long enough to carry a real explanation, short enough that no row turns the
// table into a wall of text. The full prose is always in the file above.
const MAX_MEANING = 320

// Values are for scanning, not reading — a 400-character PEM literal in a table
// cell helps nobody, and the file itself is right there.
const MAX_VALUE = 64

// Turns the rendered Config.h into the table's groups.
export function parseFirmwareConfig(configFile: string): readonly FirmwareParameterGroup[] {
  const lines = configFile.split('\n')
  const groups: FirmwareParameterGroup[] = []

  let currentGroup: { title: string; parameters: FirmwareParameter[] } | null = null

  // Comment lines seen since the last blank line or declaration — the prose that
  // belongs to whatever is declared next.
  let pending: string[] = []

  // True when the previous line was blank or a declaration, which is what makes
  // a rule line the START of a new section banner rather than a divider inside
  // one. The firmware always separates its sections with a blank line, and that
  // is the only signal distinguishing the two uses of the same ---- rule.
  let atSectionBoundary = true

  // The comment on the last declaration, and whether that declaration was the
  // line immediately above this one. The firmware writes one comment over a pair
  // (kMinSendIntervalSeconds / kMaxSendIntervalSeconds sit under a single note),
  // so the second of a pair has none of its own and inherits it — the alternative
  // is a table where every other "What it does" cell is blank.
  let previousMeaning = ''
  let previousLineWasDeclaration = false

  // The current section banner's prose, minus its title line. Consumed by the
  // first constant in the section that has no comment of its own, then cleared —
  // it describes the section, so repeating it down every row would be noise.
  let bannerBody: string[] = []

  for (let index = 0; index < lines.length; index++) {
    const line = lines[index]

    if (line.trim() === '') {
      pending = []
      atSectionBoundary = true
      previousLineWasDeclaration = false
      continue
    }

    if (RULE.test(line)) {
      if (atSectionBoundary) {
        const banner = readBanner(lines, index + 1)
        if (banner.title !== '') {
          currentGroup = { title: banner.title, parameters: [] }
          groups.push(currentGroup)
          // The rest of the banner explains the section, and for a constant the
          // banner is written about (kWifiEnabled, kMqttEnabled) that IS the
          // explanation — the declaration itself carries no comment of its own.
          bannerBody = banner.body
        }
        index = banner.endIndex
        pending = []
      }
      // A rule that is not at a section boundary closes a banner mid-flow. It
      // separates prose from the declaration below it, so the prose is kept.
      atSectionBoundary = false
      previousLineWasDeclaration = false
      continue
    }

    const comment = COMMENT.exec(line)
    if (comment !== null) {
      pending.push(comment[1])
      atSectionBoundary = false
      previousLineWasDeclaration = false
      continue
    }

    const declaration = DECLARATION.exec(line)
    if (declaration !== null) {
      const name = declaration[1]
      const value = readValue(lines, index, declaration[3])

      const forcedTitle = GROUP_TITLE_BEFORE[name]
      if (forcedTitle !== undefined) {
        currentGroup = { title: forcedTitle, parameters: [] }
        groups.push(currentGroup)
      }

      // A constant declared before any banner (there are none today, but a
      // firmware edit could add one) still needs somewhere to go.
      if (currentGroup === null) {
        currentGroup = { title: 'Configuration', parameters: [] }
        groups.push(currentGroup)
      }

      // What this constant is, in order of how specific the source is: its own
      // comment; the `<-- your network name` hint beside its value; the comment
      // it shares with the declaration above it; the section banner's prose.
      let meaning = summarise(pending)

      if (meaning === '' && value.hint !== '') {
        meaning = value.hint
      }

      if (meaning === '' && previousLineWasDeclaration) {
        meaning = previousMeaning
      }

      if (meaning === '' && bannerBody.length > 0) {
        meaning = summarise(bannerBody)
        bannerBody = []
      }

      currentGroup.parameters.push({
        name,
        value: value.text,
        meaning,
        origin: originOf(name),
      })

      index = value.endIndex
      pending = []
      atSectionBoundary = true
      previousMeaning = meaning
      previousLineWasDeclaration = true
      continue
    }

    // Anything else — #pragma, #include, namespace, the closing brace.
    pending = []
    atSectionBoundary = true
    previousLineWasDeclaration = false
  }

  // The generated header at the top of the file is a banner with no constants
  // under it, and so are a couple of the firmware's own explanatory blocks.
  return groups.filter((group) => group.parameters.length > 0)
}

// Reads a section banner starting at `start`. The title is its first line of
// prose; `body` is the rest, which explains the section and is the best (often
// only) description of the flag the section is named after.
function readBanner(
  lines: readonly string[],
  start: number,
): { title: string; body: string[]; endIndex: number } {
  let title = ''
  const body: string[] = []
  let index = start

  while (index < lines.length) {
    const line = lines[index]

    if (RULE.test(line)) {
      // The closing rule — consume it so the caller does not read it as the
      // opening of another banner.
      return { title, body, endIndex: index }
    }

    const comment = COMMENT.exec(line)
    if (comment === null) {
      // An unterminated banner: whatever follows is not part of it.
      return { title, body, endIndex: index - 1 }
    }

    if (title === '') {
      title = comment[1].trim().replace(/\.$/, '')
    } else {
      body.push(comment[1])
    }

    index++
  }

  return { title, body, endIndex: lines.length - 1 }
}

// Reads a declaration's value, which may run over several lines (the receiver
// public key is a stack of quoted PEM lines) and may end in a trailing comment.
function readValue(
  lines: readonly string[],
  start: number,
  firstFragment: string,
): { text: string; hint: string; endIndex: number } {
  const fragments: string[] = [firstFragment]
  let index = start

  // C has no statement terminator but the semicolon, so this is exact — and the
  // values here are numbers, quoted strings and brace lists, none of which can
  // contain one.
  while (!fragments[fragments.length - 1].includes(';') && index + 1 < lines.length) {
    index++
    fragments.push(lines[index].trim())
  }

  let text = fragments.join(' ').trim()

  // Split at the semicolon. What follows it is one of two different things, and
  // they belong in different columns:
  //
  //   `// 24 h`                      a gloss on the number — the only thing
  //                                  telling a reader what 86400 means, so it
  //                                  stays beside the value it explains;
  //   `// <-- your network name`     an instruction to the operator, which is an
  //                                  explanation, not part of the value.
  let hint = ''
  const terminator = text.indexOf(';')

  if (terminator !== -1) {
    const trailing = COMMENT.exec(text.slice(terminator + 1).trim())
    const note = trailing === null ? '' : trailing[1].trim()

    text = text.slice(0, terminator)

    if (note.startsWith('<--')) {
      hint = capitalise(note.replace(/^<--\s*/, ''))
    } else if (note !== '') {
      text = `${text.trimEnd()}  // ${note}`
    }
  }

  text = text.replace(/\s+/g, ' ').trim()

  if (text.length > MAX_VALUE) {
    text = `${text.slice(0, MAX_VALUE - 1).trimEnd()}…`
  }

  return { text, hint, endIndex: index }
}

// The firmware's inline hints read as sentence fragments ("your network name");
// the table's other cells are sentences.
function capitalise(text: string): string {
  return text === '' ? '' : text[0].toUpperCase() + text.slice(1)
}

// Condenses a constant's comment lines into one cell of prose.
function summarise(comment: readonly string[]): string {
  // The first paragraph only. A long comment's later paragraphs are caveats and
  // worked examples — worth reading in the file, not in a table cell — and for
  // the ack block the later paragraphs are openssl commands.
  const paragraph: string[] = []

  for (const line of comment) {
    if (line.trim() === '') {
      if (paragraph.length > 0) {
        break
      }
      continue
    }

    paragraph.push(line.trim())
  }

  const text = paragraph.join(' ').replace(/\s+/g, ' ').trim()

  if (text.length <= MAX_MEANING) {
    return text
  }

  // Cut at a word boundary rather than mid-word.
  const cut = text.lastIndexOf(' ', MAX_MEANING)

  return `${text.slice(0, cut === -1 ? MAX_MEANING : cut)}…`
}

// Where a constant's value comes from. Unlisted means 'fixed'.
function originOf(name: string): ParameterOrigin {
  return PARAMETER_ORIGINS[name] ?? 'fixed'
}
