/**
 * Event handlers for gameplay events.
 * These are called by the socket layer when events arrive.
 * All event handlers validate inputs and update game state.
 */

import { Server } from 'socket.io'
import { GameRoom, PlayerMovePayload, GuardMarkPayload, Vector3 } from './types.js'
import { updatePlayerMovement, removePlayer, distance, endGame } from './state.js'
import {
  validatePlayerMovement,
  validateGuardCatch,
  validateInteractionDistance,
  validatePayloadOwnership,
} from './validation.js'
import { getRoom } from './room-manager.js'
import { GameManager } from './systems/game-manager.js'

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

// Track last move timestamp and move count per player
const lastMoveTimestamp = new Map<string, number>()
const playerMoveCount = new Map<string, number>()

// How many moves to skip speed validation for after spawn (grace period)
const SPAWN_GRACE_MOVES = 5

export function handlePlayerMove(context: PlayerMoveContext): void {
  const { io, roomId, room, socketId, payload } = context

  moveLogCounter++
  if (moveLogCounter % 20 === 1) {
    console.log(`[MOVE] RECV #${moveLogCounter} from=${socketId} payloadId=${payload.playerId} pos=(${payload.position?.x?.toFixed(2)}, ${payload.position?.y?.toFixed(2)}, ${payload.position?.z?.toFixed(2)}) state=${payload.movementState}`)
  }

  const player = room.state.players.get(socketId)
  if (!player) {
    console.warn(`[MOVE] Player ${socketId} not in game`)
    return
  }

  // Ownership check
  const ownerCheck = validatePayloadOwnership(payload.playerId, socketId)
  if (!ownerCheck.valid) {
    console.warn(`[MOVE] Ownership mismatch for ${socketId}: payload.playerId=${payload.playerId} socket.id=${socketId}`)
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
    console.log(`[MOVE] ${socketId} spawn grace move #${moveNum} accepted pos=(${payload.position?.x?.toFixed(2)},${payload.position?.y?.toFixed(2)},${payload.position?.z?.toFixed(2)})`)
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
// player:interact handler (pickup/use/drop items)
// ============================================================================

export interface PlayerInteractContext {
  io: Server
  roomId: string
  room: GameRoom
  socketId: string
  playerId: string
  objectId: string
  action: 'pickup' | 'use' | 'drop'
  timestamp: number
}

/**
 * Handles item interactions (pickup, use, drop).
 * Validates distance and item availability, then broadcasts result.
 */
export function handlePlayerInteract(context: PlayerInteractContext): void {
  const { io, roomId, room, socketId, playerId, objectId, action, timestamp } = context

  const player = room.state.players.get(socketId)
  if (!player) {
    console.warn(`[INTERACT] Player ${socketId} not in game`)
    return
  }

  // Ownership
  if (playerId !== socketId) {
    console.warn(`[INTERACT] Ownership mismatch for ${socketId}`)
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

  // Dispatch by action type
  switch (action) {
    case 'pickup':
      handleItemPickup(io, roomId, room, socketId, objectId, item)
      break
    case 'use':
      handleItemUse(io, roomId, room, socketId, objectId, item)
      break
    case 'drop':
      handleItemDrop(io, roomId, room, socketId, objectId, item)
      break
  }
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
  const target = room.state.players.get(targetId)

  if (!guard || !target) {
    console.warn(`[CATCH] Player not found (guard=${socketId}, target=${targetId})`)
    return
  }

  if (guard.role !== 'guard') {
    console.warn(`[CATCH] ${socketId} is not a guard`)
    return
  }

  if (!target.isAlive) {
    console.warn(`[CATCH] Target ${targetId} is already caught/dead`)
    return
  }

  // Distance validation
  const catchCheck = validateGuardCatch(
    guard.position,
    target.position,
    target.movementState,
    1.5 // catch range in meters
  )

  if (!catchCheck.valid) {
    console.log(`[CATCH] Attempt failed: ${catchCheck.reason}`)
    io.to(socketId).emit('catch:failed', { reason: catchCheck.reason })
    return
  }

  // Catch is valid
  target.isAlive = false

  io.to(roomId).emit('guard:catch', {
    guardId: guard.id,
    targetId: target.id,
    success: true,
    isPlayer: true,
  })

  console.log(`[CATCH] Guard ${guard.id} caught prisoner ${target.id}`)

  // Notify game manager
  const gameManager = (room as any).gameManager as GameManager
  if (gameManager) {
    gameManager.onGuardCatch(targetId)
  }
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
  const target = room.state.players.get(targetId)

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
