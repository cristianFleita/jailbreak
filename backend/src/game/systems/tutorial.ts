/**
 * Tutorial system — Fase A authoritative training round.
 * Spec: design/gdd/tutorial-plan.md
 *
 * Flow:
 *   host presses Start Game
 *     → roles assigned (existing assignRandomRoles)
 *     → TutorialManager.start(): status='tutorial', emit tutorial:start per socket
 *     → 1 Hz tick emits tutorial:state per socket (remaining + own checklist)
 *     → at 60 s OR forceEnd(): emit tutorial:end, run cleanup, invoke onComplete
 *
 * The manager owns its own timers and emit fan-out. It does NOT touch any
 * gameplay system: NPCs, route1, item state, and the tick game loop are all
 * started later by transitionToActive after the tutorial ends. Because no
 * game loop runs during the tutorial, `game:end` cannot fire — the spec
 * guarantee is satisfied by construction.
 */

import { Server } from 'socket.io'
import {
  GameRoom,
  GameRoomState,
  NPCAssignment,
  PlayerRole,
  PlayerState,
  TutorialEndPayload,
  TutorialGuardMissionId,
  TutorialMissionId,
  TutorialPrisonerMissionId,
  TutorialReadyPayload,
  TutorialRoomState,
  TutorialStartPayload,
  TutorialStatePayload,
} from '../types.js'
import { resetPlayersToAssignedSpawns } from '../state.js'

/** Total tutorial duration. The spec calls for 60s, but we expose it for tuning + tests. */
export const TUTORIAL_DURATION_SECONDS = 90
export const TUTORIAL_TICK_INTERVAL_MS = 1000

export const PRISONER_MISSION_IDS: readonly TutorialPrisonerMissionId[] = [
  'p1_review_route',
  'p2_food_flow',
  'p3_sprint',
  'p4_grab_item',
  'p6_risky_action',
  'p7_hide_cart',
] as const

export const GUARD_MISSION_IDS: readonly TutorialGuardMissionId[] = [
  'g1_observe',
  'g2_anomaly',
  'g3_capture',
  'g4_error',
  'g5_world_cue',
] as const

const PRISONER_MISSION_SET: ReadonlySet<TutorialMissionId> = new Set(PRISONER_MISSION_IDS)
const GUARD_MISSION_SET: ReadonlySet<TutorialMissionId> = new Set(GUARD_MISSION_IDS)

export function getMissionsForRole(role: PlayerRole): readonly TutorialMissionId[] {
  return role === 'guard' ? GUARD_MISSION_IDS : PRISONER_MISSION_IDS
}

export function isMissionAllowedForRole(role: PlayerRole, missionId: TutorialMissionId): boolean {
  return role === 'guard'
    ? GUARD_MISSION_SET.has(missionId)
    : PRISONER_MISSION_SET.has(missionId)
}

/**
 * Tutorial NPC templates. Scope is deliberately small: two NPCs whose only
 * job is to give the guard a moving target for capture practice.
 *   - innocent_walker:  loops a benign path. Catching it counts as a mistake
 *                       (mission G4 — "the error costs").
 *   - suspicious_walker: looks off-routine. Catching it counts as a valid
 *                        capture (mission G3 — "capture by focus").
 * Prisoners can ignore them entirely; the scene's other interactables drive
 * the prisoner missions.
 */
export const TUTORIAL_NPC_TEMPLATE_IDS = [
  'tutorial_innocent_walker',
  'tutorial_suspicious_walker',
] as const

export type TutorialNpcTemplateId = (typeof TUTORIAL_NPC_TEMPLATE_IDS)[number]

/**
 * Builds the 2 authoritative NPC assignments for a tutorial round. Output is
 * deterministic in `seed` — same seed yields the same npcId → template
 * mapping so all clients agree without further sync. The mapping is fixed
 * (innocent first, suspicious second) so the guard's capture practice is
 * predictable across every match.
 */
export function buildTutorialNpcAssignments(_seed: number, durationSeconds: number): NPCAssignment[] {
  const assignments: NPCAssignment[] = []
  for (let i = 0; i < TUTORIAL_NPC_TEMPLATE_IDS.length; i++) {
    assignments.push({
      npcId: `tutorial_npc_${String(i + 1).padStart(2, '0')}`,
      actionId: TUTORIAL_NPC_TEMPLATE_IDS[i],
      animTrigger: 'walk',
      duration: durationSeconds,
      loop: true,
      walkSpeedMult: 1.0,
    })
  }
  return assignments
}

/**
 * Builds the initial tutorial state and writes it into the room. Pure helper
 * exposed for tests.
 */
export function buildTutorialState(now: number, durationSeconds: number, seed: number): TutorialRoomState {
  return {
    startedAt: now,
    durationSeconds,
    endsAt: now + durationSeconds * 1000,
    seed,
    completedByUserId: new Map(),
    readyUserIds: new Set(),
  }
}

/**
 * Resets every player's tutorial-only mutable state so the real match starts
 * clean. The spec calls out four buckets to clear:
 *   - inventory (held + slots)
 *   - tutorial errors / captures (none persisted on the player record today)
 *   - interaction progress (likewise client-only at this stage)
 *   - position (restored here to the real spawn assigned before tutorial)
 *
 * We also drop the room's tutorial state object.
 */
export function cleanupTutorialBeforeActive(state: GameRoomState): void {
  for (const player of state.players.values()) {
    player.heldItemId = null
    player.carrying = null
    if (player.role === 'prisoner') {
      player.inventorySlots = [null, null]
    } else {
      player.inventorySlots = []
    }
  }
  resetPlayersToAssignedSpawns(state)
  state.tutorial = undefined
}

export interface TutorialManagerOptions {
  /** Override duration for tests; defaults to TUTORIAL_DURATION_SECONDS. */
  durationSeconds?: number
  /** Override tick cadence for tests; defaults to TUTORIAL_TICK_INTERVAL_MS. */
  tickIntervalMs?: number
  /** Deterministic seed override for tests. Real runs use Date.now()-derived. */
  seed?: number
  /** Clock injection for tests. */
  now?: () => number
  /** Called when the tutorial finishes naturally OR is force-ended. */
  onComplete: () => void
}

/**
 * Per-room tutorial controller. One instance is created when the host hits
 * Start Game; it owns the room.state.tutorial object, the heartbeat interval,
 * and the end timeout.
 */
export class TutorialManager {
  private readonly io: Server
  private readonly room: GameRoom
  private readonly durationSeconds: number
  private readonly tickIntervalMs: number
  private readonly seed: number
  private readonly now: () => number
  private readonly onComplete: () => void

  private tickInterval: ReturnType<typeof setInterval> | null = null
  private endTimeout: ReturnType<typeof setTimeout> | null = null
  private finished = false
  private startedAt = 0

  constructor(io: Server, room: GameRoom, opts: TutorialManagerOptions) {
    this.io = io
    this.room = room
    this.durationSeconds = opts.durationSeconds ?? TUTORIAL_DURATION_SECONDS
    this.tickIntervalMs = opts.tickIntervalMs ?? TUTORIAL_TICK_INTERVAL_MS
    this.now = opts.now ?? Date.now
    this.seed = opts.seed ?? Math.floor(this.now() % 0x7fffffff)
    this.onComplete = opts.onComplete
  }

  /**
   * Switch the room into tutorial mode and emit `tutorial:start` to every
   * connected player on their own socket so role + mission set is per-player.
   * Schedules the heartbeat ticker and the end timer.
   */
  start(): void {
    if (this.room.state.status !== 'lobby') {
      console.warn(
        `[TUTORIAL] Cannot start tutorial for room "${this.room.state.id}" — status is "${this.room.state.status}"`
      )
      return
    }

    const now = this.now()
    this.startedAt = now
    this.room.state.status = 'tutorial'
    this.room.state.tutorial = buildTutorialState(now, this.durationSeconds, this.seed)

    const npcAssignments = buildTutorialNpcAssignments(this.seed, this.durationSeconds)
    for (const [socketId, player] of this.room.state.players) {
      const payload: TutorialStartPayload = {
        duration: this.durationSeconds,
        role: player.role,
        seed: this.seed,
        missions: [...getMissionsForRole(player.role)],
        players: Array.from(this.room.state.players.values()),
        npcAssignments,
      }
      this.io.to(socketId).emit('tutorial:start', payload)
    }

    this.tickInterval = setInterval(() => this.broadcastState(), this.tickIntervalMs)
    this.endTimeout = setTimeout(() => this.finish(), this.durationSeconds * 1000)

    console.log(
      `[TUTORIAL] Room "${this.room.state.id}" entered tutorial — ${this.durationSeconds}s, seed=${this.seed}, players=${this.room.state.players.size}`
    )
  }

  /**
   * Records a mission as completed for the given userId. Validates:
   *   - tutorial is currently active
   *   - the user is in the room
   *   - the mission belongs to the user's role
   *
   * Returns true on a state change (new completion), false otherwise. The
   * client is re-pushed an updated tutorial:state immediately on success so
   * the HUD reflects the change without waiting for the next tick.
   */
  markMissionComplete(userId: string, missionId: TutorialMissionId): boolean {
    if (this.finished || !this.room.state.tutorial) return false

    const player = this.room.state.playersByUserId.get(userId)
    if (!player) return false
    if (!isMissionAllowedForRole(player.role, missionId)) {
      console.warn(
        `[TUTORIAL] Rejected mission "${missionId}" — not in role set for ${player.role} (user=${userId})`
      )
      return false
    }

    const completedByUser = this.room.state.tutorial.completedByUserId
    let set = completedByUser.get(userId)
    if (!set) {
      set = new Set<TutorialMissionId>()
      completedByUser.set(userId, set)
    }
    if (set.has(missionId)) return false
    set.add(missionId)

    this.broadcastStateForPlayer(player)
    return true
  }

  /**
   * Records the player as "ready to skip the tutorial". When every connected
   * player in the room has marked themselves ready, the tutorial is force-ended
   * (which fires tutorial:end + transitionToActive). Idempotent.
   *
   * Returns true on a state change (new ready), false otherwise. The caller
   * receives a `tutorial:ready` broadcast on every change so the UI can
   * reflect "X / Y ready".
   */
  markReady(userId: string): boolean {
    if (this.finished || !this.room.state.tutorial) return false

    const player = this.room.state.playersByUserId.get(userId)
    if (!player) return false

    const set = this.room.state.tutorial.readyUserIds
    if (set.has(userId)) return false
    set.add(userId)

    this.broadcastReady()

    if (this.everyoneReady()) {
      console.log(
        `[TUTORIAL] Room "${this.room.state.id}" — all ${set.size} players ready, skipping tutorial`
      )
      this.forceEnd()
    }
    return true
  }

  /** True if every connected player in the room has marked themselves ready. */
  private everyoneReady(): boolean {
    if (!this.room.state.tutorial) return false
    const total = this.room.state.players.size
    if (total === 0) return false
    return this.room.state.tutorial.readyUserIds.size >= total
  }

  /** Snapshot used by reconnect payloads. */
  buildReadyPayload(): TutorialReadyPayload {
    const ids = this.room.state.tutorial
      ? Array.from(this.room.state.tutorial.readyUserIds)
      : []
    return {
      readyUserIds: ids,
      readyCount: ids.length,
      totalCount: this.room.state.players.size,
    }
  }

  private broadcastReady(): void {
    if (this.finished || !this.room.state.tutorial) return
    this.io.to(this.room.state.id).emit('tutorial:ready', this.buildReadyPayload())
  }

  /**
   * Force the tutorial to end early but still emit `tutorial:end` and run the
   * onComplete handoff. Used when the timer should be skipped (e.g. tests).
   * Idempotent.
   */
  forceEnd(): void {
    this.finish(/* runOnComplete */ true)
  }

  /**
   * Cancel the tutorial without emitting `tutorial:end` and without invoking
   * onComplete. Used when the room is being torn down (host quit, last player
   * left), where transitioning to active would be wrong. Idempotent.
   */
  cancel(): void {
    this.finish(/* runOnComplete */ false)
  }

  /**
   * Returns the current per-user checklist for tests / reconnect snapshots.
   */
  getCompletedMissions(userId: string): TutorialMissionId[] {
    const set = this.room.state.tutorial?.completedByUserId.get(userId)
    return set ? Array.from(set) : []
  }

  /**
   * Remaining seconds, clamped to [0, durationSeconds]. Exposed for reconnect
   * payloads and tests.
   */
  getRemainingSeconds(): number {
    if (!this.room.state.tutorial) return 0
    const remainingMs = this.room.state.tutorial.endsAt - this.now()
    return Math.max(0, Math.ceil(remainingMs / 1000))
  }

  /** Used by socket layer when a fresh client joins mid-tutorial. */
  buildStartPayloadForRole(role: PlayerRole): TutorialStartPayload {
    return {
      duration: this.durationSeconds,
      role,
      seed: this.seed,
      missions: [...getMissionsForRole(role)],
      players: Array.from(this.room.state.players.values()),
      npcAssignments: buildTutorialNpcAssignments(this.seed, this.durationSeconds),
    }
  }

  /** Used by socket layer when a fresh client joins mid-tutorial. */
  buildStatePayloadForUser(userId: string): TutorialStatePayload {
    return {
      remainingSeconds: this.getRemainingSeconds(),
      completedMissionIds: this.getCompletedMissions(userId),
    }
  }

  // ────────────────────────────────────────────────────────────────────────
  // internals
  // ────────────────────────────────────────────────────────────────────────

  private broadcastState(): void {
    if (this.finished) return
    for (const [_socketId, player] of this.room.state.players) {
      this.broadcastStateForPlayer(player)
    }
  }

  private broadcastStateForPlayer(player: PlayerState): void {
    const socketId = this.findSocketIdForUser(player.userId)
    if (!socketId) return
    const payload: TutorialStatePayload = {
      remainingSeconds: this.getRemainingSeconds(),
      completedMissionIds: this.getCompletedMissions(player.userId),
    }
    this.io.to(socketId).emit('tutorial:state', payload)
  }

  private findSocketIdForUser(userId: string): string | undefined {
    for (const [socketId, p] of this.room.state.players) {
      if (p.userId === userId) return socketId
    }
    return undefined
  }

  private finish(runOnComplete: boolean = true): void {
    if (this.finished) return
    this.finished = true

    if (this.tickInterval) {
      clearInterval(this.tickInterval)
      this.tickInterval = null
    }
    if (this.endTimeout) {
      clearTimeout(this.endTimeout)
      this.endTimeout = null
    }

    if (runOnComplete) {
      const endPayload: TutorialEndPayload = { nextScene: 'GameScene' }
      this.io.to(this.room.state.id).emit('tutorial:end', endPayload)
    }

    console.log(
      `[TUTORIAL] Room "${this.room.state.id}" tutorial ${runOnComplete ? 'ended' : 'cancelled'} after ${(this.now() - this.startedAt) / 1000}s`
    )

    if (!runOnComplete) return
    try {
      this.onComplete()
    } catch (err) {
      console.error(`[TUTORIAL-ERROR] onComplete threw: ${err}`)
    }
  }
}
