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
    phase: 1, name: 'Breakfast', duration: 90, zone: 'Dining Hall',
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
    phase: 2, name: 'Work', duration: 90, zone: 'Workshop',
    subZones: ['taller', 'lavanderia'],
    actions: [
      // Taller — free choice (weighted random)
      { actionId: 'work_use_workbench',     type: 'IDLE',    animTrigger: 'work_bench',    zoneId: 'zone_workshop_bench', weight: 45, minDuration: 20, maxDuration: 50 },
      { actionId: 'work_inspect_cabinets',  type: 'IDLE',    animTrigger: 'inspect',       zoneId: 'zone_workshop_cab',   weight: 35, minDuration: 10, maxDuration: 20 },
      { actionId: 'work_talk_coworker',     type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'zone_workshop_chat',  weight: 20, minDuration: 8,  maxDuration: 15 },
      // Lavandería — strict sequential flow (see buildLaundrySequence)
      { actionId: 'laundry_grab_clothes',   type: 'ONESHOT', animTrigger: 'rummaging',     zoneId: 'zone_laundry_pile',   weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'laundry_load_washer',    type: 'IDLE',    animTrigger: 'load_machine',  zoneId: 'zone_laundry_wash',   weight: 100, minDuration: 15, maxDuration: 20 },
      { actionId: 'laundry_store_clothes',  type: 'IDLE',    animTrigger: 'store_clothes', zoneId: 'zone_laundry_shelf',  weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'laundry_talk_coworker',  type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'zone_laundry_chat',   weight: 30,  minDuration: 8,  maxDuration: 15 },
    ],
  },
  {
    phase: 3, name: 'Free Time', duration: 120, zone: 'Yard',
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
      // Lavandería (ropa personal) — strict sequential flow (see buildLaundrySequence)
      { actionId: 'laundry_grab_clothes',    type: 'ONESHOT', animTrigger: 'rummaging',     zoneId: 'zone_laundry_pile',   weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'laundry_load_washer',     type: 'IDLE',    animTrigger: 'load_machine',  zoneId: 'zone_laundry_wash',   weight: 100, minDuration: 15, maxDuration: 20 },
      { actionId: 'laundry_store_clothes',   type: 'IDLE',    animTrigger: 'store_clothes', zoneId: 'zone_laundry_shelf',  weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'laundry_talk_coworker',   type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'zone_laundry_chat',   weight: 30,  minDuration: 8,  maxDuration: 15 },
      { actionId: 'cell_lie_bed',            type: 'IDLE',    animTrigger: 'lie_down',      zoneId: 'cells',           weight: 60, minDuration: 20, maxDuration: 60 },
      { actionId: 'cell_sit_bed',            type: 'IDLE',    animTrigger: 'sit_bed_edge',  zoneId: 'cells',           weight: 40, minDuration: 15, maxDuration: 40 },
    ],
  },
  {
    phase: 4, name: 'Lunch', duration: 90, zone: 'Dining Hall',
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
    phase: 5, name: 'Work', duration: 120, zone: 'Workshop',
    subZones: ['taller', 'lavanderia'],
    actions: [
      // Taller — free choice (weighted random)
      { actionId: 'work_use_workbench',     type: 'IDLE',    animTrigger: 'work_bench',    zoneId: 'zone_workshop_bench', weight: 45, minDuration: 20, maxDuration: 50 },
      { actionId: 'work_inspect_cabinets',  type: 'IDLE',    animTrigger: 'inspect',       zoneId: 'zone_workshop_cab',   weight: 35, minDuration: 10, maxDuration: 20 },
      { actionId: 'work_talk_coworker',     type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'zone_workshop_chat',  weight: 20, minDuration: 8,  maxDuration: 15 },
      // Lavandería — strict sequential flow (see buildLaundrySequence)
      { actionId: 'laundry_grab_clothes',   type: 'ONESHOT', animTrigger: 'rummaging',     zoneId: 'zone_laundry_pile',   weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'laundry_load_washer',    type: 'IDLE',    animTrigger: 'load_machine',  zoneId: 'zone_laundry_wash',   weight: 100, minDuration: 15, maxDuration: 20 },
      { actionId: 'laundry_store_clothes',  type: 'IDLE',    animTrigger: 'store_clothes', zoneId: 'zone_laundry_shelf',  weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'laundry_talk_coworker',  type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'zone_laundry_chat',   weight: 30,  minDuration: 8,  maxDuration: 15 },
    ],
  },
  {
    phase: 6, name: 'Free Time', duration: 90, zone: 'Yard',
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
      // Lavandería (ropa personal) — strict sequential flow (see buildLaundrySequence)
      { actionId: 'laundry_grab_clothes',    type: 'ONESHOT', animTrigger: 'rummaging',     zoneId: 'zone_laundry_pile',   weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'laundry_load_washer',     type: 'IDLE',    animTrigger: 'load_machine',  zoneId: 'zone_laundry_wash',   weight: 100, minDuration: 15, maxDuration: 20 },
      { actionId: 'laundry_store_clothes',   type: 'IDLE',    animTrigger: 'store_clothes', zoneId: 'zone_laundry_shelf',  weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'laundry_talk_coworker',   type: 'SOCIAL',  animTrigger: 'talk_standing', zoneId: 'zone_laundry_chat',   weight: 30,  minDuration: 8,  maxDuration: 15 },
      { actionId: 'cell_lie_bed',            type: 'IDLE',    animTrigger: 'lie_down',      zoneId: 'cells',           weight: 60, minDuration: 20, maxDuration: 60 },
      { actionId: 'cell_sit_bed',            type: 'IDLE',    animTrigger: 'sit_bed_edge',  zoneId: 'cells',           weight: 40, minDuration: 15, maxDuration: 40 },
    ],
  },
  {
    phase: 7, name: 'Dinner', duration: 90, zone: 'Dining Hall',
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
    phase: 8, name: 'Lights Out', duration: 120, zone: 'Cells',
    actions: [
      { actionId: 'lights_sleep', type: 'IDLE', animTrigger: 'sleep',     zoneId: 'cells', weight: 75, minDuration: 90, maxDuration: 120 },
      { actionId: 'lights_toss',  type: 'IDLE', animTrigger: 'toss_turn', zoneId: 'cells', weight: 25, minDuration: 5,  maxDuration: 12  },
    ],
  },
]

// Sub-zone → valid action IDs
// Note: lavanderia uses a hand-rolled strict sequence (buildLaundrySequence), so these
// entries exist only so getActionPool() returns something non-empty when queried.
const SUBZONE_ACTIONS: Record<string, string[]> = {
  taller:     ['work_use_workbench', 'work_inspect_cabinets', 'work_talk_coworker'],
  lavanderia: ['laundry_grab_clothes', 'laundry_load_washer', 'laundry_store_clothes', 'laundry_talk_coworker'],
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
    console.log('[JAIL] Routine started → Phase 1 (Breakfast)')
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
    const elapsedPhase = (Date.now() - this.phaseStartedAt) / 1000

    return Array.from(this.npcAssignments.values()).map(a => {
      const state = this.state.npcs.get(a.npcId)
      
      // Fast-forward sequence if we have an authoritative current index from the host
      if (state?.currentSequenceIndex !== undefined && a.actionSequence) {
        let sumPrevious = 0
        for (let i = 0; i < state.currentSequenceIndex; i++) {
          sumPrevious += a.actionSequence[i].duration
        }

        const timeInCurrentStep = elapsedPhase - sumPrevious
        const sliced = a.actionSequence.slice(state.currentSequenceIndex)
        
        // Adjust the duration of the current step (which is now sliced[0])
        if (sliced.length > 0) {
          const adjustedStep = { ...sliced[0] }
          adjustedStep.duration = Math.max(0, adjustedStep.duration - timeInCurrentStep)
          sliced[0] = adjustedStep
        }

        return { ...a, actionSequence: sliced }
      }
      else {
        // Single action - fast forward using the backend's explicit timer
        const remaining = this.npcTimers.get(a.npcId)
        if (remaining !== undefined) {
          return { ...a, duration: remaining }
        }
      }
      
      return a
    })
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

      // Lavandería uses a strict sequential flow (grab → load → store → opt. talk),
      // not weighted random from a pool.
      if (subZone === 'lavanderia') {
        const walkSpeedMult = 0.85 + Math.random() * 0.30
        const usePrefix = def.phase !== 8
        const prefix    = usePrefix ? this.buildTransitionPrefix(npcId) : []
        assignments.push(this.buildLaundrySequence(npcId, subZone, walkSpeedMult, prefix, def.duration))
        continue
      }

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
    const n = shuffled.length

    // Leave an 8s tail so every NPC finishes its sequence before phase end.
    const phaseBudget = def.duration - 8

    // Stagger exit from cells: evenly distribute linger across NPCs so they
    // don't all converge on the counter/sink at the same time.
    const STAGGER_MAX = 25

    // Post-sink fillers: walk back to seat and loaf there until phase ends,
    // instead of idling at the trash zone.
    // animTrigger must start with 'sit_' so the Unity client's IsSitAction()
    // routes the NPC to a deterministic chair instead of a random zone point.
    const fillerPool = [
      { actionId: 'free_cafe_sit_idle', animTrigger: 'sit_idle',      minDur: 10, maxDur: 18, social: false },
      { actionId: 'free_cafe_sit_talk', animTrigger: 'sit_eat_talk',  minDur: 10, maxDur: 18, social: true  },
      { actionId: 'cafe_sit_eat',       animTrigger: 'sit_eat',       minDur: 8,  maxDur: 14, social: false },
    ]

    for (let i = 0; i < n; i++) {
      const npcId = shuffled[i]
      if (paired.has(npcId)) continue

      const walkSpeedMult = 0.85 + Math.random() * 0.30
      const steps: NPCActionStep[] = []

      const staggerBase = n > 1 ? (i / (n - 1)) * STAGGER_MAX : 0
      const lingerTime  = Math.max(0, staggerBase + (Math.random() - 0.5) * 4)
      if (lingerTime > 1.5) {
        const idleAnims = ['idle', 'idle', 'idle', 'stretch', 'yawn']
        steps.push({
          actionId: 'linger_before_move',
          animTrigger: idleAnims[Math.floor(Math.random() * idleAnims.length)],
          duration: lingerTime,
        })
      }

      const directSit   = Math.random() < 0.30
      // Same seed for walk-to-counter/grab-food and walk-to-trash/clear-tray
      // so both steps resolve to the exact same interactable on every client.
      const seatSeed    = i
      const counterSeed = i
      const sinkSeed    = i

      if (!directSit) {
        if (Math.random() < 0.20) {
          const partner = this.findPartner(shuffled, paired, npcId)
          steps.push({
            actionId: 'cafe_wait_outside_talk', animTrigger: 'talk_standing',
            zoneId: 'cafeteria', seed: generateSeed(),
            duration: 6 + Math.random(),
            socialPartnerId: partner ?? undefined,
          })
        }
        steps.push({ actionId: 'cafe_walk_to_counter', animTrigger: 'Walking',    zoneId: 'cafeteria_counter', seed: counterSeed, duration: 0 })
        steps.push({ actionId: 'cafe_grab_food',       animTrigger: 'serve_self', zoneId: 'cafeteria_counter', seed: counterSeed, duration: 5 })
      }

      steps.push({ actionId: 'cafe_walk_to_seat', animTrigger: 'Walking', zoneId: 'cafeteria_seating', seed: seatSeed, duration: 0 })

      const baseEat     = 28 + Math.random() * 14
      const eatDuration = directSit ? baseEat + 8 + Math.random() * 8 : baseEat

      if (Math.random() < 0.40) {
        const partner = this.findPartner(shuffled, paired, npcId)
        steps.push({
          actionId: 'cafe_sit_eat_talk', animTrigger: 'sit_eat_talk',
          zoneId: 'cafeteria_seating', seed: seatSeed, duration: eatDuration,
          socialPartnerId: partner ?? undefined,
        })
      } else {
        steps.push({
          actionId: 'cafe_sit_eat', animTrigger: 'sit_eat',
          zoneId: 'cafeteria_seating', seed: seatSeed, duration: eatDuration,
        })
      }

      steps.push({ actionId: 'cafe_walk_to_trash', animTrigger: 'carry_tray',   zoneId: 'cafeteria_trash', seed: sinkSeed, duration: 0 })
      steps.push({ actionId: 'cafe_clear_tray',    animTrigger: 'deposit_tray', zoneId: 'cafeteria_trash', seed: sinkSeed, duration: 4 })

      let consumed = steps.reduce((s, st) => s + st.duration, 0)
      if (consumed < phaseBudget - 12) {
        steps.push({ actionId: 'cafe_walk_to_seat', animTrigger: 'Walking', zoneId: 'cafeteria_seating', seed: seatSeed, duration: 0 })

        let fillerCount = 0
        while (consumed < phaseBudget - 8 && fillerCount < 3) {
          const filler    = fillerPool[Math.floor(Math.random() * fillerPool.length)]
          const fillerDur = Math.min(
            filler.minDur + Math.random() * (filler.maxDur - filler.minDur),
            phaseBudget - consumed - 4,
          )
          if (fillerDur < 5) break

          const partnerForFiller = filler.social
            ? (this.findPartner(shuffled, paired, npcId) ?? undefined)
            : undefined
          steps.push({
            actionId:        filler.actionId,
            animTrigger:     filler.animTrigger,
            zoneId:          'cafeteria_seating',
            seed:            seatSeed,
            duration:        fillerDur,
            socialPartnerId: partnerForFiller,
          })
          consumed += fillerDur
          fillerCount++
        }
      }

      const totalDur = consumed + 8
      assignments.push({
        npcId, actionId: 'cafeteria_sequence', animTrigger: 'Idle',
        duration: Math.min(totalDur, def.duration),
        actionSequence: steps, walkSpeedMult,
      })
    }

    return assignments
  }

  /**
   * Strict laundry flow: grab_clothes → load_washer → store_clothes → [optional talk].
   * Used for Phase 2/5 (Work · lavanderia subzone) and Phase 3/6 (Free Time · lavanderia).
   * The backend emits the full ordered sequence as actionSequence steps so Unity
   * chains them automatically via NPCBehaviorController.UpdateSequence.
   */
  private buildLaundrySequence(
    npcId: string,
    subZone: string,
    walkSpeedMult: number,
    prefix: NPCActionStep[],
    phaseDuration: number,
  ): NPCAssignment {
    const steps: NPCActionStep[] = [...prefix]

    const grabDur  = 3 + Math.random() * 2       // 3–5s
    const loadDur  = 15 + Math.random() * 5      // 15–20s
    const storeDur = 3 + Math.random() * 2       // 3–5s

    // Reuse one seed per zone so all clients pick the same pile/washer/shelf slot.
    const pileSeed  = generateSeed()
    const washSeed  = generateSeed()
    const shelfSeed = generateSeed()

    steps.push({ actionId: 'laundry_grab_clothes',  animTrigger: 'rummaging',     zoneId: 'zone_laundry_pile',  seed: pileSeed,  duration: grabDur  })
    steps.push({ actionId: 'laundry_load_washer',   animTrigger: 'load_machine',  zoneId: 'zone_laundry_wash',  seed: washSeed,  duration: loadDur  })
    steps.push({ actionId: 'laundry_store_clothes', animTrigger: 'store_clothes', zoneId: 'zone_laundry_shelf', seed: shelfSeed, duration: storeDur })

    // Optional post-flow chat. SOCIAL partner logic is skipped here: a laundry
    // chat targets the zone, not a specific NPC, so no partner pairing needed.
    if (Math.random() < 0.30) {
      const chatDur  = 8 + Math.random() * 7     // 8–15s
      const chatSeed = generateSeed()
      steps.push({ actionId: 'laundry_talk_coworker', animTrigger: 'talk_standing', zoneId: 'zone_laundry_chat', seed: chatSeed, duration: chatDur })
    }

    const totalDur = steps.reduce((s, st) => s + st.duration, 0) + 5
    this.personality.recordAction(npcId, 'laundry_grab_clothes')

    return {
      npcId,
      actionId:       'laundry_sequence',
      animTrigger:    'idle',
      duration:       Math.min(totalDur, phaseDuration),
      subZone,
      actionSequence: steps,
      walkSpeedMult,
    }
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

      // Lavandería: rebuild the full strict sequence so NPCs keep looping
      // grab → load → store → optional chat instead of jumping to a random action.
      if (subZone === 'lavanderia') {
        const walkSpeedMult = 0.85 + Math.random() * 0.30
        const assignment = this.buildLaundrySequence(npcId, subZone, walkSpeedMult, [], def.duration)
        this.npcAssignments.set(npcId, assignment)
        this.npcTimers.set(npcId, assignment.duration)
        changed.push(assignment)
        continue
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
