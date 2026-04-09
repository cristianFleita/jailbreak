/**
 * Sistema 13: NPC Behavior
 * Controls NPC movement: phase-aware wandering and chasing prisoners.
 * Called every tick to update NPC positions.
 *
 * Wander behavior (replaces fixed 4-point patrol):
 *   - NPCs pick a random target inside the zone for the current game phase.
 *   - When they arrive, they pause 2–5 seconds, then pick a new target.
 *   - Phase transitions immediately redirect all non-chasing NPCs.
 *
 * Phase → Zone mapping:
 *   setup / active  → yard    (morning mingle)
 *   lockdown        → cells   (everyone back inside)
 *   escape          → guard NPCs → yard, helper NPCs → kitchen
 *   riot            → yard    (chaos)
 */

import { GameRoomState, GamePhase, NPCState, Vector3 } from '../types.js'
import { updateNPCPosition, distance } from '../state.js'
import { ZONES, Zone, randomPointInZone } from '../prison-layout.js'

export interface ChaseState {
  npcId: string
  targetId: string
  startTime: number
  lastSeenPosition: Vector3
}

interface NPCWander {
  npcId: string
  currentTarget: Vector3
  pauseUntil: number // Date.now() ms — NPC idles until this timestamp
}

export class NPCBehaviorSystem {
  private activeChases: Map<string, ChaseState> = new Map()
  private npcWanders:   Map<string, NPCWander>  = new Map()

  constructor(private state: GameRoomState) {
    this.initializeWanders()
  }

  /**
   * Initialize wander state for all NPCs.
   * Initial target: random point in yard (phase starts as 'setup'/'active').
   */
  private initializeWanders(): void {
    this.state.npcs.forEach((npc) => {
      this.npcWanders.set(npc.id, {
        npcId: npc.id,
        currentTarget: randomPointInZone(ZONES.yard),
        pauseUntil: 0,
      })
    })
  }

  /**
   * Called by GameManager whenever the phase transitions.
   * Forces all non-chasing NPCs to pick a new target in the appropriate zone.
   */
  onPhaseChanged(newPhase: GamePhase): void {
    this.state.npcs.forEach((npc) => {
      if (this.activeChases.has(npc.id)) return // chasing NPCs are unaffected
      const wander = this.npcWanders.get(npc.id)
      if (!wander) return
      wander.pauseUntil = 0
      wander.currentTarget = randomPointInZone(this.getZoneForNPC(npc, newPhase))
    })
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
      // Pick new wander target immediately after chase ends
      const wander = this.npcWanders.get(npcId)
      if (wander) {
        wander.pauseUntil = 0
        wander.currentTarget = randomPointInZone(
          this.getZoneForNPC(npc, this.state.phase.current)
        )
      }
    }
  }

  /**
   * Main update: called every tick (full wander + chase).
   * @deprecated Prefer updateChasingNPCsOnly — wander/movement is Unity's responsibility.
   */
  updateNPCPositions(tickDelta: number = 0.05): void {
    this.state.npcs.forEach((npc) => {
      if (this.activeChases.has(npc.id)) {
        this.updateChaseNPC(npc, tickDelta)
      } else {
        this.updateWanderNPC(npc, tickDelta)
      }
    })
  }

  /**
   * Chase-only update: only moves NPCs that are actively chasing a prisoner.
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

  /**
   * Wander: move toward current zone target, pause on arrival, then pick new target.
   * Speed: 3 units/sec.
   */
  private updateWanderNPC(npc: NPCState, tickDelta: number): void {
    const wander = this.npcWanders.get(npc.id)
    if (!wander) return

    // Paused at destination
    if (Date.now() < wander.pauseUntil) {
      updateNPCPosition(this.state, npc.id, npc.position, 'idle')
      return
    }

    const wanderSpeed  = 3.0
    const direction    = this.normalize(this.subtract(wander.currentTarget, npc.position))
    const movement     = this.scale(direction, wanderSpeed * tickDelta)
    const newPos       = this.add(npc.position, movement)

    const distToTarget = distance(newPos, wander.currentTarget)
    if (distToTarget < 0.5) {
      // Arrived — pause 2–5 seconds, then pick new target in current zone
      wander.pauseUntil    = Date.now() + 2000 + Math.random() * 3000
      wander.currentTarget = randomPointInZone(
        this.getZoneForNPC(npc, this.state.phase.current)
      )
      updateNPCPosition(this.state, npc.id, wander.currentTarget, 'idle')
    } else {
      updateNPCPosition(this.state, npc.id, newPos, 'walking')
    }
  }

  /**
   * Returns the appropriate zone for an NPC given the current game phase.
   */
  private getZoneForNPC(npc: NPCState, phase: GamePhase): Zone {
    switch (phase) {
      case 'lockdown':
        return ZONES.cells
      case 'escape':
        // Guard-type NPCs patrol the yard; helper NPCs head to kitchen exit
        return npc.type === 'guard' ? ZONES.yard : ZONES.kitchen
      case 'setup':
      case 'active':
      case 'riot':
      default:
        return ZONES.yard
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
