/**
 * Event handlers for gameplay events.
 * These are called by the socket layer when events arrive.
 * All event handlers validate inputs and update game state.
 */

import { Server } from 'socket.io'
import {
  GameRoom,
  PlayerState,
  PlayerInteractAction,
  PlayerMovePayload,
  GuardMarkPayload,
  ThrowableHitPayload,
  ThrowableItemKind,
  ThrowableThrowPayload,
  Vector3,
} from './types.js'
import { updatePlayerMovement, removePlayer, distance, endGame } from './state.js'
import {
  validatePlayerMovement,
  validateGuardCatch,
  validateInteractionDistance,
} from './validation.js'
import { getRoom } from './room-manager.js'
import { GameManager } from './systems/game-manager.js'
import { RIOT_THRESHOLD } from './systems/penalties.js'
import {
  broadcastItemState,
  dropCriticalRouteItemsForPlayer,
  dropInventoryItem,
  isCriticalRouteItem,
  isTrackedRouteItem,
  pickupToHand,
  storeHeldItem,
} from './systems/route-inventory.js'
import {
  cancelPendingRespawn,
  placeCriticalItemsInSpawnAreas,
  registerSpawnAreas,
  scheduleCriticalItemRespawn,
} from './systems/spawn-areas.js'
import type { RegisterSpawnAreasPayload, RouteItemId } from './types.js'

// ============================================================================
// player:move handler
// ============================================================================

export interface PlayerMoveContext {
  io: Server
  roomId: string
  room: GameRoom
  socketId: string
  payload: PlayerMovePayload
  timestamp: number
}

/**
 * Handles player movement input.
 * - Validates speed and bounds
 * - Updates server state
 * - State is broadcast to all clients via tick loop
 * - Clients apply rubber-band correction when they receive `player:state`
 */
let moveLogCounter = 0
const DEBUG_MOVEMENT_LOGS = process.env.DEBUG_MOVEMENT === '1'

// Track last move timestamp and move count per player
const lastMoveTimestamp = new Map<string, number>()
const playerMoveCount = new Map<string, number>()

// How many moves to skip speed validation for after spawn (grace period)
const SPAWN_GRACE_MOVES = 5

export function handlePlayerMove(context: PlayerMoveContext): void {
  const { io, roomId, room, socketId, payload } = context

  moveLogCounter++
  if (DEBUG_MOVEMENT_LOGS && moveLogCounter % 20 === 1) {
    console.log(`[MOVE] RECV #${moveLogCounter} from=${socketId} payloadId=${payload.playerId} pos=(${payload.position?.x?.toFixed(2)}, ${payload.position?.y?.toFixed(2)}, ${payload.position?.z?.toFixed(2)}) state=${payload.movementState}`)
  }

  const player = room.state.players.get(socketId)
  if (!player) {
    console.warn(`[MOVE] Player ${socketId} not in game`)
    return
  }

  // Ownership check — payload.playerId must match the stable userId for this socket
  if (payload.playerId !== player.userId) {
    console.warn(`[MOVE] Ownership mismatch for ${socketId}: payload.playerId=${payload.playerId} player.userId=${player.userId}`)
    return
  }

  const moveNum = (playerMoveCount.get(socketId) ?? 0) + 1
  playerMoveCount.set(socketId, moveNum)

  const now = Date.now()
  const lastTime = lastMoveTimestamp.get(socketId) ?? now
  const realDelta = Math.max((now - lastTime) / 1000, room.config.tickInterval / 1000)
  lastMoveTimestamp.set(socketId, now)

  // Skip speed validation for the first N moves (spawn grace period)
  // Client may need a few frames to stabilize after teleport
  if (moveNum <= SPAWN_GRACE_MOVES) {
    if (DEBUG_MOVEMENT_LOGS) {
      console.log(`[MOVE] ${socketId} spawn grace move #${moveNum} accepted pos=(${payload.position?.x?.toFixed(2)},${payload.position?.y?.toFixed(2)},${payload.position?.z?.toFixed(2)})`)
    }
  } else {
    // Movement validation with real time delta
    const moveCheck = validatePlayerMovement(
      payload.position,
      player.position,
      realDelta,
      room.config
    )

    if (!moveCheck.valid) {
      console.warn(`[MOVE] ${socketId} rejected: ${moveCheck.reason} | new=(${payload.position?.x?.toFixed(2)},${payload.position?.y?.toFixed(2)},${payload.position?.z?.toFixed(2)}) old=(${player.position.x.toFixed(2)},${player.position.y.toFixed(2)},${player.position.z.toFixed(2)}) dt=${realDelta.toFixed(2)}s`)
      return
    }
  }

  // Update state
  updatePlayerMovement(
    room.state,
    socketId,
    payload.position,
    payload.rotation,
    payload.velocity,
    payload.movementState
  )

  // No immediate echo — state broadcasted via tick loop
  // This keeps server as single source of truth

  // Notify game manager
  const gameManager = (room as any).gameManager as GameManager
  if (gameManager) {
    gameManager.onPlayerMove(socketId)
  }
}

/** Clean up tracking when a player leaves */
export function clearPlayerMoveTracking(socketId: string): void {
  lastMoveTimestamp.delete(socketId)
  playerMoveCount.delete(socketId)
}

// ============================================================================
// npc:sync_state handler (from Host)
// ============================================================================

export interface NPCSyncStateContext {
  io: Server
  roomId: string
  room: GameRoom
  socketId: string
  payload: import('./types.js').NPCSyncStatePayload
}

/**
 * Handles periodic NPC state synchronization from the Host client.
 * Updates the server's authoritative positions and current action indices
 * so that reconnecting clients can fast-forward NPC animations and spawn
 * them at the correct mid-action coordinates.
 */
export function handleNPCSyncState(context: NPCSyncStateContext): void {
  const { room, socketId, payload } = context

  const player = room.state.players.get(socketId)
  // Only accept syncs from the Host
  if (!player || player.userId !== room.state.hostUserId) return

  if (!payload.npcs) return

  for (const sync of payload.npcs) {
    const npc = room.state.npcs.get(sync.npcId)
    if (npc) {
      npc.position = sync.position
      npc.rotation = sync.rotation
      npc.currentSequenceIndex = sync.currentSequenceIndex
      npc.currentActionId = sync.currentActionId
    }
  }
}

// ============================================================================
// player:interact handler (pickup/use/drop items)
// ============================================================================

export interface PlayerInteractContext {
  io: Server
  roomId: string
  room: GameRoom
  socketId: string
  playerId: string
  objectId: string
  action: PlayerInteractAction
  slotIndex?: number
  timestamp: number
}

/**
 * Handles item interactions (pickup, use, drop).
 * Validates distance and item availability, then broadcasts result.
 */
export function handlePlayerInteract(context: PlayerInteractContext): void {
  const { io, roomId, room, socketId, playerId, objectId, action, timestamp, slotIndex } = context

  const player = room.state.players.get(socketId)
  if (!player) {
    console.warn(`[INTERACT] Player ${socketId} not in game`)
    return
  }

  // Ownership — playerId must match the stable userId for this socket
  if (playerId !== player.userId) {
    console.warn(`[INTERACT] Ownership mismatch for ${socketId}: playerId=${playerId} player.userId=${player.userId}`)
    return
  }

  const stableObjectItemId = resolveStableItemId(room, objectId)

  // Route tools use the Phase B authoritative hand/slot lifecycle.
  // They do not go through legacy immediate inventory pickup/drop.
  if (action === 'item.pickup') {
    handleRouteItemPickup(io, roomId, room, socketId, player, objectId)
    return
  }

  // Defensive alias for stale clients/components that still emit the legacy
  // pickup action with a route-critical item id.
  if (action === 'pickup' && isCriticalRouteItem(stableObjectItemId)) {
    console.warn(`[PICKUP] ${player.userId} used legacy pickup action for ${stableObjectItemId}; routing through item.pickup`)
    handleRouteItemPickup(io, roomId, room, socketId, player, objectId)
    return
  }

  if (action === 'item.store') {
    handleRouteItemStore(io, roomId, room, socketId, player, objectId, slotIndex)
    return
  }

  if (action === 'item.throw') {
    handleRouteItemThrow(io, roomId, room, socketId, player, objectId)
    return
  }

  // Phase D — Route 1 mission interactions. The Route1System owns all
  // validation (object id, inventory gates, mission flags) and broadcasting;
  // we just dispatch.
  if (action.startsWith('route1.')) {
    handleRoute1Action(io, room, socketId, player, objectId, action)
    return
  }

  if (action === 'drop' && isCriticalRouteItem(stableObjectItemId)) {
    console.warn(`[DROP] ${player.userId} attempted to drop critical route tool ${stableObjectItemId}`)
    io.to(socketId).emit('game:error', { message: 'Critical route tools cannot be dropped' })
    return
  }

  const item = room.state.items.get(objectId)
  if (!item) {
    console.warn(`[INTERACT] Item ${objectId} not found`)
    io.to(socketId).emit('game:error', { message: 'Item not found' })
    return
  }

  // Distance check (must be within 2m)
  const distCheck = validateInteractionDistance(player.position, item.position)
  if (!distCheck.valid) {
    console.warn(`[INTERACT] ${socketId} rejected: ${distCheck.reason}`)
    io.to(socketId).emit('game:error', { message: distCheck.reason })
    return
  }

  // Dispatch by action type — use stable playerId (userId) not socketId
  switch (action) {
    case 'pickup':
      handleItemPickup(io, roomId, room, playerId, objectId, item)
      break
    case 'use':
      handleItemUse(io, roomId, room, playerId, objectId, item)
      break
    case 'drop':
      handleItemDrop(io, roomId, room, playerId, objectId, item)
      break
  }
}

function resolveStableItemId(room: GameRoom, objectId: string): string {
  const item = room.state.items.get(objectId)
  return item?.itemId ?? item?.id ?? objectId
}

function shouldValidateRoutePickupDistance(item: { state?: string; spawnAreaId?: string; position?: Vector3 }): boolean {
  if (item.state === 'dropped') return true
  if (item.spawnAreaId) return true

  // Phase B does not yet have backend-driven spawn areas, so route tools may
  // still carry the placeholder origin. Once Phase C registers spawn positions,
  // this branch becomes a normal server-side range check.
  const p = item.position
  if (!p) return false
  return Math.abs(p.x) + Math.abs(p.y) + Math.abs(p.z) > 0.001
}

function handleRouteItemPickup(
  io: Server,
  roomId: string,
  room: GameRoom,
  socketId: string,
  player: PlayerState,
  objectId: string
): void {
  const item = room.state.items.get(objectId)
  const stableItemId = item?.itemId ?? item?.id ?? objectId

  if (!item || !isTrackedRouteItem(stableItemId)) {
    console.warn(`[PICKUP] ${player.userId} requested unmanaged route item ${objectId}`)
    io.to(socketId).emit('game:error', { message: 'Route item not found' })
    return
  }

  if (shouldValidateRoutePickupDistance(item)) {
    const distCheck = validateInteractionDistance(player.position, item.position)
    if (!distCheck.valid) {
      console.warn(`[PICKUP] ${player.userId} rejected: ${distCheck.reason}`)
      io.to(socketId).emit('game:error', { message: distCheck.reason })
      return
    }
  }

  const result = pickupToHand(room.state, player, stableItemId)
  if (!result.success) {
    console.warn(`[PICKUP] ${player.userId} rejected ${stableItemId}: ${result.reason}`)
    io.to(socketId).emit('game:error', { message: result.reason })
    return
  }

  // Pickup cancels any pending anti-softlock respawn for this item.
  cancelPendingRespawn(roomId, stableItemId)

  broadcastItemState(io, roomId, result.item)
  notifyRoute1InventoryChanged(room)
  console.log(`[PICKUP] ${player.userId} picked up ${stableItemId} to hand`)
}

function handleRoute1Action(
  io: Server,
  room: GameRoom,
  socketId: string,
  player: PlayerState,
  objectId: string,
  action: PlayerInteractAction
): void {
  const gm = (room as any).gameManager as GameManager | undefined
  if (!gm || !gm.route1) {
    console.warn(`[ROUTE1] gameManager.route1 missing — dropping ${action}`)
    return
  }
  const r1 = gm.route1
  let result: { ok: boolean; reason?: string } = { ok: true }

  switch (action) {
    case 'route1.search_clue.start':
      result = r1.startSearchClue(player, objectId); break
    case 'route1.search_clue.stop':
      result = r1.stopSearchClue(player, objectId); break
    case 'route1.disable_server.start':
      result = r1.startDisableServer(player, objectId); break
    case 'route1.disable_server.stop':
      result = r1.stopDisableServer(player, objectId); break
    case 'route1.open_vent.start':
      result = r1.startOpenVent(player, objectId); break
    case 'route1.open_vent.stop':
      result = r1.stopOpenVent(player, objectId); break
    case 'route1.escape.start':
      result = r1.startEscape(player, objectId); break
    case 'route1.escape.stop':
      result = r1.stopEscape(player, objectId); break
    default:
      console.warn(`[ROUTE1] Unhandled action ${action}`)
      return
  }

  if (!result.ok) {
    console.warn(`[ROUTE1] ${player.userId} ${action} on ${objectId} rejected: ${result.reason}`)
    io.to(socketId).emit('game:error', { message: result.reason })
    return
  }

  console.log(`[ROUTE1] ${player.userId} ${action} ${objectId}`)
}

function handleRouteItemStore(
  io: Server,
  roomId: string,
  room: GameRoom,
  socketId: string,
  player: PlayerState,
  objectId: string,
  slotIndex?: number
): void {
  if (objectId && player.heldItemId && objectId !== player.heldItemId) {
    console.warn(`[STORE] ${player.userId} tried to store ${objectId} while holding ${player.heldItemId}`)
    io.to(socketId).emit('game:error', { message: 'That item is not in hand' })
    return
  }

  if (!isTrackedRouteItem(player.heldItemId)) {
    io.to(socketId).emit('game:error', { message: 'No route item in hand' })
    return
  }

  const result = storeHeldItem(room.state, player, slotIndex)
  if (!result.success) {
    console.warn(`[STORE] ${player.userId} rejected: ${result.reason}`)
    io.to(socketId).emit('game:error', { message: result.reason })
    return
  }

  broadcastItemState(io, roomId, result.item)
  notifyRoute1InventoryChanged(room)
  console.log(`[STORE] ${player.userId} stored ${result.item.itemId ?? result.item.id} in slot ${result.slotIndex}`)
}

function notifyRoute1InventoryChanged(room: GameRoom): void {
  const gm = (room as any).gameManager as GameManager | undefined
  gm?.route1?.notifyInventoryChanged()
}

const THROW_FORWARD_OFFSET = 1.0

function computeThrowDropPosition(player: PlayerState): Vector3 {
  // Drop in front of the player so the prefab doesn't overlap the avatar
  // capsule and re-trigger the pickup collider on the same frame.
  const yaw = player.rotation?.y ?? 0
  const w = player.rotation?.w ?? 1
  // Quaternion (0, sin(yaw/2), 0, cos(yaw/2)) → forward = (2*y*w, 0, 1 - 2*y*y)
  const fx = 2 * yaw * w
  const fz = 1 - 2 * yaw * yaw
  const len = Math.hypot(fx, fz) || 1
  return {
    x: player.position.x + (fx / len) * THROW_FORWARD_OFFSET,
    y: player.position.y,
    z: player.position.z + (fz / len) * THROW_FORWARD_OFFSET,
  }
}

function handleRouteItemThrow(
  io: Server,
  roomId: string,
  room: GameRoom,
  socketId: string,
  player: PlayerState,
  objectId: string
): void {
  const stableItemId = resolveStableItemId(room, objectId)

  if (!isTrackedRouteItem(stableItemId)) {
    console.warn(`[THROW] ${player.userId} requested unmanaged item drop ${objectId}`)
    io.to(socketId).emit('game:error', { message: 'That item cannot be dropped' })
    return
  }

  const hasInSlot = playerHasItemInSlot(player, stableItemId)
  const hasInHand = player.heldItemId === stableItemId
  if (!hasInSlot && !(hasInHand && canDropTrackedItemFromHand(stableItemId))) {
    console.warn(`[THROW] ${player.userId} tried to drop ${stableItemId} without an allowed source`)
    io.to(socketId).emit('game:error', { message: 'Store the item in a slot before dropping it' })
    return
  }

  const position = computeThrowDropPosition(player)
  const item = dropInventoryItem(io, roomId, room.state, player, stableItemId, position)
  if (!item) {
    console.warn(`[THROW] ${player.userId} dropInventoryItem returned null for ${stableItemId}`)
    return
  }

  // Cancel any active route1 interaction this player started while still
  // holding the tool (e.g. mid disable_server). Mission rollback runs as part
  // of notifyInventoryChanged below.
  const gm = (room as any).gameManager as GameManager | undefined
  gm?.route1?.cancelPlayerInteractions(player.userId)

  notifyRoute1InventoryChanged(room)

  // Clear any stale timer defensively. Voluntary throws intentionally leave
  // the tool where it landed so players can pass tools to each other.
  cancelPendingRespawn(roomId, stableItemId)

  console.log(`[THROW] ${player.userId} threw ${stableItemId} at (${position.x.toFixed(2)}, ${position.y.toFixed(2)}, ${position.z.toFixed(2)})`)
}

function playerHasItemInSlot(player: PlayerState, itemId: string): boolean {
  return (player.inventorySlots ?? []).some((slot) => slot?.itemId === itemId)
}

function canDropTrackedItemFromHand(itemId: string): boolean {
  return itemId === 'route1_soap' || itemId.startsWith('route1_soap_')
}

function handleItemPickup(io: Server, roomId: string, room: GameRoom, playerId: string, itemId: string, item: any): void {
  if (item.isPickedUp) {
    console.log(`[PICKUP] Item ${itemId} already picked up by ${item.pickedUpBy}`)
    io.to(playerId).emit('game:error', { message: 'Item already picked up' })
    return
  }

  // Mark item as picked up
  item.isPickedUp = true
  item.pickedUpBy = playerId

  // Broadcast to all
  io.to(roomId).emit('item:pickup', {
    playerId,
    itemId,
    slot: 0, // TODO: real inventory slot management
  })

  console.log(`[PICKUP] ${playerId} picked up ${itemId}`)

  // Notify game manager
  const gameManager = (room as any).gameManager as GameManager
  if (gameManager) {
    gameManager.onItemPickup(playerId, itemId)
  }
}

function handleItemUse(io: Server, roomId: string, room: GameRoom, playerId: string, itemId: string, item: any): void {
  // TODO: Implement item-specific use logic (depends on item system)
  // For now, just log and broadcast
  io.to(roomId).emit('item:use', {
    playerId,
    itemId,
  })

  console.log(`[USE] ${playerId} used ${itemId}`)
}

function handleItemDrop(io: Server, roomId: string, room: GameRoom, playerId: string, itemId: string, item: any): void {
  if (!item.isPickedUp || item.pickedUpBy !== playerId) {
    console.warn(`[DROP] ${playerId} cannot drop item ${itemId} (not owned)`)
    return
  }

  // Mark item as dropped
  item.isPickedUp = false
  item.pickedUpBy = undefined

  io.to(roomId).emit('item:drop', {
    playerId,
    itemId,
  })

  console.log(`[DROP] ${playerId} dropped ${itemId}`)
}

// ============================================================================
// route:register_spawn_areas handler (host-only, Phase C-02)
// ============================================================================

export interface RegisterSpawnAreasContext {
  io: Server
  roomId: string
  room: GameRoom
  socketId: string
  payload: RegisterSpawnAreasPayload
}

/**
 * Host registers every scene-authored spawn area for the current GameScene.
 * The backend validates each entry, stores them, and picks one valid spawn
 * area per critical route item. `item:state` is broadcast for each placed
 * item so every client repositions its pickable prefab.
 */
export function handleRegisterSpawnAreas(context: RegisterSpawnAreasContext): void {
  const { io, roomId, room, socketId, payload } = context

  const player = room.state.players.get(socketId)
  if (!player || player.userId !== room.state.hostUserId) {
    console.warn(`[SPAWN] Non-host ${socketId} tried to register spawn areas`)
    return
  }

  const result = registerSpawnAreas(room.state, payload)

  if (result.skipped) {
    console.log(`[SPAWN] "${roomId}" already has spawn areas — ignoring re-registration`)
    return
  }

  if (result.accepted.length === 0) {
    console.warn(
      `[SPAWN] "${roomId}" registration produced zero valid areas. Rejections: ${result.rejected.join(' | ') || 'none'}`
    )
    io.to(socketId).emit('game:error', {
      message: 'Spawn area registration rejected — no valid areas',
    })
    return
  }

  console.log(
    `[SPAWN] "${roomId}" registered ${result.accepted.length} spawn areas (rejected ${result.rejected.length})`
  )
  if (result.rejected.length > 0) {
    console.warn(`[SPAWN] Rejections: ${result.rejected.join(' | ')}`)
  }

  const placed = placeCriticalItemsInSpawnAreas(io, roomId, room.state)
  if (placed.length === 0) {
    console.warn(
      `[SPAWN] No critical items were placed — check allowedItemIds on the registered areas`
    )
  }
}

// ============================================================================
// player:action handler (animation / world-interaction broadcast)
// ============================================================================

export interface PlayerActionContext {
  io: Server
  roomId: string
  room: GameRoom
  socketId: string
  objectId: string
  action: string
  timestamp: number
}

/**
 * Handles generic world interactions that only need to be relayed so
 * other clients can replay the animation on this player's avatar
 * (sit/stand/etc). No item-inventory side effects — use player:interact
 * for those.
 *
 * Exception: a short allowlist of actions mutates a small piece of PlayerState
 * (e.g. `carrying`) so the visual prop survives reconnects.
 */
export function handlePlayerAction(context: PlayerActionContext): void {
  const { io, roomId, room, socketId, objectId, action } = context

  const player = room.state.players.get(socketId)
  if (!player) return

  if (!objectId || !action) return
  if (objectId.length > 128 || action.length > 32) return

  // Persist reconnect-safe side effects for known actions.
  // Keep this list tight — purely-animation actions (sit/stand) stay stateless.
  if (action === 'takeFood') {
    player.carrying = 'food_plate'
  } else if (action === 'leaveFood') {
    player.carrying = null
  } else if (action === 'grabClothes') {
    player.carrying = 'clothes_bundle'
  } else if (action === 'leaveClothes') {
    player.carrying = null
  } else if (action === 'startLoadWasher') {
    // Player put clothes into the washer — nothing in hand during the loop.
    player.carrying = null
  } else if (action === 'stopLoadWasher') {
    // Player cancelled mid-loop — the unfolded bundle is restored to hand.
    player.carrying = 'clothes_bundle'
  } else if (action === 'loadWasher') {
    // Washer completed — folded clothes now in hand.
    player.carrying = 'folded_clothes'
  } else if (action === 'storeClothes') {
    // Player stored the folded clothes on the shelf — nothing in hand.
    player.carrying = null
  }

  io.to(roomId).except(socketId).emit('player:action', {
    playerId: player.userId,
    objectId,
    action,
  })
}

// ============================================================================
// throwable:throw / throwable:hit handlers
// ============================================================================

const THROWABLE_ITEM_KINDS = new Set<ThrowableItemKind>([
  'food_plate',
  'clothes_bundle',
  'folded_clothes',
  'container',
  'soap',
])

const MIN_THROW_FORCE = 1
const MAX_THROW_FORCE = 30
const MIN_STUN_SECONDS = 0.25
const MAX_STUN_SECONDS = 5
const STUN_HIT_DEBOUNCE_MS = 900

const recentThrowableHits = new Map<string, number>()

export interface ThrowableThrowContext {
  io: Server
  roomId: string
  room: GameRoom
  socketId: string
  payload: ThrowableThrowPayload
  timestamp: number
}

export interface ThrowableHitContext {
  io: Server
  roomId: string
  room: GameRoom
  socketId: string
  payload: ThrowableHitPayload
  timestamp: number
}

export function handleThrowableThrow(context: ThrowableThrowContext): void {
  const { io, roomId, room, socketId, payload } = context

  const player = room.state.players.get(socketId)
  if (!player || player.role !== 'prisoner' || !player.isAlive) return
  if (!isValidThrowableKind(payload?.itemKind)) return
  if (!isFiniteVector(payload.origin) || !isFiniteVector(payload.direction)) return

  // Keep the server's reconnect-safe hand state in sync with the local throw.
  // The client has already detached the prop for responsiveness.
  player.carrying = null

  io.to(roomId).except(socketId).emit('throwable:throw', {
    throwerId: player.userId,
    itemKind: payload.itemKind,
    origin: payload.origin,
    direction: normalizeVector(payload.direction),
    force: clamp(payload.force, MIN_THROW_FORCE, MAX_THROW_FORCE),
  })
}

export function handleThrowableHit(context: ThrowableHitContext): void {
  const { io, roomId, room, socketId, payload, timestamp } = context

  const attacker = room.state.players.get(socketId)
  if (!attacker || attacker.role !== 'prisoner' || !attacker.isAlive) return
  if (!isValidThrowableKind(payload?.itemKind)) return
  if (!payload.targetGuardId || !isFiniteVector(payload.hitPosition)) return

  const guard = room.state.playersByUserId.get(payload.targetGuardId)
  if (!guard || guard.role !== 'guard' || !guard.isAlive) return

  if (payload.itemKind === 'soap') {
    handleSoapSlip(io, roomId, room, attacker, guard, payload)
    return
  }

  const debounceKey = `${attacker.userId}:${guard.userId}:${payload.itemKind}`
  const lastHitAt = recentThrowableHits.get(debounceKey) ?? 0
  if (timestamp - lastHitAt < STUN_HIT_DEBOUNCE_MS) return
  recentThrowableHits.set(debounceKey, timestamp)

  io.to(roomId).emit('guard:stun', {
    guardId: guard.userId,
    attackerId: attacker.userId,
    itemKind: payload.itemKind,
    duration: clamp(payload.stunDuration, MIN_STUN_SECONDS, MAX_STUN_SECONDS),
    hitPosition: payload.hitPosition,
  })
}

const SOAP_SLIP_MAX_DISTANCE = 2.25

function handleSoapSlip(
  io: Server,
  roomId: string,
  room: GameRoom,
  reporter: PlayerState,
  guard: PlayerState,
  payload: ThrowableHitPayload
): void {
  const itemId = payload.itemId
  if (!itemId || !isTrackedRouteItem(itemId)) return
  if (!(itemId === 'route1_soap' || itemId.startsWith('route1_soap_'))) return

  const item = room.state.items.get(itemId)
  if (!item) return

  const lifecycle = item.state ?? 'spawned'
  if (lifecycle !== 'spawned' && lifecycle !== 'dropped') return

  // Server-side sanity check: the guard must be close to the authoritative soap
  // position. Unity owns the exact trigger, this just rejects obvious bad reports.
  if (distance(guard.position, item.position) > SOAP_SLIP_MAX_DISTANCE) return

  item.state = 'respawning'
  item.holderUserId = undefined
  item.isPickedUp = false
  item.pickedUpBy = undefined
  broadcastItemState(io, roomId, item)
  scheduleCriticalItemRespawn(io, roomId, room.state, itemId as RouteItemId)

  io.to(roomId).emit('guard:stun', {
    guardId: guard.userId,
    attackerId: reporter.userId,
    itemKind: payload.itemKind,
    duration: clamp(payload.stunDuration, MIN_STUN_SECONDS, MAX_STUN_SECONDS),
    hitPosition: payload.hitPosition,
  })

  console.log(`[SOAP] ${reporter.userId} tripped guard ${guard.userId} with ${itemId}`)
}

function isValidThrowableKind(kind: string | undefined): kind is ThrowableItemKind {
  return !!kind && THROWABLE_ITEM_KINDS.has(kind as ThrowableItemKind)
}

function isFiniteVector(v: Vector3 | undefined): v is Vector3 {
  return !!v
    && Number.isFinite(v.x)
    && Number.isFinite(v.y)
    && Number.isFinite(v.z)
}

function normalizeVector(v: Vector3): Vector3 {
  const mag = Math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z)
  if (mag <= 0.0001) return { x: 0, y: 0, z: 1 }
  return { x: v.x / mag, y: v.y / mag, z: v.z / mag }
}

function clamp(value: number, min: number, max: number): number {
  if (!Number.isFinite(value)) return min
  return Math.max(min, Math.min(max, value))
}

// ============================================================================
// guard:catch handler (guard attempts to catch prisoner)
// ============================================================================

export interface GuardCatchContext {
  io: Server
  roomId: string
  room: GameRoom
  socketId: string
  guardId: string
  targetId: string
  timestamp: number
}

/**
 * Handles guard catch attempt.
 * Validates distance and target state, then broadcasts result.
 * Catch is only valid if:
 * - Distance ≤ 1.5m
 * - Target is not camuflaged
 * - Guard and target both alive
 */
export function handleGuardCatch(context: GuardCatchContext): void {
  const { io, roomId, room, socketId, guardId, targetId, timestamp } = context

  const guard = room.state.players.get(socketId)
  if (!guard || guard.role !== 'guard') {
    console.warn(`[CATCH] ${socketId} is not a valid guard`)
    return
  }

  const targetPlayer = room.state.playersByUserId.get(targetId)
  const targetNpc = room.state.npcs.get(targetId)

  if (!targetPlayer && !targetNpc) {
    console.warn(`[CATCH] Target ${targetId} not found`)
    io.to(socketId).emit('catch:failed', { reason: 'Target not found' })
    return
  }

  const isPlayer = !!targetPlayer
  const targetPosition = isPlayer ? targetPlayer.position : targetNpc!.position
  const targetMovementState = isPlayer ? targetPlayer.movementState : 'idle'

  if (isPlayer && !targetPlayer!.isAlive) {
    console.warn(`[CATCH] Target ${targetId} is already caught/dead`)
    io.to(socketId).emit('catch:failed', { reason: 'Target already dead' })
    return
  }

  // Distance validation is now handled entirely on the Unity client during the 0.5s focus.
  // We skip backend distance checks because NPC positions are simulated in Unity (NavMesh) 
  // and their backend positions are stale, leading to false out-of-range rejections.

  // Catch is valid - could be player or NPC
  if (isPlayer) {
    targetPlayer!.isAlive = false
    const droppedItems = dropCriticalRouteItemsForPlayer(
      io,
      roomId,
      room.state,
      targetPlayer!,
      targetPlayer!.position,
      (itemId) => scheduleCriticalItemRespawn(io, roomId, room.state, itemId as RouteItemId)
    )
    // Cancel any in-flight route1 interaction (clue search, sabotage, vent
    // open, ESCAPE) the captured prisoner had, then refresh route1 so missions
    // reflect the inventory drops.
    const gm = (room as any).gameManager as GameManager | undefined
    gm?.route1?.cancelPlayerInteractions(targetPlayer!.userId)
    if (droppedItems.length > 0) gm?.route1?.notifyInventoryChanged()
    console.log(`[CATCH] Guard ${guard.id} caught prisoner ${targetPlayer!.id}`)
  } else {
    console.log(`[CATCH] Guard ${guard.id} mistakenly caught NPC ${targetNpc!.id}`)
    // Need to trigger error in guard penalties here, maybe via gameManager
  }

  // Notify game manager FIRST so penalty counter is recorded before broadcast,
  // letting clients display the up-to-date guard error count on the same event.
  const gameManager = (room as any).gameManager as GameManager
  if (gameManager) {
    gameManager.onGuardCatch(guard.id, targetId, isPlayer)
  }

  const guardErrorCount = gameManager?.penalty?.getGuardErrorCount(guard.id) ?? 0

  io.to(roomId).emit('guard:catch', {
    guardId: guard.id,
    targetId: targetId,
    success: true,
    isPlayer: isPlayer,
    guardErrorCount,
    guardErrorThreshold: RIOT_THRESHOLD,
  })
}

// ============================================================================
// guard:mark handler (guard marks prisoner to start chase)
// ============================================================================

export interface GuardMarkContext {
  io: Server
  roomId: string
  room: GameRoom
  socketId: string
  guardId: string
  targetId: string
  timestamp: number
}

/**
 * Handles guard marking a prisoner (initiates chase).
 * Broadcasts `chase:start` to all clients.
 * Actual chase logic will be implemented by pursuit system (Fase 3).
 */
export function handleGuardMark(context: GuardMarkContext): void {
  const { io, roomId, room, socketId, guardId, targetId } = context

  const guard = room.state.players.get(socketId)
  const target = room.state.playersByUserId.get(targetId)

  if (!guard || !target) {
    console.warn(`[MARK] Player not found`)
    return
  }

  if (guard.role !== 'guard') {
    console.warn(`[MARK] ${socketId} is not a guard`)
    return
  }

  // Emit chase:start to all (pursuit system will manage the actual chase)
  io.to(roomId).emit('chase:start', {
    guardId: guard.id,
    targetId: target.id,
  })

  console.log(`[MARK] Guard ${guard.id} marked prisoner ${target.id}`)

  // Notify game manager
  const gameManager = (room as any).gameManager as GameManager
  if (gameManager) {
    gameManager.onGuardMark(guard.id, target.id)
  }
}

// ============================================================================
// riot:activate handler (prisoner activates riot)
// ============================================================================

export interface RiotActivateContext {
  io: Server
  roomId: string
  room: GameRoom
  socketId: string
  prisonerId: string
  timestamp: number
}

/**
 * Handles prisoner activating riot.
 * Validates that riot is available (guard has 3+ errors), then ends game with prisoners winning.
 */
export function handleRiotActivate(context: RiotActivateContext): void {
  const { io, roomId, room, socketId, prisonerId } = context

  const prisoner = room.state.players.get(socketId)
  if (!prisoner) {
    console.warn(`[RIOT] Player ${socketId} not in game`)
    return
  }

  if (prisoner.role !== 'prisoner') {
    console.warn(`[RIOT] ${socketId} is not a prisoner`)
    return
  }

  // TODO: Check if riot is available (depends on guard error tracking in Fase 3)
  // For now, allow it
  const riotAvailable = true

  if (!riotAvailable) {
    io.to(socketId).emit('game:error', { message: 'Riot not available yet' })
    return
  }

  // Notify game manager (which will end the game)
  const gameManager = (room as any).gameManager as GameManager
  if (gameManager) {
    gameManager.onRiotActivated()
  }

  console.log(`[RIOT] Prisoner ${prisoner.id} activated riot — game over`)
}

// ============================================================================
// Utility: Check win conditions
// ============================================================================

/**
 * Checks if the game has reached a win condition.
 * Returns winner and reason if so, null otherwise.
 */
export function checkGameEndCondition(room: GameRoom): { winner: 'prisoners' | 'guards' | null; reason?: string } {
  const players = Array.from(room.state.players.values())
  const prisoners = players.filter((p) => p.role === 'prisoner')
  const alivePrisoners = prisoners.filter((p) => p.isAlive)

  // Guard wins if all prisoners caught
  if (alivePrisoners.length === 0) {
    return { winner: 'guards', reason: 'all_prisoners_caught' }
  }

  // TODO: Prisoners win if all escape (depends on escape system in Fase 3)
  // TODO: Prisoners win if riot activated (already handled in handleRiotActivate)

  return { winner: null }
}
