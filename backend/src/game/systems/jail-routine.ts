/**
 * Sistema de Rutina/Fases + NPC Libre Albedrío
 * See design/gdd/rutina-fases-npc.md for full specification.
 *
 * Backend authority for:
 *   - 9-phase jail schedule timer (phase:warning, phase:start)
 *   - Weighted random NPC action assignment per phase
 *   - Waypoint occupancy tracking (exclusive/shared slots)
 *   - Social action pairing between NPC pairs/groups
 *   - 25s reassign interval (libre albedrío)
 *   - Player zone validation → phase:zone_check
 */

import {
  GameRoomState,
  JailPhaseNumber,
  NPCAssignment,
  PhaseJailStartPayload,
  PhaseWarningPayload,
  NPCReassignPayload,
  PhaseZoneCheckPayload,
} from '../types.js'

// ─── Tuning Knobs ─────────────────────────────────────────────────────────────

const REASSIGN_INTERVAL_S       = 25    // seconds between reassign ticks
const REASSIGN_CHANGE_PROB      = 0.70  // probability NPC changes action on reassign
const PHASE_WARNING_BEFORE_S    = 10    // seconds before transition to emit warning
const ZONE_CHECK_GRACE_S        = 5     // grace seconds before emitting zone_check
const LOOPING_GRACE_S           = 5     // max extra seconds to finish a LOOPING cycle

// ─── Action Catalog Types ─────────────────────────────────────────────────────

type ActionType = 'SOLO' | 'SOCIAL' | 'LOOPING' | 'IDLE' | 'ONESHOT' | 'ADDITIVE'

interface ActionDef {
  actionId: string
  type: ActionType
  animTrigger: string
  waypointTag: string          // used to look up available waypoints
  weight: number
  minDuration: number          // seconds
  maxDuration: number          // seconds
  loop?: boolean
  socialGroupSize?: number     // 2 for pairs, 4 for card game
  chainLength?: number         // for LOOPING: how many WPs in the chain
}

interface JailPhaseDef {
  phase: JailPhaseNumber
  name: string
  duration: number
  zone: string
  subZones?: string[]          // phase 6 only
  actions: ActionDef[]
}

// ─── Phase Definitions ────────────────────────────────────────────────────────

const JAIL_PHASES: JailPhaseDef[] = [
  {
    // Transición al desayuno. Spawn en puerta de celda → charla informal → comedor.
    phase: 1, name: 'Inicio', duration: 30, zone: 'celda',
    actions: [
      // Spawn: cada NPC arranca parado en la puerta de su celda (20 WPs únicos)
      { actionId: 'spawn_at_door',     type: 'IDLE',    animTrigger: 'idle',          waypointTag: 'cell_door_exit',  weight: 100, minDuration: 3,  maxDuration: 8  },
      // Social sin waypoint: el NPC camina hacia la posición de su pareja (Unity navega al Transform del partner)
      { actionId: 'greet_neighbor',    type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: '',                weight: 30,  minDuration: 5,  maxDuration: 12 },
      // Idle en el lugar (sin waypoint: el NPC se queda donde está)
      { actionId: 'idle_stretch',      type: 'IDLE',    animTrigger: 'stretch',       waypointTag: '',                weight: 20,  minDuration: 2,  maxDuration: 4  },
      { actionId: 'idle_yawn',         type: 'IDLE',    animTrigger: 'yawn',          waypointTag: '',                weight: 15,  minDuration: 1,  maxDuration: 3  },
      // Transición al comedor: NPC navega al entry point del comedor
      { actionId: 'walk_to_cafeteria', type: 'ONESHOT', animTrigger: 'walk_slow',     waypointTag: 'cafeteria_path_', weight: 35,  minDuration: 10, maxDuration: 20 },
    ],
  },
  {
    phase: 2, name: 'Desayuno', duration: 90, zone: 'comedor',
    actions: [
      { actionId: 'cafe_sit_eat',         type: 'IDLE',    animTrigger: 'sit_eat',    waypointTag: 'cafeteria_seat_',        weight: 45, minDuration: 20, maxDuration: 50 },
      { actionId: 'cafe_walk_to_counter', type: 'LOOPING', animTrigger: 'walk',       waypointTag: 'cafeteria_counter_',     weight: 20, minDuration: 10, maxDuration: 18, loop: true, chainLength: 2 },
      { actionId: 'cafe_wait_in_line',    type: 'IDLE',    animTrigger: 'idle_queue', waypointTag: 'cafeteria_line_',        weight: 15, minDuration: 8,  maxDuration: 15 },
      { actionId: 'cafe_talk_seated',     type: 'SOCIAL',  animTrigger: 'talk_seated',waypointTag: 'cafeteria_seat_',        weight: 12, minDuration: 8,  maxDuration: 20 },
      { actionId: 'cafe_clear_tray',      type: 'LOOPING', animTrigger: 'carry_tray', waypointTag: 'cafeteria_tray_deposit_', weight: 8, minDuration: 6,  maxDuration: 12, loop: true, chainLength: 2 },
    ],
  },
  {
    // Primer turno de trabajo: NPCs divididos entre taller y lavandería
    phase: 3, name: 'Trabajo', duration: 90, zone: 'trabajo',
    subZones: ['taller', 'lavanderia'],
    actions: [
      // Taller
      { actionId: 'work_use_workbench',     type: 'IDLE',    animTrigger: 'work_bench',    waypointTag: 'workshop_bench_',     weight: 40, minDuration: 20, maxDuration: 50 },
      { actionId: 'work_carry_box',         type: 'LOOPING', animTrigger: 'carry_box',     waypointTag: 'workshop_shelf_',     weight: 30, minDuration: 12, maxDuration: 20, loop: true, chainLength: 2 },
      { actionId: 'work_inspect_equipment', type: 'IDLE',    animTrigger: 'inspect',       waypointTag: 'workshop_machine_',   weight: 20, minDuration: 10, maxDuration: 20 },
      { actionId: 'work_talk_coworker',     type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: 'workshop_chat_spot_', weight: 10, minDuration: 8,  maxDuration: 15 },
      // Lavandería
      { actionId: 'laundry_load_washer',    type: 'IDLE',    animTrigger: 'load_machine',  waypointTag: 'laundry_washer_',     weight: 30, minDuration: 15, maxDuration: 30 },
      { actionId: 'laundry_fold_clothes',   type: 'IDLE',    animTrigger: 'fold_clothes',  waypointTag: 'laundry_fold_',       weight: 35, minDuration: 20, maxDuration: 40 },
      { actionId: 'laundry_carry_basket',   type: 'LOOPING', animTrigger: 'carry_basket',  waypointTag: 'laundry_washer_',     weight: 25, minDuration: 10, maxDuration: 18, loop: true, chainLength: 2 },
      { actionId: 'laundry_idle_check',     type: 'IDLE',    animTrigger: 'idle_check',    waypointTag: 'laundry_washer_',     weight: 10, minDuration: 5,  maxDuration: 12 },
    ],
  },
  {
    // Hora libre: NPCs eligen entre patio, comedor (charlar) o lavandería (ropa personal)
    phase: 4, name: 'Hora libre', duration: 120, zone: 'libre',
    subZones: ['patio', 'comedor', 'lavanderia'],
    actions: [
      // Patio
      { actionId: 'yard_walk_perimeter',     type: 'LOOPING', animTrigger: 'walk',          waypointTag: 'yard_perimeter_',         weight: 20, minDuration: 30, maxDuration: 60, loop: true, chainLength: 4 },
      { actionId: 'yard_sit_bench',          type: 'IDLE',    animTrigger: 'sit_bench',     waypointTag: 'yard_bench_',             weight: 20, minDuration: 20, maxDuration: 60 },
      { actionId: 'yard_exercise',           type: 'IDLE',    animTrigger: 'exercise',      waypointTag: 'yard_exercise_area_',     weight: 15, minDuration: 15, maxDuration: 40 },
      { actionId: 'yard_conversation_group', type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: 'yard_conversation_spot_', weight: 20, minDuration: 15, maxDuration: 35 },
      { actionId: 'yard_play_cards',         type: 'SOCIAL',  animTrigger: 'sit_cards',     waypointTag: 'yard_card_table_',        weight: 10, minDuration: 30, maxDuration: 90, socialGroupSize: 4 },
      { actionId: 'yard_lean_wall',          type: 'IDLE',    animTrigger: 'lean_wall',     waypointTag: 'yard_wall_lean_',         weight: 8,  minDuration: 15, maxDuration: 40 },
      { actionId: 'yard_shadow_boxing',      type: 'IDLE',    animTrigger: 'shadowbox',     waypointTag: 'yard_exercise_area_',     weight: 5,  minDuration: 10, maxDuration: 20 },
      { actionId: 'yard_kick_ball',          type: 'SOCIAL',  animTrigger: 'kick',          waypointTag: 'yard_ball_spot',          weight: 2,  minDuration: 20, maxDuration: 40 },
      // Comedor (charlar, no comer)
      { actionId: 'free_cafe_sit_talk',      type: 'SOCIAL',  animTrigger: 'talk_seated',   waypointTag: 'cafeteria_seat_',         weight: 40, minDuration: 15, maxDuration: 40 },
      { actionId: 'free_cafe_sit_idle',      type: 'IDLE',    animTrigger: 'sit_idle',      waypointTag: 'cafeteria_seat_',         weight: 35, minDuration: 10, maxDuration: 30 },
      { actionId: 'free_cafe_stand_chat',    type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: 'cafeteria_line_',         weight: 25, minDuration: 10, maxDuration: 25 },
      // Lavandería personal (mismas acciones que turno de trabajo)
      { actionId: 'laundry_load_washer',     type: 'IDLE',    animTrigger: 'load_machine',  waypointTag: 'laundry_washer_',         weight: 30, minDuration: 15, maxDuration: 30 },
      { actionId: 'laundry_fold_clothes',    type: 'IDLE',    animTrigger: 'fold_clothes',  waypointTag: 'laundry_fold_',           weight: 35, minDuration: 20, maxDuration: 40 },
      { actionId: 'laundry_carry_basket',    type: 'LOOPING', animTrigger: 'carry_basket',  waypointTag: 'laundry_washer_',         weight: 25, minDuration: 10, maxDuration: 18, loop: true, chainLength: 2 },
      { actionId: 'laundry_idle_check',      type: 'IDLE',    animTrigger: 'idle_check',    waypointTag: 'laundry_washer_',         weight: 10, minDuration: 5,  maxDuration: 12 },
    ],
  },
  {
    phase: 5, name: 'Almuerzo', duration: 90, zone: 'comedor',
    actions: [
      { actionId: 'cafe_sit_eat',         type: 'IDLE',    animTrigger: 'sit_eat',     waypointTag: 'cafeteria_seat_',         weight: 45, minDuration: 20, maxDuration: 50 },
      { actionId: 'cafe_walk_to_counter', type: 'LOOPING', animTrigger: 'walk',         waypointTag: 'cafeteria_counter_',     weight: 20, minDuration: 10, maxDuration: 18, loop: true, chainLength: 2 },
      { actionId: 'cafe_wait_in_line',    type: 'IDLE',    animTrigger: 'idle_queue',   waypointTag: 'cafeteria_line_',        weight: 15, minDuration: 8,  maxDuration: 15 },
      { actionId: 'cafe_talk_seated',     type: 'SOCIAL',  animTrigger: 'talk_seated',  waypointTag: 'cafeteria_seat_',        weight: 12, minDuration: 8,  maxDuration: 20 },
      { actionId: 'cafe_clear_tray',      type: 'LOOPING', animTrigger: 'carry_tray',   waypointTag: 'cafeteria_tray_deposit_', weight: 8, minDuration: 6,  maxDuration: 12, loop: true, chainLength: 2 },
    ],
  },
  {
    // Segundo turno de trabajo: misma distribución taller/lavandería
    phase: 6, name: 'Trabajo', duration: 120, zone: 'trabajo',
    subZones: ['taller', 'lavanderia'],
    actions: [
      // Taller
      { actionId: 'work_use_workbench',     type: 'IDLE',    animTrigger: 'work_bench',    waypointTag: 'workshop_bench_',     weight: 40, minDuration: 20, maxDuration: 50 },
      { actionId: 'work_carry_box',         type: 'LOOPING', animTrigger: 'carry_box',     waypointTag: 'workshop_shelf_',     weight: 30, minDuration: 12, maxDuration: 20, loop: true, chainLength: 2 },
      { actionId: 'work_inspect_equipment', type: 'IDLE',    animTrigger: 'inspect',       waypointTag: 'workshop_machine_',   weight: 20, minDuration: 10, maxDuration: 20 },
      { actionId: 'work_talk_coworker',     type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: 'workshop_chat_spot_', weight: 10, minDuration: 8,  maxDuration: 15 },
      // Lavandería
      { actionId: 'laundry_load_washer',    type: 'IDLE',    animTrigger: 'load_machine',  waypointTag: 'laundry_washer_',     weight: 30, minDuration: 15, maxDuration: 30 },
      { actionId: 'laundry_fold_clothes',   type: 'IDLE',    animTrigger: 'fold_clothes',  waypointTag: 'laundry_fold_',       weight: 35, minDuration: 20, maxDuration: 40 },
      { actionId: 'laundry_carry_basket',   type: 'LOOPING', animTrigger: 'carry_basket',  waypointTag: 'laundry_washer_',     weight: 25, minDuration: 10, maxDuration: 18, loop: true, chainLength: 2 },
      { actionId: 'laundry_idle_check',     type: 'IDLE',    animTrigger: 'idle_check',    waypointTag: 'laundry_washer_',     weight: 10, minDuration: 5,  maxDuration: 12 },
    ],
  },
  {
    phase: 7, name: 'Siesta', duration: 90, zone: 'celdas',
    actions: [
      { actionId: 'cell_lie_bed',          type: 'IDLE',   animTrigger: 'lie_down',       waypointTag: 'cell_bed_',    weight: 50, minDuration: 30, maxDuration: 90 },
      { actionId: 'cell_sit_bed',          type: 'IDLE',   animTrigger: 'sit_bed_edge',   waypointTag: 'cell_bed_',    weight: 20, minDuration: 15, maxDuration: 40 },
      { actionId: 'cell_read_book',        type: 'IDLE',   animTrigger: 'read_book',      waypointTag: 'cell_desk_',   weight: 15, minDuration: 20, maxDuration: 60 },
      { actionId: 'cell_stare_window',     type: 'IDLE',   animTrigger: 'idle_window',    waypointTag: 'cell_window_', weight: 10, minDuration: 10, maxDuration: 25 },
      { actionId: 'cell_whisper_cellmate', type: 'SOCIAL', animTrigger: 'whisper_seated', waypointTag: 'cell_bed_',    weight: 5,  minDuration: 8,  maxDuration: 20 },
    ],
  },
  {
    phase: 8, name: 'Cena', duration: 90, zone: 'comedor',
    actions: [
      { actionId: 'cafe_sit_eat',         type: 'IDLE',    animTrigger: 'sit_eat',      waypointTag: 'cafeteria_seat_',         weight: 45, minDuration: 20, maxDuration: 50 },
      { actionId: 'cafe_walk_to_counter', type: 'LOOPING', animTrigger: 'walk',          waypointTag: 'cafeteria_counter_',     weight: 20, minDuration: 10, maxDuration: 18, loop: true, chainLength: 2 },
      { actionId: 'cafe_wait_in_line',    type: 'IDLE',    animTrigger: 'idle_queue',    waypointTag: 'cafeteria_line_',        weight: 15, minDuration: 8,  maxDuration: 15 },
      { actionId: 'cafe_talk_seated',     type: 'SOCIAL',  animTrigger: 'talk_seated',   waypointTag: 'cafeteria_seat_',        weight: 12, minDuration: 8,  maxDuration: 20 },
      { actionId: 'cafe_clear_tray',      type: 'LOOPING', animTrigger: 'carry_tray',    waypointTag: 'cafeteria_tray_deposit_', weight: 8, minDuration: 6,  maxDuration: 12, loop: true, chainLength: 2 },
    ],
  },
  {
    phase: 9, name: 'Luces apagadas', duration: 120, zone: 'celdas',
    // AC-9: no SOCIAL actions in phase 9
    actions: [
      { actionId: 'lights_sleep', type: 'IDLE', animTrigger: 'sleep',     waypointTag: 'cell_bed_', weight: 75, minDuration: 90, maxDuration: 120 },
      { actionId: 'lights_toss',  type: 'IDLE', animTrigger: 'toss_turn', waypointTag: 'cell_bed_', weight: 25, minDuration: 5,  maxDuration: 12  },
    ],
  },
]

// ─── Waypoint Pool ────────────────────────────────────────────────────────────

interface WaypointSlot { id: string; cap: number }

function slots(prefix: string, count: number, cap = 1): WaypointSlot[] {
  return Array.from({ length: count }, (_, i) => ({
    id: `${prefix}${String(i + 1).padStart(2, '0')}`,
    cap,
  }))
}

// Static pool: waypointTag → available slot definitions
// Unity WaypointRegistry must define matching IDs with Transform positions.
const WAYPOINT_POOL: Record<string, WaypointSlot[]> = {
  'cell_door_exit':      slots('cell_door_exit_', 20),
  'hallway_slot_':       slots('hallway_slot_', 20),
  'cafeteria_path_':     slots('cafeteria_path_', 5, 4),
  'cafeteria_seat_':     slots('cafeteria_seat_', 16, 2),
  'cafeteria_counter_':  slots('cafeteria_counter_', 6),
  'cafeteria_line_':     slots('cafeteria_line_', 8),
  'cafeteria_tray_deposit_': slots('cafeteria_tray_deposit_', 3),
  'corridor_start_':     slots('corridor_start_', 6),
  'clean_zone_':         slots('clean_zone_', 8),
  'cell_door_clean_':    slots('cell_door_clean_', 12),
  'supply_closet_':      [{ id: 'supply_closet_01', cap: 2 }],
  'corridor_idle_':      slots('corridor_idle_', 6),
  'corridor_chat_spot_': slots('corridor_chat_spot_', 4, 2),
  'yard_perimeter_':     slots('yard_perimeter_', 8),
  'yard_bench_':         slots('yard_bench_', 8),
  'yard_exercise_area_': slots('yard_exercise_area_', 4, 2),
  'yard_conversation_spot_': slots('yard_conversation_spot_', 6, 2),
  'yard_card_table_':    slots('yard_card_table_', 2, 4),
  'yard_wall_lean_':     slots('yard_wall_lean_', 6),
  'yard_ball_spot':      [{ id: 'yard_ball_spot', cap: 2 }],
  'workshop_bench_':     slots('workshop_bench_', 6),
  'workshop_shelf_':     slots('workshop_shelf_', 4),
  'workshop_machine_':   slots('workshop_machine_', 4),
  'workshop_chat_spot_': slots('workshop_chat_spot_', 3, 2),
  'laundry_washer_':     slots('laundry_washer_', 6),
  'laundry_fold_':       slots('laundry_fold_', 6),
  'laundry_dryer_':      slots('laundry_dryer_', 4),
  'floor_start_':        slots('floor_start_', 4),
  'floor_zone_':         slots('floor_zone_', 6),
  'floor_bucket_start':  [{ id: 'floor_bucket_start', cap: 3 }],
  'floor_drain_':        slots('floor_drain_', 3),
  'floor_rest_spot_':    slots('floor_rest_spot_', 4),
  // cell_* tags are resolved dynamically per NPC's assigned cell
  'cell_bed_':    [],
  'cell_desk_':   [],
  'cell_window_': [],
}

// Sub-zone → valid action IDs (used by phases 3, 4, 6)
const SUBZONE_ACTIONS: Record<string, string[]> = {
  // Fases 3 y 6 — Trabajo
  taller:     ['work_use_workbench', 'work_carry_box', 'work_inspect_equipment', 'work_talk_coworker'],
  lavanderia: ['laundry_load_washer', 'laundry_fold_clothes', 'laundry_carry_basket', 'laundry_idle_check'],
  // Fase 4 — Hora libre
  patio:      ['yard_walk_perimeter', 'yard_sit_bench', 'yard_exercise', 'yard_conversation_group', 'yard_play_cards', 'yard_lean_wall', 'yard_shadow_boxing', 'yard_kick_ball'],
  comedor:    ['free_cafe_sit_talk', 'free_cafe_sit_idle', 'free_cafe_stand_chat'],
}

// ─── Zone Map (for player zone validation) ────────────────────────────────────

// Maps jail phase zone names to coordinate bounds (matches prison-layout.ts)
const ZONE_BOUNDS: Record<string, { minX: number; maxX: number; minZ: number; maxZ: number }> = {
  celda:         { minX: -20, maxX: 20, minZ: -40, maxZ: -25 },
  celdas:        { minX: -20, maxX: 20, minZ: -40, maxZ: -25 },
  comedor:       { minX: -20, maxX: 20, minZ:  25, maxZ:  40 },
  pasillos:      { minX: -25, maxX: 25, minZ: -25, maxZ: -15 },
  patio_exterior:{ minX: -30, maxX: 30, minZ: -15, maxZ:  15 },
  trabajo:       { minX: -30, maxX: 30, minZ: -15, maxZ:  40 }, // all work areas combined
}

// ─── JailRoutineSystem ────────────────────────────────────────────────────────

export class JailRoutineSystem {
  // Callbacks set by GameManager / room-manager after construction
  onPhaseWarning!: (payload: PhaseWarningPayload) => void
  onPhaseStart!:   (payload: PhaseJailStartPayload) => void
  onNPCReassign!:  (payload: NPCReassignPayload) => void
  onZoneCheck!:    (playerId: string, payload: PhaseZoneCheckPayload) => void

  private currentPhase: JailPhaseNumber = 1
  private phaseStartedAt  = 0  // Date.now() ms
  private warningEmitted  = false
  private lastReassignAt  = 0  // Date.now() ms
  private zoneCheckDoneAt = 0  // Date.now() ms (for per-phase zone check)
  private zoneCheckedPlayers = new Set<string>()

  // Per-NPC state
  private npcAssignments  = new Map<string, NPCAssignment>()  // npcId → current assignment
  private npcTimers       = new Map<string, number>()         // npcId → remaining secs
  private npcCells        = new Map<string, string>()         // npcId → cell number "00"–"09"
  private npcSubZones     = new Map<string, string>()         // npcId → subzone (phase 6 lock)
  private npcPartners     = new Map<string, string>()         // npcId → socialPartnerId

  // Waypoint occupancy: waypointId → set of npcIds
  private waypointOccupants = new Map<string, Set<string>>()

  constructor(private state: GameRoomState) {
    this.assignCells()
  }

  /** Called when the game transitions to active state. */
  start(): void {
    this.currentPhase   = 1
    this.phaseStartedAt = Date.now()
    this.warningEmitted = false
    this.lastReassignAt = Date.now()
    this.zoneCheckDoneAt = 0
    this.zoneCheckedPlayers.clear()
    this.releaseAllWaypoints()
    this.emitPhaseStart()
    console.log('[JAIL] Routine started → Phase 1 (Inicio)')
  }

  /** Main update: called from tick() every 50ms. tickDelta in seconds. */
  update(tickDelta: number): void {
    if (this.phaseStartedAt === 0) return  // not started

    this.updateNPCTimers(tickDelta)
    this.checkPhaseTimer()
    this.checkReassignInterval()
    this.checkZoneViolations()
  }

  getCurrentJailPhase(): JailPhaseNumber { return this.currentPhase }
  getCurrentZone(): string { return this.getPhaseDef(this.currentPhase)?.zone ?? 'unknown' }

  /** Returns full assignment snapshot (for reconnecting clients). */
  buildReconnectAssignments(): NPCAssignment[] {
    return Array.from(this.npcAssignments.values())
  }

  // ─── Private: Phase Timer ─────────────────────────────────────────────────

  private checkPhaseTimer(): void {
    const def = this.getPhaseDef(this.currentPhase)
    if (!def) return

    const elapsed = (Date.now() - this.phaseStartedAt) / 1000

    // Emit warning 10s before transition
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

    // Transition when duration expires
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
    this.releaseAllWaypoints()
    this.npcSubZones.clear()    // reset sub-zone locks for new phase
    this.emitPhaseStart()
    console.log(`[JAIL] Phase ${this.currentPhase} (${this.getPhaseDef(this.currentPhase)!.name})`)
  }

  private nextPhaseNumber(current: JailPhaseNumber): JailPhaseNumber {
    return (current === 9 ? 1 : current + 1) as JailPhaseNumber
  }

  // ─── Private: NPC Assignment ──────────────────────────────────────────────

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

    console.log(`[NPC-ACTIONS] Phase ${this.currentPhase} (${def.name}) — assignments:`)
    assignments.forEach(a => {
      console.log(`  npc=${a.npcId}  action=${a.actionId}  anim=${a.animTrigger}  dur=${a.duration}s${a.waypointId ? `  wp=${a.waypointId}` : ''}${a.socialPartnerId ? `  partner=${a.socialPartnerId}` : ''}`)
    })
  }

  /** Builds assignments for ALL NPCs for the given phase. */
  private buildPhaseAssignments(def: JailPhaseDef): NPCAssignment[] {
    const npcIds = Array.from(this.state.npcs.keys())
    const assignments: NPCAssignment[] = []
    const paired = new Set<string>()  // NPCs already assigned a social partner

    // Phase 6: assign sub-zones first (balanced distribution)
    if (def.subZones && def.subZones.length > 0) {
      this.distributeSubZones(npcIds, def.subZones)
    }

    for (const npcId of npcIds) {
      if (paired.has(npcId)) continue  // already handled as a social partner

      const subZone  = this.npcSubZones.get(npcId)
      const actionPool = this.getActionPool(def, subZone)

      const action = this.weightedRandom(actionPool)
      if (!action) {
        assignments.push(this.buildIdleAssignment(npcId))
        continue
      }

      if (action.type === 'SOCIAL') {
        const partner = this.findPartner(npcIds, paired, npcId)
        if (partner) {
          const wp = this.reserveWaypoint(npcId, action.waypointTag)
          const dur = this.randomDuration(action)
          const a1: NPCAssignment = {
            npcId,
            actionId:        action.actionId,
            animTrigger:     action.animTrigger,
            waypointId:      wp ?? undefined,
            duration:        dur,
            socialPartnerId: partner,
            subZone:         subZone ?? undefined,
          }
          const a2: NPCAssignment = {
            npcId:           partner,
            actionId:        action.actionId,
            animTrigger:     action.animTrigger,
            waypointId:      wp ?? undefined,
            duration:        dur,
            socialPartnerId: npcId,
            subZone:         this.npcSubZones.get(partner) ?? undefined,
          }
          assignments.push(a1, a2)
          paired.add(npcId)
          paired.add(partner)
          this.npcPartners.set(npcId, partner)
          this.npcPartners.set(partner, npcId)
          continue
        }
        // No partner available: fall through to solo action
      }

      const assignment = this.buildSoloAssignment(npcId, action, subZone)
      assignments.push(assignment)
    }

    return assignments
  }

  /** Builds a single NPC's assignment for a given action. */
  private buildSoloAssignment(npcId: string, action: ActionDef, subZone?: string): NPCAssignment {
    const dur = this.randomDuration(action)

    if (action.loop && action.chainLength) {
      const chain = this.reserveWaypointChain(npcId, action.waypointTag, action.chainLength)
      return {
        npcId,
        actionId:    action.actionId,
        animTrigger: action.animTrigger,
        waypointChain: chain.length > 0 ? chain : undefined,
        duration:    dur,
        loop:        true,
        subZone:     subZone ?? undefined,
      }
    }

    // Cell actions: resolve to this NPC's assigned cell waypoints
    const wpTag  = this.resolveCellTag(npcId, action.waypointTag)
    const wp     = this.reserveWaypoint(npcId, wpTag)
    return {
      npcId,
      actionId:    action.actionId,
      animTrigger: action.animTrigger,
      waypointId:  wp ?? undefined,
      duration:    dur,
      subZone:     subZone ?? undefined,
    }
  }

  private buildIdleAssignment(npcId: string): NPCAssignment {
    return { npcId, actionId: 'idle_stand', animTrigger: 'idle', duration: 10 }
  }

  // ─── Private: Reassign (Libre Albedrío) ──────────────────────────────────

  private checkReassignInterval(): void {
    const now = Date.now()
    if ((now - this.lastReassignAt) / 1000 < REASSIGN_INTERVAL_S) return

    this.lastReassignAt = now
    const def = this.getPhaseDef(this.currentPhase)!
    const changed: NPCAssignment[] = []

    for (const [npcId, timer] of this.npcTimers) {
      // Only reassign NPCs whose action is almost done
      if (timer > 5) continue
      if (Math.random() > REASSIGN_CHANGE_PROB) continue

      const subZone    = this.npcSubZones.get(npcId)
      const actionPool = this.getActionPool(def, subZone)
      const current    = this.npcAssignments.get(npcId)
      // Exclude current action to force variety
      const pool       = actionPool.filter(a => a.actionId !== current?.actionId)
      const action     = this.weightedRandom(pool)
      if (!action) continue

      // Skip social (partner logic is complex for incremental reassign)
      if (action.type === 'SOCIAL') continue

      // Release old waypoint
      if (current?.waypointId)   this.releaseWaypoint(npcId, current.waypointId)
      if (current?.waypointChain) current.waypointChain.forEach(wp => this.releaseWaypoint(npcId, wp))

      const assignment = this.buildSoloAssignment(npcId, action, subZone)
      this.npcAssignments.set(npcId, assignment)
      this.npcTimers.set(npcId, assignment.duration)
      changed.push(assignment)
    }

    if (changed.length > 0) {
      this.onNPCReassign?.({ timestamp: Date.now(), assignments: changed })
      console.log(`[NPC-ACTIONS] Reassigned ${changed.length} NPCs (libre albedrío):`)
      changed.forEach(a => {
        console.log(`  npc=${a.npcId}  action=${a.actionId}  anim=${a.animTrigger}  dur=${a.duration}s${a.waypointId ? `  wp=${a.waypointId}` : ''}`)
      })
    }
  }

  // ─── Private: Player Zone Check ──────────────────────────────────────────

  private checkZoneViolations(): void {
    // Only check once per phase, 5 seconds after phase start
    const elapsed = (Date.now() - this.phaseStartedAt) / 1000
    if (elapsed < ZONE_CHECK_GRACE_S) return
    if (this.zoneCheckDoneAt !== 0) return  // already done for this phase

    this.zoneCheckDoneAt = Date.now()
    const expectedZone = this.getCurrentZone()
    const bounds       = ZONE_BOUNDS[expectedZone]
    if (!bounds) return

    for (const [socketId, player] of this.state.players) {
      if (player.role !== 'prisoner') continue
      if (this.zoneCheckedPlayers.has(socketId)) continue

      const { x, z } = player.position
      const inZone =
        x >= bounds.minX && x <= bounds.maxX &&
        z >= bounds.minZ && z <= bounds.maxZ

      if (!inZone) {
        const detectedZone = this.detectZone(x, z)
        this.onZoneCheck?.(socketId, {
          playerId:     socketId,
          currentZone:  detectedZone,
          expectedZone,
          phase:        this.currentPhase,
          graceSeconds: ZONE_CHECK_GRACE_S,
        })
        this.zoneCheckedPlayers.add(socketId)
        console.log(`[JAIL] Zone check: player ${socketId} in "${detectedZone}", expected "${expectedZone}"`)
      }
    }
  }

  private detectZone(x: number, z: number): string {
    for (const [name, b] of Object.entries(ZONE_BOUNDS)) {
      if (x >= b.minX && x <= b.maxX && z >= b.minZ && z <= b.maxZ) return name
    }
    return 'unknown'
  }

  // ─── Private: NPC Timers ──────────────────────────────────────────────────

  private updateNPCTimers(tickDelta: number): void {
    for (const [npcId, timer] of this.npcTimers) {
      const next = Math.max(0, timer - tickDelta)
      this.npcTimers.set(npcId, next)
    }
  }

  // ─── Private: Waypoint Management ────────────────────────────────────────

  /**
   * Reserves an available waypoint slot for the given tag.
   * Returns the waypoint ID or null if all slots are full.
   */
  private reserveWaypoint(npcId: string, tag: string): string | null {
    const pool = WAYPOINT_POOL[tag]
    if (!pool || pool.length === 0) return null

    for (const slot of pool) {
      const occ = this.waypointOccupants.get(slot.id) ?? new Set()
      if (occ.size < slot.cap) {
        occ.add(npcId)
        this.waypointOccupants.set(slot.id, occ)
        return slot.id
      }
    }
    return null  // all slots full
  }

  private reserveWaypointChain(npcId: string, tag: string, count: number): string[] {
    const pool  = WAYPOINT_POOL[tag]
    if (!pool || pool.length === 0) return []
    const chain: string[] = []
    // Pick `count` different waypoints from the pool (cycling if needed)
    const available = pool.filter(s => {
      const occ = this.waypointOccupants.get(s.id)
      return !occ || occ.size < s.cap
    })
    const src = available.length > 0 ? available : pool  // fallback to any
    for (let i = 0; i < count; i++) {
      const slot = src[i % src.length]
      const occ  = this.waypointOccupants.get(slot.id) ?? new Set()
      occ.add(npcId)
      this.waypointOccupants.set(slot.id, occ)
      chain.push(slot.id)
    }
    return chain
  }

  private releaseWaypoint(npcId: string, waypointId: string): void {
    const occ = this.waypointOccupants.get(waypointId)
    if (occ) occ.delete(npcId)
  }

  private releaseAllWaypoints(): void {
    this.waypointOccupants.clear()
    this.npcPartners.clear()
  }

  /**
   * Resolves cell-specific waypoint tags (cell_bed_, cell_desk_, cell_window_)
   * to the NPC's assigned cell tag (e.g. "cell_03_bed_").
   */
  private resolveCellTag(npcId: string, tag: string): string {
    if (!tag.startsWith('cell_')) return tag
    const cellNum = this.npcCells.get(npcId) ?? '00'
    // e.g. "cell_bed_" → "cell_03_bed_"
    return tag.replace('cell_', `cell_${cellNum}_`)
  }

  // ─── Private: Sub-zone Distribution (Phase 6) ────────────────────────────

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

  // ─── Private: Action Pool Filtering ──────────────────────────────────────

  /**
   * Returns the relevant action pool for the current phase.
   * For phase 6, filters by the NPC's sub-zone.
   */
  private getActionPool(def: JailPhaseDef, subZone?: string): ActionDef[] {
    // Phases with sub-zones (3 Trabajo, 4 Hora libre, 6 Trabajo): filter by assigned sub-zone
    if (def.subZones && def.subZones.length > 0 && subZone) {
      const allowed = SUBZONE_ACTIONS[subZone] ?? []
      return def.actions.filter(a => allowed.includes(a.actionId))
    }
    // Phase 9: no social actions (AC-9)
    if (def.phase === 9) {
      return def.actions.filter(a => a.type !== 'SOCIAL')
    }
    return def.actions
  }

  // ─── Private: Weighted Random ────────────────────────────────────────────

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

  // ─── Private: Social Partner ─────────────────────────────────────────────

  private findPartner(
    npcIds: string[],
    paired: Set<string>,
    requesterId: string
  ): string | null {
    const candidates = npcIds.filter(id => id !== requesterId && !paired.has(id))
    if (candidates.length === 0) return null
    return candidates[Math.floor(Math.random() * candidates.length)]
  }

  // ─── Private: Cell Assignment ────────────────────────────────────────────

  /**
   * Pre-assigns each NPC to a cell (2 NPCs per cell, 10 cells for 20 NPCs).
   * NPC 000, 001 → cell "00"; NPC 002, 003 → cell "01"; etc.
   */
  private assignCells(): void {
    const npcIds = Array.from(this.state.npcs.keys()).sort()
    npcIds.forEach((id, i) => {
      const cellNum = Math.floor(i / 2)
      this.npcCells.set(id, String(cellNum).padStart(2, '0'))
    })
  }

  // ─── Private: Helpers ────────────────────────────────────────────────────

  private getPhaseDef(phase: JailPhaseNumber): JailPhaseDef | undefined {
    return JAIL_PHASES.find(p => p.phase === phase)
  }
}
