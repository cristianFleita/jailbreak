/**
 * Room manager: manages game rooms, tick loop, and state broadcasts.
 * Handles the core game loop that synchronizes state to all clients.
 */

import { Server } from 'socket.io'
import { GameRoom, GameRoomState, GameConfig, NPCPositionUpdate, PlayerStateUpdate } from './types.js'
import { createGameRoomState, advanceTick, computeNPCDelta, spawnNPCs, startGame, endGame } from './state.js'
import { GameManager } from './systems/game-manager.js'

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
    minX: -50,
    maxX: 50,
    minY: 0,
    maxY: 20,
    minZ: -50,
    maxZ: 50,
  },
  maxPlayers: 4,
}

/**
 * Central registry of all active game rooms.
 */
const activeRooms = new Map<string, GameRoom>()

/**
 * Creates a new game room (called when first player joins a room ID).
 */
export function createRoom(roomId: string, config: Partial<GameConfig> = {}): GameRoom {
  const finalConfig = { ...defaultGameConfig, ...config }
  const state = createGameRoomState(roomId, finalConfig)

  const room: GameRoom = {
    state,
    config: finalConfig,
  }

  activeRooms.set(roomId, room)
  return room
}

/**
 * Gets an existing room or creates it if it doesn't exist.
 */
export function getOrCreateRoom(roomId: string, config?: Partial<GameConfig>): GameRoom {
  if (activeRooms.has(roomId)) {
    return activeRooms.get(roomId)!
  }
  return createRoom(roomId, config)
}

/**
 * Retrieves a room by ID.
 */
export function getRoom(roomId: string): GameRoom | undefined {
  return activeRooms.get(roomId)
}

/**
 * Destroys a room (called when last player leaves or game ends).
 */
export function destroyRoom(roomId: string): void {
  const room = activeRooms.get(roomId)
  if (!room) return

  // Stop all intervals
  if (room.tickLoopInterval) clearInterval(room.tickLoopInterval)
  if (room.phaseLoopInterval) clearInterval(room.phaseLoopInterval)

  activeRooms.delete(roomId)
  console.log(`Room ${roomId} destroyed`)
}

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

  let npcBroadcastCounter = 0
  const npcBroadcastThreshold = config.tickRate / npcSendRate // emit NPC every N ticks

  room.tickLoopInterval = setInterval(() => {
    try {
      // ========== Game Logic Tick ==========
      // Updates: NPC behavior, pursuits, victory conditions, etc.
      const tickResult = gameManager.tick()

      // Check if game should end
      if (tickResult.shouldEnd) {
        console.log(`[TICK] Game ending: winner=${tickResult.winner}, reason=${tickResult.reason}`)
        endGame(state, tickResult.winner as 'prisoners' | 'guards', tickResult.reason || 'unknown')

        // Broadcast game end to all
        io.to(state.id).emit('game:end', {
          winner: tickResult.winner,
          reason: tickResult.reason,
        })

        // Stop loop
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

  console.log(`Game loop started for room ${state.id} at ${config.tickRate} ticks/sec`)
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
  console.log(`Spawned ${count} NPCs in room ${room.state.id}`)
}

/**
 * Transitions a room from lobby to active game.
 * Spawns NPCs and starts tick loop.
 */
export function transitionToActive(io: Server, room: GameRoom): void {
  if (room.state.status !== 'lobby') {
    console.warn(`Cannot transition room ${room.state.id} from ${room.state.status} to active`)
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

  console.log(`Room ${room.state.id} transitioned to ACTIVE`)
}

/**
 * Gets the count of active rooms (for monitoring).
 */
export function getActiveRoomCount(): number {
  return activeRooms.size
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
