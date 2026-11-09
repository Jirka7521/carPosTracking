// ============================================================
// Journey tree — the flat journey list the API sends, turned into the shape
// the sidebar, the pickers and the journey header need, plus the placement
// rules the server enforces, mirrored so the pickers can grey out a choice
// before it is made rather than after the request fails.
//
// The API sends every journey of a car in one flat list (up to 10,000), each
// pointing at its parent. The tree is built ONCE per list (buildJourneyTree)
// into two maps, and everything else here walks those maps. Nothing renders
// the whole tree: the sidebar draws children only when a node is open, and
// search returns at most a capped number of matches.
//
// The rules mirror JourneyTreeRules.cs on the API: at most MAX_JOURNEY_DEPTH
// levels (a top-level journey is level 1), and a journey can never move inside
// itself or one of its own sub-journeys. The server still decides; this only
// keeps the reader from picking something it would refuse.
// ============================================================

import type { JourneyDto } from '../services/apiTypes'

// Mirrors TripRules.MaxJourneyDepth on the API.
export const MAX_JOURNEY_DEPTH: number = 5

// Shown between the names of a journey's path. Not a word, so not translated.
export const JOURNEY_PATH_SEPARATOR: string = ' › '

export type JourneyTree = {
  byId: ReadonlyMap<string, JourneyDto>
  // Children of each journey, by name; key null holds the top level. A journey
  // whose parent is missing from the list is treated as top level rather than
  // lost.
  children: ReadonlyMap<string | null, readonly JourneyDto[]>
}

const EMPTY: readonly JourneyDto[] = []

export function buildJourneyTree(journeys: readonly JourneyDto[]): JourneyTree {
  const byId = new Map<string, JourneyDto>(journeys.map((journey) => [journey.id, journey]))
  const children = new Map<string | null, JourneyDto[]>()

  for (const journey of journeys) {
    const parentKey: string | null =
      journey.parentId !== null && byId.has(journey.parentId) ? journey.parentId : null
    const siblings: JourneyDto[] | undefined = children.get(parentKey)
    if (siblings === undefined) {
      children.set(parentKey, [journey])
    } else {
      siblings.push(journey)
    }
  }

  // Numeric collation, so "Day 2" comes before "Day 10".
  const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' })
  for (const siblings of children.values()) {
    siblings.sort((a, b) => collator.compare(a.name, b.name))
  }

  return { byId, children }
}

export function childrenOf(tree: JourneyTree, id: string | null): readonly JourneyDto[] {
  return tree.children.get(id) ?? EMPTY
}

// The journeys from the top level down to `id`, inclusive; empty when `id` is
// unknown. Guarded against a cycle in the data, which the server forbids but a
// list fetched mid-change could still briefly show.
export function journeyPath(tree: JourneyTree, id: string): JourneyDto[] {
  const path: JourneyDto[] = []
  const visited = new Set<string>()
  let current: JourneyDto | undefined = tree.byId.get(id)

  while (current !== undefined && !visited.has(current.id)) {
    visited.add(current.id)
    path.unshift(current)
    current = current.parentId === null ? undefined : tree.byId.get(current.parentId)
  }
  return path
}

export function journeyPathLabel(tree: JourneyTree, id: string): string {
  return journeyPath(tree, id).map((journey) => journey.name).join(JOURNEY_PATH_SEPARATOR)
}

// 1 for a top-level journey.
export function journeyLevel(tree: JourneyTree, id: string): number {
  return journeyPath(tree, id).length
}

// How many levels a journey and its deepest sub-journey span: 1 for a journey
// with no sub-journeys.
export function subtreeHeight(tree: JourneyTree, id: string): number {
  let height = 0
  let level: string[] = [id]
  const visited = new Set<string>()

  while (level.length > 0) {
    height += 1
    const next: string[] = []
    for (const current of level) {
      if (visited.has(current)) {
        continue
      }
      visited.add(current)
      for (const child of childrenOf(tree, current)) {
        next.push(child.id)
      }
    }
    level = next
  }
  return height
}

// A journey and every journey beneath it.
export function subtreeIds(tree: JourneyTree, id: string): Set<string> {
  const ids = new Set<string>()
  const stack: string[] = [id]

  while (stack.length > 0) {
    const current: string = stack.pop() as string
    if (ids.has(current)) {
      continue
    }
    ids.add(current)
    for (const child of childrenOf(tree, current)) {
      stack.push(child.id)
    }
  }
  return ids
}

// Whether a journey — `movingId`, or a new one when null — may sit directly
// inside `parentId` (null: at the top level). Mirrors the server's checks: not
// inside itself or its own sub-journeys, and not deeper than MAX_JOURNEY_DEPTH
// counting everything that moves along with it.
export function canPlaceUnder(
  tree: JourneyTree,
  movingId: string | null,
  parentId: string | null,
): boolean {
  const movingHeight: number = movingId === null ? 1 : subtreeHeight(tree, movingId)
  if (parentId === null) {
    return movingHeight <= MAX_JOURNEY_DEPTH
  }
  if (movingId !== null && subtreeIds(tree, movingId).has(parentId)) {
    return false
  }
  return journeyLevel(tree, parentId) + movingHeight <= MAX_JOURNEY_DEPTH
}

// Every journey in depth-first order — each one followed by its sub-journeys —
// with its level, for a picker's unfiltered list.
export function flattenJourneyTree(
  tree: JourneyTree,
): { journey: JourneyDto; level: number }[] {
  const result: { journey: JourneyDto; level: number }[] = []
  const visited = new Set<string>()

  const visit = (parentId: string | null, level: number): void => {
    for (const journey of childrenOf(tree, parentId)) {
      if (visited.has(journey.id)) {
        continue
      }
      visited.add(journey.id)
      result.push({ journey, level })
      visit(journey.id, level + 1)
    }
  }

  visit(null, 1)
  return result
}

// Folds case and accents, so "cesta" finds "Česta" and "CROATIA" finds "Croatia".
export function normalizeForSearch(text: string): string {
  return text.normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase()
}

// The journeys whose name contains the query, names that START with it first,
// then by full path. `total` is how many matched before the cap, so the caller
// can say how many it left out.
export function searchJourneys(
  tree: JourneyTree,
  query: string,
  limit: number,
): { matches: JourneyDto[]; total: number } {
  const needle: string = normalizeForSearch(query.trim())
  if (needle.length === 0) {
    return { matches: [], total: 0 }
  }

  const prefixed: JourneyDto[] = []
  const contained: JourneyDto[] = []
  for (const journey of tree.byId.values()) {
    const name: string = normalizeForSearch(journey.name)
    const index: number = name.indexOf(needle)
    if (index === 0) {
      prefixed.push(journey)
    } else if (index > 0) {
      contained.push(journey)
    }
  }

  const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' })
  const byPath = (a: JourneyDto, b: JourneyDto): number =>
    collator.compare(journeyPathLabel(tree, a.id), journeyPathLabel(tree, b.id))
  const all: JourneyDto[] = [...prefixed.sort(byPath), ...contained.sort(byPath)]

  return { matches: all.slice(0, limit), total: all.length }
}
