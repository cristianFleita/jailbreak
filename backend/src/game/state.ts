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
  // Row A — 
  { id: 'cell_door_exit_01', position: { x: 1.78, y: 0, z:  7.96 } },
  { id: 'cell_door_exit_02', position: { x: 1.78, y: 0, z:  3.597 } },
  { id: 'cell_door_exit_03', position: { x: 1.78, y: 0, z: -0.26 } },
  { id: 'cell_door_exit_04', position: { x: 1.78, y: 0, z: -4.1 } },
  { id: 'cell_door_exit_05', position: { x: 1.78, y: 0, z: -8.48 } },
  // Row B — 1st floor
  { id: 'cell_door_exit_06', position: { x: -5.974, y: 3.193, z: -6.155 } },
  { id: 'cell_door_exit_07', position: { x: -5.974, y: 3.193, z: -1.826 } },
  { id: 'cell_door_exit_08', position: { x: -5.974, y: 3.193, z: 2.276 } },
  { id: 'cell_door_exit_09', position: { x: -6.832, y: 3.193, z: 6.718 } },
  { id: 'cell_door_exit_10', position: { x: -3.499, y: 3.193, z: 6.718 } },
  { id: 'cell_door_exit_11', position: { x:  -0.264, y: 3.193, z:  6.718 } },
  { id: 'cell_door_exit_12', position: { x:  2.986, y: 3.193, z:  6.718 } },
  { id: 'cell_door_exit_13', position: { x:  2.236, y: 3.193, z: 1.973 } },
  { id: 'cell_door_exit_14', position: { x:  2.236, y: 3.193, z: -2.031 } },
  { id: 'cell_door_exit_15', position: { x:  2.236, y: 3.193, z: -6.395 } },
  // Row A — 
  { id: 'cell_door_exit_16', position: { x:  -6.065, y: 0, z: 8.237 } },
  { id: 'cell_door_exit_17', position: { x:  -6.065, y: 0, z: 4.166 } },
  { id: 'cell_door_exit_18', position: { x:  -6.065, y: 0, z: -0.15 } },
  { id: 'cell_door_exit_19', position: { x:  -6.065, y: 0, z: -3.837 } },
  { id: 'cell_door_exit_20', position: { x:  -6.065, y: 0, z: -8.054 } },
]

// Guard spawns at a dedicated position (guard post / center of map)
const GUARD_SPAWN: { id: string; position: Vector3 } = {
  id: 'guard_spawn',
  position: { x: -1.82251, y: -0.14, z: -0.84076 },
}

// Players can spawn at ANY of the 20 cell door positions (randomized at game start).
// This makes player positions indistinguishable from NPCs.
// The actual random selection happens in assignRandomRoles().

export function addPlayer(
  state: GameRoomState,
  playerId: string,
  userId: string,
  initialPosition: Vector3
): PlayerState {
  if (state.players.size >= 4) {
    throw new Error('Room is full (max 4 players)')
  }

  // Temporary spawn — real position assigned randomly in assignRandomRoles()
  const player: PlayerState = {
    id: userId,
    userId,
    role: 'prisoner', // placeholder — reassigned on game start
    position: { x:  0, y: 0, z: 0 },
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

  // Shuffle ALL 20 cell door spawns — prisoners pick from the shuffled pool
  const shuffledSpawns = [...CELL_DOOR_SPAWNS].sort(() => Math.random() - 0.5)
  let spawnIndex = 0

  for (let i = 0; i < players.length; i++) {
    const isGuard = i === guardIndex
    players[i].role = isGuard ? 'guard' : 'prisoner'

    if (isGuard) {
      // Guard spawns at dedicated guard post
      players[i].position = { ...GUARD_SPAWN.position }
      players[i].spawnWaypointId = GUARD_SPAWN.id
    } else {
      // Prisoners spawn at random cell doors (any of the 20)
      const slot = shuffledSpawns[spawnIndex]
      players[i].position = { ...slot.position }
      players[i].spawnWaypointId = slot.id
      spawnIndex++
    }
  }

  // Log role assignments
  console.log('[ROLES] Assigned roles + spawn positions:')
  for (const p of players) {
    console.log(`  → ${p.userId}: ${p.role.toUpperCase()} @ ${p.spawnWaypointId} (${p.position.x.toFixed(2)}, ${p.position.y.toFixed(2)}, ${p.position.z.toFixed(2)})`)
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
 * NPC count = 20 - (playerCount - 1).
 * The guard has a dedicated spawn, prisoner players take random cell doors,
 * and NPCs fill the remaining CELL_DOOR_SPAWNS with unique positions.
 */
export function spawnNPCs(state: GameRoomState, _config: GameConfig): void {
  const playerCount = state.players.size
  // Guard has its own spawn, so prisoner players = playerCount - 1
  // NPCs fill the remaining 20 - (playerCount - 1) cell slots
  const npcCount = 20 - (playerCount - 1)

  // Collect spawn slot IDs already taken by prisoner players
  const usedSpawnIds = new Set<string>()
  for (const player of state.players.values()) {
    if (player.spawnWaypointId && player.spawnWaypointId !== GUARD_SPAWN.id) {
      usedSpawnIds.add(player.spawnWaypointId)
    }
  }

  // Available slots = all 20 cell door spawns minus player-occupied ones
  const availableSlots = CELL_DOOR_SPAWNS.filter(s => !usedSpawnIds.has(s.id))

  console.log(`[NPC] Spawning ${npcCount} NPCs (${playerCount} players, ${playerCount - 1} prisoner players)`)

  for (let i = 0; i < npcCount; i++) {
    const npcId     = `npc_prisoner_${String(i).padStart(3, '0')}`
    const spawnSlot = availableSlots[i % availableSlots.length]

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
    console.log(`[NPC] ${npcId} → spawn: ${spawnSlot.id} @ (${spawnSlot.position.x}, ${spawnSlot.position.y}, ${spawnSlot.position.z})`)
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
