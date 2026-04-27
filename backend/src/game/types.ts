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
  carrying?: string | null // e.g. "food_plate" — visual prop parented to player's hand (reconnect-safe)

  // ── Authoritative inventory (Ruta 1 / Phase A contract) ─────────────────
  // heldItemId: item currently in hand (after pickup with E, before store with F)
  // inventorySlots: fixed-length array (2 for prisoners). Slots may contain null.
  heldItemId?: string | null
  inventorySlots?: (InventorySlotSync | null)[]
}

// ============================================================================
// Inventory Slot Sync (authoritative across Unity + backend)
// ============================================================================

export type RouteItemId = 'route1_cutters' | 'route1_wrench' | string
export type RouteItemType = 'route_tool' | string

export interface InventorySlotSync {
  itemId: RouteItemId
  itemType: RouteItemType
  iconId?: string
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
  currentSequenceIndex?: number // fast-forward index for F5 reconnect
  currentActionId?: string      // action name currently executing
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

  // ── Extended fields (Ruta 1 / Phase A contract) ─────────────────────────
  // Coexist with legacy fields above; new route-tool items populate these.
  itemId?: RouteItemId            // stable item identifier (e.g. "route1_wrench")
  itemType?: RouteItemType        // e.g. "route_tool"
  state?: RouteItemLifecycle      // finite-state lifecycle for networked pickables
  holderUserId?: string           // userId of current holder (distinct from legacy pickedUpBy=socketId)
  spawnAreaId?: string            // spawn area chosen by backend for initial placement
}

export type RouteItemLifecycle =
  | 'spawned'     // available at a spawn area in the world
  | 'held'        // in a player's hand
  | 'stored'      // in a player's inventory slot
  | 'dropped'     // on the floor (after capture, not after voluntary throw in MVP)
  | 'respawning'  // being returned to a spawn area after anti-softlock delay

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

  // ── Escape route system (selector-based architecture, MVP registers only Ruta 1) ──
  activeRouteId: ActiveRouteId
  route1?: Route1State
  spawnAreas?: Map<string, SpawnAreaRegistration>
}

// ============================================================================
// Escape Route System — Route Selector + Ruta 1 state
// Spec: design/gdd/ruta-1-ventilacion-industrial.md
// ============================================================================

/**
 * Active route selector. MVP only registers `route1_ventilation`.
 * Future routes will extend this union without breaking the contract.
 */
export type ActiveRouteId = 'route1_ventilation'

export type Route1MissionId =
  | 'find_cutters'
  | 'find_clue'
  | 'disable_server'
  | 'find_wrench'
  | 'open_vent'
  | 'escape'

export type Route1MissionStatus = 'locked' | 'available' | 'in_progress' | 'complete'

export type GuardDeskId = 'guard_desk_1' | 'guard_desk_2' | 'guard_desk_3' | 'guard_desk_4'

export type ServerRackId =
  | 'server_1' | 'server_2' | 'server_3' | 'server_4'
  | 'server_5' | 'server_6' | 'server_7' | 'server_8'
  | 'server_9' | 'server_10' | 'server_11' | 'server_12'

export type VentId = 'vent_1' | 'vent_2' | 'vent_3'

export interface Route1ActiveInteraction {
  objectId: string
  action: string
  startedByUserId: string
  helperUserIds: string[]
  progress: number // 0..1
}

export interface Route1State {
  routeId: 'route1_ventilation'
  correctDeskId: GuardDeskId
  correctServerId: ServerRackId
  clueFound: boolean
  serverDisabled: boolean
  wrongServerAttempts: string[]
  ventProgressById: Record<string, number>   // key: VentId
  openVentIds: string[]                      // subset of VentId
  activeInteractions: Record<string, Route1ActiveInteraction> // key: objectId
  escapingPlayerIds: string[]
  escapedPlayerIds: string[]
  missions: Record<Route1MissionId, Route1MissionStatus>
  updatedAt: number
}

/**
 * Spawn area registered by Unity at scene load.
 * Backend chooses one valid spawn area per critical item at match start.
 */
export interface SpawnAreaRegistration {
  spawnAreaId: string
  zoneId: string
  position: Vector3
  allowedItemIds: RouteItemId[]
}

/**
 * Client → server: host registers every scene-authored spawn area once the
 * GameScene has loaded. Backend uses the list to choose one spawn area per
 * critical route item (Phase C-02).
 */
export interface RegisterSpawnAreasPayload {
  areas: SpawnAreaRegistration[]
}

// ============================================================================
// Escape Route — Socket Payloads
// ============================================================================

/**
 * Emitted on game start AND on reconnect so clients know which route is active
 * before any route UI renders. MVP value is always 'route1_ventilation'.
 */
export interface EscapeRouteSelectedPayload {
  activeRouteId: ActiveRouteId
}

/**
 * Route1 checklist + progress delivered to prisoners only.
 * Guard audience never receives this event.
 *
 * `correctServerId` is included only when `clueFound = true`.
 */
/**
 * One vent's progress. Emitted as an array entry because Unity JsonUtility
 * cannot deserialize Record<string, number> directly.
 */
export interface VentProgressEntry {
  ventId: string
  progress: number // 0..1
}

export interface EscapeRoute1StatePayload {
  routeId: 'route1_ventilation'
  missions: Record<Route1MissionId, Route1MissionStatus>
  clueFound: boolean
  correctServerId?: ServerRackId  // only populated when clueFound = true
  serverDisabled: boolean
  ventProgress: VentProgressEntry[]  // flattened from Route1State.ventProgressById
  openVentIds: string[]
  activeInteractions: Route1ActiveInteraction[]
  escapingPlayerIds: string[]
  escapedPlayerIds: string[]
  updatedAt: number
}

/**
 * Observable world cues pushed to the guard (alarms, metal noise, etc.).
 * Never includes the correct server ID — guard must infer from observation.
 */
export type WorldCueType =
  | 'server_wrong_alarm'
  | 'server_correct_power_off'
  | 'vent_unscrew_noise'
  | 'vent_opened'

export interface WorldCuePayload {
  cue: WorldCueType
  zone?: string
  position?: Vector3
}

/**
 * Public props state visible to everyone (ventilation on/off, open vents).
 */
export interface WorldStatePayload {
  ventilationPowered: boolean
  openVentIds: string[]
}

/**
 * Authoritative broadcast of a single item's lifecycle state.
 * Delivered on spawn, pickup, store, drop, and respawn.
 */
export interface ItemStateBroadcast {
  itemId: RouteItemId
  itemType: RouteItemType
  state: RouteItemLifecycle
  holderUserId?: string
  spawnAreaId?: string
  position: Vector3
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

/**
 * Union of every `player:interact` action the server accepts.
 * Legacy molestia items use 'pickup' | 'use' | 'drop' (see handleItemPickup).
 * Route tools use 'item.pickup' | 'item.store' (authoritative hand/slots).
 * Route missions (Phase D) will add 'route1.*' start/stop actions.
 */
export type PlayerInteractAction =
  | 'pickup'
  | 'use'
  | 'drop'
  | 'item.pickup'
  | 'item.store'
  | 'route1.search_clue.start'
  | 'route1.search_clue.stop'
  | 'route1.disable_server.start'
  | 'route1.disable_server.stop'
  | 'route1.open_vent.start'
  | 'route1.open_vent.stop'
  | 'route1.escape.start'
  | 'route1.escape.stop'

export interface PlayerInteractPayload {
  playerId: string
  objectId: string
  action: PlayerInteractAction
}

export interface GuardMarkPayload {
  guardId: string
  targetId: string
}

export interface RiotActivatePayload {
  prisonerId: string
}

export interface NPCStateSync {
  npcId: string
  position: Vector3
  rotation: Rotation
  currentSequenceIndex: number
  currentActionId: string
}

export interface NPCSyncStatePayload {
  npcs: NPCStateSync[]
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
  displayName?: string // ignored — display name is generated server-side
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

export interface RoomListEntryPayload {
  roomId: string
  hostUserId: string
  hostDisplayName: string
  status: GameRoomState['status']
  playerCount: number
  maxPlayers: number
  createdAt: number
  players: RoomStatePayload['players']
}

export interface RoomListPayload {
  rooms: RoomListEntryPayload[]
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

export type JailPhaseNumber = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8

/** A single step inside an ordered action sequence (cafeteria flow, etc.). */
export interface NPCActionStep {
  actionId: string
  animTrigger: string
  zoneId?: string               // target zone (null = stay in place / use partner pos)
  seed?: number                 // deterministic RNG seed for point generation inside zone
  duration: number              // seconds to perform action at destination (0 = walk-only, no dwell)
  socialPartnerId?: string      // set for SOCIAL steps
}

/** A single NPC's action assignment for a phase or reassign event. */
export interface NPCAssignment {
  npcId: string
  actionId: string
  animTrigger: string
  zoneId?: string               // target zone for navigation
  seed?: number                 // deterministic seed for random point in zone
  seedChain?: number[]          // LOOPING: multiple seeds for patrol points
  duration: number              // seconds before action expires (total for sequences)
  loop?: boolean                // true for LOOPING actions
  socialPartnerId?: string      // set for SOCIAL actions
  subZone?: string              // sub-zone within parent zone
  actionSequence?: NPCActionStep[]  // ordered steps (cafeteria flow, etc.)
  walkSpeedMult?: number        // per-NPC walk speed variation (0.85–1.15)
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

/** Emitted every ~15-25s with partial NPC reassignments (libre albedrío). */
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

// ============================================================================
// NPC Personality & Emergent Behavior System
// ============================================================================

/**
 * Archetype determines base behavior tendencies.
 * Each NPC gets one at game start — it biases action weights, not overrides them.
 */
export type NPCArchetype =
  | 'troublemaker'   // provokes guard, starts commotions, higher agitation
  | 'loner'          // prefers solo actions, avoids social, wanders alone
  | 'social'         // seeks groups, talks often, pairs up frequently
  | 'worker'         // high work ethic, stays on task, rarely idles
  | 'athletic'       // exercises, shadowboxes, runs perimeter
  | 'nervous'        // fidgets, paces, reacts strongly to guard proximity
  | 'schemer'        // lingers near escape routes, whispers, acts suspicious

/**
 * Dynamic mood that shifts based on game events and time.
 * Mood modifies action weight multipliers per tick.
 */
export type NPCMood =
  | 'calm'           // default — follows routine normally
  | 'bored'          // seeks variety, switches actions faster
  | 'agitated'       // paces, fidgets, may provoke guard
  | 'nervous'        // avoids guard, stays in groups, fidgets
  | 'rebellious'     // anti-guard actions unlocked, confrontational
  | 'social'         // seeks conversation partners actively
  | 'tired'          // slower movement, prefers sitting/lying

/** Mood transition trigger — what caused the mood shift. */
export type MoodTrigger =
  | 'time_decay'     // natural drift toward calm over time
  | 'guard_nearby'   // guard entered NPC's zone
  | 'guard_left'     // guard left NPC's zone
  | 'caught_witness' // NPC saw a player get caught
  | 'phase_change'   // new phase started
  | 'social_interaction' // finished talking with someone
  | 'boredom'        // stayed on same action too long
  | 'riot_brewing'   // riot meter is high

/** Full NPC personality profile — assigned at game start, persists for match. */
export interface NPCProfile {
  npcId: string
  archetype: NPCArchetype
  mood: NPCMood
  moodIntensity: number        // 0.0–1.0 — how strongly mood affects weights
  actionHistory: string[]      // last 5 action IDs (prevents repetition)
  socialPreference: string[]   // NPC IDs this NPC prefers to pair with (2-3)
  lastMoodChange: number       // timestamp
  guardProximityTimer: number  // seconds guard has been nearby
  spontaneousTimer: number     // countdown to next spontaneous action check
}

/**
 * Emergent action — an unscheduled behavior an NPC can trigger
 * based on mood, archetype, and environmental context.
 */
export interface EmergentAction {
  actionId: string
  animTrigger: string
  type: 'anti_guard' | 'environmental' | 'social_reactive' | 'self_expression'
  duration: number
  waypointTag?: string
  requiresMood?: NPCMood[]     // only available in these moods
  requiresArchetype?: NPCArchetype[] // only available for these archetypes
  guardProximityRequired?: boolean    // needs guard nearby
  cooldownSeconds: number      // per-NPC cooldown after use
  probability: number          // base chance per check (0.0–1.0)
}

/** Emitted to clients when an NPC triggers an emergent behavior. */
export interface NPCEmergentPayload {
  npcId: string
  actionId: string
  animTrigger: string
  waypointId?: string
  duration: number
  targetNpcId?: string         // for social reactive actions
  mood: NPCMood
}

/** Emitted to clients when an NPC's visible behavior changes noticeably. */
export interface NPCMoodShiftPayload {
  npcId: string
  newMood: NPCMood
  animHint: string             // idle animation that reflects mood (fidget, pace, etc.)
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
