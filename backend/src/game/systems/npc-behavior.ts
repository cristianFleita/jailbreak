/**
 * Sistema 13: NPC Behavior
 * Controls NPC movement: chasing prisoners.
 * Called every tick to update NPC positions.
 *
 * Wander behavior is entirely handled by Unity clients based on assignments
 * emitted by JailRoutineSystem. The backend only tracks chase states.
 */

import { GameRoomState, GamePhase, NPCState, Vector3 } from '../types.js'
import { updateNPCPosition, distance } from '../state.js'

export interface ChaseState {
  npcId: string
  targetId: string
  startTime: number
  lastSeenPosition: Vector3
}

export class NPCBehaviorSystem {
  private activeChases: Map<string, ChaseState> = new Map()

  constructor(private state: GameRoomState) {
    // Wanders are handled by JailRoutineSystem & Unity clients
  }

  /**
   * Called by GameManager whenever the phase transitions.
   */
  onPhaseChanged(newPhase: GamePhase): void {
    // Chasing logic could react here if needed
  }

  /**
   * Start chase: NPC will pursue target.
   */
  startChase(npcId: string, targetId: string, targetPosition: Vector3): void {
    const npc = this.state.npcs.get(npcId)
    if (!npc) return

    this.activeChases.set(npcId, {
      npcId,
      targetId,
      startTime: Date.now(),
      lastSeenPosition: { ...targetPosition },
    })

    updateNPCPosition(this.state, npcId, npc.position, 'chasing')
  }

  /**
   * End chase: NPC returns to wandering.
   */
  endChase(npcId: string, reason: 'caught' | 'lost' | 'timeout'): void {
    this.activeChases.delete(npcId)
    const npc = this.state.npcs.get(npcId)
    if (npc) {
      updateNPCPosition(this.state, npcId, npc.position, 'idle')
      // Once chase ends, Unity will resume its previous assignment or we can trigger a reassign
    }
  }

  /**
   * Main update: called every tick.
   * Only moves NPCs that are actively chasing a prisoner.
   * Wander positions are handled by Unity clients based on jail-routine assignments.
   */
  updateChasingNPCsOnly(tickDelta: number = 0.05): void {
    this.activeChases.forEach((_chase, npcId) => {
      const npc = this.state.npcs.get(npcId)
      if (npc) this.updateChaseNPC(npc, tickDelta)
    })
  }

  /**
   * Chase: move NPC toward last known target position.
   * Speed: 6 units/sec.
   */
  private updateChaseNPC(npc: NPCState, tickDelta: number): void {
    const chase = this.activeChases.get(npc.id)
    if (!chase) return

    const target = this.state.players.get(chase.targetId)
    if (!target) {
      this.endChase(npc.id, 'lost')
      return
    }

    const chaseSpeed = 6.0
    const direction  = this.normalize(this.subtract(target.position, npc.position))
    const movement   = this.scale(direction, chaseSpeed * tickDelta)
    const newPos     = this.add(npc.position, movement)

    updateNPCPosition(this.state, npc.id, newPos, 'chasing')
    chase.lastSeenPosition = { ...target.position }

    const chaseDuration = (Date.now() - chase.startTime) / 1000
    if (chaseDuration > 15) {
      this.endChase(npc.id, 'timeout')
    }
  }

  // ──────────── Query helpers ────────────

  isChasing(npcId: string): boolean {
    return this.activeChases.has(npcId)
  }

  getChaseTarget(npcId: string): string | null {
    return this.activeChases.get(npcId)?.targetId ?? null
  }

  // ──────────── Vector utilities ────────────

  private normalize(v: Vector3): Vector3 {
    const len = Math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z)
    if (len === 0) return { x: 0, y: 0, z: 0 }
    return { x: v.x / len, y: v.y / len, z: v.z / len }
  }

  private subtract(a: Vector3, b: Vector3): Vector3 {
    return { x: a.x - b.x, y: a.y - b.y, z: a.z - b.z }
  }

  private add(a: Vector3, b: Vector3): Vector3 {
    return { x: a.x + b.x, y: a.y + b.y, z: a.z + b.z }
  }

  private scale(v: Vector3, s: number): Vector3 {
    return { x: v.x * s, y: v.y * s, z: v.z * s }
  }
}
