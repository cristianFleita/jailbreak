/**
 * Sistema 13: NPC Behavior
 * Controls NPC movement: patrolling, chasing prisoners, searching.
 * Called every tick to update NPC positions.
 */

import { GameRoomState, NPCState, Vector3 } from '../types.js'
import { updateNPCPosition, distance } from '../state.js'

export interface ChaseState {
  npcId: string
  targetId: string
  startTime: number
  lastSeenPosition: Vector3
}

export class NPCBehaviorSystem {
  private activeChases: Map<string, ChaseState> = new Map()
  private npcPatrols: Map<string, NPCPatrol> = new Map()

  constructor(private state: GameRoomState) {
    this.initializePatrols()
  }

  /**
   * Initialize patrol routes for NPCs.
   * In a real game, these would come from level design.
   * For now, simple circular patrol around spawn point.
   */
  private initializePatrols(): void {
    this.state.npcs.forEach((npc) => {
      const spawnX = npc.position.x
      const spawnZ = npc.position.z

      this.npcPatrols.set(npc.id, {
        npcId: npc.id,
        patrolPoints: [
          { x: spawnX + 5, y: npc.position.y, z: spawnZ + 5 },
          { x: spawnX - 5, y: npc.position.y, z: spawnZ + 5 },
          { x: spawnX - 5, y: npc.position.y, z: spawnZ - 5 },
          { x: spawnX + 5, y: npc.position.y, z: spawnZ - 5 },
        ],
        currentPointIndex: 0,
        progress: 0, // 0–1, progress to next waypoint
      })
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

    // Change animation state
    updateNPCPosition(this.state, npcId, npc.position, 'chasing')
  }

  /**
   * End chase: NPC will return to patrol.
   */
  endChase(npcId: string, reason: 'caught' | 'lost' | 'timeout'): void {
    this.activeChases.delete(npcId)
    const npc = this.state.npcs.get(npcId)
    if (npc) {
      updateNPCPosition(this.state, npcId, npc.position, 'idle')
    }
  }

  /**
   * Main update: called every tick.
   * Updates all NPC positions based on behavior (chase or patrol).
   */
  updateNPCPositions(tickDelta: number = 0.05): void {
    this.state.npcs.forEach((npc) => {
      if (this.activeChases.has(npc.id)) {
        // Chase behavior
        this.updateChaseNPC(npc, tickDelta)
      } else {
        // Patrol behavior
        this.updatePatrolNPC(npc, tickDelta)
      }
    })
  }

  /**
   * Chase: move NPC toward last known target position.
   * Speed: 6 units/sec (faster than prisoner walk speed 5, slower than sprint).
   */
  private updateChaseNPC(npc: NPCState, tickDelta: number): void {
    const chase = this.activeChases.get(npc.id)
    if (!chase) return

    const target = this.state.players.get(chase.targetId)
    if (!target) {
      // Target disconnected, end chase
      this.endChase(npc.id, 'lost')
      return
    }

    const chaseSpeed = 6.0 // units/sec
    const direction = this.normalize(
      this.subtract(target.position, npc.position)
    )

    const movement = this.scale(direction, chaseSpeed * tickDelta)
    const newPos = this.add(npc.position, movement)

    updateNPCPosition(this.state, npc.id, newPos, 'chasing')
    chase.lastSeenPosition = { ...target.position }

    // Check timeout: if chasing for >15 seconds, end chase
    const chaseDuration = (Date.now() - chase.startTime) / 1000
    if (chaseDuration > 15) {
      this.endChase(npc.id, 'timeout')
    }
  }

  /**
   * Patrol: move NPC along patrol route waypoints.
   * Speed: 3 units/sec (slow, wandering pace).
   */
  private updatePatrolNPC(npc: NPCState, tickDelta: number): void {
    const patrol = this.npcPatrols.get(npc.id)
    if (!patrol) return

    const patrolSpeed = 3.0 // units/sec
    const currentPoint = patrol.patrolPoints[patrol.currentPointIndex]

    const direction = this.normalize(this.subtract(currentPoint, npc.position))
    const movement = this.scale(direction, patrolSpeed * tickDelta)
    const newPos = this.add(npc.position, movement)

    // Check if reached waypoint
    const distToWaypoint = distance(newPos, currentPoint)
    if (distToWaypoint < 0.5) {
      // Move to next waypoint
      patrol.currentPointIndex = (patrol.currentPointIndex + 1) % patrol.patrolPoints.length
      patrol.progress = 0
    } else {
      patrol.progress += patrolSpeed * tickDelta / distance(npc.position, currentPoint)
    }

    updateNPCPosition(this.state, npc.id, newPos, 'walking')
  }

  /**
   * Query: is this NPC chasing anyone?
   */
  isChasing(npcId: string): boolean {
    return this.activeChases.has(npcId)
  }

  /**
   * Query: who is this NPC chasing?
   */
  getChaseTarget(npcId: string): string | null {
    return this.activeChases.get(npcId)?.targetId ?? null
  }

  // ========== Vector utilities ==========

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

interface NPCPatrol {
  npcId: string
  patrolPoints: Vector3[]
  currentPointIndex: number
  progress: number // 0–1
}
