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
  RoomListPayload, RoomStatePayload, PlayerRole, EscapeRouteSelectedPayload,
  MatchStatusPayload, PlayerState,
} from './types.js'
import { createGameRoomState, advanceTick, computeNPCDelta, spawnNPCs, startGame, endGame } from './state.js'
import { GameManager } from './systems/game-manager.js'
import { JailRoutineSystem } from './systems/jail-routine.js'
import { getUser, setUserStatus } from './user-identity.js'
import { initializeRouteState } from './routes/route-registry.js'
import { broadcastAllRouteItemStates } from './systems/route-inventory.js'
import { clearSpawnAreaRegistration } from './systems/spawn-areas.js'
import { TutorialManager, cleanupTutorialBeforeActive } from './systems/tutorial.js'

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

  // Cancel any in-flight tutorial timers (host-quit during tutorial). Use
  // cancel() not forceEnd() so we do NOT fire tutorial:end / transitionToActive
  // on a room that is about to be removed from the registry.
  const tutorialManager = (room as any).tutorialManager as TutorialManager | undefined
  if (tutorialManager) {
    tutorialManager.cancel()
    ;(room as any).tutorialManager = undefined
  }

  // Drop any pending spawn-area respawn timers for this room.
  clearSpawnAreaRegistration(roomId)

  activeRooms.delete(roomId)
  console.log(`[ROOM] Destroyed room "${roomId}"`)
}

/**
 * Returns the active tutorial manager for a room, if any. Used by the socket
 * layer to forward `tutorial:mission:complete` events.
 */
export function getTutorialManager(room: GameRoom): TutorialManager | undefined {
  return (room as any).tutorialManager as TutorialManager | undefined
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
 * Builds one public row for the room browser.
 */
export function buildRoomListEntryPayload(room: GameRoom): RoomListPayload['rooms'][number] {
  const hostProfile = getUser(room.state.hostUserId)

  return {
    roomId: room.state.id,
    hostUserId: room.state.hostUserId,
    hostDisplayName: hostProfile?.displayName || `Player_${room.state.hostUserId.slice(0, 6)}`,
    status: room.state.status,
    playerCount: room.state.players.size,
    maxPlayers: room.config.maxPlayers,
    createdAt: room.state.createdAt,
    players: buildRoomPlayersPayload(room),
  }
}

/**
 * Builds the full public room browser payload.
 */
export function buildRoomListPayload(): RoomListPayload {
  return {
    rooms: listJoinableRooms(),
  }
}

/**
 * Result of {@link evaluateLeaveWinCondition} when a leaver instantly ends
 * the match. Winner + reason fields mirror the `game:end` payload.
 */
export interface LeaveWinResult {
  winner: 'prisoners' | 'guards'
  reason: string
}

/**
 * Decides whether a player leaving an active match should immediately end the
 * game.
 *
 *   - Guard leaves → prisoners win (`guard-left`).
 *   - Last remaining prisoner leaves → guards win (`all-prisoners-left`).
 *
 * Pure function — caller MUST invoke this BEFORE removing the player from
 * the room state (the leaver still counts in `state.players`).
 */
export function evaluateLeaveWinCondition(
  state: GameRoomState,
  leavingPlayer: PlayerState
): LeaveWinResult | null {
  if (state.status !== 'active') return null

  if (leavingPlayer.role === 'guard') {
    return { winner: 'prisoners', reason: 'guard-left' }
  }

  let prisonersAfter = 0
  for (const p of state.players.values()) {
    if (p === leavingPlayer) continue
    if (p.role === 'prisoner') prisonersAfter++
  }
  if (prisonersAfter === 0) {
    return { winner: 'guards', reason: 'all-prisoners-left' }
  }
  return null
}

/**
 * Authoritative end-of-match cleanup. Used by both the tick-loop's win-check
 * and the leave-room flow (guard left / all prisoners left), so the two paths
 * cannot drift.
 *
 * Steps: mutate state → emit `game:end` → stop loops → mark every user idle →
 * force every socket out of the socket.io room → destroy the room.
 *
 * No-ops if the match is already finished.
 */
export function endMatchAndCleanup(
  io: Server,
  room: GameRoom,
  winner: 'prisoners' | 'guards',
  reason: string
): void {
  const state = room.state
  if (state.status === 'finished') return

  endGame(state, winner, reason)
  io.to(state.id).emit('game:end', { winner, reason })
  stopGameLoop(room)

  for (const [sid, p] of state.players) {
    setUserStatus(p.userId, 'idle')
    const targetSocket = io.sockets.sockets.get(sid)
    if (targetSocket) targetSocket.data.currentRoomId = null
  }

  io.in(state.id).socketsLeave(state.id)
  destroyRoom(state.id)
}

/**
 * Builds the public match scoreboard (timer + prisoner count) used by the
 * `match:status` broadcast. Both roles see the same payload — no route data
 * leaks here.
 */
export function buildMatchStatus(room: GameRoom): MatchStatusPayload {
  const totalMatchSeconds = JailRoutineSystem.getTotalMatchDurationSeconds()
  const gm = (room as any).gameManager as GameManager | undefined
  const remainingSeconds = gm?.jailRoutine
    ? gm.jailRoutine.getMatchRemainingSeconds()
    : totalMatchSeconds

  let prisonersTotal = 0
  let caughtCount = 0
  const livePrisonerUserIds: string[] = []
  for (const player of room.state.players.values()) {
    if (player.role !== 'prisoner') continue
    prisonersTotal++
    if (!player.isAlive) caughtCount++
    else livePrisonerUserIds.push(player.userId)
  }

  const escapedSet = new Set(room.state.route1?.escapedPlayerIds ?? [])
  let escapedCount = 0
  let prisonersRemaining = 0
  for (const userId of livePrisonerUserIds) {
    if (escapedSet.has(userId)) escapedCount++
    else prisonersRemaining++
  }
  // Account for prisoners who escaped AND are no longer alive (edge cases).
  for (const userId of escapedSet) {
    const player = room.state.playersByUserId.get(userId)
    if (player && !player.isAlive) escapedCount++
  }

  return {
    remainingSeconds: Math.round(remainingSeconds),
    totalMatchSeconds,
    prisonersRemaining,
    prisonersTotal,
    caughtCount,
    escapedCount,
  }
}

/**
 * Finds a player's socketId by their userId within a room.
 * Iterates the socket-keyed players map to return the actual socket ID.
 */
export function findSocketByUserId(room: GameRoom, userId: string): string | undefined {
  for (const [socketId, player] of room.state.players) {
    if (player.userId === userId) return socketId
  }
  return undefined
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

  // Wire NPC personality system callbacks
  const personality = gameManager.jailRoutine.getPersonalitySystem()
  personality.onEmergentAction = (payload) => {
    io.to(state.id).emit('npc:emergent', payload)
  }
  personality.onMoodShift = (payload) => {
    io.to(state.id).emit('npc:mood_shift', payload)
  }

  // ── Wire Route 1 broadcasts (Phase D) ──
  // Prisoner-only fan-out keeps `correctServerId` away from the guard. We
  // iterate every tick's emit because socket.io rooms in this codebase aren't
  // sharded by role.
  gameManager.route1.onRoute1StateChanged = (payload) => {
    for (const [socketId, p] of state.players) {
      if (p.role === 'prisoner') io.to(socketId).emit('escape:route1:state', payload)
    }
  }
  gameManager.route1.onWorldStateChanged = (payload) => {
    io.to(state.id).emit('world:state', payload)
  }
  gameManager.route1.onWorldCue = (payload) => {
    for (const [socketId, p] of state.players) {
      if (p.role === 'guard') io.to(socketId).emit('world:cue', payload)
    }
  }

  // Jail routine is started by transitionToActive AFTER game:start is emitted,
  // so clients have time to load GameScene and subscribe to phase:start.

  let npcBroadcastCounter = 0
  const npcBroadcastThreshold = config.tickRate / npcSendRate // emit NPC every N ticks
  // match:status broadcast cadence — once per second is plenty for a UI label.
  let matchStatusCounter = 0
  const matchStatusThreshold = Math.max(1, config.tickRate)

  room.tickLoopInterval = setInterval(() => {
    try {
      // ========== Game Logic Tick ==========
      const tickResult = gameManager.tick()

      // Check if game should end
      if (tickResult.shouldEnd) {
        console.log(`[TICK] Game ending: winner=${tickResult.winner}, reason=${tickResult.reason}`)
        endMatchAndCleanup(
          io,
          room,
          tickResult.winner as 'prisoners' | 'guards',
          tickResult.reason || 'unknown'
        )
        return
      }

      advanceTick(state)

      // ========== Broadcast player state every tick ==========
      const playerStatePayload: PlayerStateUpdate = {
        players: Array.from(state.players.values()),
      }

      io.to(state.id).emit('player:state', playerStatePayload)

      // ========== Broadcast match status (~1Hz) ==========
      matchStatusCounter++
      if (matchStatusCounter >= matchStatusThreshold) {
        io.to(state.id).emit('match:status', buildMatchStatus(room))
        matchStatusCounter = 0
      }

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
 * NPC count is computed dynamically based on player count.
 */
export function initializeNPCs(room: GameRoom, count?: number): void {
  spawnNPCs(room.state, room.config, count)
  console.log(`[ROOM] Spawned ${room.state.npcs.size} NPCs in "${room.state.id}"`)
}

/**
 * Transitions a room from lobby to active game.
 * Only the host can trigger this.
 */
/**
 * Delay (ms) between emitting `game:start` and starting the jail routine.
 * Clients need this window to load the GameScene and let JailRoutineManager
 * subscribe to `phase:start` before the server fires the first one.
 */
const JAIL_ROUTINE_START_DELAY_MS = 2000

export function transitionToActive(io: Server, room: GameRoom): void {
  // Tutorial → active is the normal post-Fase-A path; lobby → active is kept
  // for tests and any future flow that wants to skip the training round.
  if (room.state.status !== 'lobby' && room.state.status !== 'tutorial') {
    console.warn(`Cannot transition room "${room.state.id}" from ${room.state.status} to active`)
    return
  }

  // Tutorial-to-active hand-off: scrub any tutorial-only mutations off the
  // player records before the real match seeds inventory + items.
  if (room.state.status === 'tutorial') {
    cleanupTutorialBeforeActive(room.state)
    ;(room as any).tutorialManager = undefined
  }

  startGame(room.state)

  // Randomize the selected route's authoritative state (desk/server, etc.)
  // now that players are locked in but before NPC spawn / loop / broadcasts.
  initializeRouteState(room.state)

  initializeNPCs(room)
  startGameLoop(io, room)

  // Notify all clients which escape route is active. Sent BEFORE game:start
  // so Unity can cache activeRouteId before any route UI renders.
  const routePayload: EscapeRouteSelectedPayload = {
    activeRouteId: room.state.activeRouteId,
  }
  io.to(room.state.id).emit('escape:route:selected', routePayload)
  broadcastAllRouteItemStates(io, room.state.id, room.state)

  // Initial Route 1 snapshot so prisoner HUDs hydrate before the first tick.
  // World state is broadcast publicly so the guard hears the ventilation.
  const gameManager = (room as any).gameManager as GameManager | undefined
  if (gameManager?.route1) {
    const initialPrisonerPayload = gameManager.route1.buildPrisonerStatePayload()
    for (const [socketId, p] of room.state.players) {
      if (p.role === 'prisoner') io.to(socketId).emit('escape:route1:state', initialPrisonerPayload)
    }
    io.to(room.state.id).emit('world:state', gameManager.route1.buildWorldStatePayload())
  }

  // Notify all clients that game started
  io.to(room.state.id).emit('game:start', {
    players: Array.from(room.state.players.values()),
    npcs: Array.from(room.state.npcs.values()),
    phase: room.state.phase,
  })

  // Initial match scoreboard so the HUD shows the correct prisoner count and
  // total match length even before the jail routine has fired its first tick.
  io.to(room.state.id).emit('match:status', buildMatchStatus(room))

  console.log(`[ROOM] "${room.state.id}" transitioned to ACTIVE`)

  // Start the jail routine AFTER clients have time to load GameScene and
  // register JailRoutineManager subscribers. Without this delay, the first
  // phase:start is emitted before Unity's JailRoutineManager.Start() runs,
  // so NPCs receive no assignments and stay idle.
  const roomId = room.state.id
  setTimeout(() => {
    const current = activeRooms.get(roomId)
    if (!current || current.state.status !== 'active') return
    const gm = (current as any).gameManager as GameManager | undefined
    if (!gm) {
      console.warn(`[JAIL] Cannot start routine — gameManager missing for room "${roomId}"`)
      return
    }
    gm.jailRoutine.start()
  }, JAIL_ROUTINE_START_DELAY_MS)
}

/**
 * Transitions a lobby room into the tutorial training round (Fase A).
 *
 * Preconditions: roles must already have been assigned by the caller — the
 * tutorial fans out role-specific mission lists. The tutorial manager owns
 * its own timers; when the 60s end, it invokes the onComplete callback to
 * hand off to {@link transitionToActive}.
 *
 * Pass `tutorialOptions` to override duration / seed / clock for tests.
 */
export function transitionToTutorial(
  io: Server,
  room: GameRoom,
  tutorialOptions?: { durationSeconds?: number; tickIntervalMs?: number; seed?: number; now?: () => number }
): TutorialManager | null {
  if (room.state.status !== 'lobby') {
    console.warn(
      `Cannot transition room "${room.state.id}" from ${room.state.status} to tutorial`
    )
    return null
  }

  const manager = new TutorialManager(io, room, {
    ...tutorialOptions,
    onComplete: () => {
      const current = activeRooms.get(room.state.id)
      if (!current) return
      // Drop the manager BEFORE transitioning so transitionToActive's lobby
      // status gate flips cleanly without re-entering the tutorial branch.
      ;(current as any).tutorialManager = undefined
      transitionToActive(io, current)
    },
  })

  ;(room as any).tutorialManager = manager
  manager.start()
  console.log(`[ROOM] "${room.state.id}" transitioned to TUTORIAL`)
  return manager
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
 * Lists only rooms a new player can join from the lobby browser.
 */
export function listJoinableRooms(): RoomListPayload['rooms'] {
  const result: RoomListPayload['rooms'] = []
  for (const [_id, room] of activeRooms) {
    if (room.state.status === 'lobby' && room.state.players.size < room.config.maxPlayers) {
      result.push(buildRoomListEntryPayload(room))
    }
  }

  return result.sort((a, b) => b.createdAt - a.createdAt)
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
