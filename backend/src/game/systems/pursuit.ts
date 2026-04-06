/**
 * Sistema 2: Pursuit (Persecución)
 * Manages chase state: when guard marks prisoner, starts chase.
 * Tracks which NPCs are chasing which prisoners.
 */

import { GameRoomState } from '../types.js'
import { NPCBehaviorSystem } from './npc-behavior.js'

export interface PursuitState {
  guardPlayerId: string
  prisonerPlayerId: string
  npcChasingId: string // which NPC is handling this chase
  startTime: number
  isActive: boolean
}

export class PursuitSystem {
  private pursuits: Map<string, PursuitState> = new Map() // key: prisonerPlayerId
  private npcBehavior: NPCBehaviorSystem

  constructor(private state: GameRoomState, npcBehavior: NPCBehaviorSystem) {
    this.npcBehavior = npcBehavior
  }

  /**
   * Guard marks prisoner: initiate chase.
   * Find the main guard NPC and have it chase the prisoner.
   */
  startPursuit(guardPlayerId: string, prisonerPlayerId: string): void {
    // Find the main guard NPC (first one spawned, type='guard')
    const mainGuardNpc = Array.from(this.state.npcs.values()).find(
      (npc) => npc.type === 'guard'
    )

    if (!mainGuardNpc) {
      console.warn('[PURSUIT] No guard NPC found')
      return
    }

    const prisoner = this.state.players.get(prisonerPlayerId)
    if (!prisoner) return

    // Start NPC chase
    this.npcBehavior.startChase(mainGuardNpc.id, prisonerPlayerId, prisoner.position)

    // Track pursuit
    const pursuitKey = prisonerPlayerId
    this.pursuits.set(pursuitKey, {
      guardPlayerId,
      prisonerPlayerId,
      npcChasingId: mainGuardNpc.id,
      startTime: Date.now(),
      isActive: true,
    })

    console.log(`[PURSUIT] Guard ${guardPlayerId} started chasing prisoner ${prisonerPlayerId}`)
  }

  /**
   * End chase: NPC stops chasing prisoner.
   */
  endPursuit(prisonerPlayerId: string, reason: 'caught' | 'lost' | 'timeout'): void {
    const pursuit = this.pursuits.get(prisonerPlayerId)
    if (!pursuit) return

    this.npcBehavior.endChase(pursuit.npcChasingId, reason)
    this.pursuits.delete(prisonerPlayerId)

    console.log(`[PURSUIT] Chase ended for ${prisonerPlayerId}: ${reason}`)
  }

  /**
   * Query: is this prisoner being chased?
   */
  isBeingChased(prisonerPlayerId: string): boolean {
    const pursuit = this.pursuits.get(prisonerPlayerId)
    return pursuit ? pursuit.isActive : false
  }

  /**
   * Query: how far is chaser from prisoner?
   */
  getChaseDist(prisonerPlayerId: string): number {
    const pursuit = this.pursuits.get(prisonerPlayerId)
    if (!pursuit) return Infinity

    const prisoner = this.state.players.get(prisonerPlayerId)
    const npc = this.state.npcs.get(pursuit.npcChasingId)

    if (!prisoner || !npc) return Infinity

    const dx = prisoner.position.x - npc.position.x
    const dy = prisoner.position.y - npc.position.y
    const dz = prisoner.position.z - npc.position.z
    return Math.sqrt(dx * dx + dy * dy + dz * dz)
  }

  /**
   * Called every tick: update pursuit distances, check for escape.
   */
  updatePursuits(): void {
    for (const [prisonerPlayerId, pursuit] of this.pursuits.entries()) {
      if (!pursuit.isActive) continue

      const dist = this.getChaseDist(prisonerPlayerId)

      // If prisoner escaped far enough, end chase
      if (dist > 30) {
        this.endPursuit(prisonerPlayerId, 'lost')
      }
    }
  }

  /**
   * Get all active pursuits.
   */
  getActivePursuits(): PursuitState[] {
    return Array.from(this.pursuits.values()).filter((p) => p.isActive)
  }
}
