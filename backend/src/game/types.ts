/**
 * Core types for the game state synchronization system.
 * Defines all data structures transmitted over Socket.io.
 */

// ============================================================================
// Vector/Position/Rotation
// ============================================================================

export interface Vector3 {
  x: number
  y: number
  z: number
}

export interface Rotation {
  x: number
  y: number
  z: number
  w: number
}

// ============================================================================
// User Identity (persistent across sessions)
// ============================================================================

export type UserStatus = 'idle' | 'in-lobby' | 'in-game' | 'won' | 'lost' | 'spectating'

export interface UserProfile {
  userId: string // persistent UUID (stored in browser localStorage)
  displayName: string
  status: UserStatus
  currentRoomId?: string
  socketId?: string // current socket connection (changes on reconnect)
  createdAt: number
  lastSeenAt: number
}

// ============================================================================
// Player / Client State
// ============================================================================

export type MovementState = 'idle' | 'walking' | 'sprinting' | 'camuflaged'
export type PlayerRole = 'prisoner' | 'guard'

export interface PlayerState {
  id: string // socket ID (runtime identifier)
  userId: string // persistent user ID
  role: PlayerRole
  position: Vector3
  rotation: Rotation
  velocity: Vector3
  movementState: MovementState
  isAlive: boolean
  health?: number
  spawnWaypointId?: string // e.g. "cell_door_exit_03" — Unity resolves to world position
}

// ============================================================================
// NPC State (guards and helpers)
// ============================================================================

export interface NPCState {
  id: string // unique NPC ID (e.g., "npc_prisoner_001")
  type: 'guard' | 'helper' // kept for backend AI zone routing; all NPCs look like prisoners
  position: Vector3
  rotation: Rotation
  animState: 'idle' | 'walking' | 'chasing' | 'searching'
  lastBroadcastPosition: Vector3 // for delta compression
  spawnWaypointId?: string // e.g. "cell_door_exit_07" — Unity resolves to world position
}

// ============================================================================
// Game Phase / Timing
// ============================================================================

export type GamePhase = 'setup' | 'active' | 'lockdown' | 'escape' | 'riot'

export interface PhaseData {
  current: GamePhase
  phaseName: string
  duration: number // seconds
  startedAt: number // timestamp
  activeZone?: string // zone restricted in this phase
}

// ============================================================================
// Game Item / Inventory
// ============================================================================

export interface ItemState {
  id: string
  type: string // 'keycard', 'tool', 'distraction', etc.
  position: Vector3
  isPickedUp: boolean
  pickedUpBy?: string // player ID
}

// ============================================================================
// Room State (Complete game state snapshot)
// ============================================================================

export interface GameRoomState {
  id: string // room name given by host
  hostUserId: string // persistent userId of the host
  status: 'lobby' | 'loading' | 'active' | 'finished'
  players: Map<string, PlayerState> // socket ID → PlayerState
  playersByUserId: Map<string, PlayerState> // userId → PlayerState (reverse lookup)
  npcs: Map<string, NPCState> // npc ID → NPCState
  items: Map<string, ItemState>
  phase: PhaseData
  tick: number // current tick number (increments every 50ms)
  createdAt: number
  startedAt?: number
  endedAt?: number
  winner?: 'prisoners' | 'guards'
  reason?: string
}

// ============================================================================
// Socket.io Payload Types (Client → Server)
// ============================================================================

export interface PlayerMovePayload {
  playerId: string
  position: Vector3
  rotation: Rotation
  velocity: Vector3
  movementState: MovementState
}

export interface PlayerInteractPayload {
  playerId: string
  objectId: string
  action: 'pickup' | 'use' | 'drop'
}

export interface GuardMarkPayload {
  guardId: string
  targetId: string
}

export interface RiotActivatePayload {
  prisonerId: string
}

// ============================================================================
// Socket.io Payload Types (Server → Clients)
// ============================================================================

export interface PlayerStateUpdate {
  players: PlayerState[]
}

export interface NPCPositionUpdate {
  npcs: NPCState[] // only delta — NPCs that moved >0.1m
  tick: number
}

export interface PhaseChangePayload {
  phase: GamePhase
  phaseName: string
  duration: number
  activeZone?: string
}

export interface GuardCatchPayload {
  guardId: string
  targetId: string
  success: boolean
  isPlayer: boolean
}

export interface ChaseStartPayload {
  guardId: string
  targetId: string
}

export interface ChaseEndPayload {
  reason: 'caught' | 'lost' | 'timeout'
}

export interface ItemPickupPayload {
  playerId: string
  itemId: string
  slot: number
}

export interface GameEndPayload {
  winner: 'prisoners' | 'guards'
  reason: string
}

export interface RiotAvailablePayload {
  errorsCount: number
}

// ============================================================================
// Auth & Room Lobby Payloads (Client → Server)
// ============================================================================

export interface AuthRegisterPayload {
  userId?: string // existing userId from localStorage (omit for new user)
  displayName: string
}

export interface RoomCreatePayload {
  roomName: string // human-readable name = roomId
}

export interface RoomJoinPayload {
  roomId: string
}

export interface RoomKickPayload {
  targetUserId: string // userId of the player to kick
}

// ============================================================================
// Auth & Room Lobby Payloads (Server → Client)
// ============================================================================

export interface AuthRegisteredPayload {
  userId: string
  displayName: string
}

export interface RoomCreatedPayload {
  roomId: string
  hostUserId: string
}

export interface RoomStatePayload {
  roomId: string
  hostUserId: string
  status: GameRoomState['status']
  players: Array<{ userId: string; displayName: string; role: PlayerRole; isHost: boolean }>
}

export interface RoomPlayerJoinedPayload {
  userId: string
  displayName: string
  role: PlayerRole
  players: RoomStatePayload['players']
}

export interface RoomPlayerLeftPayload {
  userId: string
  reason: 'left' | 'kicked' | 'disconnected'
  players: RoomStatePayload['players']
}

export interface RoomDestroyedPayload {
  roomId: string
  reason: 'host-left' | 'empty' | 'game-ended'
}

// ============================================================================
// Jail Routine / NPC Phase System (Sistema Rutina/Fases)
// See design/gdd/rutina-fases-npc.md for full specification.
// ============================================================================

export type JailPhaseNumber = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9

/** A single NPC's action assignment for a phase or reassign event. */
export interface NPCAssignment {
  npcId: string
  actionId: string
  animTrigger: string
  waypointId?: string           // single waypoint (most actions)
  waypointChain?: string[]      // LOOPING chain (e.g. yard_perimeter)
  duration: number              // seconds before action expires
  loop?: boolean                // true for LOOPING actions
  socialPartnerId?: string      // set for SOCIAL actions
  subZone?: string              // set for Phase 6 (taller/lavanderia/piso)
}

/** Emitted to all clients when a jail phase starts. */
export interface PhaseJailStartPayload {
  phase: JailPhaseNumber
  phaseName: string
  duration: number
  zone: string
  npcAssignments: NPCAssignment[]
}

/** Emitted 10 seconds before a phase transition. */
export interface PhaseWarningPayload {
  nextPhase: JailPhaseNumber
  nextPhaseName: string
  warningInSeconds: number
}

/** Emitted every ~25s with partial NPC reassignments (libre albedrío). */
export interface NPCReassignPayload {
  timestamp: number
  assignments: NPCAssignment[]
}

/** Emitted to a specific player who is in the wrong zone. */
export interface PhaseZoneCheckPayload {
  playerId: string
  currentZone: string
  expectedZone: string
  phase: JailPhaseNumber
  graceSeconds: number
}

// ============================================================================
// Map Configuration
// ============================================================================

export interface MapBounds {
  minX: number
  maxX: number
  minY: number
  maxY: number
  minZ: number
  maxZ: number
}

// ============================================================================
// Configuration / Tuning
// ============================================================================

export interface GameConfig {
  tickRate: number // ticks per second (default 20)
  tickInterval: number // ms per tick (default 50)
  npcSendRate: number // sends per second (default 5)
  npcDeltaThreshold: number // meters (default 0.1)
  interpolationBuffer: number // ms (default 100)
  reconciliationThreshold: number // meters (default 1.0)
  reconciliationLerpSpeed: number // 0–1 (default 0.3)
  anticheatSpeedMultiplier: number // multiplier (default 1.5)
  reconnectTimeout: number // seconds (default 30)
  mapBounds: MapBounds
  maxPlayers: number // default 4 (1 guard + 3 prisoners)
}

// ============================================================================
// Room Instance (in-memory during match)
// ============================================================================

export interface GameRoom {
  state: GameRoomState
  config: GameConfig
  tickLoopInterval?: NodeJS.Timeout
  phaseLoopInterval?: NodeJS.Timeout
}
