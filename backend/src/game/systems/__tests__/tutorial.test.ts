/**
 * Tutorial — Fase A backend tests.
 * Covers TutorialManager unit behavior (state, missions, cleanup) and the
 * room-manager integration (lobby → tutorial → active hand-off).
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { Server } from 'socket.io'
import { createServer } from 'http'
import {
  createRoom,
  destroyRoom,
  getRoom,
  getTutorialManager,
  stopGameLoop,
  transitionToTutorial,
} from '../../room-manager.js'
import { addPlayer, assignRandomRoles } from '../../state.js'
import {
  GUARD_MISSION_IDS,
  PRISONER_MISSION_IDS,
  TUTORIAL_NPC_TEMPLATE_IDS,
  TutorialManager,
  buildTutorialNpcAssignments,
  buildTutorialState,
  cleanupTutorialBeforeActive,
  getMissionsForRole,
  isMissionAllowedForRole,
} from '../tutorial.js'
import type {
  GameRoom,
  TutorialEndPayload,
  TutorialMissionId,
  TutorialStartPayload,
  TutorialStatePayload,
} from '../../types.js'

// ─────────────────────────────────────────────────────────────────────────
// Test helpers
// ─────────────────────────────────────────────────────────────────────────

interface CapturedEmit {
  socketId: string
  event: string
  payload: any
}

/**
 * Builds a Socket.io server stub that records every `io.to(target).emit(...)`
 * call. We use this instead of a real network so tests are deterministic and
 * don't need port allocation. The shape mirrors what the production code
 * actually invokes.
 */
function createIoStub(): { io: Server; emits: CapturedEmit[]; emitsFor(target: string): CapturedEmit[] } {
  const emits: CapturedEmit[] = []
  const io = {
    to(target: string) {
      return {
        emit(event: string, payload: any) {
          emits.push({ socketId: target, event, payload })
        },
      }
    },
  } as unknown as Server

  return {
    io,
    emits,
    emitsFor(target: string) {
      return emits.filter(e => e.socketId === target)
    },
  }
}

const HOST = 'host-user-1'
const P1 = 'user-prisoner-1'
const P2 = 'user-prisoner-2'

const ROOM_NAMES = [
  'tut-room-basic',
  'tut-room-flow',
  'tut-room-missions',
  'tut-room-cleanup',
  'tut-room-cancel',
  'tut-room-end',
  'tut-room-room-mgr',
  'tut-room-mid-disc',
  'tut-room-active-from-tutorial',
]

function cleanupAllRooms() {
  for (const name of ROOM_NAMES) {
    const room = getRoom(name)
    if (room) {
      stopGameLoop(room)
      destroyRoom(name)
    }
  }
}

function setupRoomWithPlayers(roomId: string): GameRoom {
  const room = createRoom(roomId, HOST)!
  addPlayer(room.state, 'sock_host', HOST, { x: 0, y: 1.5, z: 0 })
  addPlayer(room.state, 'sock_p1', P1, { x: 1, y: 1.5, z: 1 })
  addPlayer(room.state, 'sock_p2', P2, { x: 2, y: 1.5, z: 2 })
  // Force deterministic roles for tests instead of random — we pin one guard
  // (host) and two prisoners so we can assert role-gated logic.
  room.state.players.get('sock_host')!.role = 'guard'
  room.state.players.get('sock_host')!.inventorySlots = []
  room.state.players.get('sock_p1')!.role = 'prisoner'
  room.state.players.get('sock_p1')!.inventorySlots = [null, null]
  room.state.players.get('sock_p2')!.role = 'prisoner'
  room.state.players.get('sock_p2')!.inventorySlots = [null, null]
  return room
}

// ─────────────────────────────────────────────────────────────────────────
// Pure helpers
// ─────────────────────────────────────────────────────────────────────────

describe('Tutorial — pure helpers', () => {
  it('exposes mission lists keyed by role', () => {
    expect(PRISONER_MISSION_IDS).toContain('p1_review_route')
    expect(PRISONER_MISSION_IDS).toContain('p7_hide_cart')
    expect(GUARD_MISSION_IDS).toContain('g1_observe')
    expect(GUARD_MISSION_IDS).toContain('g4_error')
  })

  it('getMissionsForRole returns the correct set per role', () => {
    expect(getMissionsForRole('prisoner')).toEqual(PRISONER_MISSION_IDS)
    expect(getMissionsForRole('guard')).toEqual(GUARD_MISSION_IDS)
  })

  it('isMissionAllowedForRole rejects cross-role missions', () => {
    expect(isMissionAllowedForRole('prisoner', 'p1_review_route')).toBe(true)
    expect(isMissionAllowedForRole('prisoner', 'g1_observe')).toBe(false)
    expect(isMissionAllowedForRole('guard', 'g3_capture')).toBe(true)
    expect(isMissionAllowedForRole('guard', 'p4_store_item')).toBe(false)
  })

  it('buildTutorialState computes endsAt from now+duration', () => {
    const state = buildTutorialState(10_000, 60, 42)
    expect(state.startedAt).toBe(10_000)
    expect(state.endsAt).toBe(70_000)
    expect(state.durationSeconds).toBe(60)
    expect(state.seed).toBe(42)
    expect(state.completedByUserId.size).toBe(0)
  })

  it('buildTutorialNpcAssignments returns the two capture-practice targets in fixed order', () => {
    const a = buildTutorialNpcAssignments(42, 60)
    const b = buildTutorialNpcAssignments(99, 60)
    // Two NPCs, fixed actionId order, fixed npcIds — seed should NOT change it
    // since the guard's capture practice needs the same setup every match.
    expect(a).toEqual(b)
    expect(a).toHaveLength(2)
    expect(a.map(x => x.actionId)).toEqual([...TUTORIAL_NPC_TEMPLATE_IDS])
    expect(a.map(x => x.npcId)).toEqual(['tutorial_npc_01', 'tutorial_npc_02'])
    for (const entry of a) {
      expect(entry.loop).toBe(true)
      expect(entry.duration).toBe(60)
    }
  })

  it('cleanupTutorialBeforeActive zeroes inventory and drops tutorial state', () => {
    const room = setupRoomWithPlayers('tut-room-cleanup')
    const prisoner = room.state.players.get('sock_p1')!
    prisoner.heldItemId = 'route1_cutters_a'
    prisoner.carrying = 'food_plate'
    prisoner.inventorySlots = [{ itemId: 'route1_cutters_a', itemType: 'route_tool' }, null]
    room.state.tutorial = buildTutorialState(0, 60, 1)

    cleanupTutorialBeforeActive(room.state)

    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.carrying).toBeNull()
    expect(prisoner.inventorySlots).toEqual([null, null])
    expect(room.state.players.get('sock_host')!.inventorySlots).toEqual([])
    expect(room.state.tutorial).toBeUndefined()
  })
})

// ─────────────────────────────────────────────────────────────────────────
// TutorialManager — start / state / mission flow
// ─────────────────────────────────────────────────────────────────────────

describe('TutorialManager — start + emits', () => {
  let room: GameRoom
  let ioStub: ReturnType<typeof createIoStub>

  beforeEach(() => {
    vi.useFakeTimers()
    cleanupAllRooms()
    room = setupRoomWithPlayers('tut-room-basic')
    ioStub = createIoStub()
  })

  afterEach(() => {
    vi.useRealTimers()
    cleanupAllRooms()
  })

  it('emits tutorial:start to every player with their own role + missions', () => {
    const onComplete = vi.fn()
    const manager = new TutorialManager(ioStub.io, room, {
      durationSeconds: 60,
      seed: 12345,
      now: () => 1000,
      onComplete,
    })

    manager.start()

    expect(room.state.status).toBe('tutorial')
    expect(room.state.tutorial).toBeDefined()
    expect(room.state.tutorial!.endsAt).toBe(1000 + 60_000)

    const startEmits = ioStub.emits.filter(e => e.event === 'tutorial:start')
    expect(startEmits).toHaveLength(3)

    const guardEmit = startEmits.find(e => e.socketId === 'sock_host')!
    const guardPayload = guardEmit.payload as TutorialStartPayload
    expect(guardPayload.role).toBe('guard')
    expect(guardPayload.duration).toBe(60)
    expect(guardPayload.seed).toBe(12345)
    expect(guardPayload.missions).toEqual([...GUARD_MISSION_IDS])
    expect(guardPayload.players).toHaveLength(3)

    const prisonerEmit = startEmits.find(e => e.socketId === 'sock_p1')!
    const prisonerPayload = prisonerEmit.payload as TutorialStartPayload
    expect(prisonerPayload.role).toBe('prisoner')
    expect(prisonerPayload.missions).toEqual([...PRISONER_MISSION_IDS])
    expect(prisonerPayload.players.map(p => p.userId)).toEqual([HOST, P1, P2])

    // Every player receives the same authoritative NPC assignment list — two
    // capture-practice targets in a fixed order (innocent first, suspicious
    // second) so the guard's training is predictable.
    expect(prisonerPayload.npcAssignments).toHaveLength(2)
    expect(guardPayload.npcAssignments).toEqual(prisonerPayload.npcAssignments)
    expect(prisonerPayload.npcAssignments.map(a => a.actionId)).toEqual([...TUTORIAL_NPC_TEMPLATE_IDS])
  })

  it('refuses to start from a non-lobby status', () => {
    room.state.status = 'active'
    const onComplete = vi.fn()
    const manager = new TutorialManager(ioStub.io, room, { onComplete })
    manager.start()

    expect(room.state.status).toBe('active') // unchanged
    expect(ioStub.emits.filter(e => e.event === 'tutorial:start')).toHaveLength(0)
  })

  it('emits tutorial:state once per second per player with remaining time', () => {
    const nowFn = vi.fn(() => 0)
    const onComplete = vi.fn()
    const manager = new TutorialManager(ioStub.io, room, {
      durationSeconds: 60,
      tickIntervalMs: 1000,
      seed: 1,
      now: nowFn,
      onComplete,
    })

    manager.start()
    ioStub.emits.length = 0 // drop initial tutorial:start emits

    nowFn.mockReturnValue(1000)
    vi.advanceTimersByTime(1000)

    const tickEmits = ioStub.emits.filter(e => e.event === 'tutorial:state')
    expect(tickEmits).toHaveLength(3)
    for (const e of tickEmits) {
      const payload = e.payload as TutorialStatePayload
      expect(payload.remainingSeconds).toBe(59)
      expect(payload.completedMissionIds).toEqual([])
    }

    nowFn.mockReturnValue(5000)
    vi.advanceTimersByTime(4000)
    const lastTick = ioStub.emits.filter(e => e.event === 'tutorial:state').slice(-1)[0]!
    expect((lastTick.payload as TutorialStatePayload).remainingSeconds).toBe(55)
  })
})

describe('TutorialManager — missions', () => {
  let room: GameRoom
  let ioStub: ReturnType<typeof createIoStub>
  let manager: TutorialManager

  beforeEach(() => {
    vi.useFakeTimers()
    cleanupAllRooms()
    room = setupRoomWithPlayers('tut-room-missions')
    ioStub = createIoStub()
    manager = new TutorialManager(ioStub.io, room, {
      durationSeconds: 60,
      seed: 7,
      now: () => 0,
      onComplete: vi.fn(),
    })
    manager.start()
    ioStub.emits.length = 0
  })

  afterEach(() => {
    manager.cancel()
    vi.useRealTimers()
    cleanupAllRooms()
  })

  it('records a valid mission and immediately re-broadcasts that user', () => {
    const ok = manager.markMissionComplete(P1, 'p1_review_route')
    expect(ok).toBe(true)
    expect(manager.getCompletedMissions(P1)).toEqual(['p1_review_route'])

    // Only the affected player gets the immediate push, not the whole room.
    const p1Emits = ioStub.emitsFor('sock_p1').filter(e => e.event === 'tutorial:state')
    expect(p1Emits).toHaveLength(1)
    expect((p1Emits[0].payload as TutorialStatePayload).completedMissionIds).toEqual(['p1_review_route'])

    const guardEmits = ioStub.emitsFor('sock_host').filter(e => e.event === 'tutorial:state')
    expect(guardEmits).toHaveLength(0)
  })

  it('rejects missions that do not belong to the requesting role', () => {
    const ok = manager.markMissionComplete(P1, 'g3_capture' as TutorialMissionId)
    expect(ok).toBe(false)
    expect(manager.getCompletedMissions(P1)).toEqual([])
    expect(ioStub.emitsFor('sock_p1').filter(e => e.event === 'tutorial:state')).toHaveLength(0)
  })

  it('treats duplicate completions as no-ops', () => {
    expect(manager.markMissionComplete(P1, 'p2_food_flow')).toBe(true)
    expect(manager.markMissionComplete(P1, 'p2_food_flow')).toBe(false)
    expect(manager.getCompletedMissions(P1)).toEqual(['p2_food_flow'])
  })

  it('isolates checklists between users', () => {
    manager.markMissionComplete(P1, 'p1_review_route')
    manager.markMissionComplete(P2, 'p3_sprint')

    expect(manager.getCompletedMissions(P1)).toEqual(['p1_review_route'])
    expect(manager.getCompletedMissions(P2)).toEqual(['p3_sprint'])
    expect(manager.getCompletedMissions(HOST)).toEqual([])
  })

  it('rejects missions for users not in the room', () => {
    expect(manager.markMissionComplete('ghost-user', 'p1_review_route')).toBe(false)
  })

  it('rejects missions after the tutorial has finished', () => {
    manager.forceEnd()
    expect(manager.markMissionComplete(P1, 'p1_review_route')).toBe(false)
  })
})

// ─────────────────────────────────────────────────────────────────────────
// TutorialManager — end / cancel
// ─────────────────────────────────────────────────────────────────────────

describe('TutorialManager — end and cancel', () => {
  let room: GameRoom
  let ioStub: ReturnType<typeof createIoStub>

  beforeEach(() => {
    vi.useFakeTimers()
    cleanupAllRooms()
    room = setupRoomWithPlayers('tut-room-end')
    ioStub = createIoStub()
  })

  afterEach(() => {
    vi.useRealTimers()
    cleanupAllRooms()
  })

  it('fires tutorial:end and onComplete after the duration elapses', () => {
    let nowMs = 0
    const onComplete = vi.fn()
    const manager = new TutorialManager(ioStub.io, room, {
      durationSeconds: 2,
      tickIntervalMs: 1000,
      seed: 1,
      now: () => nowMs,
      onComplete,
    })
    manager.start()

    nowMs = 2000
    vi.advanceTimersByTime(2000)

    const endEmits = ioStub.emits.filter(e => e.event === 'tutorial:end')
    expect(endEmits).toHaveLength(1)
    expect((endEmits[0].payload as TutorialEndPayload).nextScene).toBe('GameScene')
    expect(endEmits[0].socketId).toBe(room.state.id) // room-wide
    expect(onComplete).toHaveBeenCalledTimes(1)

    // Heartbeat must have stopped after end.
    ioStub.emits.length = 0
    nowMs += 5000
    vi.advanceTimersByTime(5000)
    expect(ioStub.emits.filter(e => e.event === 'tutorial:state')).toHaveLength(0)
  })

  it('forceEnd ends the tutorial early but still notifies clients + onComplete', () => {
    const onComplete = vi.fn()
    const manager = new TutorialManager(ioStub.io, room, {
      durationSeconds: 60,
      seed: 1,
      now: () => 0,
      onComplete,
    })
    manager.start()
    manager.forceEnd()

    expect(ioStub.emits.some(e => e.event === 'tutorial:end')).toBe(true)
    expect(onComplete).toHaveBeenCalledTimes(1)

    // Calling again is idempotent.
    manager.forceEnd()
    expect(onComplete).toHaveBeenCalledTimes(1)
  })

  it('cancel suppresses tutorial:end and onComplete (used during destroyRoom)', () => {
    const onComplete = vi.fn()
    const manager = new TutorialManager(ioStub.io, room, {
      durationSeconds: 60,
      seed: 1,
      now: () => 0,
      onComplete,
    })
    manager.start()
    ioStub.emits.length = 0
    manager.cancel()

    expect(ioStub.emits.filter(e => e.event === 'tutorial:end')).toHaveLength(0)
    expect(onComplete).not.toHaveBeenCalled()
  })

  it('getRemainingSeconds clamps to 0 after the deadline', () => {
    let nowMs = 0
    const manager = new TutorialManager(ioStub.io, room, {
      durationSeconds: 60,
      seed: 1,
      now: () => nowMs,
      onComplete: vi.fn(),
    })
    manager.start()

    nowMs = 60_000
    expect(manager.getRemainingSeconds()).toBe(0)

    nowMs = 999_999
    expect(manager.getRemainingSeconds()).toBe(0)
  })
})

// ─────────────────────────────────────────────────────────────────────────
// Room-manager integration: lobby → tutorial → active hand-off
// ─────────────────────────────────────────────────────────────────────────

describe('room-manager — transitionToTutorial', () => {
  let httpServer: any
  let io: Server

  beforeEach(() => {
    vi.useFakeTimers()
    cleanupAllRooms()
    httpServer = createServer()
    io = new Server(httpServer)
  })

  afterEach(() => {
    vi.useRealTimers()
    cleanupAllRooms()
    io.close()
    httpServer.close()
  })

  it('rejects transition when room is not in lobby', () => {
    const room = setupRoomWithPlayers('tut-room-active-from-tutorial')
    room.state.status = 'active'

    const manager = transitionToTutorial(io, room)
    expect(manager).toBeNull()
    expect(getTutorialManager(room)).toBeUndefined()
  })

  it('hands off to active match when the timer elapses', () => {
    const room = setupRoomWithPlayers('tut-room-flow')
    // Pre-assign roles like room:start would.
    assignRandomRoles(room.state)

    const manager = transitionToTutorial(io, room, {
      durationSeconds: 1,
      tickIntervalMs: 250,
      seed: 99,
    })
    expect(manager).not.toBeNull()
    expect(room.state.status).toBe('tutorial')
    expect(getTutorialManager(room)).toBe(manager)

    // Pollute inventory like a tutorial run would, then verify cleanup.
    const anyPrisoner = Array.from(room.state.players.values()).find(p => p.role === 'prisoner')!
    anyPrisoner.heldItemId = 'route1_cutters_a'
    anyPrisoner.inventorySlots = [
      { itemId: 'route1_cutters_a', itemType: 'route_tool' },
      null,
    ]

    vi.advanceTimersByTime(1000)

    expect(room.state.status).toBe('active')
    expect(getTutorialManager(room)).toBeUndefined()
    expect(anyPrisoner.heldItemId).toBeNull()
    expect(anyPrisoner.inventorySlots).toEqual([null, null])

    // Stop the tick loop the active match started so the test can clean up.
    stopGameLoop(room)
  })

  it('destroyRoom during tutorial cancels timers without entering active', () => {
    const room = setupRoomWithPlayers('tut-room-mid-disc')
    transitionToTutorial(io, room, { durationSeconds: 60, seed: 1 })
    expect(room.state.status).toBe('tutorial')

    destroyRoom(room.state.id)

    expect(getRoom(room.state.id)).toBeUndefined()
    // Advancing past the tutorial deadline must not blow up — the timers
    // were cancelled, and onComplete must not run on a missing room.
    expect(() => vi.advanceTimersByTime(70_000)).not.toThrow()
  })
})
