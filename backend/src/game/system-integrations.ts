/**
 * System integrations: interfaces for connecting Sincronización de Estado
 * to other gameplay systems (Persecución, Inventario, etc).
 *
 * These are stub types that will be implemented by other systems in Fase 2.
 * This file documents the contract each system must fulfill.
 */

import { Vector3, PlayerState, NPCState } from './types.js'

/**
 * Sistema 1: Movimiento FPS
 * Connection: Hard (bidirectional)
 * Flows in: Input from client via `player:move`
 * Flows out: Authoritative position for rubber-band reconciliation
 *
 * TODO (Fase 2): Implement FPS controller that reads `player:state` broadcasts
 * and applies rubber-band correction when diff > threshold.
 */
export interface IMovementSystem {
  // Called by state sync to update a player's position in the FPS system
  setPlayerPosition(playerId: string, position: Vector3): void

  // Called by state sync to apply rubber-band correction
  applyRubberbandCorrection(
    playerId: string,
    authorityPosition: Vector3,
    threshold: number,
    lerpSpeed: number
  ): void
}

/**
 * Sistema 2: Persecución
 * Connection: Hard (bidirectional)
 * Flows in: `guard:mark` event from guard client
 * Flows out: `chase:start`, `chase:end`, `guard:catch` events
 *
 * TODO (Fase 2): Implement chase state machine that:
 * - Detects when guard marked a prisoner (chase:start)
 * - Updates NPC movement toward prisoner
 * - Validates catch distance and emits guard:catch when valid
 * - Ends chase when prisoner escapes or gets caught
 */
export interface IPursuitSystem {
  // Called when guard:mark is received
  startChase(guardId: string, targetId: string): void

  // Called by tick loop to update pursuit state
  updateChaseStates(): void

  // Called when pursuer or target disconnects
  endChase(reason: 'disconnect' | 'timeout'): void

  // Query: is this player currently being chased?
  isBeingChased(playerId: string): boolean
}

/**
 * Sistema 3: Camuflaje
 * Connection: Hard (one-way from state sync)
 * Flows out: Camuflage state, checked before allowing catch
 *
 * TODO (Fase 2): Implement camuflage state that:
 * - Tracks which prisoners are camuflaged
 * - Prevents guard:catch while camuflaged
 * - Breaks camuflage on movement (optional mechanic)
 */
export interface IDisguiseSystem {
  // Query: is this prisoner camuflaged?
  isCamuflaged(playerId: string): boolean

  // Called by movement or action systems
  setCamuflage(playerId: string, camuflaged: boolean): void
}

/**
 * Sistema 4: Rutina/Fases
 * Connection: Hard (one-way from state sync)
 * Flows in: Timer management, phase transitions
 * Flows out: `phase:change` events to all clients
 *
 * TODO (Fase 2): Implement phase timer that:
 * - Tracks current phase duration
 * - Emits phase:change when phase duration expires
 * - Transitions active → lockdown → escape → riot (per game design)
 */
export interface IPhaseSystem {
  // Called by tick loop each tick
  updatePhaseTimer(): void

  // Query: time remaining in current phase (seconds)
  getRemainingPhaseTime(): number

  // Called by end-game logic
  requestPhaseTransition(nextPhase: string): void
}

/**
 * Sistema 5: Inventario
 * Connection: Hard (bidirectional)
 * Flows in: `player:interact` event (pickup/use/drop)
 * Flows out: `item:pickup`, `item:use` confirmations
 *
 * TODO (Fase 2): Implement inventory system that:
 * - Validates player position vs item position (distance check)
 * - Assigns items to player inventory
 * - Handles simultaneous pickup (first-come-first-served by timestamp)
 * - Emits item:pickup/item:use broadcasts
 */
export interface IInventorySystem {
  // Called when player:interact received
  tryPickupItem(playerId: string, itemId: string): boolean

  // Called when player:interact with use action
  tryUseItem(playerId: string, itemId: string, targetId?: string): boolean

  // Query: is this item in this player's inventory?
  hasItem(playerId: string, itemId: string): boolean
}

/**
 * Sistema 6: Rutas de Escape
 * Connection: Hard (bidirectional)
 * Flows in: Route completion actions, item collection
 * Flows out: `escape:progress`, `game:end` with winner=prisoners
 *
 * TODO (Fase 2): Implement escape routes that:
 * - Track which prisoners are on which route
 * - Track collected items per route
 * - Emit game:end when route complete + all items collected
 * - Check if escape is blocked (e.g., locked door)
 */
export interface IEscapeRouteSystem {
  // Called when prisoner reaches escape zone
  attemptEscape(playerId: string, routeId: string): boolean

  // Query: progress on this route (items collected / needed)
  getRouteProgress(routeId: string): { collected: number; needed: number }

  // Called by game logic to check win condition
  checkEscapeVictory(): boolean
}

/**
 * Sistema 9: Penalizaciones (Guard Errors)
 * Connection: Hard (one-way from state sync)
 * Flows in: Guard mistakes (false accusations, hitting innocent NPCs)
 * Flows out: `riot:available` when error count reaches threshold
 *
 * TODO (Fase 2): Implement penalty system that:
 * - Tracks guard error count
 * - Emits riot:available when errors >= 3
 * - Tracks error cooldown (can't penalty-lock a guard forever)
 */
export interface IPenaltySystem {
  // Called when guard makes an error
  recordGuardError(guardId: string, errorType: string): void

  // Query: error count for this guard
  getGuardErrorCount(guardId: string): number

  // Query: is riot available (prisoners can activate)?
  isRiotAvailable(): boolean
}

/**
 * Sistema 10: Motín
 * Connection: Hard (bidirectional)
 * Flows in: `riot:activate` from prisoner
 * Flows out: Game-wide effects (guards slowed, riot win condition)
 *
 * TODO (Fase 2): Implement riot system that:
 * - Guards at error threshold (riot:available sent to prisoners)
 * - One prisoner can emit riot:activate
 * - Game immediately ends with winner=prisoners
 */
export interface IRiotSystem {
  // Called when riot:activate received
  activateRiot(prisonerId: string): void

  // Query: is riot currently active?
  isRiotActive(): boolean

  // Called by game loop to apply riot effects (e.g., guard slowdown)
  applyRiotEffects(): void
}

/**
 * Sistema 11: Condiciones Victoria
 * Connection: Hard (one-way from state sync)
 * Flows out: `game:end` when any win condition met
 *
 * TODO (Fase 2): Implement win condition checker that:
 * - Checks if all prisoners escaped (prisoners win)
 * - Checks if all prisoners caught (guards win)
 * - Checks if riot activated (prisoners win)
 * - Emits game:end with reason
 */
export interface IVictoryConditionSystem {
  // Called by tick loop each tick
  updateVictoryConditions(): void

  // Query: has someone won?
  checkGameEnd(): { winner: 'prisoners' | 'guards' | null; reason?: string }
}

/**
 * Sistema 13: NPC Rutina
 * Connection: Hard (one-way from state sync)
 * Flows out: NPC position updates for delta broadcast
 *
 * TODO (Fase 2): Implement NPC behavior that:
 * - Patrol designated routes
 * - Chase prisoners when marked
 * - Return to patrol when chase ends
 * - Update position each tick
 */
export interface INPCBehaviorSystem {
  // Called by tick loop each tick to update all NPC positions
  updateNPCPositions(): void

  // Get NPC target position (for movement toward prey)
  getNPCTargetPosition(npcId: string): Vector3 | null

  // Called when chase ends for this NPC
  endChaseBehavior(npcId: string): void
}

/**
 * Sistema 17: Lobby
 * Connection: Hard (one-way from state sync)
 * Flows in: Player joins → room creation
 * Flows out: Room assignment, role assignment
 *
 * Already implemented in room-manager.ts; documented here for completeness.
 */
export interface ILobbySystem {
  // Called when player:join-room first arrives
  createGameRoom(roomId: string): void

  // Called by lobby timeout or manual start
  startGame(roomId: string): void
}

/**
 * Sistema 19: Reconexión
 * Connection: Soft (optional, enhances experience)
 * Flows in: Player socket reconnection attempt
 * Flows out: State snapshot for recovery
 *
 * TODO (Fase 3): Implement reconnection that:
 * - Detects socket disconnect
 * - Holds player slot for reconnect_timeout (30s)
 * - On reconnect, sends snapshot of full game state
 * - Player resumes in-game without reset
 */
export interface IReconnectionSystem {
  // Called when socket disconnects
  markPlayerDisconnected(playerId: string): void

  // Called when socket reconnects
  restorePlayerConnection(playerId: string): boolean

  // Called by cleanup to invalidate stale slots
  invalidateDisconnectedPlayer(playerId: string): void

  // Query: state snapshot for this player
  getStateSnapshot(playerId: string): any
}

/**
 * Integration checklist (for Phase 2/3 implementation):
 *
 * [ ] Movement (1): Implement IMovementSystem, wire to player:move validation
 * [ ] Pursuit (2): Implement IPursuitSystem, wire to guard:mark event
 * [ ] Disguise (3): Implement IDisguiseSystem, wire to catch validation
 * [ ] Phases (4): Implement IPhaseSystem, wire to phase timer tick
 * [ ] Inventory (5): Implement IInventorySystem, wire to player:interact
 * [ ] Escape (6): Implement IEscapeRouteSystem, wire to escape actions
 * [ ] Penalties (9): Implement IPenaltySystem, wire to error recording
 * [ ] Riot (10): Implement IRiotSystem, wire to riot:activate event
 * [ ] Victory (11): Implement IVictoryConditionSystem, call each tick
 * [ ] NPC Behavior (13): Implement INPCBehaviorSystem, call each tick
 * [ ] Reconnection (19): Implement IReconnectionSystem, wire to disconnect/reconnect
 */
