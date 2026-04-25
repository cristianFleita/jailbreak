/**
 * Route1System tests (Phase D).
 *
 * Covers the server-authoritative mission state machine for Ruta 1:
 *   - clue search (correct vs wrong desk)
 *   - server sabotage gate (cutters required, wrong → alarm, correct → power off)
 *   - vent open (gate, solo 25s, coop 12s, helpers don't need wrench, progress persists)
 *   - escape (gate, 5s, cancellation on capture)
 *   - mission checklist recompute from inventory + flags
 *   - prisoner-only payload omits correctServerId until clue is found
 *   - cancelPlayerInteractions cleans up escapingPlayerIds and active entries
 *   - victory system ends the game on first escape
 */

import { describe, it, expect, beforeEach } from 'vitest'
import { createGameRoomState } from '../state.js'
import { defaultGameConfig } from '../room-manager.js'
import { initializeRouteState } from '../routes/route-registry.js'
import { ROUTE1_CONFIG } from '../routes/route1/state.js'
import { Route1System } from '../systems/route1-system.js'
import {
  ensureItemState,
  pickupToHand,
  storeHeldItem,
} from '../systems/route-inventory.js'
import {
  EscapeRoute1StatePayload,
  GameRoomState,
  GuardDeskId,
  PlayerState,
  ServerRackId,
  VentId,
  WorldCuePayload,
  WorldStatePayload,
} from '../types.js'

// ─── Test helpers ───────────────────────────────────────────────────────────

interface TestRig {
  state: GameRoomState
  system: Route1System
  prisonerA: PlayerState
  prisonerB: PlayerState
  guard: PlayerState
  emitted: {
    route1State: EscapeRoute1StatePayload[]
    worldState: WorldStatePayload[]
    cues: WorldCuePayload[]
  }
}

function makePlayer(
  state: GameRoomState,
  socketId: string,
  userId: string,
  role: PlayerState['role']
): PlayerState {
  const player: PlayerState = {
    id: userId,
    userId,
    role,
    position: { x: 0, y: 0, z: 0 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    velocity: { x: 0, y: 0, z: 0 },
    movementState: 'idle',
    isAlive: true,
    carrying: null,
    heldItemId: null,
    inventorySlots: role === 'prisoner' ? [null, null] : [],
  }
  state.players.set(socketId, player)
  state.playersByUserId.set(userId, player)
  return player
}

function buildRig(): TestRig {
  const state = createGameRoomState('room-route1', 'host', defaultGameConfig)
  initializeRouteState(state)
  // Pin the random desk/server choices so tests are deterministic.
  state.route1!.correctDeskId = 'guard_desk_2'
  state.route1!.correctServerId = 'server_8'

  const prisonerA = makePlayer(state, 'sockA', 'userA', 'prisoner')
  const prisonerB = makePlayer(state, 'sockB', 'userB', 'prisoner')
  const guard = makePlayer(state, 'sockG', 'userG', 'guard')

  // Items already exist via initializeRouteState, but ensure positions.
  ensureItemState(state, 'route1_cutters', 'route_tool')
  ensureItemState(state, 'route1_wrench', 'route_tool')

  const system = new Route1System(state)

  const emitted = {
    route1State: [] as EscapeRoute1StatePayload[],
    worldState: [] as WorldStatePayload[],
    cues: [] as WorldCuePayload[],
  }
  system.onRoute1StateChanged = (p) => emitted.route1State.push(p)
  system.onWorldStateChanged = (p) => emitted.worldState.push(p)
  system.onWorldCue = (p) => emitted.cues.push(p)

  return { state, system, prisonerA, prisonerB, guard, emitted }
}

function giveItem(state: GameRoomState, player: PlayerState, itemId: 'route1_cutters' | 'route1_wrench'): void {
  const result = pickupToHand(state, player, itemId)
  if (!result.success) throw new Error(`pickup failed: ${result.reason}`)
}

/** Advance the system tick-by-tick so progress crosses each interaction's
 * completion threshold smoothly (matches game-loop dt=50ms). */
function advance(system: Route1System, totalSeconds: number, dt = 0.05): void {
  const ticks = Math.ceil(totalSeconds / dt)
  for (let i = 0; i < ticks; i++) system.tick(dt)
}

// ─── Tests ──────────────────────────────────────────────────────────────────

describe('Route1System (Phase D)', () => {
  let rig: TestRig
  beforeEach(() => {
    rig = buildRig()
  })

  // ── Initialization ──────────────────────────────────────────────────────

  describe('initialization', () => {
    it('seeds vent progress for all canonical vent IDs', () => {
      const ventProgress = rig.state.route1!.ventProgressById
      expect(ventProgress['vent_1']).toBe(0)
      expect(ventProgress['vent_2']).toBe(0)
      expect(ventProgress['vent_3']).toBe(0)
    })

    it('builds prisoner payload without correctServerId before clueFound', () => {
      const payload = rig.system.buildPrisonerStatePayload()
      expect(payload.clueFound).toBe(false)
      expect(payload.correctServerId).toBeUndefined()
    })

    it('initial world state has ventilation powered and no open vents', () => {
      const w = rig.system.buildWorldStatePayload()
      expect(w.ventilationPowered).toBe(true)
      expect(w.openVentIds).toEqual([])
    })
  })

  // ── Clue search ─────────────────────────────────────────────────────────

  describe('search_clue', () => {
    it('rejects invalid desk IDs', () => {
      const r = rig.system.startSearchClue(rig.prisonerA, 'guard_desk_99')
      expect(r.ok).toBe(false)
    })

    it('rejects guards', () => {
      const r = rig.system.startSearchClue(rig.guard, 'guard_desk_1')
      expect(r.ok).toBe(false)
    })

    it('completes after clue_search_seconds and reveals correct desk', () => {
      rig.system.startSearchClue(rig.prisonerA, rig.state.route1!.correctDeskId)
      advance(rig.system, ROUTE1_CONFIG.clueSearchSeconds + 0.1)

      expect(rig.state.route1!.clueFound).toBe(true)
      expect(rig.state.route1!.missions.find_clue).toBe('complete')
      // Last broadcast contains correctServerId now that clue is known.
      const last = rig.emitted.route1State.at(-1)!
      expect(last.clueFound).toBe(true)
      expect(last.correctServerId).toBe('server_8')
    })

    it('completes silently on a wrong desk and never sets clueFound', () => {
      const wrongDesk: GuardDeskId =
        rig.state.route1!.correctDeskId === 'guard_desk_1' ? 'guard_desk_3' : 'guard_desk_1'
      rig.system.startSearchClue(rig.prisonerA, wrongDesk)
      advance(rig.system, ROUTE1_CONFIG.clueSearchSeconds + 0.1)

      expect(rig.state.route1!.clueFound).toBe(false)
      // No world cue should fire from a wrong desk — search is silent.
      expect(rig.emitted.cues).toEqual([])
    })

    it('stop removes the active interaction without completing', () => {
      rig.system.startSearchClue(rig.prisonerA, rig.state.route1!.correctDeskId)
      advance(rig.system, ROUTE1_CONFIG.clueSearchSeconds * 0.4)
      rig.system.stopSearchClue(rig.prisonerA)
      advance(rig.system, ROUTE1_CONFIG.clueSearchSeconds + 1)

      expect(rig.state.route1!.clueFound).toBe(false)
      expect(Object.keys(rig.state.route1!.activeInteractions)).toHaveLength(0)
    })
  })

  // ── Server sabotage ─────────────────────────────────────────────────────

  describe('disable_server', () => {
    it('rejects when prisoner has no cutters', () => {
      const r = rig.system.startDisableServer(rig.prisonerA, 'server_8')
      expect(r.ok).toBe(false)
      expect(r.ok ? '' : r.reason).toMatch(/cutters/)
    })

    it('rejects invalid server IDs', () => {
      giveItem(rig.state, rig.prisonerA, 'route1_cutters')
      const r = rig.system.startDisableServer(rig.prisonerA, 'server_99')
      expect(r.ok).toBe(false)
    })

    it('correct server disables ventilation, emits power_off cue and world state', () => {
      giveItem(rig.state, rig.prisonerA, 'route1_cutters')
      rig.system.startDisableServer(rig.prisonerA, 'server_8')
      advance(rig.system, ROUTE1_CONFIG.serverDisableSeconds + 0.1)

      expect(rig.state.route1!.serverDisabled).toBe(true)
      expect(rig.emitted.cues.some((c) => c.cue === 'server_correct_power_off')).toBe(true)
      // World state must have updated to ventilationPowered=false.
      const lastWorld = rig.emitted.worldState.at(-1)!
      expect(lastWorld.ventilationPowered).toBe(false)
    })

    it('wrong server emits alarm cue, leaves route open for retry', () => {
      giveItem(rig.state, rig.prisonerA, 'route1_cutters')
      const wrong: ServerRackId =
        rig.state.route1!.correctServerId === 'server_1' ? 'server_2' : 'server_1'
      rig.system.startDisableServer(rig.prisonerA, wrong)
      advance(rig.system, ROUTE1_CONFIG.serverDisableSeconds + 0.1)

      expect(rig.state.route1!.serverDisabled).toBe(false)
      expect(rig.state.route1!.wrongServerAttempts).toContain(wrong)
      expect(rig.emitted.cues.some((c) => c.cue === 'server_wrong_alarm')).toBe(true)

      // Retry on correct server should still succeed.
      const retry = rig.system.startDisableServer(rig.prisonerA, rig.state.route1!.correctServerId)
      expect(retry.ok).toBe(true)
    })

    it('cutters in stored slot count as held for the gate', () => {
      giveItem(rig.state, rig.prisonerA, 'route1_cutters')
      storeHeldItem(rig.state, rig.prisonerA)
      expect(rig.prisonerA.heldItemId).toBeNull()
      const r = rig.system.startDisableServer(rig.prisonerA, 'server_8')
      expect(r.ok).toBe(true)
    })

    it('rejects sabotage once ventilation is already disabled', () => {
      giveItem(rig.state, rig.prisonerA, 'route1_cutters')
      rig.state.route1!.serverDisabled = true
      const r = rig.system.startDisableServer(rig.prisonerA, 'server_8')
      expect(r.ok).toBe(false)
    })
  })

  // ── Vent open ───────────────────────────────────────────────────────────

  describe('open_vent', () => {
    function setupServerDisabled(): void {
      rig.state.route1!.serverDisabled = true
    }

    it('rejects when server is not disabled', () => {
      giveItem(rig.state, rig.prisonerA, 'route1_wrench')
      const r = rig.system.startOpenVent(rig.prisonerA, 'vent_1')
      expect(r.ok).toBe(false)
    })

    it('rejects when initiator has no wrench', () => {
      setupServerDisabled()
      const r = rig.system.startOpenVent(rig.prisonerA, 'vent_1')
      expect(r.ok).toBe(false)
    })

    it('completes solo open in vent_open_seconds_single', () => {
      setupServerDisabled()
      giveItem(rig.state, rig.prisonerA, 'route1_wrench')
      rig.system.startOpenVent(rig.prisonerA, 'vent_1')

      // Just before completion: should not be open yet.
      advance(rig.system, ROUTE1_CONFIG.ventOpenSecondsSingle - 1)
      expect(rig.state.route1!.openVentIds).not.toContain('vent_1')

      advance(rig.system, 1.5)
      expect(rig.state.route1!.openVentIds).toContain('vent_1')
      expect(rig.emitted.cues.some((c) => c.cue === 'vent_opened')).toBe(true)
    })

    it('coop with two prisoners completes in vent_open_seconds_coop', () => {
      setupServerDisabled()
      giveItem(rig.state, rig.prisonerA, 'route1_wrench')
      rig.system.startOpenVent(rig.prisonerA, 'vent_1')
      const helper = rig.system.startOpenVent(rig.prisonerB, 'vent_1')
      expect(helper.ok).toBe(true)

      advance(rig.system, ROUTE1_CONFIG.ventOpenSecondsCoop + 0.1)
      expect(rig.state.route1!.openVentIds).toContain('vent_1')

      // Sanity: must complete strictly faster than the solo duration.
      // (12s vs 25s — by the 13s mark, vent must be open.)
      const rig2 = buildRig()
      rig2.state.route1!.serverDisabled = true
      giveItem(rig2.state, rig2.prisonerA, 'route1_wrench')
      rig2.system.startOpenVent(rig2.prisonerA, 'vent_1')
      rig2.system.startOpenVent(rig2.prisonerB, 'vent_1')
      advance(rig2.system, ROUTE1_CONFIG.ventOpenSecondsCoop + 0.5)
      expect(rig2.state.route1!.openVentIds).toContain('vent_1')
    })

    it('helper does not need a wrench', () => {
      setupServerDisabled()
      giveItem(rig.state, rig.prisonerA, 'route1_wrench')
      rig.system.startOpenVent(rig.prisonerA, 'vent_1')

      // Helper never picks up a wrench — must still join successfully.
      const helper = rig.system.startOpenVent(rig.prisonerB, 'vent_1')
      expect(helper.ok).toBe(true)

      const inter = Object.values(rig.state.route1!.activeInteractions).find(
        (i) => i.action === 'route1.open_vent'
      )!
      expect(inter.helperUserIds).toContain(rig.prisonerB.userId)
    })

    it('vent progress persists across stop and resumes on restart', () => {
      setupServerDisabled()
      giveItem(rig.state, rig.prisonerA, 'route1_wrench')
      rig.system.startOpenVent(rig.prisonerA, 'vent_2')
      advance(rig.system, ROUTE1_CONFIG.ventOpenSecondsSingle * 0.5)
      const partial = rig.state.route1!.ventProgressById['vent_2']
      expect(partial).toBeGreaterThan(0.4)
      expect(partial).toBeLessThan(0.6)

      rig.system.stopOpenVent(rig.prisonerA, 'vent_2')
      // Persistent progress must survive the stop.
      expect(rig.state.route1!.ventProgressById['vent_2']).toBeCloseTo(partial, 5)
      expect(Object.values(rig.state.route1!.activeInteractions)).toHaveLength(0)

      // Restart and check the new interaction picks up where we left off.
      rig.system.startOpenVent(rig.prisonerA, 'vent_2')
      const inter = Object.values(rig.state.route1!.activeInteractions).find(
        (i) => i.action === 'route1.open_vent'
      )!
      expect(inter.progress).toBeCloseTo(partial, 5)
    })

    it('helper.stop removes only the helper, leaving the interaction running', () => {
      setupServerDisabled()
      giveItem(rig.state, rig.prisonerA, 'route1_wrench')
      rig.system.startOpenVent(rig.prisonerA, 'vent_1')
      rig.system.startOpenVent(rig.prisonerB, 'vent_1')

      rig.system.stopOpenVent(rig.prisonerB, 'vent_1')
      const inter = Object.values(rig.state.route1!.activeInteractions).find(
        (i) => i.action === 'route1.open_vent'
      )!
      expect(inter.startedByUserId).toBe(rig.prisonerA.userId)
      expect(inter.helperUserIds).not.toContain(rig.prisonerB.userId)
    })

    it('rejects opening an already-open vent', () => {
      setupServerDisabled()
      rig.state.route1!.openVentIds.push('vent_1')
      giveItem(rig.state, rig.prisonerA, 'route1_wrench')
      const r = rig.system.startOpenVent(rig.prisonerA, 'vent_1')
      expect(r.ok).toBe(false)
    })
  })

  // ── Escape ──────────────────────────────────────────────────────────────

  describe('escape', () => {
    function openVent(ventId: VentId = 'vent_1'): void {
      rig.state.route1!.serverDisabled = true
      rig.state.route1!.openVentIds.push(ventId)
      rig.state.route1!.ventProgressById[ventId] = 1
    }

    it('rejects when no vent is open', () => {
      const r = rig.system.startEscape(rig.prisonerA, 'vent_1')
      expect(r.ok).toBe(false)
    })

    it('completes after escape_seconds and adds player to escapedPlayerIds', () => {
      openVent('vent_2')
      rig.system.startEscape(rig.prisonerA, 'vent_2')
      expect(rig.state.route1!.escapingPlayerIds).toContain(rig.prisonerA.userId)

      advance(rig.system, ROUTE1_CONFIG.escapeSeconds + 0.1)
      expect(rig.state.route1!.escapedPlayerIds).toContain(rig.prisonerA.userId)
      expect(rig.state.route1!.escapingPlayerIds).not.toContain(rig.prisonerA.userId)
      // Escaped is a win-state, not a catch-state — isAlive must remain true.
      expect(rig.prisonerA.isAlive).toBe(true)
    })

    it('cancelPlayerInteractions cancels in-flight escape (capture during 5s window)', () => {
      openVent('vent_1')
      rig.system.startEscape(rig.prisonerA, 'vent_1')
      advance(rig.system, ROUTE1_CONFIG.escapeSeconds * 0.5)

      rig.system.cancelPlayerInteractions(rig.prisonerA.userId)
      advance(rig.system, ROUTE1_CONFIG.escapeSeconds + 1)

      expect(rig.state.route1!.escapedPlayerIds).not.toContain(rig.prisonerA.userId)
      expect(rig.state.route1!.escapingPlayerIds).not.toContain(rig.prisonerA.userId)
    })

    it('two prisoners can escape via the same vent in parallel', () => {
      openVent('vent_3')
      rig.system.startEscape(rig.prisonerA, 'vent_3')
      rig.system.startEscape(rig.prisonerB, 'vent_3')
      advance(rig.system, ROUTE1_CONFIG.escapeSeconds + 0.1)

      expect(rig.state.route1!.escapedPlayerIds).toContain(rig.prisonerA.userId)
      expect(rig.state.route1!.escapedPlayerIds).toContain(rig.prisonerB.userId)
    })
  })

  // ── Mission checklist ───────────────────────────────────────────────────

  describe('mission checklist', () => {
    it('find_cutters flips to complete on pickup, back to available on drop', () => {
      expect(rig.state.route1!.missions.find_cutters).toBe('available')
      giveItem(rig.state, rig.prisonerA, 'route1_cutters')
      rig.system.notifyInventoryChanged()
      expect(rig.state.route1!.missions.find_cutters).toBe('complete')

      // Simulate drop on capture: clear the player's hand, then notify.
      rig.prisonerA.heldItemId = null
      rig.system.notifyInventoryChanged()
      expect(rig.state.route1!.missions.find_cutters).toBe('available')
    })

    it('disable_server is locked until cutters are held, then available', () => {
      expect(rig.state.route1!.missions.disable_server).toBe('locked')
      giveItem(rig.state, rig.prisonerA, 'route1_cutters')
      rig.system.notifyInventoryChanged()
      expect(rig.state.route1!.missions.disable_server).toBe('available')
    })

    it('open_vent is locked until both serverDisabled and a wrench is held', () => {
      expect(rig.state.route1!.missions.open_vent).toBe('locked')
      rig.state.route1!.serverDisabled = true
      rig.system.notifyInventoryChanged()
      expect(rig.state.route1!.missions.open_vent).toBe('locked')

      giveItem(rig.state, rig.prisonerA, 'route1_wrench')
      rig.system.notifyInventoryChanged()
      expect(rig.state.route1!.missions.open_vent).toBe('available')
    })

    it('escape unlocks once any vent is open', () => {
      expect(rig.state.route1!.missions.escape).toBe('locked')
      rig.state.route1!.openVentIds.push('vent_1')
      rig.system.notifyInventoryChanged()
      expect(rig.state.route1!.missions.escape).toBe('available')
    })

    it('in_progress reflects active interactions', () => {
      rig.system.startSearchClue(rig.prisonerA, 'guard_desk_2')
      expect(rig.state.route1!.missions.find_clue).toBe('in_progress')
    })
  })

  // ── Concurrency / anti-griefing ─────────────────────────────────────────

  describe('concurrency guards', () => {
    it('starting a second action replaces the prior one for the same player', () => {
      giveItem(rig.state, rig.prisonerA, 'route1_cutters')
      rig.system.startSearchClue(rig.prisonerA, 'guard_desk_1')
      rig.system.startDisableServer(rig.prisonerA, 'server_8')

      const interactions = Object.values(rig.state.route1!.activeInteractions)
      expect(interactions).toHaveLength(1)
      expect(interactions[0].action).toBe('route1.disable_server')
    })

    it('cancelPlayerInteractions removes interactions and escaping flag', () => {
      rig.state.route1!.serverDisabled = true
      rig.state.route1!.openVentIds.push('vent_1')
      rig.system.startEscape(rig.prisonerA, 'vent_1')
      expect(rig.state.route1!.escapingPlayerIds).toContain(rig.prisonerA.userId)

      const changed = rig.system.cancelPlayerInteractions(rig.prisonerA.userId)
      expect(changed).toBe(true)
      expect(rig.state.route1!.escapingPlayerIds).not.toContain(rig.prisonerA.userId)
      expect(Object.values(rig.state.route1!.activeInteractions)).toHaveLength(0)
    })

    it('emits a state broadcast on every commit', () => {
      const before = rig.emitted.route1State.length
      giveItem(rig.state, rig.prisonerA, 'route1_cutters')
      rig.system.startDisableServer(rig.prisonerA, 'server_8')
      expect(rig.emitted.route1State.length).toBeGreaterThan(before)
    })
  })

  // ── Caught prisoners cannot interact ────────────────────────────────────

  describe('caught prisoner gate', () => {
    it('rejects all start actions when isAlive is false', () => {
      rig.prisonerA.isAlive = false
      const r1 = rig.system.startSearchClue(rig.prisonerA, 'guard_desk_1')
      const r2 = rig.system.startDisableServer(rig.prisonerA, 'server_8')
      expect(r1.ok).toBe(false)
      expect(r2.ok).toBe(false)
    })
  })
})

// ─── Victory integration ────────────────────────────────────────────────────

describe('VictoryConditionSystem + Route1 escape', () => {
  it('first escape ends the game with prisoners winning (reason=escape_route)', async () => {
    // Build a full GameManager so victory.checkVictoryConditions runs against
    // the same state route1 mutates.
    const { GameManager } = await import('../systems/game-manager.js')

    const state = createGameRoomState('room-victory', 'host', defaultGameConfig)
    initializeRouteState(state)
    state.status = 'active'
    state.route1!.correctDeskId = 'guard_desk_1'
    state.route1!.correctServerId = 'server_1'

    makePlayer(state, 'sockA', 'userA', 'prisoner')
    makePlayer(state, 'sockG', 'userG', 'guard')

    const gm = new GameManager({ state, config: defaultGameConfig } as any)

    // No escape yet → no end.
    expect(gm.victory.checkVictoryConditions().winner).toBeNull()

    // Mark the prisoner escaped directly and re-check.
    state.route1!.escapedPlayerIds.push('userA')
    const result = gm.victory.checkVictoryConditions()
    expect(result.winner).toBe('prisoners')
    expect(result.reason).toBe('escape_route')
  })
})
