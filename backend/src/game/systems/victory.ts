/**
 * Sistema 11: Victory Conditions (Condiciones de Victoria)
 * Checks for win conditions every tick:
 * - Guards win if all prisoners caught
 * - Prisoners win if all escape
 * - Prisoners win if riot activated
 * - Timeout (if escape phase ends without escape)
 */

import { GameRoomState } from '../types.js'
import { EscapeRouteSystem } from './escape-routes.js'
import { PhaseSystem } from './phases.js'

export interface VictoryResult {
  winner: 'prisoners' | 'guards' | null
  reason?: string
}

export class VictoryConditionSystem {
  constructor(
    private state: GameRoomState,
    private escapeSystem: EscapeRouteSystem,
    private phaseSystem: PhaseSystem
  ) {}

  /**
   * Check all win conditions.
   * Called every tick.
   */
  checkVictoryConditions(): VictoryResult {
    // Check guards win: all prisoners dead/caught
    const allPrisonersCaught = this.checkAllPrisonersCaught()
    if (allPrisonersCaught) {
      return { winner: 'guards', reason: 'all_prisoners_caught' }
    }

    // Check prisoners win: all escaped
    const allEscaped = this.escapeSystem.allPrisonersEscaped()
    if (allEscaped) {
      return { winner: 'prisoners', reason: 'escape_route' }
    }

    // Check timeout: escape phase ended without escape
    const phase = this.phaseSystem.getCurrentPhase()
    const remainingTime = this.phaseSystem.getRemainingTime()

    if (phase === 'escape' && remainingTime <= 0) {
      // Escape time ran out
      return { winner: 'guards', reason: 'escape_timeout' }
    }

    return { winner: null }
  }

  /**
   * Check if all alive prisoners have been caught.
   */
  private checkAllPrisonersCaught(): boolean {
    const prisoners = Array.from(this.state.players.values()).filter((p) => p.role === 'prisoner')

    if (prisoners.length === 0) return false

    return prisoners.every((prisoner) => !prisoner.isAlive)
  }

  /**
   * Get current game stats.
   */
  getGameStats(): {
    totalPrisoners: number
    alivePrisoners: number
    caughtPrisoners: number
    escapedPrisoners: number
    currentPhase: string
  } {
    const prisoners = Array.from(this.state.players.values()).filter((p) => p.role === 'prisoner')
    const alive = prisoners.filter((p) => p.isAlive).length
    const escaped = this.escapeSystem.getEscapedPrisoners().length

    return {
      totalPrisoners: prisoners.length,
      alivePrisoners: alive,
      caughtPrisoners: prisoners.length - alive,
      escapedPrisoners: escaped,
      currentPhase: this.phaseSystem.getCurrentPhase(),
    }
  }
}
