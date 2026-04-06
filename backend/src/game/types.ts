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
// Player / Client State
// ============================================================================

export type MovementState = 'idle' | 'walking' | 'sprinting' | 'camuflaged'
export type PlayerRole = 'prisoner' | 'guard'

export interface PlayerState {
  id: string // socket ID
  role: PlayerRole
  position: Vector3
  rotation: Rotation
  velocity: Vector3
  movementState: MovementState
  isAlive: boolean
  health?: number
}

// ============================================================================
// NPC State (guards and helpers)
// ============================================================================

export interface NPCState {
  id: string // unique NPC ID (e.g., "npc_guard_001")
  type: 'guard' | 'helper' // guard = chases prisoners, helper = neutral
  position: Vector3
  rotation: Rotation
  animState: 'idle' | 'walking' | 'chasing' | 'searching'
  lastBroadcastPosition: Vector3 // for delta compression
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
  id: string
  status: 'lobby' | 'loading' | 'active' | 'finished'
  players: Map<string, PlayerState> // socket ID → PlayerState
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
