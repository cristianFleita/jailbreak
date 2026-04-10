/**
 * Room manager: manages game rooms, tick loop, and state broadcasts.
 * Handles the core game loop that synchronizes state to all clients.
 *
 * Room lifecycle:
 *   host creates room → players join → host starts → game active → game ends
 *   host disconnects → room destroyed (all players kicked)
 */

import { Server } from 'socket.io'
import {
  GameRoom, GameRoomState, GameConfig, NPCPositionUpdate, PlayerStateUpdate,
  RoomStatePayload, PlayerRole,
} from './types.js'
import { createGameRoomState, advanceTick, computeNPCDelta, spawnNPCs, startGame, endGame } from './state.js'
import { GameManager } from './systems/game-manager.js'
import { getUser } from './user-identity.js'

/**
 * Default game configuration (tuning knobs from design doc).
 */
export const defaultGameConfig: GameConfig = {
  tickRate: 20, // 20 ticks per second
  tickInterval: 50, // 50ms per tick
  npcSendRate: 5, // send NPC positions 5 times per second (every 200ms)
  npcDeltaThreshold: 0.1, // only send NPCs that moved >0.1m
  interpolationBuffer: 100, // clients buffer 100ms (2 ticks)
  reconciliationThreshold: 1.0, // rubber-band if diff >1m
  reconciliationLerpSpeed: 0.3, // lerp speed for rubber-band
  anticheatSpeedMultiplier: 1.5, // speed multiplier for anti-cheat
  reconnectTimeout: 30, // 30 seconds to reconnect
  mapBounds: {
    minX: -300,
    maxX: 300,
    minY: -10,   // buffer for floating-point ground level
    maxY: 100,
    minZ: -300,
    maxZ: 300,
  },
  maxPlayers: 4,
}

/**
 * Central registry of all active game rooms.
 */
const activeRooms = new Map<string, GameRoom>()

// ============================================================================
// Room CRUD
// ============================================================================

/**
 * Creates a new room. The host's userId becomes the room owner.
 * Returns null if a room with that name already exists.
 */
export function createRoom(
  roomId: string,
  hostUserId: string,
  config: Partial<GameConfig> = {}
): GameRoom | null {
  if (activeRooms.has(roomId)) {
    return null // room name taken
  }

  const finalConfig = { ...defaultGameConfig, ...config }
  const state = createGameRoomState(roomId, hostUserId, finalConfig)

  const room: GameRoom = {
    state,
    config: finalConfig,
  }

  activeRooms.set(roomId, room)
  console.log(`[ROOM] Created room "${roomId}" (host: ${hostUserId})`)
  return room
}

/**
 * Retrieves a room by ID.
 */
export function getRoom(roomId: string): GameRoom | undefined {
  return activeRooms.get(roomId)
}

/**
 * Checks if a room exists.
 */
export function roomExists(roomId: string): boolean {
  return activeRooms.has(roomId)
}

/**
 * Destroys a room (called when host leaves, game ends, or room is empty).
 */
export function destroyRoom(roomId: string): void {
  const room = activeRooms.get(roomId)
  if (!room) return

  // Stop all intervals
  if (room.tickLoopInterval) clearInterval(room.tickLoopInterval)
  if (room.phaseLoopInterval) clearInterval(room.phaseLoopInterval)

  activeRooms.delete(roomId)
  console.log(`[ROOM] Destroyed room "${roomId}"`)
}

// ============================================================================
// Room player list helpers
// ============================================================================

/**
 * Builds the player list payload for room state broadcasts.
 */
export function buildRoomPlayersPayload(room: GameRoom): RoomStatePayload['players'] {
  const players: RoomStatePayload['players'] = []

  for (const [_socketId, player] of room.state.players) {
    const userProfile = getUser(player.userId)
    players.push({
      userId: player.userId,
      displayName: userProfile?.displayName || `Player_${player.userId.slice(0, 6)}`,
      role: player.role,
      isHost: player.userId === room.state.hostUserId,
    })
  }

  return players
}

/**
 * Builds the full room state payload.
 */
export function buildRoomStatePayload(room: GameRoom): RoomStatePayload {
  return {
    roomId: room.state.id,
    hostUserId: room.state.hostUserId,
    status: room.state.status,
    players: buildRoomPlayersPayload(room),
  }
}

/**
 * Finds a player's socketId by their userId within a room.
 */
export function findSocketByUserId(room: GameRoom, userId: string): string | undefined {
  const player = room.state.playersByUserId.get(userId)
  return player?.id // player.id is the socketId
}

// ============================================================================
// Game loop (unchanged from before — drives tick, broadcast, win checks)
// ============================================================================

/**
 * Starts the game loop for a room.
 * - Executes game manager tick (physics, logic, win conditions)
 * - Emits `player:state` every 50ms (20 ticks/sec)
 * - Emits `npc:positions` every 200ms (5 sends/sec, delta compressed)
 * - Emits `game:end` if game-ending condition reached
 */
export function startGameLoop(io: Server, room: GameRoom): void {
  const { state, config } = room
  const { tickInterval, npcSendRate } = config

  // Initialize game manager (all systems)
  const gameManager = new GameManager(room)
  ;(room as any).gameManager = gameManager

  // ── Wire jail routine callbacks ──
  gameManager.jailRoutine.onPhaseWarning = (payload) => {
    io.to(state.id).emit('phase:warning', payload)
    console.log(`[JAIL] Emitted phase:warning → Phase ${payload.nextPhase}`)
  }
  gameManager.jailRoutine.onPhaseStart = (payload) => {
    io.to(state.id).emit('phase:start', payload)
    console.log(`[JAIL] Emitted phase:start → Phase ${payload.phase} (${payload.phaseName})`)
  }
  gameManager.jailRoutine.onNPCReassign = (payload) => {
    io.to(state.id).emit('npc:reassign', payload)
  }
  gameManager.jailRoutine.onZoneCheck = (playerId, payload) => {
    io.to(playerId).emit('phase:zone_check', payload)
  }

  // Start jail routine
  gameManager.jailRoutine.start()

  let npcBroadcastCounter = 0
  const npcBroadcastThreshold = config.tickRate / npcSendRate // emit NPC every N ticks

  room.tickLoopInterval = setInterval(() => {
    try {
      // ========== Game Logic Tick ==========
      const tickResult = gameManager.tick()

      // Check if game should end
      if (tickResult.shouldEnd) {
        console.log(`[TICK] Game ending: winner=${tickResult.winner}, reason=${tickResult.reason}`)
        endGame(state, tickResult.winner as 'prisoners' | 'guards', tickResult.reason || 'unknown')

        io.to(state.id).emit('game:end', {
          winner: tickResult.winner,
          reason: tickResult.reason,
        })

        stopGameLoop(room)
        return
      }

      advanceTick(state)

      // ========== Broadcast player state every tick ==========
      const playerStatePayload: PlayerStateUpdate = {
        players: Array.from(state.players.values()),
      }

      io.to(state.id).emit('player:state', playerStatePayload)

      // ========== Broadcast NPC positions every Nth tick (delta compressed) ==========
      npcBroadcastCounter++
      if (npcBroadcastCounter >= npcBroadcastThreshold) {
        const deltaedNPCs = computeNPCDelta(state, config.npcDeltaThreshold)
        const npcPayload: NPCPositionUpdate = {
          npcs: deltaedNPCs,
          tick: state.tick,
        }

        io.to(state.id).emit('npc:positions', npcPayload)
        npcBroadcastCounter = 0
      }
    } catch (err) {
      console.error(`[TICK-ERROR] ${err}`)
    }
  }, tickInterval)

  console.log(`[ROOM] Game loop started for "${state.id}" at ${config.tickRate} ticks/sec`)
}

/**
 * Stops the game loop for a room.
 */
export function stopGameLoop(room: GameRoom): void {
  if (room.tickLoopInterval) {
    clearInterval(room.tickLoopInterval)
    room.tickLoopInterval = undefined
  }
}

/**
 * Initializes NPCs for a room (called when game starts).
 */
export function initializeNPCs(room: GameRoom, count: number = 20): void {
  spawnNPCs(room.state, room.config, count)
  console.log(`[ROOM] Spawned ${count} NPCs in "${room.state.id}"`)
}

/**
 * Transitions a room from lobby to active game.
 * Only the host can trigger this.
 */
export function transitionToActive(io: Server, room: GameRoom): void {
  if (room.state.status !== 'lobby') {
    console.warn(`Cannot transition room "${room.state.id}" from ${room.state.status} to active`)
    return
  }

  startGame(room.state)
  initializeNPCs(room)
  startGameLoop(io, room)

  // Notify all clients that game started
  io.to(room.state.id).emit('game:start', {
    players: Array.from(room.state.players.values()),
    npcs: Array.from(room.state.npcs.values()),
    phase: room.state.phase,
  })

  console.log(`[ROOM] "${room.state.id}" transitioned to ACTIVE`)
}

// ============================================================================
// Monitoring
// ============================================================================

/**
 * Gets the count of active rooms (for monitoring).
 */
export function getActiveRoomCount(): number {
  return activeRooms.size
}

/**
 * Lists all active rooms (for room browser).
 */
export function listRooms(): RoomStatePayload[] {
  const result: RoomStatePayload[] = []
  for (const [_id, room] of activeRooms) {
    if (room.state.status === 'lobby') {
      result.push(buildRoomStatePayload(room))
    }
  }
  return result
}

/**
 * Gets room state for debugging/monitoring.
 */
export function debugGetRoom(roomId: string): {
  state: GameRoomState
  playerCount: number
  npcCount: number
} | null {
  const room = getRoom(roomId)
  if (!room) return null

  return {
    state: room.state,
    playerCount: room.state.players.size,
    npcCount: room.state.npcs.size,
  }
}
