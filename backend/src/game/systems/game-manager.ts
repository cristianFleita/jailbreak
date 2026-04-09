/**
 * Game Manager: Coordinates all game systems.
 * Single entry point for tick-by-tick game logic updates.
 * All systems are initialized here and updated in dependency order.
 */

import { GameRoom } from '../types.js'
import { NPCBehaviorSystem } from './npc-behavior.js'
import { PursuitSystem } from './pursuit.js'
import { DisguiseSystem } from './disguise.js'
import { PenaltySystem } from './penalties.js'
import { InventorySystem } from './inventory.js'
import { EscapeRouteSystem } from './escape-routes.js'
import { PhaseSystem } from './phases.js'
import { VictoryConditionSystem } from './victory.js'
import { JailRoutineSystem } from './jail-routine.js'

export class GameManager {
  // All systems
  npcBehavior: NPCBehaviorSystem
  pursuit: PursuitSystem
  disguise: DisguiseSystem
  penalty: PenaltySystem
  inventory: InventorySystem
  escapeRoutes: EscapeRouteSystem
  phases: PhaseSystem
  victory: VictoryConditionSystem
  jailRoutine: JailRoutineSystem

  constructor(private room: GameRoom) {
    const state = room.state

    // Initialize systems (in dependency order)
    this.npcBehavior = new NPCBehaviorSystem(state)
    this.pursuit = new PursuitSystem(state, this.npcBehavior)
    this.disguise = new DisguiseSystem(state)
    this.penalty = new PenaltySystem(state)
    this.inventory = new InventorySystem(state)
    this.escapeRoutes = new EscapeRouteSystem(state)
    this.phases = new PhaseSystem(state)
    this.victory = new VictoryConditionSystem(state, this.escapeRoutes, this.phases)
    this.jailRoutine = new JailRoutineSystem(state)

    console.log('[GAME-MANAGER] Initialized all systems')
  }

  /**
   * Main game loop tick: called every 50ms (20 ticks/sec).
   * Updates all systems in order, returns any game-end condition.
   */
  tick(): { shouldEnd: boolean; winner?: 'prisoners' | 'guards'; reason?: string } {
    const tickDelta = this.room.config.tickInterval / 1000 // convert to seconds

    // ========== Phase Management ==========
    const phaseTransitioned = this.phases.updatePhaseTimer()
    if (phaseTransitioned) {
      const currentPhase = this.phases.getCurrentPhase()
      console.log(`[TICK] Phase changed to: ${currentPhase}`)

      // Redirect NPCs to the zone appropriate for the new phase
      this.npcBehavior.onPhaseChanged(currentPhase)

      // Handle phase-specific logic
      if (currentPhase === 'riot') {
        // Riot mode: prisoners win
        return {
          shouldEnd: true,
          winner: 'prisoners',
          reason: 'riot_activated',
        }
      }
    }

    // ========== Jail Routine (NPC Phase System) ==========
    this.jailRoutine.update(tickDelta)

    // ========== NPC Behavior ==========
    // Only update positions for NPCs actively chasing a prisoner.
    // Routine movement/animations are driven by jail-routine assignments → Unity clients.
    this.npcBehavior.updateChasingNPCsOnly(tickDelta)

    // ========== Pursuit Management ==========
    // Check active chases, end chases if prisoner escaped far enough
    this.pursuit.updatePursuits()

    // ========== Escape Route Tracking ==========
    // (Items collected are recorded via event handlers, not in tick)

    // ========== Victory Condition Check ==========
    const victoryResult = this.victory.checkVictoryConditions()
    if (victoryResult.winner) {
      return {
        shouldEnd: true,
        winner: victoryResult.winner,
        reason: victoryResult.reason,
      }
    }

    return { shouldEnd: false }
  }

  /**
   * Called when guard marks a prisoner (via guard:mark event).
   * Initiates pursuit.
   */
  onGuardMark(guardPlayerId: string, prisonerPlayerId: string): void {
    this.pursuit.startPursuit(guardPlayerId, prisonerPlayerId)
  }

  /**
   * Called when guard catches a prisoner (via guard:catch event).
   * Ends pursuit.
   */
  onGuardCatch(prisonerPlayerId: string): void {
    this.pursuit.endPursuit(prisonerPlayerId, 'caught')
  }

  /**
   * Called when prisoner picks up an item.
   */
  onItemPickup(playerId: string, itemId: string): void {
    this.inventory.tryPickupItem(playerId, itemId)
    this.escapeRoutes.recordItemCollection(playerId, itemId)
  }

  /**
   * Called when prisoner moves.
   */
  onPlayerMove(playerId: string): void {
    // Check if prisoner is trying to escape
    const player = this.room.state.players.get(playerId)
    if (player?.role === 'prisoner') {
      const escapeCheck = this.escapeRoutes.checkEscapeCondition(playerId)
      if (escapeCheck.canEscape) {
        this.escapeRoutes.recordEscape(playerId)
        console.log(`[GAME-MANAGER] Prisoner ${playerId} escaped!`)
      }
    }

    // Optional: break camuflage on movement
    this.disguise.onPlayerMove(playerId)
  }

  /**
   * Called when riot is activated.
   */
  onRiotActivated(): void {
    this.phases.requestPhaseTransition('riot')
    console.log('[GAME-MANAGER] Riot activated! Game ending.')
  }

  /**
   * Get all game stats for debugging.
   */
  getGameStats() {
    return {
      victoryStats: this.victory.getGameStats(),
      guardErrors: this.penalty.debugGetGuardStats(),
      activePursuits: this.pursuit.getActivePursuits(),
      escapedPrisoners: this.escapeRoutes.getEscapedPrisoners(),
      currentPhase: this.phases.getCurrentPhase(),
      phaseTimeRemaining: this.phases.getRemainingTime(),
    }
  }

  /**
   * Reset all systems (called on new game).
   */
  reset(): void {
    console.log('[GAME-MANAGER] Resetting all systems')
    // Systems will be re-initialized by constructor
  }
}
