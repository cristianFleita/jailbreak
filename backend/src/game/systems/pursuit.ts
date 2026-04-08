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
   * The human guard player does the actual chasing. Optionally, the nearest
   * idle NPC can also assist the chase (if one is available).
   */
  startPursuit(guardPlayerId: string, prisonerPlayerId: string): void {
    const prisoner = this.state.players.get(prisonerPlayerId)
    if (!prisoner) return

    // Optionally find the nearest idle NPC to assist the chase
    let assistNpcId = ''
    const npcs = Array.from(this.state.npcs.values())
    let closestDist = Infinity
    for (const npc of npcs) {
      if (this.npcBehavior.isChasing(npc.id)) continue
      const dx = npc.position.x - prisoner.position.x
      const dz = npc.position.z - prisoner.position.z
      const d  = Math.sqrt(dx * dx + dz * dz)
      if (d < closestDist) {
        closestDist = d
        assistNpcId = npc.id
      }
    }

    if (assistNpcId) {
      this.npcBehavior.startChase(assistNpcId, prisonerPlayerId, prisoner.position)
    }

    this.pursuits.set(prisonerPlayerId, {
      guardPlayerId,
      prisonerPlayerId,
      npcChasingId: assistNpcId,
      startTime: Date.now(),
      isActive: true,
    })

    console.log(`[PURSUIT] Guard player ${guardPlayerId} chasing prisoner ${prisonerPlayerId}` +
      (assistNpcId ? ` (NPC ${assistNpcId} assisting)` : ' (no NPC assist)'))
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
