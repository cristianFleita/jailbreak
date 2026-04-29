import { describe, expect, it, vi } from 'vitest'
import { addPlayer, createGameRoomState, spawnNPCs } from '../state.js'
import { defaultGameConfig } from '../room-manager.js'
import { JailRoutineSystem } from '../systems/jail-routine.js'
import { isCanonicalNPCTrigger } from '../systems/npc-animations.js'
import type { GameRoomState, JailPhaseNumber, PhaseJailStartPayload } from '../types.js'

function createRoutineWithNPCs(): { state: GameRoomState; routine: JailRoutineSystem } {
  const state = createGameRoomState('routine-room', 'host-user', defaultGameConfig)
  addPlayer(state, 'socket_host', 'host-user', { x: 0, y: 1.5, z: 0 })
  addPlayer(state, 'socket_prisoner', 'prisoner-user', { x: 1, y: 1.5, z: 0 })
  spawnNPCs(state, defaultGameConfig)
  return { state, routine: new JailRoutineSystem(state) }
}

function emitPhase(routine: JailRoutineSystem, phase: JailPhaseNumber): PhaseJailStartPayload {
  let payload: PhaseJailStartPayload | undefined
  routine.onPhaseStart = data => { payload = data }
    ; (routine as any).currentPhase = phase
    ; (routine as any).emitPhaseStart()

  if (!payload) throw new Error(`phase ${phase} did not emit phase:start`)
  return payload
}

describe('JailRoutineSystem', () => {
  it.each([3, 6] as JailPhaseNumber[])('emits multi-zone free time assignments for phase %s', phase => {
    const { state, routine } = createRoutineWithNPCs()
    const payload = emitPhase(routine, phase)
    const yardActionIds = new Set(['yard_idle', 'yard_bench_idle', 'yard_exercise', 'yard_shadow_box', 'yard_lean_wall'])
    const removedYardActionIds = new Set([
      'yard_walk_perimeter',
      'yard_sit_bench',
      'yard_conversation_group',
      'yard_play_cards',
      'yard_shadow_boxing',
      'yard_kick_ball',
    ])

    expect(payload.phase).toBe(phase)
    expect(payload.zone).toBe('Free Time')
    expect(payload.npcAssignments).toHaveLength(state.npcs.size)

    let hasYard = false
    let hasCell = false
    let hasLaundry = false
    let hasKitchen = false

    for (const assignment of payload.npcAssignments) {
      const steps = assignment.actionSequence ?? [assignment]

      for (const step of steps) {
        expect(removedYardActionIds.has(step.actionId)).toBe(false)

        if (yardActionIds.has(step.actionId)) {
          hasYard = true
          expect(['yard', 'yard_benches', 'yard_exercise']).toContain(step.zoneId)
          expect(['idle', 'exercise', 'shadowbox', 'lean_wall']).toContain(step.animTrigger)
          expect(step.seed).toEqual(expect.any(Number))
        }

        if (step.actionId === 'cell_stand_idle' || step.actionId === 'cell_sleep') {
          hasCell = true
          expect(step.zoneId).toMatch(/^cell_area_0[1-8]$/)
          expect(['idle', 'sleep']).toContain(step.animTrigger)
          expect(step.seed).toEqual(expect.any(Number))
        }

        if (step.actionId.startsWith('laundry_')) {
          hasLaundry = true
          expect(step.zoneId).toMatch(/^zone_laundry_/)
        }

        if (step.actionId.startsWith('free_cafe_')) {
          hasKitchen = true
          expect(['cafeteria', 'cafeteria_seating']).toContain(step.zoneId)
        }
      }
    }

    expect(hasYard).toBe(true)
    expect(hasCell).toBe(true)
    expect(hasLaundry).toBe(true)
    expect(hasKitchen).toBe(true)
  })

  it('emits deterministic cell-area assignments per NPC during lockdown', () => {
    const { state, routine } = createRoutineWithNPCs()
    const payload = emitPhase(routine, 8)
    const cellZonePattern = /^cell_area_0[1-8]$/
    const validCellActions = new Set(['cell_stand_idle', 'cell_sleep'])
    const validAnimations = new Set(['idle', 'sleep'])
    const cellCapacities = new Map([
      ['cell_area_01', 2],
      ['cell_area_02', 3],
      ['cell_area_03', 2],
      ['cell_area_04', 3],
      ['cell_area_05', 2],
      ['cell_area_06', 3],
      ['cell_area_07', 2],
      ['cell_area_08', 3],
    ])

    expect(payload.phase).toBe(8)
    expect(payload.phaseName).toBe('Lockdown')
    expect(payload.duration).toBe(60)
    expect(payload.zone).toBe('Cells')
    expect(payload.npcAssignments).toHaveLength(state.npcs.size)

    const assignedCells = new Map<string, number>()
    for (const assignment of payload.npcAssignments) {
      expect(validCellActions.has(assignment.actionId)).toBe(true)
      expect(validAnimations.has(assignment.animTrigger)).toBe(true)
      expect(assignment.zoneId).toMatch(cellZonePattern)
      expect(assignment.seed).toEqual(expect.any(Number))
      expect(assignment.actionSequence).toBeUndefined()
      assignedCells.set(assignment.zoneId!, (assignedCells.get(assignment.zoneId!) ?? 0) + 1)
    }

    for (const [cellId, assignedCount] of assignedCells) {
      expect(assignedCount).toBeLessThanOrEqual(cellCapacities.get(cellId)!)
    }
  })

  it('can emit sleep assignments during lockdown', () => {
    const { routine } = createRoutineWithNPCs()
    const randomSpy = vi.spyOn(Math, 'random').mockReturnValue(0.99)

    try {
      const payload = emitPhase(routine, 8)

      expect(payload.npcAssignments.length).toBeGreaterThan(0)
      for (const assignment of payload.npcAssignments) {
        expect(assignment.actionId).toBe('cell_sleep')
        expect(assignment.animTrigger).toBe('sleep')
        expect(assignment.zoneId).toMatch(/^cell_area_0[1-8]$/)
      }
    } finally {
      randomSpy.mockRestore()
    }
  })

  describe('animation trigger parity with Unity', () => {
    // The Unity NPCBehaviorController.MapTriggerToStateName() falls back to
    // "Idle" on any unknown trigger string — silently. Catching it here in TS
    // before it ships keeps the disguise feature honest: a prisoner emoting
    // "Rummage" must look identical to an NPC at the laundry pile, which only
    // works if the NPC trigger resolves to "Rummaging" instead of "Idle".
    it.each([1, 2, 3, 4, 5, 6, 7, 8] as JailPhaseNumber[])(
      'every animTrigger emitted on phase %s is canonical',
      phase => {
        const { routine } = createRoutineWithNPCs()
        const payload = emitPhase(routine, phase)
        const seen: string[] = []

        for (const assignment of payload.npcAssignments) {
          seen.push(assignment.animTrigger)
          for (const step of assignment.actionSequence ?? []) {
            seen.push(step.animTrigger)
          }
        }

        expect(seen.length).toBeGreaterThan(0)
        for (const trigger of seen) {
          expect(
            isCanonicalNPCTrigger(trigger),
            `phase ${phase} emitted non-canonical animTrigger "${trigger}"`
          ).toBe(true)
        }
      }
    )

    it('reassign loop only emits canonical triggers', () => {
      const { routine } = createRoutineWithNPCs()
      routine.start()

      // Force a yard subzone phase, drain timers, and request a forced reassignment.
      ;(routine as any).currentPhase = 3
      ;(routine as any).phaseStartedAt = Date.now()
      ;(routine as any).lastReassignAt = 0
      for (const id of (routine as any).npcTimers.keys()) {
        ;(routine as any).npcTimers.set(id, 0)
      }
      const randomSpy = vi.spyOn(Math, 'random').mockReturnValue(0.01)
      let reassigned: any[] = []
      routine.onNPCReassign = ({ assignments }) => { reassigned = assignments }

      try {
        ;(routine as any).checkReassignInterval()
        for (const a of reassigned) {
          expect(isCanonicalNPCTrigger(a.animTrigger)).toBe(true)
          for (const step of a.actionSequence ?? []) {
            expect(isCanonicalNPCTrigger(step.animTrigger)).toBe(true)
          }
        }
      } finally {
        randomSpy.mockRestore()
      }
    })

    it('exposes the new yard variety actions to the action pool', () => {
      const { routine } = createRoutineWithNPCs()
      const newYardActions = ['yard_salute_pair', 'yard_argue_pair', 'yard_dance']

      // Force every yard NPC to roll the new entries by stubbing the weighted
      // random to land on each action ID at least once.
      for (const phase of [3, 6] as JailPhaseNumber[]) {
        const def = (routine as any).getPhaseDef(phase)
        const ids = new Set(def.actions.map((a: { actionId: string }) => a.actionId))
        for (const expected of newYardActions) {
          expect(ids.has(expected)).toBe(true)
        }
      }
    })
  })

  describe('routine completion (phase 8 → game end)', () => {
    it('starts with routine not complete', () => {
      const { routine } = createRoutineWithNPCs()
      expect(routine.isRoutineComplete()).toBe(false)
    })

    it('marks routine complete when phase 8 timer expires; does not loop back to phase 1', () => {
      const { routine } = createRoutineWithNPCs()
      routine.start()

        // Force phase 8 with the timer already past its 60s duration.
        ; (routine as any).currentPhase = 8
        ; (routine as any).phaseStartedAt = Date.now() - 61_000

      let phaseStartEmittedAfter = 0
      routine.onPhaseStart = () => { phaseStartEmittedAfter++ }

      routine.update(0.05)

      expect(routine.isRoutineComplete()).toBe(true)
      expect(routine.getCurrentJailPhase()).toBe(8) // stays on 8, no loop to 1
      expect(phaseStartEmittedAfter).toBe(0)        // no new phase:start emitted
    })

    it('does not emit phase:warning while on phase 8 (no next phase)', () => {
      const { routine } = createRoutineWithNPCs()
      routine.start()

        // Inside the 10s warning window but not yet expired (phase 8 = 60s).
        ; (routine as any).currentPhase = 8
        ; (routine as any).phaseStartedAt = Date.now() - 55_000

      let warningEmitted = false
      routine.onPhaseWarning = () => { warningEmitted = true }

      routine.update(0.05)

      expect(warningEmitted).toBe(false)
      expect(routine.isRoutineComplete()).toBe(false)
    })

    it('phase 7 expiry still advances to phase 8 (does not complete)', () => {
      const { routine } = createRoutineWithNPCs()
      routine.start()

        ; (routine as any).currentPhase = 7
        ; (routine as any).phaseStartedAt = Date.now() - 200_000

      routine.update(0.05)

      expect(routine.getCurrentJailPhase()).toBe(8)
      expect(routine.isRoutineComplete()).toBe(false)
    })

    it('further updates after completion are idempotent', () => {
      const { routine } = createRoutineWithNPCs()
      routine.start()

        ; (routine as any).currentPhase = 8
        ; (routine as any).phaseStartedAt = Date.now() - 61_000

      routine.update(0.05)
      expect(routine.isRoutineComplete()).toBe(true)

      let phaseStartEmittedAfter = 0
      routine.onPhaseStart = () => { phaseStartEmittedAfter++ }
      routine.update(0.05)
      routine.update(0.05)

      expect(routine.getCurrentJailPhase()).toBe(8)
      expect(routine.isRoutineComplete()).toBe(true)
      expect(phaseStartEmittedAfter).toBe(0)
    })
  })
})
