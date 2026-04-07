/**
 * Game state management and lifecycle.
 * Maintains all mutable state for a game room.
 */

import { GameRoomState, GameConfig, PlayerState, NPCState, ItemState, Vector3 } from './types.js'

/**
 * Creates an empty game room state ready for players to join.
 */
export function createGameRoomState(roomId: string, hostUserId: string, config: GameConfig): GameRoomState {
  return {
    id: roomId,
    hostUserId,
    status: 'lobby',
    players: new Map(),
    playersByUserId: new Map(),
    npcs: new Map(),
    items: new Map(),
    phase: {
      current: 'setup',
      phaseName: 'Setup',
      duration: 30, // 30 seconds to gather in lobby
      startedAt: Date.now(),
    },
    tick: 0,
    createdAt: Date.now(),
  }
}

/**
 * Adds a player to the game state.
 * Role defaults to 'prisoner' in lobby — roles are reassigned randomly
 * when the host starts the game via assignRandomRoles().
 */
export function addPlayer(
  state: GameRoomState,
  playerId: string,
  userId: string,
  initialPosition: Vector3
): PlayerState {
  if (state.players.size >= 4) {
    throw new Error('Room is full (max 4 players)')
  }

  const player: PlayerState = {
    id: playerId,
    userId,
    role: 'prisoner', // placeholder — reassigned on game start
    position: { ...initialPosition },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    velocity: { x: 0, y: 0, z: 0 },
    movementState: 'idle',
    isAlive: true,
  }

  state.players.set(playerId, player)
  state.playersByUserId.set(userId, player)
  return player
}

/**
 * Randomly assigns roles: exactly 1 guard, rest are prisoners.
 * Called when the host starts the game.
 */
export function assignRandomRoles(state: GameRoomState): void {
  const players = Array.from(state.players.values())
  if (players.length === 0) return

  // Pick a random player to be the guard
  const guardIndex = Math.floor(Math.random() * players.length)

  for (let i = 0; i < players.length; i++) {
    players[i].role = i === guardIndex ? 'guard' : 'prisoner'
  }
}

/**
 * Removes a player from the game state (disconnect or timeout).
 */
export function removePlayer(state: GameRoomState, playerId: string): void {
  const player = state.players.get(playerId)
  if (player) {
    state.playersByUserId.delete(player.userId)
  }
  state.players.delete(playerId)
}

/**
 * Updates player position and movement state.
 * Called each time a client sends `player:move` event.
 */
export function updatePlayerMovement(
  state: GameRoomState,
  playerId: string,
  position: Vector3,
  rotation: { x: number; y: number; z: number; w: number },
  velocity: Vector3,
  movementState: 'idle' | 'walking' | 'sprinting' | 'camuflaged'
): void {
  const player = state.players.get(playerId)
  if (!player) return

  player.position = { ...position }
  player.rotation = { ...rotation }
  player.velocity = { ...velocity }
  player.movementState = movementState
}

/**
 * Spawns NPCs for the room (called when game starts).
 * Creates 20 NPCs with random positions within map bounds.
 */
export function spawnNPCs(state: GameRoomState, config: GameConfig, npcCount: number = 20): void {
  const { mapBounds } = config

  for (let i = 0; i < npcCount; i++) {
    const npcId = `npc_guard_${String(i).padStart(3, '0')}`

    const randomPosition: Vector3 = {
      x: Math.random() * (mapBounds.maxX - mapBounds.minX) + mapBounds.minX,
      y: mapBounds.minY + 1.5, // above ground
      z: Math.random() * (mapBounds.maxZ - mapBounds.minZ) + mapBounds.minZ,
    }

    const npc: NPCState = {
      id: npcId,
      type: i === 0 ? 'guard' : 'helper', // first NPC is the main guard
      position: { ...randomPosition },
      rotation: { x: 0, y: Math.random() * Math.PI * 2, z: 0, w: 1 },
      animState: 'idle',
      lastBroadcastPosition: { ...randomPosition },
    }

    state.npcs.set(npcId, npc)
  }
}

/**
 * Updates NPC position (called by NPC behavior system).
 * Used by game logic to move NPCs before each tick broadcast.
 */
export function updateNPCPosition(
  state: GameRoomState,
  npcId: string,
  newPosition: Vector3,
  animState?: 'idle' | 'walking' | 'chasing' | 'searching'
): void {
  const npc = state.npcs.get(npcId)
  if (!npc) return

  npc.position = { ...newPosition }
  if (animState) npc.animState = animState
}

/**
 * Computes delta NPCs: only those that moved >threshold since last broadcast.
 * Efficiency win: avoid sending 20 NPCs every tick if only 2 moved.
 */
export function computeNPCDelta(
  state: GameRoomState,
  deltaThreshold: number = 0.1
): NPCState[] {
  const delta: NPCState[] = []

  state.npcs.forEach((npc) => {
    const dist = distance(npc.position, npc.lastBroadcastPosition)
    if (dist > deltaThreshold) {
      delta.push({ ...npc })
      npc.lastBroadcastPosition = { ...npc.position }
    }
  })

  return delta
}

/**
 * Transitions the game to 'active' phase.
 * Called when min players reached and lobby timer expires.
 */
export function startGame(state: GameRoomState): void {
  state.status = 'active'
  state.startedAt = Date.now()
  state.phase = {
    current: 'active',
    phaseName: 'Active',
    duration: 120, // 2 minutes active phase
    startedAt: Date.now(),
  }
}

/**
 * Transitions the game to 'finished' state.
 */
export function endGame(
  state: GameRoomState,
  winner: 'prisoners' | 'guards',
  reason: string
): void {
  state.status = 'finished'
  state.endedAt = Date.now()
  state.winner = winner
  state.reason = reason
}

/**
 * Increment tick counter.
 */
export function advanceTick(state: GameRoomState): void {
  state.tick++
}

/**
 * Helper: euclidean distance between two points.
 */
export function distance(a: Vector3, b: Vector3): number {
  const dx = a.x - b.x
  const dy = a.y - b.y
  const dz = a.z - b.z
  return Math.sqrt(dx * dx + dy * dy + dz * dz)
}
