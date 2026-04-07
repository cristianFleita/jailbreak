/**
 * Sistema 4: Phases (Fases)
 * Manages game phases: active → lockdown → escape → riot
 * Each phase has a duration; transitions happen automatically.
 */

import { GameRoomState, GamePhase } from '../types.js'

export interface PhaseTransition {
  from: GamePhase
  to: GamePhase
  duration: number // seconds
}

export class PhaseSystem {
  private phaseTransitions: PhaseTransition[] = [
    { from: 'active', to: 'lockdown', duration: 120 }, // 2 min active, then lockdown
    { from: 'lockdown', to: 'escape', duration: 60 }, // 1 min lockdown
    { from: 'escape', to: 'escape', duration: 300 }, // escape lasts 5 min
  ]

  constructor(private state: GameRoomState) {}

  /**
   * Update phase timer and check for transitions.
   * Called every tick (50ms).
   */
  updatePhaseTimer(): boolean {
    const phase = this.state.phase
    const elapsedSeconds = (Date.now() - phase.startedAt) / 1000

    // Check if phase duration expired
    if (elapsedSeconds >= phase.duration) {
      return this.transitionPhase()
    }

    return false // no transition
  }

  /**
   * Transition to next phase.
   */
  private transitionPhase(): boolean {
    const currentPhase = this.state.phase.current

    // Find transition rule
    const transition = this.phaseTransitions.find((t) => t.from === currentPhase)

    if (!transition) {
      console.warn(`[PHASE] No transition defined for phase ${currentPhase}`)
      return false
    }

    const nextPhase = transition.to
    const nextName = this.getPhaseName(nextPhase)

    this.state.phase = {
      current: nextPhase,
      phaseName: nextName,
      duration: this.getPhaseDuration(nextPhase),
      startedAt: Date.now(),
    }

    console.log(`[PHASE] Transitioned to ${nextPhase} (duration: ${this.state.phase.duration}s)`)
    return true
  }

  /**
   * Get human-readable phase name.
   */
  private getPhaseName(phase: GamePhase): string {
    switch (phase) {
      case 'setup':
        return 'Setup'
      case 'active':
        return 'Active'
      case 'lockdown':
        return 'Lockdown'
      case 'escape':
        return 'Escape'
      case 'riot':
        return 'Riot'
      default:
        return 'Unknown'
    }
  }

  /**
   * Get duration for a phase.
   */
  private getPhaseDuration(phase: GamePhase): number {
    switch (phase) {
      case 'setup':
        return 30 // 30 seconds
      case 'active':
        return 120 // 2 minutes
      case 'lockdown':
        return 60 // 1 minute
      case 'escape':
        return 300 // 5 minutes
      case 'riot':
        return 180 // 3 minutes
      default:
        return 60
    }
  }

  /**
   * Get remaining time in current phase (seconds).
   */
  getRemainingTime(): number {
    const phase = this.state.phase
    const elapsedSeconds = (Date.now() - phase.startedAt) / 1000
    return Math.max(0, phase.duration - elapsedSeconds)
  }

  /**
   * Manually request phase transition (e.g., for riot).
   */
  requestPhaseTransition(nextPhase: GamePhase): void {
    const nextName = this.getPhaseName(nextPhase)

    this.state.phase = {
      current: nextPhase,
      phaseName: nextName,
      duration: this.getPhaseDuration(nextPhase),
      startedAt: Date.now(),
    }

    console.log(`[PHASE] Manual transition to ${nextPhase}`)
  }

  /**
   * Get current phase.
   */
  getCurrentPhase(): GamePhase {
    return this.state.phase.current
  }
}
