/**
 * Sistema de Rutina/Fases + NPC Libre Albedrío
 *
 * Backend authority for:
 *   - 8-phase jail schedule timer (phase:warning, phase:start)
 *   - Weighted random NPC action assignment per phase
 *   - Zone-based random target generation (seeded determinism)
 *   - Social action pairing between NPC pairs/groups
 *   - 15-25s reassign interval (libre albedrío)
 *   - Player zone validation → phase:zone_check
 */

import {
  GameRoomState,
  JailPhaseNumber,
  NPCAssignment,
  NPCActionStep,
  PhaseJailStartPayload,
  PhaseWarningPayload,
  NPCReassignPayload,
  PhaseZoneCheckPayload,
} from '../types.js'
import { ZONES, generateSeed } from '../prison-layout.js'
import { NPCPersonalitySystem } from './npc-personality.js'

// ─── Tuning Knobs ─────────────────────────────────────────────────────────────

const REASSIGN_INTERVAL_S            = 20
const REASSIGN_CHANGE_PROB           = 0.80
const PHASE_WARNING_BEFORE_S         = 10
const ZONE_CHECK_GRACE_S             = 5
const LOOPING_GRACE_S                = 5

// ─── Organic Transition Knobs ──────────────────────────────────────────────────
const TRANSITION_LINGER_MAX_S        = 20
const TRANSITION_DETOUR_PROB         = 0.40
const TRANSITION_ENROUTE_CHAT_PROB   = 0.30
const SUBZONE_CHANGE_PROB            = 0.25

// ─── Action Catalog Types ─────────────────────────────────────────────────────

type ActionType = 'SOLO' | 'SOCIAL' | 'LOOPING' | 'IDLE' | 'ONESHOT' | 'ADDITIVE'

interface ActionDef {
  actionId: string
  type: ActionType
  animTrigger: string
  zoneId: string               // null for 'stay in place'
  weight: number
  minDuration: number
  maxDuration: number
  loop?: boolean
  socialGroupSize?: number
  chainLength?: number
}

interface JailPhaseDef {
  phase: JailPhaseNumber
  name: string
  duration: number
  zone: string
  subZones?: string[]
  actions: ActionDef[]
}

// ─── Phase Definitions ────────────────────────────────────────────────────────

const JAIL_PHASES: JailPhaseDef[] = [
  {
    phase: 1, name: 'Desayuno', duration: 90, zone: 'comedor',
    actions: [
      { actionId: 'cafe_wait_outside_talk',  type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'cafeteria',  weight: 20,  minDuration: 6,  maxDuration: 7  },
      { actionId: 'cafe_walk_to_counter',    type: 'ONESHOT', animTrigger: 'Walking',       zoneId: 'cafeteria_counter', weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_grab_food',          type: 'IDLE',    animTrigger: 'serve_self',    zoneId: 'cafeteria_counter', weight: 100, minDuration: 4,  maxDuration: 6  },
      { actionId: 'cafe_walk_to_seat',       type: 'ONESHOT', animTrigger: 'Walking',       zoneId: 'cafeteria_seating', weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_sit_eat',            type: 'IDLE',    animTrigger: 'sit_eat',       zoneId: 'cafeteria_seating', weight: 60,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_sit_eat_talk',       type: 'SOCIAL',  animTrigger: 'sit_eat_talk',  zoneId: 'cafeteria_seating', weight: 40,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_walk_to_trash',      type: 'ONESHOT', animTrigger: 'carry_tray',    zoneId: 'cafeteria_trash',   weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_clear_tray',         type: 'IDLE',    animTrigger: 'deposit_tray',  zoneId: 'cafeteria_trash',   weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'cafe_talk_after_trash',   type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'cafeteria_trash',   weight: 25,  minDuration: 6,  maxDuration: 7  },
    ],
  },
  {
    phase: 2, name: 'Trabajo', duration: 90, zone: 'trabajo',
    subZones: ['taller', 'lavanderia'],
    actions: [
      { actionId: 'work_use_workbench',     type: 'IDLE',    animTrigger: 'work_bench',    zoneId: 'workshop_benches', weight: 40, minDuration: 20, maxDuration: 50 },
      { actionId: 'work_carry_box',         type: 'LOOPING', animTrigger: 'carry_box',     zoneId: 'workshop',         weight: 30, minDuration: 12, maxDuration: 20, loop: true, chainLength: 2 },
      { actionId: 'work_inspect_equipment', type: 'IDLE',    animTrigger: 'inspect',       zoneId: 'workshop',         weight: 20, minDuration: 10, maxDuration: 20 },
      { actionId: 'work_talk_coworker',     type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'workshop',         weight: 10, minDuration: 8,  maxDuration: 15 },
      { actionId: 'laundry_load_washer',    type: 'IDLE',    animTrigger: 'load_machine',  zoneId: 'laundry_machines', weight: 30, minDuration: 15, maxDuration: 30 },
      { actionId: 'laundry_fold_clothes',   type: 'IDLE',    animTrigger: 'fold_clothes',  zoneId: 'laundry',          weight: 35, minDuration: 20, maxDuration: 40 },
      { actionId: 'laundry_carry_basket',   type: 'LOOPING', animTrigger: 'carry_basket',  zoneId: 'laundry_machines', weight: 25, minDuration: 10, maxDuration: 18, loop: true, chainLength: 2 },
      { actionId: 'laundry_idle_check',     type: 'IDLE',    animTrigger: 'idle_check',    zoneId: 'laundry_machines', weight: 10, minDuration: 5,  maxDuration: 12 },
    ],
  },
  {
    phase: 3, name: 'Hora libre', duration: 120, zone: 'libre',
    subZones: ['patio', 'comedor', 'lavanderia', 'celdas'],
    actions: [
      { actionId: 'yard_walk_perimeter',     type: 'LOOPING', animTrigger: 'Walking',       zoneId: 'yard',            weight: 20, minDuration: 30, maxDuration: 60, loop: true, chainLength: 4 },
      { actionId: 'yard_sit_bench',          type: 'IDLE',    animTrigger: 'sit_bench',     zoneId: 'yard_benches',    weight: 20, minDuration: 20, maxDuration: 60 },
      { actionId: 'yard_exercise',           type: 'IDLE',    animTrigger: 'exercise',      zoneId: 'yard_exercise',   weight: 15, minDuration: 15, maxDuration: 40 },
      { actionId: 'yard_conversation_group', type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'yard',            weight: 20, minDuration: 15, maxDuration: 35 },
      { actionId: 'yard_play_cards',         type: 'SOCIAL',  animTrigger: 'sit_cards',     zoneId: 'yard_benches',    weight: 10, minDuration: 30, maxDuration: 90, socialGroupSize: 4 },
      { actionId: 'yard_lean_wall',          type: 'IDLE',    animTrigger: 'lean_wall',     zoneId: 'yard',            weight: 8,  minDuration: 15, maxDuration: 40 },
      { actionId: 'yard_shadow_boxing',      type: 'IDLE',    animTrigger: 'shadowbox',     zoneId: 'yard_exercise',   weight: 5,  minDuration: 10, maxDuration: 20 },
      { actionId: 'yard_kick_ball',          type: 'SOCIAL',  animTrigger: 'kick',          zoneId: 'yard',            weight: 2,  minDuration: 20, maxDuration: 40 },
      { actionId: 'free_cafe_sit_talk',      type: 'SOCIAL',  animTrigger: 'talk_seated',   zoneId: 'cafeteria_seating',weight: 40, minDuration: 15, maxDuration: 40 },
      { actionId: 'free_cafe_sit_idle',      type: 'IDLE',    animTrigger: 'sit_idle',      zoneId: 'cafeteria_seating',weight: 35, minDuration: 10, maxDuration: 30 },
      { actionId: 'free_cafe_stand_chat',    type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'cafeteria',       weight: 25, minDuration: 10, maxDuration: 25 },
      { actionId: 'laundry_load_washer',     type: 'IDLE',    animTrigger: 'load_machine',  zoneId: 'laundry_machines',weight: 30, minDuration: 15, maxDuration: 30 },
      { actionId: 'laundry_fold_clothes',    type: 'IDLE',    animTrigger: 'fold_clothes',  zoneId: 'laundry',         weight: 35, minDuration: 20, maxDuration: 40 },
      { actionId: 'cell_lie_bed',            type: 'IDLE',    animTrigger: 'lie_down',      zoneId: 'cells',           weight: 60, minDuration: 20, maxDuration: 60 },
      { actionId: 'cell_sit_bed',            type: 'IDLE',    animTrigger: 'sit_bed_edge',  zoneId: 'cells',           weight: 40, minDuration: 15, maxDuration: 40 },
    ],
  },
  {
    phase: 4, name: 'Almuerzo', duration: 90, zone: 'comedor',
    actions: [
      { actionId: 'cafe_wait_outside_talk',  type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'cafeteria',  weight: 20,  minDuration: 6,  maxDuration: 7  },
      { actionId: 'cafe_walk_to_counter',    type: 'ONESHOT', animTrigger: 'Walking',       zoneId: 'cafeteria_counter', weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_grab_food',          type: 'IDLE',    animTrigger: 'serve_self',    zoneId: 'cafeteria_counter', weight: 100, minDuration: 4,  maxDuration: 6  },
      { actionId: 'cafe_walk_to_seat',       type: 'ONESHOT', animTrigger: 'Walking',       zoneId: 'cafeteria_seating', weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_sit_eat',            type: 'IDLE',    animTrigger: 'sit_eat',       zoneId: 'cafeteria_seating', weight: 60,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_sit_eat_talk',       type: 'SOCIAL',  animTrigger: 'sit_eat_talk',  zoneId: 'cafeteria_seating', weight: 40,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_walk_to_trash',      type: 'ONESHOT', animTrigger: 'carry_tray',    zoneId: 'cafeteria_trash',   weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_clear_tray',         type: 'IDLE',    animTrigger: 'deposit_tray',  zoneId: 'cafeteria_trash',   weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'cafe_talk_after_trash',   type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'cafeteria_trash',   weight: 25,  minDuration: 6,  maxDuration: 7  },
    ],
  },
  {
    phase: 5, name: 'Trabajo', duration: 120, zone: 'trabajo',
    subZones: ['taller', 'lavanderia'],
    actions: [
      { actionId: 'work_use_workbench',     type: 'IDLE',    animTrigger: 'work_bench',    zoneId: 'workshop_benches', weight: 40, minDuration: 20, maxDuration: 50 },
      { actionId: 'work_carry_box',         type: 'LOOPING', animTrigger: 'carry_box',     zoneId: 'workshop',         weight: 30, minDuration: 12, maxDuration: 20, loop: true, chainLength: 2 },
      { actionId: 'work_inspect_equipment', type: 'IDLE',    animTrigger: 'inspect',       zoneId: 'workshop',         weight: 20, minDuration: 10, maxDuration: 20 },
      { actionId: 'work_talk_coworker',     type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'workshop',         weight: 10, minDuration: 8,  maxDuration: 15 },
      { actionId: 'laundry_load_washer',    type: 'IDLE',    animTrigger: 'load_machine',  zoneId: 'laundry_machines', weight: 30, minDuration: 15, maxDuration: 30 },
      { actionId: 'laundry_fold_clothes',   type: 'IDLE',    animTrigger: 'fold_clothes',  zoneId: 'laundry',          weight: 35, minDuration: 20, maxDuration: 40 },
      { actionId: 'laundry_carry_basket',   type: 'LOOPING', animTrigger: 'carry_basket',  zoneId: 'laundry_machines', weight: 25, minDuration: 10, maxDuration: 18, loop: true, chainLength: 2 },
      { actionId: 'laundry_idle_check',     type: 'IDLE',    animTrigger: 'idle_check',    zoneId: 'laundry_machines', weight: 10, minDuration: 5,  maxDuration: 12 },
    ],
  },
  {
    phase: 6, name: 'Hora libre', duration: 90, zone: 'libre',
    subZones: ['patio', 'comedor', 'lavanderia', 'celdas'],
    actions: [
      { actionId: 'yard_walk_perimeter',     type: 'LOOPING', animTrigger: 'Walking',       zoneId: 'yard',            weight: 20, minDuration: 30, maxDuration: 60, loop: true, chainLength: 4 },
      { actionId: 'yard_sit_bench',          type: 'IDLE',    animTrigger: 'sit_bench',     zoneId: 'yard_benches',    weight: 20, minDuration: 20, maxDuration: 60 },
      { actionId: 'yard_exercise',           type: 'IDLE',    animTrigger: 'exercise',      zoneId: 'yard_exercise',   weight: 15, minDuration: 15, maxDuration: 40 },
      { actionId: 'yard_conversation_group', type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'yard',            weight: 20, minDuration: 15, maxDuration: 35 },
      { actionId: 'yard_play_cards',         type: 'SOCIAL',  animTrigger: 'sit_cards',     zoneId: 'yard_benches',    weight: 10, minDuration: 30, maxDuration: 90, socialGroupSize: 4 },
      { actionId: 'free_cafe_sit_talk',      type: 'SOCIAL',  animTrigger: 'talk_seated',   zoneId: 'cafeteria_seating',weight: 40, minDuration: 15, maxDuration: 40 },
      { actionId: 'free_cafe_sit_idle',      type: 'IDLE',    animTrigger: 'sit_idle',      zoneId: 'cafeteria_seating',weight: 35, minDuration: 10, maxDuration: 30 },
      { actionId: 'free_cafe_stand_chat',    type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'cafeteria',       weight: 25, minDuration: 10, maxDuration: 25 },
      { actionId: 'cell_lie_bed',            type: 'IDLE',    animTrigger: 'lie_down',      zoneId: 'cells',           weight: 60, minDuration: 20, maxDuration: 60 },
      { actionId: 'cell_sit_bed',            type: 'IDLE',    animTrigger: 'sit_bed_edge',  zoneId: 'cells',           weight: 40, minDuration: 15, maxDuration: 40 },
    ],
  },
  {
    phase: 7, name: 'Cena', duration: 90, zone: 'comedor',
    actions: [
      { actionId: 'cafe_wait_outside_talk',  type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'cafeteria',  weight: 20,  minDuration: 6,  maxDuration: 7  },
      { actionId: 'cafe_walk_to_counter',    type: 'ONESHOT', animTrigger: 'Walking',       zoneId: 'cafeteria_counter', weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_grab_food',          type: 'IDLE',    animTrigger: 'serve_self',    zoneId: 'cafeteria_counter', weight: 100, minDuration: 4,  maxDuration: 6  },
      { actionId: 'cafe_walk_to_seat',       type: 'ONESHOT', animTrigger: 'Walking',       zoneId: 'cafeteria_seating', weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_sit_eat',            type: 'IDLE',    animTrigger: 'sit_eat',       zoneId: 'cafeteria_seating', weight: 60,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_sit_eat_talk',       type: 'SOCIAL',  animTrigger: 'sit_eat_talk',  zoneId: 'cafeteria_seating', weight: 40,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_walk_to_trash',      type: 'ONESHOT', animTrigger: 'carry_tray',    zoneId: 'cafeteria_trash',   weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_clear_tray',         type: 'IDLE',    animTrigger: 'deposit_tray',  zoneId: 'cafeteria_trash',   weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'cafe_talk_after_trash',   type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'cafeteria_trash',   weight: 25,  minDuration: 6,  maxDuration: 7  },
    ],
  },
  {
    phase: 8, name: 'Luces apagadas', duration: 120, zone: 'celdas',
    actions: [
      { actionId: 'lights_sleep', type: 'IDLE', animTrigger: 'sleep',     zoneId: 'cells', weight: 75, minDuration: 90, maxDuration: 120 },
      { actionId: 'lights_toss',  type: 'IDLE', animTrigger: 'toss_turn', zoneId: 'cells', weight: 25, minDuration: 5,  maxDuration: 12  },
    ],
  },
]

// Sub-zone → valid action IDs
const SUBZONE_ACTIONS: Record<string, string[]> = {
  taller:     ['work_use_workbench', 'work_carry_box', 'work_inspect_equipment', 'work_talk_coworker'],
  lavanderia: ['laundry_load_washer', 'laundry_fold_clothes', 'laundry_carry_basket', 'laundry_idle_check'],
  patio:      ['yard_walk_perimeter', 'yard_sit_bench', 'yard_exercise', 'yard_conversation_group', 'yard_play_cards', 'yard_lean_wall', 'yard_shadow_boxing', 'yard_kick_ball'],
  comedor:    ['free_cafe_sit_talk', 'free_cafe_sit_idle', 'free_cafe_stand_chat'],
  celdas:     ['cell_lie_bed', 'cell_sit_bed', 'cell_read_book', 'cell_idle_window'],
}

// ─── JailRoutineSystem ────────────────────────────────────────────────────────

export class JailRoutineSystem {
  onPhaseWarning!: (payload: PhaseWarningPayload) => void
  onPhaseStart!:   (payload: PhaseJailStartPayload) => void
  onNPCReassign!:  (payload: NPCReassignPayload) => void
  onZoneCheck!:    (playerId: string, payload: PhaseZoneCheckPayload) => void

  private currentPhase: JailPhaseNumber = 1
  private phaseStartedAt  = 0
  private warningEmitted  = false
  private lastReassignAt  = 0
  private zoneCheckDoneAt = 0
  private zoneCheckedPlayers = new Set<string>()

  private npcAssignments  = new Map<string, NPCAssignment>()
  private npcTimers       = new Map<string, number>()
  private npcCells        = new Map<string, string>()
  private npcSubZones     = new Map<string, string>()
  private npcPartners     = new Map<string, string>()

  private personality: NPCPersonalitySystem

  constructor(private state: GameRoomState) {
    this.assignCells()
    this.personality = new NPCPersonalitySystem(state)
  }

  getPersonalitySystem(): NPCPersonalitySystem {
    return this.personality
  }

  start(): void {
    this.currentPhase = 1
    this.phaseStartedAt = Date.now()
    this.warningEmitted = false
    this.lastReassignAt = Date.now()
    this.zoneCheckDoneAt = 0
    this.zoneCheckedPlayers.clear()
    this.emitPhaseStart()
    console.log('[JAIL] Routine started → Phase 1 (Desayuno)')
  }

  update(tickDelta: number): void {
    if (this.phaseStartedAt === 0) return

    this.updateNPCTimers(tickDelta)
    this.checkPhaseTimer()
    this.checkReassignInterval()
    this.checkZoneViolations()
    this.personality.update(tickDelta)
  }

  getCurrentJailPhase(): JailPhaseNumber { return this.currentPhase }
  getCurrentZone(): string { return this.getPhaseDef(this.currentPhase)?.zone ?? 'unknown' }

  buildReconnectAssignments(): NPCAssignment[] {
    return Array.from(this.npcAssignments.values())
  }

  private checkPhaseTimer(): void {
    const def = this.getPhaseDef(this.currentPhase)
    if (!def) return

    const elapsed = (Date.now() - this.phaseStartedAt) / 1000

    if (!this.warningEmitted && elapsed >= def.duration - PHASE_WARNING_BEFORE_S) {
      this.warningEmitted = true
      const nextPhase = this.nextPhaseNumber(this.currentPhase)
      const nextDef   = this.getPhaseDef(nextPhase)!
      this.onPhaseWarning?.({
        nextPhase,
        nextPhaseName: nextDef.name,
        warningInSeconds: PHASE_WARNING_BEFORE_S,
      })
      console.log(`[JAIL] Phase warning: Phase ${nextPhase} (${nextDef.name}) in ${PHASE_WARNING_BEFORE_S}s`)
    }

    if (elapsed >= def.duration) {
      this.advancePhase()
    }
  }

  private advancePhase(): void {
    this.currentPhase   = this.nextPhaseNumber(this.currentPhase)
    this.phaseStartedAt = Date.now()
    this.warningEmitted = false
    this.zoneCheckDoneAt = 0
    this.zoneCheckedPlayers.clear()
    this.npcSubZones.clear()
    this.personality.onPhaseChanged()
    this.emitPhaseStart()
    console.log(`[JAIL] Phase ${this.currentPhase} (${this.getPhaseDef(this.currentPhase)!.name})`)
  }

  private nextPhaseNumber(current: JailPhaseNumber): JailPhaseNumber {
    return (current === 8 ? 1 : current + 1) as JailPhaseNumber
  }

  private emitPhaseStart(): void {
    const def = this.getPhaseDef(this.currentPhase)!
    const assignments = this.buildPhaseAssignments(def)

    assignments.forEach(a => {
      this.npcAssignments.set(a.npcId, a)
      this.npcTimers.set(a.npcId, a.duration)
    })

    this.onPhaseStart?.({
      phase:          this.currentPhase,
      phaseName:      def.name,
      duration:       def.duration,
      zone:           def.zone,
      npcAssignments: assignments,
    })

    console.log(`[NPC-ACTIONS] Phase ${this.currentPhase} (${def.name}) — ${assignments.length} assignments:`)
    assignments.forEach(a => {
      const base = `  npc=${a.npcId}  action=${a.actionId}  anim=${a.animTrigger}  dur=${a.duration.toFixed(1)}s  zone=${a.zoneId ?? 'none'}  seed=${a.seed ?? 0}${a.socialPartnerId ? `  partner=${a.socialPartnerId}` : ''}${a.loop ? `  loop=true` : ''}`
      console.log(base)
      if (a.actionSequence && a.actionSequence.length > 0) {
        a.actionSequence.forEach((step, i) => {
          console.log(`    [seq ${i + 1}/${a.actionSequence!.length}] action=${step.actionId}  anim=${step.animTrigger}  zone=${step.zoneId ?? 'none'}  seed=${step.seed ?? 0}  dur=${step.duration.toFixed(1)}s${step.socialPartnerId ? `  partner=${step.socialPartnerId}` : ''}`)
        })
      }
    })
  }

  private buildPhaseAssignments(def: JailPhaseDef): NPCAssignment[] {
    const npcIds = Array.from(this.state.npcs.keys())

    if (def.phase === 1 || def.phase === 4 || def.phase === 7) {
      return this.buildCafeteriaAssignments(npcIds, def)
    }

    return this.buildStandardAssignments(npcIds, def)
  }

  private buildStandardAssignments(npcIds: string[], def: JailPhaseDef): NPCAssignment[] {
    const assignments: NPCAssignment[] = []
    const paired = new Set<string>()

    if (def.subZones && def.subZones.length > 0) {
      this.distributeSubZones(npcIds, def.subZones)
    }

    for (const npcId of npcIds) {
      if (paired.has(npcId)) continue

      const subZone    = this.npcSubZones.get(npcId)
      const actionPool = this.getActionPool(def, subZone)
      const personalizedPool = this.personality.applyWeightModifiers(npcId, actionPool)
      const action     = this.weightedRandom(personalizedPool)

      if (!action) {
        assignments.push(this.buildIdleAssignment(npcId))
        continue
      }

      this.personality.recordAction(npcId, action.actionId)

      const walkSpeedMult = 0.85 + Math.random() * 0.30

      if (action.loop && action.chainLength) {
        assignments.push(this.buildSoloAssignment(npcId, action, subZone, walkSpeedMult))
        continue
      }

      const usePrefix = def.phase !== 8
      const prefix    = usePrefix ? this.buildTransitionPrefix(npcId) : []

      if (action.type === 'SOCIAL') {
        const partner = this.findPartner(npcIds, paired, npcId)
        if (partner) {
          const isSit = action.animTrigger.startsWith('sit_') || action.animTrigger.startsWith('talk_seated') || action.actionId.includes('sit');
          const seed    = isSit ? npcIds.indexOf(npcId) : generateSeed()
          const dur     = this.randomDuration(action)
          const prefix2 = usePrefix ? this.buildTransitionPrefix(partner) : []

          const mainStep1: NPCActionStep = {
            actionId: action.actionId, animTrigger: action.animTrigger,
            zoneId: action.zoneId, seed, duration: dur, socialPartnerId: partner,
          }
          const mainStep2: NPCActionStep = {
            actionId: action.actionId, animTrigger: action.animTrigger,
            zoneId: action.zoneId, seed, duration: dur, socialPartnerId: npcId,
          }

          const seq1 = [...prefix,  mainStep1]
          const seq2 = [...prefix2, mainStep2]

          assignments.push({
            npcId, actionId: 'phase_transition_seq', animTrigger: 'idle',
            duration: Math.min(seq1.reduce((s, st) => s + st.duration, 0) + 5, def.duration),
            subZone: subZone ?? undefined, actionSequence: seq1, walkSpeedMult,
          })
          assignments.push({
            npcId: partner, actionId: 'phase_transition_seq', animTrigger: 'idle',
            duration: Math.min(seq2.reduce((s, st) => s + st.duration, 0) + 5, def.duration),
            subZone: this.npcSubZones.get(partner) ?? undefined, actionSequence: seq2,
            walkSpeedMult: 0.85 + Math.random() * 0.30,
          })

          paired.add(npcId);  paired.add(partner)
          this.npcPartners.set(npcId, partner)
          this.npcPartners.set(partner, npcId)
          continue
        }
      }

      const isSit = action.animTrigger.startsWith('sit_') || action.animTrigger.startsWith('talk_seated') || action.actionId.includes('sit');
      const seed     = isSit ? npcIds.indexOf(npcId) : generateSeed()
      const dur      = this.randomDuration(action)
      const mainStep: NPCActionStep = {
        actionId: action.actionId, animTrigger: action.animTrigger,
        zoneId: action.zoneId, seed, duration: dur,
      }
      const seq      = [...prefix, mainStep]
      const totalDur = seq.reduce((s, st) => s + st.duration, 0) + 5

      assignments.push({
        npcId, actionId: 'phase_transition_seq', animTrigger: 'idle',
        duration: Math.min(totalDur, def.duration),
        subZone: subZone ?? undefined, actionSequence: seq, walkSpeedMult,
      })
    }

    return assignments
  }

  private buildCafeteriaAssignments(npcIds: string[], def: JailPhaseDef): NPCAssignment[] {
    const assignments: NPCAssignment[] = []
    const paired = new Set<string>()

    const shuffled = [...npcIds].sort(() => Math.random() - 0.5)

    for (const npcId of shuffled) {
      if (paired.has(npcId)) continue;

      const walkSpeedMult = 0.85 + Math.random() * 0.30;
      const steps: NPCActionStep[] = [...this.buildTransitionPrefix(npcId)];

      const directSit = Math.random() < 0.30;
      const seatSeed = shuffled.indexOf(npcId);

      if (!directSit) {
        if (Math.random() < 0.20) {
          const partner = this.findPartner(shuffled, paired, npcId);
          steps.push({
            actionId: 'cafe_wait_outside_talk',
            animTrigger: 'talk_standing',
            zoneId: 'cafeteria', seed: generateSeed(),
            duration: 6 + Math.random(),
            socialPartnerId: partner ?? undefined,
          });
        }

        steps.push({ actionId: 'cafe_walk_to_counter', animTrigger: 'Walking', zoneId: 'cafeteria_counter', seed: generateSeed(), duration: 0 });
        steps.push({ actionId: 'cafe_grab_food', animTrigger: 'serve_self', zoneId: 'cafeteria_counter', seed: generateSeed(), duration: 4 + Math.random() * 2 });
      }

      steps.push({ actionId: 'cafe_walk_to_seat', animTrigger: 'Walking', zoneId: 'cafeteria_seating', seed: seatSeed, duration: 0 });

      let eatDuration = 10 + Math.random() * 5;
      if (directSit) {
        eatDuration += 15 + Math.random() * 20; // Sit for a longer time
      }

      if (Math.random() < 0.40) {
        const partner = this.findPartner(shuffled, paired, npcId);
        steps.push({
          actionId: 'cafe_sit_eat_talk', animTrigger: 'sit_eat_talk',
          zoneId: 'cafeteria_seating', seed: seatSeed, duration: eatDuration,
          socialPartnerId: partner ?? undefined,
        });
      } else {
        steps.push({
          actionId: 'cafe_sit_eat', animTrigger: 'sit_eat',
          zoneId: 'cafeteria_seating', seed: seatSeed, duration: eatDuration,
        });
      }

      steps.push({ actionId: 'cafe_walk_to_trash', animTrigger: 'carry_tray', zoneId: 'cafeteria_trash', seed: generateSeed(), duration: 0 });
      steps.push({ actionId: 'cafe_clear_tray', animTrigger: 'deposit_tray', zoneId: 'cafeteria_trash', seed: generateSeed(), duration: 3 + Math.random() * 2 });

      if (Math.random() < 0.25) {
        const chatPartner = this.findPartner(shuffled, paired, npcId)
        steps.push({
          actionId: 'cafe_talk_after_trash', animTrigger: 'talk_standing',
          zoneId: 'cafeteria_trash', seed: generateSeed(), duration: 6 + Math.random(),
          socialPartnerId: chatPartner ?? undefined,
        })
      }

      const totalDur = steps.reduce((s, st) => s + st.duration, 0) + 8
      assignments.push({
        npcId, actionId: 'cafeteria_sequence', animTrigger: 'Idle',
        duration: Math.min(totalDur, def.duration),
        actionSequence: steps, walkSpeedMult,
      })
    }

    return assignments
  }

  private buildSoloAssignment(npcId: string, action: ActionDef, subZone?: string, walkSpeedMult?: number): NPCAssignment {
    const dur  = this.randomDuration(action)
    const seed = generateSeed()

    if (action.loop && action.chainLength) {
      const seedChain = Array.from({ length: action.chainLength }, () => generateSeed())
      return {
        npcId, actionId: action.actionId, animTrigger: action.animTrigger,
        zoneId: action.zoneId, seed, seedChain, duration: dur, loop: true,
        subZone: subZone ?? undefined, walkSpeedMult,
      }
    }

    return {
      npcId, actionId: action.actionId, animTrigger: action.animTrigger,
      zoneId: action.zoneId, seed, duration: dur, subZone: subZone ?? undefined, walkSpeedMult,
    }
  }

  private buildIdleAssignment(npcId: string): NPCAssignment {
    return { npcId, actionId: 'idle_stand', animTrigger: 'idle', duration: 10 }
  }

  private buildTransitionPrefix(npcId: string): NPCActionStep[] {
    const steps: NPCActionStep[] = []

    const profileRoll = Math.random()
    let lingerTime: number
    let mayDetour: boolean

    if (profileRoll < 0.30) {
      lingerTime = Math.random() * 5; mayDetour = false;
    } else if (profileRoll < 0.80) {
      lingerTime = 5 + Math.random() * 10; mayDetour = Math.random() < TRANSITION_DETOUR_PROB;
    } else {
      lingerTime = 15 + Math.random() * (TRANSITION_LINGER_MAX_S - 15); mayDetour = Math.random() < (TRANSITION_DETOUR_PROB + 0.20);
    }

    if (lingerTime > 1.5) {
      const idleAnims = ['idle', 'idle', 'idle', 'stretch', 'yawn']
      steps.push({
        actionId: 'linger_before_move',
        animTrigger: idleAnims[Math.floor(Math.random() * idleAnims.length)],
        duration: lingerTime,
      })
    }

    if (mayDetour) {
      const seed = generateSeed()
      steps.push({ actionId: 'walk_to_corridor', animTrigger: 'Walking', zoneId: 'hallway', seed, duration: 0 })
      if (Math.random() < TRANSITION_ENROUTE_CHAT_PROB) {
        steps.push({ actionId: 'corridor_chat_stop', animTrigger: 'talk_standing', zoneId: 'hallway', seed: generateSeed(), duration: 4 + Math.random() * 7 })
      } else {
        steps.push({ actionId: 'corridor_idle_pause', animTrigger: 'idle', zoneId: 'hallway', seed: generateSeed(), duration: 2 + Math.random() * 3 })
      }
    }

    return steps
  }

  private checkReassignInterval(): void {
    const now = Date.now()
    if ((now - this.lastReassignAt) / 1000 < REASSIGN_INTERVAL_S) return

    if (this.currentPhase === 1 || this.currentPhase === 4 || this.currentPhase === 7) return

    this.lastReassignAt = now
    const def = this.getPhaseDef(this.currentPhase)!
    const changed: NPCAssignment[] = []

    for (const [npcId, timer] of this.npcTimers) {
      if (timer > 5) continue
      if (Math.random() > REASSIGN_CHANGE_PROB) continue

      let subZone = this.npcSubZones.get(npcId)

      if ((this.currentPhase === 3 || this.currentPhase === 6) && def.subZones && def.subZones.length > 0) {
        if (Math.random() < SUBZONE_CHANGE_PROB) {
          const candidates = def.subZones.filter(z => z !== subZone)
          const newSubZone = candidates[Math.floor(Math.random() * candidates.length)]
          this.npcSubZones.set(npcId, newSubZone)
          subZone = newSubZone
        }
      }

      const actionPool = this.getActionPool(def, subZone)
      const current    = this.npcAssignments.get(npcId)
      const pool       = actionPool.filter(a => a.actionId !== current?.actionId)
      const personalizedPool = this.personality.applyWeightModifiers(npcId, pool)
      const action     = this.weightedRandom(personalizedPool)
      if (!action) continue

      if (action.type === 'SOCIAL') continue

      this.personality.recordAction(npcId, action.actionId)

      const profile = this.personality.getProfile(npcId)
      if (profile && timer <= 0 && Math.random() < 0.25) {
        this.personality.triggerMoodEvent(npcId, 'boredom')
      }

      const assignment = this.buildSoloAssignment(npcId, action, subZone)
      this.npcAssignments.set(npcId, assignment)
      this.npcTimers.set(npcId, assignment.duration)
      changed.push(assignment)
    }

    if (changed.length > 0) {
      this.onNPCReassign?.({ timestamp: Date.now(), assignments: changed })
      console.log(`[NPC-ACTIONS] Reassigned ${changed.length} NPCs (libre albedrío):`)
      changed.forEach(a => {
        console.log(`  npc=${a.npcId}  action=${a.actionId}  anim=${a.animTrigger}  dur=${a.duration.toFixed(1)}s  zone=${a.zoneId ?? 'none'}  seed=${a.seed ?? 0}${a.loop ? `  loop=true` : ''}`)
      })
    }
  }

  private checkZoneViolations(): void {
    // Bounds removed from backend. Zone validation must happen on Unity clients
    // or via a separate server-side authoritative map if strictly needed.
  }

  private updateNPCTimers(tickDelta: number): void {
    for (const [npcId, timer] of this.npcTimers) {
      const next = Math.max(0, timer - tickDelta)
      this.npcTimers.set(npcId, next)
    }
  }

  private distributeSubZones(npcIds: string[], subZones: string[]): void {
    const n          = npcIds.length
    const perZone    = Math.floor(n / subZones.length)
    let   remainder  = n - perZone * subZones.length

    let idx = 0
    for (const zone of subZones) {
      const count = perZone + (remainder-- > 0 ? 1 : 0)
      for (let i = 0; i < count && idx < n; i++, idx++) {
        this.npcSubZones.set(npcIds[idx], zone)
      }
    }
  }

  private getActionPool(def: JailPhaseDef, subZone?: string): ActionDef[] {
    if (def.subZones && def.subZones.length > 0 && subZone) {
      const allowed = SUBZONE_ACTIONS[subZone] ?? []
      return def.actions.filter(a => allowed.includes(a.actionId))
    }
    if (def.phase === 8) {
      return def.actions.filter(a => a.type !== 'SOCIAL')
    }
    return def.actions
  }

  private weightedRandom(pool: ActionDef[]): ActionDef | null {
    if (pool.length === 0) return null
    const total = pool.reduce((s, a) => s + a.weight, 0)
    let r = Math.random() * total
    for (const action of pool) {
      r -= action.weight
      if (r <= 0) return action
    }
    return pool[pool.length - 1]
  }

  private randomDuration(action: ActionDef): number {
    return action.minDuration + Math.random() * (action.maxDuration - action.minDuration)
  }

  private findPartner(
    npcIds: string[],
    paired: Set<string>,
    requesterId: string
  ): string | null {
    const candidates = npcIds.filter(id => id !== requesterId && !paired.has(id))
    if (candidates.length === 0) return null
    return candidates[Math.floor(Math.random() * candidates.length)]
  }

  private assignCells(): void {
    const npcIds = Array.from(this.state.npcs.keys()).sort()
    npcIds.forEach((id, i) => {
      const cellNum = Math.floor(i / 2)
      this.npcCells.set(id, String(cellNum).padStart(2, '0'))
    })
  }

  private getPhaseDef(phase: JailPhaseNumber): JailPhaseDef | undefined {
    return JAIL_PHASES.find(p => p.phase === phase)
  }
}
