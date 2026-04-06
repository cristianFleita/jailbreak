/**
 * Sistema 9: Penalties (Penalizaciones)
 * Tracks guard errors and makes riot available when threshold is reached.
 * Error types: false_accusation, hitting_innocent_npc, etc.
 */

import { GameRoomState } from '../types.js'

export interface GuardErrorRecord {
  guardId: string
  errorCount: number
  lastErrorTime: number
  errorTypes: string[]
}

export const RIOT_THRESHOLD = 3 // Prisoners can riot after guard gets 3 errors

export class PenaltySystem {
  private guardErrors: Map<string, GuardErrorRecord> = new Map()
  private riotAvailable: boolean = false

  constructor(private state: GameRoomState) {
    // Initialize error tracking for guard
    const guard = Array.from(state.players.values()).find((p) => p.role === 'guard')
    if (guard) {
      this.guardErrors.set(guard.id, {
        guardId: guard.id,
        errorCount: 0,
        lastErrorTime: Date.now(),
        errorTypes: [],
      })
    }
  }

  /**
   * Record a guard error.
   */
  recordGuardError(guardId: string, errorType: string): void {
    let record = this.guardErrors.get(guardId)
    if (!record) {
      record = {
        guardId,
        errorCount: 0,
        lastErrorTime: Date.now(),
        errorTypes: [],
      }
      this.guardErrors.set(guardId, record)
    }

    record.errorCount++
    record.lastErrorTime = Date.now()
    record.errorTypes.push(errorType)

    console.log(`[PENALTY] Guard ${guardId} error #${record.errorCount}: ${errorType}`)

    // Check if riot threshold reached
    if (record.errorCount >= RIOT_THRESHOLD) {
      this.setRiotAvailable(true)
    }
  }

  /**
   * Get error count for a guard.
   */
  getGuardErrorCount(guardId: string): number {
    return this.guardErrors.get(guardId)?.errorCount ?? 0
  }

  /**
   * Make riot available (called when error threshold reached).
   */
  setRiotAvailable(available: boolean): void {
    if (available && !this.riotAvailable) {
      console.log('[PENALTY] RIOT AVAILABLE for prisoners!')
      this.riotAvailable = true
    } else if (!available && this.riotAvailable) {
      console.log('[PENALTY] Riot no longer available')
      this.riotAvailable = false
    }
  }

  /**
   * Query: is riot available?
   */
  isRiotAvailable(): boolean {
    return this.riotAvailable
  }

  /**
   * Get diagnostic info.
   */
  debugGetGuardStats(): GuardErrorRecord[] {
    return Array.from(this.guardErrors.values())
  }
}
