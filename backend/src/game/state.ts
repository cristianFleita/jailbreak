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
// ─── 20 Cell Door Spawn Positions ───────────────────────────────────────────
// Each entry: { id, position } — positions match the Unity scene layout.
// TODO: adjust these Vector3 values to match your actual cell door transforms in Unity.
// Cells are arranged in two rows of 10 along a corridor.
// Row A (left side, facing +X):  Z from ~2 to ~38, X = -5
// Row B (right side, facing -X): Z from ~2 to ~38, X = +5
const CELL_DOOR_SPAWNS: { id: string; position: Vector3 }[] = [
  // Row A — cells 1-10 (NPCs)
  { id: 'cell_door_exit_01', position: { x: 1.78, y: 0, z:  7.96 } },
  { id: 'cell_door_exit_02', position: { x: 1.78, y: 0, z:  3.597 } },
  { id: 'cell_door_exit_03', position: { x: 1.78, y: 0, z: -0.26 } },
  { id: 'cell_door_exit_04', position: { x: 1.78, y: 0, z: -4.1 } },
  { id: 'cell_door_exit_05', position: { x: 1.78, y: 0, z: -8.48 } },
  { id: 'cell_door_exit_06', position: { x: -5, y: 0, z: 22 } },
  { id: 'cell_door_exit_07', position: { x: -5, y: 0, z: 26 } },
  { id: 'cell_door_exit_08', position: { x: -5, y: 0, z: 30 } },
  { id: 'cell_door_exit_09', position: { x: -5, y: 0, z: 34 } },
  { id: 'cell_door_exit_10', position: { x: -5, y: 0, z: 38 } },
  // Row B — cells 11-16 (NPCs)
  { id: 'cell_door_exit_11', position: { x:  5, y: 0, z:  2 } },
  { id: 'cell_door_exit_12', position: { x:  5, y: 0, z:  6 } },
  { id: 'cell_door_exit_13', position: { x:  5, y: 0, z: 10 } },
  { id: 'cell_door_exit_14', position: { x:  5, y: 0, z: 14 } },
  { id: 'cell_door_exit_15', position: { x:  5, y: 0, z: 18 } },
  { id: 'cell_door_exit_16', position: { x:  -6.065, y: 0, z: 8.237 } },
  // Row B — cells 17-20 (Players)
  { id: 'cell_door_exit_17', position: { x:  -6.065, y: 0, z: 4.166 } },
  { id: 'cell_door_exit_18', position: { x:  -6.065, y: 0, z: -0.15 } },
  { id: 'cell_door_exit_19', position: { x:  -6.065, y: 0, z: -3.837 } },
  { id: 'cell_door_exit_20', position: { x:  -6.065, y: 0, z: -8.054 } },
]

// Players take the last 4 slots (17-20)
const PLAYER_SPAWN_SLOTS = CELL_DOOR_SPAWNS.filter(s => ['cell_door_exit_17','cell_door_exit_18','cell_door_exit_19','cell_door_exit_20'].includes(s.id))
// NPCs take the first 16 slots (01-16)
const NPC_SPAWN_SLOTS = CELL_DOOR_SPAWNS.slice(0, 16)

export function addPlayer(
  state: GameRoomState,
  playerId: string,
  userId: string,
  initialPosition: Vector3
): PlayerState {
  if (state.players.size >= 4) {
    throw new Error('Room is full (max 4 players)')
  }

  const spawnSlot = PLAYER_SPAWN_SLOTS[state.players.size] ?? PLAYER_SPAWN_SLOTS[0]

  const player: PlayerState = {
    id: playerId,
    userId,
    role: 'prisoner', // placeholder — reassigned on game start
    position: { x:  0, y: 0, z: 0 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    velocity: { x: 0, y: 0, z: 0 },
    movementState: 'idle',
    isAlive: true,
    spawnWaypointId: spawnSlot.id,
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

  let prisonerSlotIndex = 0
  for (let i = 0; i < players.length; i++) {
    const isGuard = i === guardIndex
    players[i].role = isGuard ? 'guard' : 'prisoner'

    if (isGuard) {
      // Guard spawns at origin (center of map / guard post)
      players[i].position = { x: 0, y: 0, z: 0 }
      players[i].spawnWaypointId = undefined
    } else {
      // Prisoners spawn at cell doors
      const slot = PLAYER_SPAWN_SLOTS[prisonerSlotIndex % PLAYER_SPAWN_SLOTS.length]
      players[i].position = { ...slot.position }
      players[i].spawnWaypointId = slot.id
      prisonerSlotIndex++
    }
  }

  // Log role assignments
  console.log('[ROLES] Assigned roles + spawn positions:')
  for (const p of players) {
    console.log(`  → ${p.userId} (socket ${p.id}): ${p.role.toUpperCase()} @ (${p.position.x}, ${p.position.y}, ${p.position.z})`)
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
export function spawnNPCs(state: GameRoomState, _config: GameConfig, npcCount: number = 2): void {
  for (let i = 0; i < npcCount; i++) {
    const npcId     = `npc_prisoner_${String(i).padStart(3, '0')}`
    const spawnSlot = NPC_SPAWN_SLOTS[i % NPC_SPAWN_SLOTS.length]

    const npc: NPCState = {
      id: npcId,
      type: 'helper', // all NPCs look like prisoners
      position: { ...spawnSlot.position },
      rotation: { x: 0, y: Math.random() * Math.PI * 2, z: 0, w: 1 },
      animState: 'idle',
      lastBroadcastPosition: { ...spawnSlot.position },
      spawnWaypointId: spawnSlot.id,
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
