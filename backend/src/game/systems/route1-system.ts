/**
 * Route1System — server-authoritative mission state machine for Ruta 1 (Ventilation).
 *
 * Spec: design/gdd/ruta-1-ventilacion-industrial.md
 * Plan: design/gdd/ruta-1-implementation-plan.md (Phase D)
 *
 * Responsibilities:
 *  - Track active mission interactions (search clue, disable server, open vent, escape)
 *    keyed by a unique key per (player, action). Open-vent is collaborative — helpers
 *    join the initiator's entry.
 *  - Tick advances progress on every active interaction. Completion mutates Route1State
 *    and emits the appropriate cues.
 *  - Recompute mission checklist (find_cutters / find_clue / disable_server / find_wrench
 *    / open_vent / escape) from authoritative inventory + flags every change.
 *  - Broadcast `escape:route1:state` to prisoners only (correctServerId filtered),
 *    `world:state` to all, `world:cue` to guard.
 *
 * Network authority:
 *  - Distance / proximity is enforced client-side (Unity NetworkInteractable).
 *    The backend trusts that "this prisoner can interact with this object" and only
 *    validates inventory / mission gates / object-id sanity. This mirrors the
 *    pattern used by guard:catch and player:action in this codebase.
 */

import {
  EscapeRoute1StatePayload,
  GameRoomState,
  GuardDeskId,
  PlayerState,
  Route1ActiveInteraction,
  Route1MissionId,
  Route1MissionStatus,
  Route1State,
  ServerRackId,
  VentId,
  VentProgressEntry,
  WorldCuePayload,
  WorldStatePayload,
} from '../types.js'
import {
  GUARD_DESK_IDS,
  ROUTE1_CONFIG,
  SERVER_IDS,
  VENT_IDS,
} from '../routes/route1/state.js'
import {
  CRITICAL_ROUTE_ITEM_IDS,
  findPlayerWithItem,
  isCriticalRouteItem,
  playerHasItem,
} from './route-inventory.js'

// ─── Action contract ────────────────────────────────────────────────────────

export type Route1ActionKind =
  | 'route1.search_clue'
  | 'route1.disable_server'
  | 'route1.open_vent'
  | 'route1.escape'

export type Route1ActionResult =
  | { ok: true }
  | { ok: false; reason: string }

interface CompletionResult {
  worldStateChanged?: boolean
  cue?: WorldCuePayload
}

// ─── System ─────────────────────────────────────────────────────────────────

export class Route1System {
  /**
   * Emit the prisoner-only payload. Wired by room-manager to fan out to every
   * socket whose player.role === 'prisoner'.
   */
  onRoute1StateChanged?: (payload: EscapeRoute1StatePayload) => void

  /**
   * Emit the public world-state payload to every player in the room.
   */
  onWorldStateChanged?: (payload: WorldStatePayload) => void

  /**
   * Emit a single observable cue to the guard. Never carries the correct
   * server id — the guard infers from observation only.
   */
  onWorldCue?: (payload: WorldCuePayload) => void

  constructor(
    private readonly state: GameRoomState,
    private readonly config = ROUTE1_CONFIG
  ) {
    this.ensureVentProgress()
    // Mission checklist may need an initial recompute if items spawned
    // before the system was constructed.
    this.recomputeMissions()
  }

  // ─── Init helpers ─────────────────────────────────────────────────────────

  private ensureVentProgress(): void {
    const r1 = this.state.route1
    if (!r1) return
    for (const ventId of VENT_IDS) {
      if (r1.ventProgressById[ventId] === undefined) {
        r1.ventProgressById[ventId] = 0
      }
    }
  }

  private get r1(): Route1State {
    if (!this.state.route1) {
      throw new Error('[ROUTE1] route1 state missing — initializeRouteState must run first')
    }
    return this.state.route1
  }

  // ─── Public payload builders (used by reconnect emission) ─────────────────

  buildPrisonerStatePayload(): EscapeRoute1StatePayload {
    const r1 = this.r1
    const ventProgress: VentProgressEntry[] = Object.entries(r1.ventProgressById).map(
      ([ventId, progress]) => ({ ventId, progress })
    )
    return {
      routeId: 'route1_ventilation',
      missions: { ...r1.missions },
      clueFound: r1.clueFound,
      correctServerId: r1.clueFound ? r1.correctServerId : undefined,
      serverDisabled: r1.serverDisabled,
      ventProgress,
      openVentIds: [...r1.openVentIds],
      activeInteractions: Object.values(r1.activeInteractions).map((i) => ({ ...i })),
      escapingPlayerIds: [...r1.escapingPlayerIds],
      escapedPlayerIds: [...r1.escapedPlayerIds],
      updatedAt: r1.updatedAt,
    }
  }

  buildWorldStatePayload(): WorldStatePayload {
    const r1 = this.r1
    return {
      ventilationPowered: !r1.serverDisabled,
      openVentIds: [...r1.openVentIds],
    }
  }

  // ─── Player action entry points ───────────────────────────────────────────

  startSearchClue(player: PlayerState, deskId: string): Route1ActionResult {
    const guard = this.guardPrisonerAlive(player)
    if (!guard.ok) return guard
    if (!GUARD_DESK_IDS.includes(deskId as GuardDeskId)) {
      return fail(`Invalid desk "${deskId}"`)
    }
    if (this.r1.clueFound) {
      return fail('Clue already found')
    }
    this.replaceUserInteraction(player.userId, {
      objectId: deskId,
      action: 'route1.search_clue',
      startedByUserId: player.userId,
      helperUserIds: [],
      progress: 0,
    })
    this.commit()
    return { ok: true }
  }

  stopSearchClue(player: PlayerState, deskId?: string): Route1ActionResult {
    const removed = this.removeUserInteractionMatching(player.userId, (inter) =>
      inter.action === 'route1.search_clue' && (!deskId || inter.objectId === deskId)
    )
    if (removed) this.commit()
    return { ok: true }
  }

  startDisableServer(player: PlayerState, serverId: string): Route1ActionResult {
    const guard = this.guardPrisonerAlive(player)
    if (!guard.ok) return guard
    if (!SERVER_IDS.includes(serverId as ServerRackId)) {
      return fail(`Invalid server "${serverId}"`)
    }
    if (!playerHasItem(player, 'route1_cutters')) {
      return fail('route1_cutters required')
    }
    if (this.r1.serverDisabled) {
      return fail('Ventilation already disabled')
    }
    this.replaceUserInteraction(player.userId, {
      objectId: serverId,
      action: 'route1.disable_server',
      startedByUserId: player.userId,
      helperUserIds: [],
      progress: 0,
    })
    this.commit()
    return { ok: true }
  }

  stopDisableServer(player: PlayerState, serverId?: string): Route1ActionResult {
    const removed = this.removeUserInteractionMatching(player.userId, (inter) =>
      inter.action === 'route1.disable_server' && (!serverId || inter.objectId === serverId)
    )
    if (removed) this.commit()
    return { ok: true }
  }

  startOpenVent(player: PlayerState, ventId: string): Route1ActionResult {
    const guard = this.guardPrisonerAlive(player)
    if (!guard.ok) return guard
    if (!VENT_IDS.includes(ventId as VentId)) {
      return fail(`Invalid vent "${ventId}"`)
    }
    if (!this.r1.serverDisabled) {
      return fail('Ventilation must be disabled first')
    }
    if (this.r1.openVentIds.includes(ventId)) {
      return fail('Vent already open')
    }

    const r1 = this.r1
    const key = ventOpenKey(ventId)
    const existing = r1.activeInteractions[key]

    if (existing) {
      // Join as helper — only the initiator needs the wrench.
      if (existing.startedByUserId === player.userId) {
        // Re-issuing start while already initiator — no-op.
        return { ok: true }
      }
      if (existing.helperUserIds.includes(player.userId)) {
        return { ok: true }
      }
      // Drop any other interaction this user may have had so they only do one
      // thing at a time, but DO NOT touch the vent interaction we're joining.
      this.removeUserInteractionMatching(
        player.userId,
        (inter) => inter !== existing
      )
      existing.helperUserIds = [...existing.helperUserIds, player.userId]
      this.commit()
      return { ok: true }
    }

    // New interaction: initiator must have the wrench.
    if (!playerHasItem(player, 'route1_wrench')) {
      return fail('route1_wrench required to start opening vent')
    }
    this.replaceUserInteraction(player.userId, {
      objectId: ventId,
      action: 'route1.open_vent',
      startedByUserId: player.userId,
      helperUserIds: [],
      progress: r1.ventProgressById[ventId] ?? 0,
    }, key)
    this.commit()
    return { ok: true }
  }

  stopOpenVent(player: PlayerState, ventId?: string): Route1ActionResult {
    const r1 = this.r1
    let mutated = false
    for (const key of Object.keys(r1.activeInteractions)) {
      const inter = r1.activeInteractions[key]
      if (inter.action !== 'route1.open_vent') continue
      if (ventId && inter.objectId !== ventId) continue

      if (inter.startedByUserId === player.userId) {
        // Initiator stops → drop the whole entry; helpers can't continue alone
        // because the wrench-bearing initiator left. Persistent ventProgress is
        // preserved on r1.ventProgressById so a later restart resumes.
        delete r1.activeInteractions[key]
        mutated = true
      } else if (inter.helperUserIds.includes(player.userId)) {
        inter.helperUserIds = inter.helperUserIds.filter((u) => u !== player.userId)
        mutated = true
      }
    }
    if (mutated) this.commit()
    return { ok: true }
  }

  startEscape(player: PlayerState, ventId: string): Route1ActionResult {
    const guard = this.guardPrisonerAlive(player)
    if (!guard.ok) return guard
    if (!VENT_IDS.includes(ventId as VentId)) {
      return fail(`Invalid vent "${ventId}"`)
    }
    if (!this.r1.openVentIds.includes(ventId)) {
      return fail('Vent is not open yet')
    }
    if (this.r1.escapedPlayerIds.includes(player.userId)) {
      return fail('Already escaped')
    }
    this.replaceUserInteraction(player.userId, {
      objectId: ventId,
      action: 'route1.escape',
      startedByUserId: player.userId,
      helperUserIds: [],
      progress: 0,
    }, escapeKey(ventId, player.userId))

    if (!this.r1.escapingPlayerIds.includes(player.userId)) {
      this.r1.escapingPlayerIds.push(player.userId)
    }
    this.commit()
    return { ok: true }
  }

  stopEscape(player: PlayerState, ventId?: string): Route1ActionResult {
    const r1 = this.r1
    let mutated = false
    for (const key of Object.keys(r1.activeInteractions)) {
      const inter = r1.activeInteractions[key]
      if (inter.action !== 'route1.escape') continue
      if (inter.startedByUserId !== player.userId) continue
      if (ventId && inter.objectId !== ventId) continue
      delete r1.activeInteractions[key]
      mutated = true
    }
    if (r1.escapingPlayerIds.includes(player.userId)) {
      r1.escapingPlayerIds = r1.escapingPlayerIds.filter((u) => u !== player.userId)
      mutated = true
    }
    if (mutated) this.commit()
    return { ok: true }
  }

  // ─── External hooks ───────────────────────────────────────────────────────

  /**
   * Cancels every interaction owned (or helped) by `userId`. Called on capture
   * and on disconnect. Returns true if state actually changed.
   */
  cancelPlayerInteractions(userId: string): boolean {
    const r1 = this.state.route1
    if (!r1) return false

    let mutated = false
    for (const key of Object.keys(r1.activeInteractions)) {
      const inter = r1.activeInteractions[key]
      if (inter.startedByUserId === userId) {
        delete r1.activeInteractions[key]
        mutated = true
      } else if (inter.helperUserIds.includes(userId)) {
        inter.helperUserIds = inter.helperUserIds.filter((u) => u !== userId)
        mutated = true
      }
    }
    if (r1.escapingPlayerIds.includes(userId)) {
      r1.escapingPlayerIds = r1.escapingPlayerIds.filter((u) => u !== userId)
      mutated = true
    }
    if (mutated) this.commit()
    return mutated
  }

  /**
   * Hook fired by event handlers after item:state mutations (pickup/store/drop)
   * so the mission checklist (find_cutters / find_wrench / disable_server /
   * open_vent gates) can update without waiting for the next tick.
   */
  notifyInventoryChanged(): void {
    if (!this.state.route1) return
    const before = JSON.stringify(this.r1.missions)
    this.recomputeMissions()
    if (JSON.stringify(this.r1.missions) !== before) {
      this.r1.updatedAt = Date.now()
      this.broadcastPrisonerState()
    }
  }

  // ─── Tick ────────────────────────────────────────────────────────────────

  /**
   * Advances every active interaction by `dt` seconds. Completes interactions
   * that hit progress >= 1 and emits world cues / state. Recomputes missions
   * and broadcasts at most once per tick.
   */
  tick(dt: number): void {
    const r1 = this.state.route1
    if (!r1) return

    const completions: { inter: Route1ActiveInteraction; result: CompletionResult }[] = []
    let mutated = false

    for (const key of Object.keys(r1.activeInteractions)) {
      const inter = r1.activeInteractions[key]
      const totalSeconds = this.getDurationFor(inter)
      if (totalSeconds <= 0) {
        delete r1.activeInteractions[key]
        mutated = true
        continue
      }

      const rate = 1 / totalSeconds
      const next = Math.min(1, inter.progress + rate * dt)
      if (next === inter.progress) continue

      inter.progress = next
      mutated = true

      // Vents persist their progress across stops/restarts.
      if (inter.action === 'route1.open_vent') {
        r1.ventProgressById[inter.objectId] = next
      }

      if (next >= 1) {
        const result = this.completeInteraction(inter)
        delete r1.activeInteractions[key]
        completions.push({ inter, result })
      }
    }

    if (mutated || completions.length > 0) {
      this.recomputeMissions()
      r1.updatedAt = Date.now()
      this.broadcastPrisonerState()
    }

    let worldStateNeedsBroadcast = false
    const cues: WorldCuePayload[] = []
    for (const c of completions) {
      if (c.result.worldStateChanged) worldStateNeedsBroadcast = true
      if (c.result.cue) cues.push(c.result.cue)
    }
    if (worldStateNeedsBroadcast) this.broadcastWorldState()
    for (const cue of cues) this.onWorldCue?.(cue)
  }

  // ─── Internals ────────────────────────────────────────────────────────────

  private getDurationFor(inter: Route1ActiveInteraction): number {
    switch (inter.action) {
      case 'route1.search_clue':
        return this.config.clueSearchSeconds
      case 'route1.disable_server':
        return this.config.serverDisableSeconds
      case 'route1.open_vent': {
        // Initiator counts as 1, plus every helper currently attached.
        const participants = 1 + inter.helperUserIds.length
        return participants >= 2
          ? this.config.ventOpenSecondsCoop
          : this.config.ventOpenSecondsSingle
      }
      case 'route1.escape':
        return this.config.escapeSeconds
      default:
        return 0
    }
  }

  private completeInteraction(inter: Route1ActiveInteraction): CompletionResult {
    const r1 = this.r1
    const result: CompletionResult = {}

    switch (inter.action) {
      case 'route1.search_clue': {
        if (inter.objectId === r1.correctDeskId) {
          r1.clueFound = true
        }
        // Wrong desk simply finishes silently — prisoners learn nothing,
        // guard hears nothing. No cue intentionally; the clue is a quiet step.
        break
      }

      case 'route1.disable_server': {
        if (inter.objectId === r1.correctServerId) {
          r1.serverDisabled = true
          result.worldStateChanged = true
          result.cue = { cue: 'server_correct_power_off' }
        } else {
          if (!r1.wrongServerAttempts.includes(inter.objectId)) {
            r1.wrongServerAttempts.push(inter.objectId)
          }
          result.cue = { cue: 'server_wrong_alarm' }
        }
        break
      }

      case 'route1.open_vent': {
        if (!r1.openVentIds.includes(inter.objectId)) {
          r1.openVentIds.push(inter.objectId)
        }
        r1.ventProgressById[inter.objectId] = 1
        result.worldStateChanged = true
        result.cue = { cue: 'vent_opened' }
        break
      }

      case 'route1.escape': {
        if (!r1.escapedPlayerIds.includes(inter.startedByUserId)) {
          r1.escapedPlayerIds.push(inter.startedByUserId)
        }
        r1.escapingPlayerIds = r1.escapingPlayerIds.filter(
          (u) => u !== inter.startedByUserId
        )
        // Note: do NOT touch player.isAlive — escaped is a win-state, not a
        // catch-state. VictoryConditionSystem ends the game on escapedPlayerIds.length > 0.
        break
      }
    }

    return result
  }

  /**
   * Compute every mission status from authoritative inventory + flags.
   * Idempotent — safe to call as often as needed.
   */
  private recomputeMissions(): void {
    if (!this.state.route1) return
    const r1 = this.state.route1
    const m = r1.missions

    const cuttersHeld = !!findPlayerWithItem(this.state, 'route1_cutters')
    const wrenchHeld = !!findPlayerWithItem(this.state, 'route1_wrench')

    m.find_cutters = cuttersHeld ? 'complete' : 'available'
    m.find_wrench = wrenchHeld ? 'complete' : 'available'

    m.find_clue = r1.clueFound
      ? 'complete'
      : this.hasActionInProgress('route1.search_clue')
        ? 'in_progress'
        : 'available'

    m.disable_server = r1.serverDisabled
      ? 'complete'
      : this.hasActionInProgress('route1.disable_server')
        ? 'in_progress'
        : cuttersHeld
          ? 'available'
          : 'locked'

    m.open_vent = r1.openVentIds.length > 0
      ? 'complete'
      : this.hasActionInProgress('route1.open_vent')
        ? 'in_progress'
        : (r1.serverDisabled && wrenchHeld)
          ? 'available'
          : 'locked'

    m.escape = r1.escapedPlayerIds.length > 0
      ? 'complete'
      : this.hasActionInProgress('route1.escape')
        ? 'in_progress'
        : r1.openVentIds.length > 0
          ? 'available'
          : 'locked'
  }

  private hasActionInProgress(action: Route1ActionKind): boolean {
    if (!this.state.route1) return false
    for (const inter of Object.values(this.state.route1.activeInteractions)) {
      if (inter.action === action) return true
    }
    return false
  }

  /**
   * Replaces (or installs) `userId`'s active interaction. Helper interactions
   * are dropped from any other entries so a player can never be in two actions
   * simultaneously. The new entry is keyed by `customKey` if provided, else
   * `${action}:${userId}`.
   */
  private replaceUserInteraction(
    userId: string,
    inter: Route1ActiveInteraction,
    customKey?: string
  ): void {
    this.removeUserInteractionMatching(userId, () => true)
    const key = customKey ?? `${inter.action}:${userId}`
    this.r1.activeInteractions[key] = inter
  }

  private removeUserInteractionMatching(
    userId: string,
    predicate: (inter: Route1ActiveInteraction) => boolean
  ): boolean {
    const r1 = this.r1
    let mutated = false
    for (const key of Object.keys(r1.activeInteractions)) {
      const inter = r1.activeInteractions[key]
      if (!predicate(inter)) continue
      if (inter.startedByUserId === userId) {
        delete r1.activeInteractions[key]
        mutated = true
      } else if (inter.helperUserIds.includes(userId)) {
        inter.helperUserIds = inter.helperUserIds.filter((u) => u !== userId)
        mutated = true
      }
    }
    return mutated
  }

  private guardPrisonerAlive(player: PlayerState): Route1ActionResult {
    if (player.role !== 'prisoner') return fail('Only prisoners can run route1 actions')
    if (player.isAlive === false) return fail('Caught prisoners cannot interact')
    return { ok: true }
  }

  private commit(): void {
    this.recomputeMissions()
    this.r1.updatedAt = Date.now()
    this.broadcastPrisonerState()
  }

  private broadcastPrisonerState(): void {
    if (!this.onRoute1StateChanged) return
    this.onRoute1StateChanged(this.buildPrisonerStatePayload())
  }

  private broadcastWorldState(): void {
    if (!this.onWorldStateChanged) return
    this.onWorldStateChanged(this.buildWorldStatePayload())
  }
}

// ─── Module helpers ─────────────────────────────────────────────────────────

function fail(reason: string): Route1ActionResult {
  return { ok: false, reason }
}

function ventOpenKey(ventId: string): string {
  return `route1.open_vent:${ventId}`
}

function escapeKey(ventId: string, userId: string): string {
  return `route1.escape:${ventId}:${userId}`
}

// Re-exported so test/debug code can probe known critical items without a
// second import on the route-inventory module.
export { CRITICAL_ROUTE_ITEM_IDS, isCriticalRouteItem }
