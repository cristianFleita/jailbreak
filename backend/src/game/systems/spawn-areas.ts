/**
 * Spawn Area system — backend-driven placement for route-critical items.
 *
 * Spec: design/gdd/ruta-1-ventilacion-industrial.md
 * Plan: design/gdd/ruta-1-implementation-plan.md (Phase C)
 *
 * Unity authors spawn areas in the GameScene and registers them once with
 * the backend via `route:register_spawn_areas`. The backend then chooses
 * one valid spawn area per critical item (`route1_cutters`, `route1_wrench`)
 * and re-broadcasts `item:state` with the final authoritative position.
 *
 * Registration is idempotent per roomId — the first non-empty registration
 * wins so two clients sending the same payload cannot flip positions mid-match.
 * A subsequent respawn (anti-softlock) reuses the stored areas without
 * re-asking Unity.
 */

import { Server } from 'socket.io'
import {
  GameRoomState,
  RouteItemId,
  RouteItemKind,
  SpawnAreaRegistration,
  Vector3,
} from '../types.js'
import {
  broadcastItemState,
  ensureItemState,
  getItemKind,
  inferItemKind,
  isTrackedRouteItem,
} from './route-inventory.js'

// Per-room flag: true once spawn areas have been registered for the match.
// Kept outside state to avoid polluting the serialized snapshot, and cleared
// when the room is destroyed (best-effort — short-lived Map entries).
const roomRegistrationLocked = new Map<string, boolean>()

// Anti-softlock: pending respawn timers keyed by `${roomId}:${itemId}`.
// Cleared whenever an item transitions out of the `dropped` state, or when
// the respawn fires and repositions the item.
const respawnTimers = new Map<string, NodeJS.Timeout>()

const DEFAULT_RESPAWN_DELAY_SECONDS = 45

function respawnKey(roomId: string, itemId: string): string {
  return `${roomId}:${itemId}`
}

/**
 * True if the room already has at least one spawn area registered
 * AND initial route item placement has been performed.
 */
export function hasSpawnAreasRegistered(state: GameRoomState): boolean {
  return roomRegistrationLocked.get(state.id) === true
}

export function clearSpawnAreaRegistration(roomId: string): void {
  roomRegistrationLocked.delete(roomId)
  for (const key of Array.from(respawnTimers.keys())) {
    if (key.startsWith(`${roomId}:`)) {
      const timer = respawnTimers.get(key)
      if (timer) clearTimeout(timer)
      respawnTimers.delete(key)
    }
  }
}

/**
 * Validates a single registration entry. Returns a cleaned copy on success
 * or a string describing the rejection reason on failure.
 */
function validateArea(raw: unknown): SpawnAreaRegistration | string {
  if (!raw || typeof raw !== 'object') return 'entry is not an object'

  const entry = raw as Partial<SpawnAreaRegistration>
  const { spawnAreaId, zoneId, position, allowedItemIds } = entry

  if (!spawnAreaId || typeof spawnAreaId !== 'string') return 'missing spawnAreaId'
  if (spawnAreaId.length > 64) return `spawnAreaId "${spawnAreaId}" exceeds 64 chars`
  if (!zoneId || typeof zoneId !== 'string') return `missing zoneId for ${spawnAreaId}`
  if (zoneId.length > 32) return `zoneId "${zoneId}" exceeds 32 chars`
  if (!position || typeof position !== 'object') return `missing position for ${spawnAreaId}`

  const { x, y, z } = position as Vector3
  if (![x, y, z].every((n) => Number.isFinite(n))) {
    return `non-finite position for ${spawnAreaId}`
  }

  if (!Array.isArray(allowedItemIds) || allowedItemIds.length === 0) {
    return `allowedItemIds empty for ${spawnAreaId}`
  }

  const unique = Array.from(new Set(allowedItemIds.filter((id): id is RouteItemId =>
    typeof id === 'string' && id.length > 0
  )))
  if (unique.length === 0) return `allowedItemIds invalid for ${spawnAreaId}`

  return {
    spawnAreaId,
    zoneId,
    position: { x, y, z },
    allowedItemIds: unique,
  }
}

export interface RegisterSpawnAreasResult {
  accepted: SpawnAreaRegistration[]
  rejected: string[]
  skipped: boolean
}

/**
 * Registers a batch of scene-authored spawn areas on the given room state.
 * The first successful registration locks placement for the match — later
 * registrations (e.g. a host re-sending) are no-ops.
 *
 * Returns the accepted list and rejection diagnostics so the caller can log
 * scene mis-authoring without dropping the entire payload.
 */
export function registerSpawnAreas(
  state: GameRoomState,
  payload: unknown
): RegisterSpawnAreasResult {
  const result: RegisterSpawnAreasResult = { accepted: [], rejected: [], skipped: false }

  if (hasSpawnAreasRegistered(state)) {
    result.skipped = true
    return result
  }

  const rawAreas = Array.isArray((payload as any)?.areas) ? (payload as any).areas : null
  if (!rawAreas) {
    result.rejected.push('payload missing areas[] field')
    return result
  }

  if (!state.spawnAreas) state.spawnAreas = new Map()

  for (const raw of rawAreas) {
    const validated = validateArea(raw)
    if (typeof validated === 'string') {
      result.rejected.push(validated)
      continue
    }
    state.spawnAreas.set(validated.spawnAreaId, validated)
    result.accepted.push(validated)
  }

  if (result.accepted.length === 0) return result

  roomRegistrationLocked.set(state.id, true)
  return result
}

/**
 * Returns areas whose `allowedItemIds` accepts the given item — either by the
 * exact instance id or by the item's kind (e.g. an area listing
 * "route1_cutters" accepts both `route1_cutters_a` and `route1_cutters_b`).
 */
function candidatesForItem(
  state: GameRoomState,
  itemId: RouteItemId,
  kind?: RouteItemKind
): SpawnAreaRegistration[] {
  if (!state.spawnAreas) return []
  const out: SpawnAreaRegistration[] = []
  for (const area of state.spawnAreas.values()) {
    if (area.allowedItemIds.includes(itemId)) {
      out.push(area)
      continue
    }
    if (kind && area.allowedItemIds.includes(kind)) out.push(area)
  }
  return out
}

/**
 * For each critical route item already seeded in `state.items`, picks a valid
 * spawn area and updates its authoritative ItemState. When several instances
 * of the same kind exist (e.g. two cutters) they are placed at different
 * areas whenever there are enough candidates, so a softlock can never come
 * from both copies clustering on the same spot.
 *
 * Held/stored items are skipped — their placement is driven by players.
 *
 * This is the Phase C-02 entry point called right after a successful
 * registerSpawnAreas. It is also reused by the respawn path so an item
 * returned to the world picks a new area.
 */
export function placeCriticalItemsInSpawnAreas(
  io: Server,
  roomId: string,
  state: GameRoomState,
  targetItemIds?: readonly RouteItemId[]
): RouteItemId[] {
  if (!state.spawnAreas || state.spawnAreas.size === 0) return []

  // Default target list = every critical instance currently in state.items.
  // This keeps callers (and tests) that pre-seed items via ensureItemState
  // working transparently with N instances per kind.
  const targets: RouteItemId[] = targetItemIds
    ? targetItemIds.slice()
    : Array.from(state.items.values())
        .filter((it) => isTrackedRouteItem(it.itemId ?? it.id))
        .map((it) => (it.itemId ?? it.id) as RouteItemId)

  const placed: RouteItemId[] = []
  const usedAreaByKind = new Map<RouteItemKind, Set<string>>()

  for (const itemId of targets) {
    const item = ensureItemState(state, itemId, 'route_tool')

    // Skip items that are mid-play (held/stored) — their placement is already
    // driven by the player, not the spawn area system.
    const lifecycle = item.state ?? 'spawned'
    if (lifecycle === 'held' || lifecycle === 'stored') continue

    const kind = getItemKind(item) ?? inferItemKind(itemId)
    const candidates = candidatesForItem(state, itemId, kind)

    if (candidates.length === 0) {
      console.warn(
        `[SPAWN] No registered spawn area allows item "${itemId}" (kind=${kind ?? 'unknown'}) — leaving at current position ${JSON.stringify(item.position)}`
      )
      continue
    }

    // Prefer an area not yet used by another instance of the same kind.
    let pool = candidates
    if (kind) {
      const used = usedAreaByKind.get(kind) ?? new Set<string>()
      const fresh = candidates.filter((c) => !used.has(c.spawnAreaId))
      if (fresh.length > 0) pool = fresh
    }

    const chosen = pool[Math.floor(Math.random() * pool.length)]
    item.spawnAreaId = chosen.spawnAreaId
    item.position = { ...chosen.position }
    item.state = 'spawned'
    item.holderUserId = undefined
    item.isPickedUp = false
    item.pickedUpBy = undefined
    if (kind && !item.itemKind) item.itemKind = kind

    if (kind) {
      const used = usedAreaByKind.get(kind) ?? new Set<string>()
      used.add(chosen.spawnAreaId)
      usedAreaByKind.set(kind, used)
    }

    broadcastItemState(io, roomId, item)
    placed.push(itemId)

    console.log(
      `[SPAWN] Placed "${itemId}" (kind=${kind ?? 'unknown'}) at ${chosen.spawnAreaId} (zone=${chosen.zoneId}) pos=(${chosen.position.x.toFixed(2)}, ${chosen.position.y.toFixed(2)}, ${chosen.position.z.toFixed(2)})`
    )
  }

  return placed
}

/**
 * Respawns a single critical item back into a valid spawn area.
 * Used by the anti-softlock timer when a tool is lost and no player can
 * recover it (e.g. dropped in an unreachable spot, or abandoned after
 * disconnect timeout). Picks a new spawn area — intentionally not the
 * previous one when multiple candidates exist — so the run varies slightly.
 */
export function respawnCriticalItem(
  io: Server,
  roomId: string,
  state: GameRoomState,
  itemId: RouteItemId
): boolean {
  if (!isTrackedRouteItem(itemId)) return false
  if (!state.spawnAreas || state.spawnAreas.size === 0) return false

  const item = state.items.get(itemId)
  if (!item) return false

  const lifecycle = item.state ?? 'spawned'
  if (lifecycle === 'held' || lifecycle === 'stored') return false

  const kind = getItemKind(item) ?? inferItemKind(itemId)
  const candidates = candidatesForItem(state, itemId, kind)
  if (candidates.length === 0) return false

  // Prefer an area different from the current one if we have options
  const prevAreaId = item.spawnAreaId
  const rotated = candidates.length > 1
    ? candidates.filter((a) => a.spawnAreaId !== prevAreaId)
    : candidates
  const pool = rotated.length > 0 ? rotated : candidates

  const chosen = pool[Math.floor(Math.random() * pool.length)]
  item.spawnAreaId = chosen.spawnAreaId
  item.position = { ...chosen.position }
  item.state = 'spawned'
  item.holderUserId = undefined
  item.isPickedUp = false
  item.pickedUpBy = undefined

  broadcastItemState(io, roomId, item)
  console.log(`[SPAWN] Respawned "${itemId}" at ${chosen.spawnAreaId}`)
  return true
}

/**
 * Schedules an anti-softlock respawn for a dropped critical item. Called
 * whenever a tool transitions to `dropped` (capture, voluntary leave,
 * reconnect timeout). Re-calling while a timer is already pending resets
 * the delay so repeated drops don't starve the route.
 *
 * The respawn is cancelled automatically when the item is picked up again
 * (via cancelPendingRespawn, called from route-inventory on state change).
 */
export function scheduleCriticalItemRespawn(
  io: Server,
  roomId: string,
  state: GameRoomState,
  itemId: RouteItemId,
  delaySeconds: number = DEFAULT_RESPAWN_DELAY_SECONDS
): void {
  if (!isTrackedRouteItem(itemId)) return

  const key = respawnKey(roomId, itemId)
  const existing = respawnTimers.get(key)
  if (existing) clearTimeout(existing)

  const timer = setTimeout(() => {
    respawnTimers.delete(key)

    // Refuse to respawn if the item has since been picked up or the room
    // itself went away.
    const item = state.items.get(itemId)
    if (!item) return
    const lifecycle = item.state ?? 'spawned'
    if (lifecycle !== 'dropped' && lifecycle !== 'respawning') return

    respawnCriticalItem(io, roomId, state, itemId)
  }, Math.max(1, delaySeconds) * 1000)

  respawnTimers.set(key, timer)
  console.log(
    `[SPAWN] Scheduled respawn for "${itemId}" in ${delaySeconds}s (room=${roomId})`
  )
}

export function cancelPendingRespawn(roomId: string, itemId: string): void {
  const key = respawnKey(roomId, itemId)
  const timer = respawnTimers.get(key)
  if (!timer) return
  clearTimeout(timer)
  respawnTimers.delete(key)
}
