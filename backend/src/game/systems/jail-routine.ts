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
  NPCActionStep,
  PhaseJailStartPayload,
  PhaseWarningPayload,
  NPCReassignPayload,
  PhaseZoneCheckPayload,
} from '../types.js'
import { NPCPersonalitySystem } from './npc-personality.js'

// ─── Tuning Knobs ─────────────────────────────────────────────────────────────

const REASSIGN_INTERVAL_S            = 20    // seconds between reassign ticks (reduced for more life)
const REASSIGN_CHANGE_PROB           = 0.80  // probability NPC changes action on reassign
const PHASE_WARNING_BEFORE_S         = 10    // seconds before transition to emit warning
const ZONE_CHECK_GRACE_S             = 5     // grace seconds before emitting zone_check
const LOOPING_GRACE_S                = 5     // max extra seconds to finish a LOOPING cycle
// ─── Organic Transition Knobs ──────────────────────────────────────────────────
const TRANSITION_LINGER_MAX_S        = 20    // max linger before NPC heads to phase zone
const TRANSITION_DETOUR_PROB         = 0.40  // chance of taking a corridor detour en route
const TRANSITION_ENROUTE_CHAT_PROB   = 0.30  // chance of stopping to chat in the corridor
const SUBZONE_CHANGE_PROB            = 0.25  // chance NPC switches sub-zone on reassign (phases 4/7)

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
    // Transición orgánica al desayuno. Spawn en puerta de celda → interacciones informales → caminar al comedor.
    // El backend encadena acciones para cada NPC: spawn → [greet/idle] → walk_to_cafeteria → [talk_at_door]
    // Esto se resuelve con actionSequence en buildPhase1Assignments().
    phase: 1, name: 'Inicio', duration: 30, zone: 'celda',
    actions: [
      { actionId: 'spawn_at_door',          type: 'IDLE',    animTrigger: 'Idle',          waypointTag: 'cell_door_exit',              weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'greet_neighbor',         type: 'SOCIAL',  animTrigger: 'Salute',        waypointTag: '',                            weight: 35,  minDuration: 3,  maxDuration: 4  },
      { actionId: 'talk_standing',          type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: '',                            weight: 30,  minDuration: 6,  maxDuration: 7  },
      { actionId: 'idle_stretch',           type: 'IDLE',    animTrigger: 'stretch',       waypointTag: '',                            weight: 15,  minDuration: 2,  maxDuration: 4  },
      { actionId: 'idle_yawn',              type: 'IDLE',    animTrigger: 'yawn',          waypointTag: '',                            weight: 10,  minDuration: 1,  maxDuration: 3  },
      { actionId: 'walk_to_cafeteria_area', type: 'ONESHOT', animTrigger: 'walk_slow',     waypointTag: 'cafeteria_entrance_spot_',    weight: 40,  minDuration: 0,  maxDuration: 0  },
      { actionId: 'talk_at_cafeteria_door', type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: 'cafeteria_entrance_spot_',    weight: 25,  minDuration: 6,  maxDuration: 7  },
    ],
  },
  {
    // Desayuno: flujo ordenado obligatorio (counter → grab → seat → eat → trash → clear).
    // Resuelto con actionSequence en buildCafeteriaSequence().
    // Las acciones aquí son referencia para el catálogo y reassign fallback.
    phase: 2, name: 'Desayuno', duration: 90, zone: 'comedor',
    actions: [
      { actionId: 'cafe_wait_outside_talk',  type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: 'cafeteria_entrance_spot_',  weight: 20,  minDuration: 6,  maxDuration: 7  },
      { actionId: 'cafe_walk_to_counter',    type: 'ONESHOT', animTrigger: 'Walking',       waypointTag: 'cafeteria_counter_',        weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_grab_food',          type: 'IDLE',    animTrigger: 'serve_self',    waypointTag: 'cafeteria_counter_',        weight: 100, minDuration: 4,  maxDuration: 6  },
      { actionId: 'cafe_walk_to_seat',       type: 'ONESHOT', animTrigger: 'Walking',       waypointTag: 'cafeteria_seat_',           weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_sit_eat',            type: 'IDLE',    animTrigger: 'sit_eat',       waypointTag: 'cafeteria_seat_',           weight: 60,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_sit_eat_talk',       type: 'SOCIAL',  animTrigger: 'sit_eat_talk',  waypointTag: 'cafeteria_seat_',           weight: 40,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_walk_to_trash',      type: 'ONESHOT', animTrigger: 'carry_tray',    waypointTag: 'cafeteria_tray_deposit_',   weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_clear_tray',         type: 'IDLE',    animTrigger: 'deposit_tray',  waypointTag: 'cafeteria_tray_deposit_',   weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'cafe_talk_after_trash',   type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: 'cafeteria_tray_deposit_',   weight: 25,  minDuration: 6,  maxDuration: 7  },
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
    // Hora libre: NPCs eligen libremente entre patio, comedor (charlar), lavandería (ropa personal) o celdas (descanso).
    // Los NPCs pueden cambiar de sub-zona durante la fase (libre albedrío).
    phase: 4, name: 'Hora libre', duration: 120, zone: 'libre',
    subZones: ['patio', 'comedor', 'lavanderia', 'celdas'],
    actions: [
      // Patio
      { actionId: 'yard_walk_perimeter',     type: 'LOOPING', animTrigger: 'Walking',          waypointTag: 'yard_perimeter_',         weight: 20, minDuration: 30, maxDuration: 60, loop: true, chainLength: 4 },
      { actionId: 'yard_sit_bench',          type: 'IDLE',    animTrigger: 'sit_bench',        waypointTag: 'yard_bench_',             weight: 20, minDuration: 20, maxDuration: 60 },
      { actionId: 'yard_exercise',           type: 'IDLE',    animTrigger: 'exercise',         waypointTag: 'yard_exercise_area_',     weight: 15, minDuration: 15, maxDuration: 40 },
      { actionId: 'yard_conversation_group', type: 'SOCIAL',  animTrigger: 'talk_standing',    waypointTag: 'yard_conversation_spot_', weight: 20, minDuration: 15, maxDuration: 35 },
      { actionId: 'yard_play_cards',         type: 'SOCIAL',  animTrigger: 'sit_cards',        waypointTag: 'yard_card_table_',        weight: 10, minDuration: 30, maxDuration: 90, socialGroupSize: 4 },
      { actionId: 'yard_lean_wall',          type: 'IDLE',    animTrigger: 'lean_wall',        waypointTag: 'yard_wall_lean_',         weight: 8,  minDuration: 15, maxDuration: 40 },
      { actionId: 'yard_shadow_boxing',      type: 'IDLE',    animTrigger: 'shadowbox',        waypointTag: 'yard_exercise_area_',     weight: 5,  minDuration: 10, maxDuration: 20 },
      { actionId: 'yard_kick_ball',          type: 'SOCIAL',  animTrigger: 'kick',             waypointTag: 'yard_ball_spot',          weight: 2,  minDuration: 20, maxDuration: 40 },
      // Patio — acciones de personalidad (sesgadas por archetype/mood)
      { actionId: 'yard_pushups',            type: 'IDLE',    animTrigger: 'PushUp',           waypointTag: 'yard_exercise_area_',     weight: 8,  minDuration: 10, maxDuration: 25 },
      { actionId: 'yard_pace_nervous',       type: 'IDLE',    animTrigger: 'pace',             waypointTag: 'yard_perimeter_',         weight: 5,  minDuration: 8,  maxDuration: 20 },
      { actionId: 'yard_stare_wall',         type: 'IDLE',    animTrigger: 'idle_window',      waypointTag: 'yard_wall_lean_',         weight: 4,  minDuration: 10, maxDuration: 30 },
      { actionId: 'yard_whisper_secret',     type: 'SOCIAL',  animTrigger: 'whisper',          waypointTag: 'yard_conversation_spot_', weight: 6,  minDuration: 8,  maxDuration: 15 },
      { actionId: 'yard_argue_loud',         type: 'SOCIAL',  animTrigger: 'argue',            waypointTag: 'yard_conversation_spot_', weight: 3,  minDuration: 6,  maxDuration: 12 },
      { actionId: 'yard_sit_floor',          type: 'IDLE',    animTrigger: 'sit_floor',        waypointTag: 'yard_wall_lean_',         weight: 4,  minDuration: 12, maxDuration: 30 },
      // Comedor (charlar, no comer)
      { actionId: 'free_cafe_sit_talk',      type: 'SOCIAL',  animTrigger: 'talk_seated',      waypointTag: 'cafeteria_seat_',         weight: 40, minDuration: 15, maxDuration: 40 },
      { actionId: 'free_cafe_sit_idle',      type: 'IDLE',    animTrigger: 'sit_idle',         waypointTag: 'cafeteria_seat_',         weight: 35, minDuration: 10, maxDuration: 30 },
      { actionId: 'free_cafe_stand_chat',    type: 'SOCIAL',  animTrigger: 'talk_standing',    waypointTag: 'cafeteria_line_',         weight: 25, minDuration: 10, maxDuration: 25 },
      // Lavandería personal (mismas acciones que turno de trabajo)
      { actionId: 'laundry_load_washer',     type: 'IDLE',    animTrigger: 'load_machine',     waypointTag: 'laundry_washer_',         weight: 30, minDuration: 15, maxDuration: 30 },
      { actionId: 'laundry_fold_clothes',    type: 'IDLE',    animTrigger: 'fold_clothes',     waypointTag: 'laundry_fold_',           weight: 35, minDuration: 20, maxDuration: 40 },
      { actionId: 'laundry_carry_basket',    type: 'LOOPING', animTrigger: 'carry_basket',     waypointTag: 'laundry_washer_',         weight: 25, minDuration: 10, maxDuration: 18, loop: true, chainLength: 2 },
      { actionId: 'laundry_idle_check',      type: 'IDLE',    animTrigger: 'idle_check',       waypointTag: 'laundry_washer_',         weight: 10, minDuration: 5,  maxDuration: 12 },
      // Celdas (descanso — NPC vuelve a su celda asignada)
      { actionId: 'cell_lie_bed',            type: 'IDLE',    animTrigger: 'lie_down',         waypointTag: 'cell_bed_',               weight: 60, minDuration: 20, maxDuration: 60 },
      { actionId: 'cell_sit_bed',            type: 'IDLE',    animTrigger: 'sit_bed_edge',     waypointTag: 'cell_bed_',               weight: 40, minDuration: 15, maxDuration: 40 },
      // Celdas — acciones de personalidad
      { actionId: 'cell_read_book',          type: 'IDLE',    animTrigger: 'read_book',        waypointTag: 'cell_bed_',               weight: 20, minDuration: 15, maxDuration: 40 },
      { actionId: 'cell_idle_window',        type: 'IDLE',    animTrigger: 'idle_window',      waypointTag: 'cell_bed_',               weight: 15, minDuration: 10, maxDuration: 30 },
    ],
  },
  {
    // Almuerzo: mismo flujo ordenado que Desayuno. buildCafeteriaSequence() reutilizado.
    phase: 5, name: 'Almuerzo', duration: 90, zone: 'comedor',
    actions: [
      { actionId: 'cafe_wait_outside_talk',  type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: 'cafeteria_entrance_spot_',  weight: 20,  minDuration: 6,  maxDuration: 7  },
      { actionId: 'cafe_walk_to_counter',    type: 'ONESHOT', animTrigger: 'Walking',       waypointTag: 'cafeteria_counter_',        weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_grab_food',          type: 'IDLE',    animTrigger: 'serve_self',    waypointTag: 'cafeteria_counter_',        weight: 100, minDuration: 4,  maxDuration: 6  },
      { actionId: 'cafe_walk_to_seat',       type: 'ONESHOT', animTrigger: 'Walking',       waypointTag: 'cafeteria_seat_',           weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_sit_eat',            type: 'IDLE',    animTrigger: 'sit_eat',       waypointTag: 'cafeteria_seat_',           weight: 60,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_sit_eat_talk',       type: 'SOCIAL',  animTrigger: 'sit_eat_talk',  waypointTag: 'cafeteria_seat_',           weight: 40,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_walk_to_trash',      type: 'ONESHOT', animTrigger: 'carry_tray',    waypointTag: 'cafeteria_tray_deposit_',   weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_clear_tray',         type: 'IDLE',    animTrigger: 'deposit_tray',  waypointTag: 'cafeteria_tray_deposit_',   weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'cafe_talk_after_trash',   type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: 'cafeteria_tray_deposit_',   weight: 25,  minDuration: 6,  maxDuration: 7  },
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
    // Segundo turno de hora libre: idéntico a Fase 4. Los NPCs pueden cambiar de sub-zona respecto al primer turno.
    phase: 7, name: 'Hora libre', duration: 90, zone: 'libre',
    subZones: ['patio', 'comedor', 'lavanderia', 'celdas'],
    actions: [
      // Patio
      { actionId: 'yard_walk_perimeter',     type: 'LOOPING', animTrigger: 'Walking',          waypointTag: 'yard_perimeter_',         weight: 20, minDuration: 30, maxDuration: 60, loop: true, chainLength: 4 },
      { actionId: 'yard_sit_bench',          type: 'IDLE',    animTrigger: 'sit_bench',        waypointTag: 'yard_bench_',             weight: 20, minDuration: 20, maxDuration: 60 },
      { actionId: 'yard_exercise',           type: 'IDLE',    animTrigger: 'exercise',         waypointTag: 'yard_exercise_area_',     weight: 15, minDuration: 15, maxDuration: 40 },
      { actionId: 'yard_conversation_group', type: 'SOCIAL',  animTrigger: 'talk_standing',    waypointTag: 'yard_conversation_spot_', weight: 20, minDuration: 15, maxDuration: 35 },
      { actionId: 'yard_play_cards',         type: 'SOCIAL',  animTrigger: 'sit_cards',        waypointTag: 'yard_card_table_',        weight: 10, minDuration: 30, maxDuration: 90, socialGroupSize: 4 },
      { actionId: 'yard_lean_wall',          type: 'IDLE',    animTrigger: 'lean_wall',        waypointTag: 'yard_wall_lean_',         weight: 8,  minDuration: 15, maxDuration: 40 },
      { actionId: 'yard_shadow_boxing',      type: 'IDLE',    animTrigger: 'shadowbox',        waypointTag: 'yard_exercise_area_',     weight: 5,  minDuration: 10, maxDuration: 20 },
      { actionId: 'yard_kick_ball',          type: 'SOCIAL',  animTrigger: 'kick',             waypointTag: 'yard_ball_spot',          weight: 2,  minDuration: 20, maxDuration: 40 },
      // Patio — acciones de personalidad (sesgadas por archetype/mood)
      { actionId: 'yard_pushups',            type: 'IDLE',    animTrigger: 'PushUp',           waypointTag: 'yard_exercise_area_',     weight: 8,  minDuration: 10, maxDuration: 25 },
      { actionId: 'yard_pace_nervous',       type: 'IDLE',    animTrigger: 'pace',             waypointTag: 'yard_perimeter_',         weight: 5,  minDuration: 8,  maxDuration: 20 },
      { actionId: 'yard_stare_wall',         type: 'IDLE',    animTrigger: 'idle_window',      waypointTag: 'yard_wall_lean_',         weight: 4,  minDuration: 10, maxDuration: 30 },
      { actionId: 'yard_whisper_secret',     type: 'SOCIAL',  animTrigger: 'whisper',          waypointTag: 'yard_conversation_spot_', weight: 6,  minDuration: 8,  maxDuration: 15 },
      { actionId: 'yard_argue_loud',         type: 'SOCIAL',  animTrigger: 'argue',            waypointTag: 'yard_conversation_spot_', weight: 3,  minDuration: 6,  maxDuration: 12 },
      { actionId: 'yard_sit_floor',          type: 'IDLE',    animTrigger: 'sit_floor',        waypointTag: 'yard_wall_lean_',         weight: 4,  minDuration: 12, maxDuration: 30 },
      // Comedor (charlar, no comer)
      { actionId: 'free_cafe_sit_talk',      type: 'SOCIAL',  animTrigger: 'talk_seated',      waypointTag: 'cafeteria_seat_',         weight: 40, minDuration: 15, maxDuration: 40 },
      { actionId: 'free_cafe_sit_idle',      type: 'IDLE',    animTrigger: 'sit_idle',         waypointTag: 'cafeteria_seat_',         weight: 35, minDuration: 10, maxDuration: 30 },
      { actionId: 'free_cafe_stand_chat',    type: 'SOCIAL',  animTrigger: 'talk_standing',    waypointTag: 'cafeteria_line_',         weight: 25, minDuration: 10, maxDuration: 25 },
      // Lavandería personal
      { actionId: 'laundry_load_washer',     type: 'IDLE',    animTrigger: 'load_machine',     waypointTag: 'laundry_washer_',         weight: 30, minDuration: 15, maxDuration: 30 },
      { actionId: 'laundry_fold_clothes',    type: 'IDLE',    animTrigger: 'fold_clothes',     waypointTag: 'laundry_fold_',           weight: 35, minDuration: 20, maxDuration: 40 },
      { actionId: 'laundry_carry_basket',    type: 'LOOPING', animTrigger: 'carry_basket',     waypointTag: 'laundry_washer_',         weight: 25, minDuration: 10, maxDuration: 18, loop: true, chainLength: 2 },
      { actionId: 'laundry_idle_check',      type: 'IDLE',    animTrigger: 'idle_check',       waypointTag: 'laundry_washer_',         weight: 10, minDuration: 5,  maxDuration: 12 },
      // Celdas (descanso)
      { actionId: 'cell_lie_bed',            type: 'IDLE',    animTrigger: 'lie_down',         waypointTag: 'cell_bed_',               weight: 60, minDuration: 20, maxDuration: 60 },
      { actionId: 'cell_sit_bed',            type: 'IDLE',    animTrigger: 'sit_bed_edge',     waypointTag: 'cell_bed_',               weight: 40, minDuration: 15, maxDuration: 40 },
      // Celdas — acciones de personalidad
      { actionId: 'cell_read_book',          type: 'IDLE',    animTrigger: 'read_book',        waypointTag: 'cell_bed_',               weight: 20, minDuration: 15, maxDuration: 40 },
      { actionId: 'cell_idle_window',        type: 'IDLE',    animTrigger: 'idle_window',      waypointTag: 'cell_bed_',               weight: 15, minDuration: 10, maxDuration: 30 },
    ],
  },
  {
    // Cena: mismo flujo ordenado que Desayuno/Almuerzo. buildCafeteriaSequence() reutilizado.
    phase: 8, name: 'Cena', duration: 90, zone: 'comedor',
    actions: [
      { actionId: 'cafe_wait_outside_talk',  type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: 'cafeteria_entrance_spot_',  weight: 20,  minDuration: 6,  maxDuration: 7  },
      { actionId: 'cafe_walk_to_counter',    type: 'ONESHOT', animTrigger: 'Walking',       waypointTag: 'cafeteria_counter_',        weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_grab_food',          type: 'IDLE',    animTrigger: 'serve_self',    waypointTag: 'cafeteria_counter_',        weight: 100, minDuration: 4,  maxDuration: 6  },
      { actionId: 'cafe_walk_to_seat',       type: 'ONESHOT', animTrigger: 'Walking',       waypointTag: 'cafeteria_seat_',           weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_sit_eat',            type: 'IDLE',    animTrigger: 'sit_eat',       waypointTag: 'cafeteria_seat_',           weight: 60,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_sit_eat_talk',       type: 'SOCIAL',  animTrigger: 'sit_eat_talk',  waypointTag: 'cafeteria_seat_',           weight: 40,  minDuration: 10, maxDuration: 15 },
      { actionId: 'cafe_walk_to_trash',      type: 'ONESHOT', animTrigger: 'carry_tray',    waypointTag: 'cafeteria_tray_deposit_',   weight: 100, minDuration: 0,  maxDuration: 0  },
      { actionId: 'cafe_clear_tray',         type: 'IDLE',    animTrigger: 'deposit_tray',  waypointTag: 'cafeteria_tray_deposit_',   weight: 100, minDuration: 3,  maxDuration: 5  },
      { actionId: 'cafe_talk_after_trash',   type: 'SOCIAL',  animTrigger: 'talk_standing', waypointTag: 'cafeteria_tray_deposit_',   weight: 25,  minDuration: 6,  maxDuration: 7  },
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
  'hallway_slot_':       slots('hallway_slot_', 10),
  'cafeteria_path_':     slots('cafeteria_path_', 5, 4),
  'cafeteria_entrance_spot_': slots('cafeteria_entrance_spot_', 6, 3),
  'cafeteria_seat_':     slots('cafeteria_seat_', 16, 2),
  'cafeteria_counter_':  slots('cafeteria_counter_', 6),
  'cafeteria_line_':     slots('cafeteria_line_', 8),
  'cafeteria_tray_deposit_': slots('cafeteria_tray_deposit_', 4, 2),
  'corridor_start_':     slots('corridor_start_', 6),
  'clean_zone_':         slots('clean_zone_', 8),
  'cell_door_clean_':    slots('cell_door_clean_', 12),
  'supply_closet_':      [{ id: 'supply_closet_01', cap: 2 }],
  'corridor_idle_':      slots('corridor_idle_', 10),
  'corridor_chat_spot_': slots('corridor_chat_spot_', 4, 2),   // cap 2 — pairs can share
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
  // cell_bed_ tags are resolved dynamically per NPC's assigned cell (e.g. cell_03_bed_01)
  // Used by phases 4, 7 (hora libre / celdas sub-zone) and 9 (luces apagadas)
  'cell_bed_': [],
}

// Sub-zone → valid action IDs (used by phases 3, 4, 6, 7)
const SUBZONE_ACTIONS: Record<string, string[]> = {
  // Fases 3 y 6 — Trabajo
  taller:     ['work_use_workbench', 'work_carry_box', 'work_inspect_equipment', 'work_talk_coworker'],
  lavanderia: ['laundry_load_washer', 'laundry_fold_clothes', 'laundry_carry_basket', 'laundry_idle_check'],
  // Fases 4 y 7 — Hora libre
  patio:      ['yard_walk_perimeter', 'yard_sit_bench', 'yard_exercise', 'yard_conversation_group', 'yard_play_cards', 'yard_lean_wall', 'yard_shadow_boxing', 'yard_kick_ball', 'yard_pushups', 'yard_pace_nervous', 'yard_stare_wall', 'yard_whisper_secret', 'yard_argue_loud', 'yard_sit_floor'],
  comedor:    ['free_cafe_sit_talk', 'free_cafe_sit_idle', 'free_cafe_stand_chat'],
  celdas:     ['cell_lie_bed', 'cell_sit_bed', 'cell_read_book', 'cell_idle_window'],
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

  // Personality system — modifies action weights per NPC archetype/mood
  private personality: NPCPersonalitySystem

  constructor(private state: GameRoomState) {
    this.assignCells()
    this.personality = new NPCPersonalitySystem(state)
  }

  /** Returns the personality system for external event hooks (guard catches, etc.) */
  getPersonalitySystem(): NPCPersonalitySystem {
    return this.personality
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
    this.personality.update(tickDelta)
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
    this.personality.onPhaseChanged()
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

    // Phase 1: organic spawn → cafeteria transition with action chaining
    if (def.phase === 1) {
      return this.buildPhase1Assignments(npcIds, def)
    }

    // Phases 2, 5, 8: ordered cafeteria flow (counter → grab → seat → eat → trash → clear)
    if (def.phase === 2 || def.phase === 5 || def.phase === 8) {
      return this.buildCafeteriaAssignments(npcIds, def)
    }

    // All other phases: standard weighted random assignment
    return this.buildStandardAssignments(npcIds, def)
  }

  /**
   * Standard weighted random assignment with organic transition sequences (phases 3, 4, 6, 7, 9).
   *
   * Each NPC gets a departure profile (early / normal / lingerer) so they don't all
   * move to the phase zone at the same time. Some take corridor detours, some stop to
   * chat on the way. The result is staggered movement that gives cover to player prisoners.
   */
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
      // Apply personality-weighted action selection per NPC
      const personalizedPool = this.personality.applyWeightModifiers(npcId, actionPool)
      const action     = this.weightedRandom(personalizedPool)

      if (!action) {
        assignments.push(this.buildIdleAssignment(npcId))
        continue
      }

      // Record action for anti-repetition
      this.personality.recordAction(npcId, action.actionId)

      // LOOPING: skip transition prefix — the loop itself is already visually organic
      if (action.loop && action.chainLength) {
        assignments.push(this.buildSoloAssignment(npcId, action, subZone))
        continue
      }

      // Phase 9 (luces apagadas): NPCs go directly to bed, no wandering
      const usePrefix = def.phase !== 9
      const prefix    = usePrefix ? this.buildTransitionPrefix(npcId) : []

      if (action.type === 'SOCIAL') {
        const partner = this.findPartner(npcIds, paired, npcId)
        if (partner) {
          const wp      = this.reserveWaypoint(npcId, action.waypointTag)
          const dur     = this.randomDuration(action)
          const prefix2 = usePrefix ? this.buildTransitionPrefix(partner) : []

          const mainStep1: NPCActionStep = {
            actionId: action.actionId, animTrigger: action.animTrigger,
            waypointId: wp ?? undefined, duration: dur, socialPartnerId: partner,
          }
          const mainStep2: NPCActionStep = {
            actionId: action.actionId, animTrigger: action.animTrigger,
            waypointId: wp ?? undefined, duration: dur, socialPartnerId: npcId,
          }

          const seq1 = [...prefix,  mainStep1]
          const seq2 = [...prefix2, mainStep2]

          assignments.push({
            npcId, actionId: 'phase_transition_seq', animTrigger: 'idle',
            duration: Math.min(seq1.reduce((s, st) => s + st.duration, 0) + 5, def.duration),
            subZone: subZone ?? undefined, actionSequence: seq1,
          })
          assignments.push({
            npcId: partner, actionId: 'phase_transition_seq', animTrigger: 'idle',
            duration: Math.min(seq2.reduce((s, st) => s + st.duration, 0) + 5, def.duration),
            subZone: this.npcSubZones.get(partner) ?? undefined, actionSequence: seq2,
          })

          paired.add(npcId);  paired.add(partner)
          this.npcPartners.set(npcId, partner)
          this.npcPartners.set(partner, npcId)
          continue
        }
      }

      // Solo / IDLE: wrap in sequence with transition prefix
      const wpTag    = this.resolveCellTag(npcId, action.waypointTag)
      const wp       = this.reserveWaypoint(npcId, wpTag)
      const dur      = this.randomDuration(action)
      const mainStep: NPCActionStep = {
        actionId: action.actionId, animTrigger: action.animTrigger,
        waypointId: wp ?? undefined, duration: dur,
      }
      const seq      = [...prefix, mainStep]
      const totalDur = seq.reduce((s, st) => s + st.duration, 0) + 5

      assignments.push({
        npcId, actionId: 'phase_transition_seq', animTrigger: 'idle',
        duration: Math.min(totalDur, def.duration),
        subZone: subZone ?? undefined, actionSequence: seq,
      })
    }

    return assignments
  }

  // ─── Phase 1: Organic Spawn → Cafeteria Transition ──────────────────────

  /**
   * Builds Phase 1 assignments with action chaining.
   * Each NPC gets a unique sequence: spawn → [social/idle] → walk to cafeteria → [chat at door].
   * This creates organic dispersal — some NPCs arrive at the cafeteria door by 10s, others by 25s.
   */
  private buildPhase1Assignments(npcIds: string[], def: JailPhaseDef): NPCAssignment[] {
    const assignments: NPCAssignment[] = []
    const paired = new Set<string>()

    // Shuffle NPC order for organic variation
    const shuffled = [...npcIds].sort(() => Math.random() - 0.5)

    for (const npcId of shuffled) {
      if (paired.has(npcId)) continue

      const steps: NPCActionStep[] = []

      // ─── IMMEDIATE MOVEMENT ─────────────────────────────────────────────
      // NPCs do NOT idle at their cell door. The first step is always a walk
      // to a meeting point (hallway, corridor, neighbor's cell). This makes
      // all 20 entities start moving from frame 1.
      //
      // Behavior distribution:
      //   35% — walk to hallway meeting point, greet a partner there, chat, then cafeteria
      //   25% — walk to a neighbor's cell, salute, walk together to corridor, then cafeteria
      //   20% — walk to corridor, stretch/look around while walking, then cafeteria
      //   12% — walk directly toward cafeteria (early bird) with a mid-walk yawn/stretch
      //    8% — walk to hallway, idle briefly (loner), then cafeteria

      const roll = Math.random()

      if (roll < 0.35 && !paired.has(npcId)) {
        // ── Meet at hallway, chat, walk to cafeteria together ──
        const partner = this.findPartner(shuffled, paired, npcId)
        if (partner) {
          const meetingWp = this.reserveWaypoint(npcId, 'hallway_slot_')
          const partnerMeetWp = meetingWp ?? this.reserveWaypoint(npcId, 'corridor_idle_')

          // NPC walks to meeting point immediately
          if (partnerMeetWp) {
            steps.push({ actionId: 'walk_to_meeting', animTrigger: 'walk_slow', waypointId: partnerMeetWp, duration: 0 })
            steps.push({ actionId: 'greet_at_meeting', animTrigger: 'Salute', waypointId: partnerMeetWp, duration: 2 + Math.random() * 1.5, socialPartnerId: partner })
            steps.push({ actionId: 'chat_at_meeting', animTrigger: 'talk_standing', waypointId: partnerMeetWp, duration: 3 + Math.random() * 4, socialPartnerId: partner })
          }

          // Partner walks to the same meeting point
          const partnerCellNum = this.npcCells.get(partner) ?? '00'
          const partnerSpawnWp = `cell_door_exit_${partnerCellNum}`
          const partnerSteps: NPCActionStep[] = []
          if (partnerMeetWp) {
            partnerSteps.push({ actionId: 'walk_to_meeting', animTrigger: 'walk_slow', waypointId: partnerMeetWp, duration: 0 })
            partnerSteps.push({ actionId: 'greet_at_meeting', animTrigger: 'Salute', waypointId: partnerMeetWp, duration: 2 + Math.random() * 1.5, socialPartnerId: npcId })
            partnerSteps.push({ actionId: 'chat_at_meeting', animTrigger: 'talk_standing', waypointId: partnerMeetWp, duration: 3 + Math.random() * 4, socialPartnerId: npcId })
          }
          // Both walk to cafeteria
          const partnerEntranceWp = this.reserveWaypoint(partner, 'cafeteria_entrance_spot_')
          partnerSteps.push({ actionId: 'walk_to_cafeteria_area', animTrigger: 'walk_slow', waypointId: partnerEntranceWp ?? undefined, duration: 0 })
          if (Math.random() < 0.5) {
            partnerSteps.push({ actionId: 'talk_at_cafeteria_door', animTrigger: 'talk_standing', waypointId: partnerEntranceWp ?? undefined, duration: 3 + Math.random() * 3, socialPartnerId: npcId })
          }
          const partnerTotalDur = partnerSteps.reduce((s, st) => s + st.duration, 0) + 5
          assignments.push({
            npcId: partner, actionId: 'phase1_sequence', animTrigger: 'Idle',
            duration: Math.min(partnerTotalDur, def.duration), actionSequence: partnerSteps,
          })
          paired.add(partner); paired.add(npcId)
          this.npcPartners.set(npcId, partner); this.npcPartners.set(partner, npcId)
        }
      } else if (roll < 0.60 && !paired.has(npcId)) {
        // ── Walk to neighbor's cell, salute, walk together through corridor ──
        const partner = this.findPartner(shuffled, paired, npcId)
        if (partner) {
          const partnerCellNum = this.npcCells.get(partner) ?? '00'
          const partnerDoorWp = `cell_door_exit_${partnerCellNum}`

          // NPC walks to partner's cell door
          steps.push({ actionId: 'walk_to_neighbor', animTrigger: 'walk_slow', waypointId: partnerDoorWp, duration: 0 })
          steps.push({ actionId: 'greet_neighbor', animTrigger: 'Salute', waypointId: partnerDoorWp, duration: 1.5 + Math.random() * 1.5, socialPartnerId: partner })

          // Walk together through corridor
          const corridorWp = this.reserveWaypoint(npcId, 'corridor_idle_')
          if (corridorWp) {
            steps.push({ actionId: 'walk_corridor_together', animTrigger: 'walk_slow', waypointId: corridorWp, duration: 0 })
            steps.push({ actionId: 'corridor_chat', animTrigger: 'talk_standing', waypointId: corridorWp, duration: 3 + Math.random() * 3, socialPartnerId: partner })
          }

          // Partner mirrors: walks to corridor too
          const partnerCorridorWp = this.reserveWaypoint(partner, 'corridor_idle_')
          const partnerEntranceWp = this.reserveWaypoint(partner, 'cafeteria_entrance_spot_')
          const partnerSteps: NPCActionStep[] = []
          // Partner walks to meeting in corridor (or same corridor wp)
          if (partnerCorridorWp) {
            partnerSteps.push({ actionId: 'walk_to_corridor', animTrigger: 'walk_slow', waypointId: partnerCorridorWp, duration: 0 })
            partnerSteps.push({ actionId: 'corridor_chat', animTrigger: 'talk_standing', waypointId: partnerCorridorWp, duration: 3 + Math.random() * 3, socialPartnerId: npcId })
          }
          partnerSteps.push({ actionId: 'walk_to_cafeteria_area', animTrigger: 'walk_slow', waypointId: partnerEntranceWp ?? undefined, duration: 0 })
          const partnerTotalDur = partnerSteps.reduce((s, st) => s + st.duration, 0) + 5
          assignments.push({
            npcId: partner, actionId: 'phase1_sequence', animTrigger: 'Idle',
            duration: Math.min(partnerTotalDur, def.duration), actionSequence: partnerSteps,
          })
          paired.add(partner); paired.add(npcId)
          this.npcPartners.set(npcId, partner); this.npcPartners.set(partner, npcId)
        }
      } else if (roll < 0.80) {
        // ── Walk through corridor solo, stretch/look around while moving ──
        const corridorWp = this.reserveWaypoint(npcId, 'corridor_idle_')
        if (corridorWp) {
          steps.push({ actionId: 'walk_to_corridor', animTrigger: 'walk_slow', waypointId: corridorWp, duration: 0 })
          // Brief expression at corridor (stretch or look around — not standing still)
          const expr = Math.random() < 0.5 ? 'stretch' : 'look_around'
          steps.push({ actionId: 'corridor_express', animTrigger: expr, waypointId: corridorWp, duration: 1.5 + Math.random() * 2 })
        }
        // Optional: walk through hallway too before cafeteria
        if (Math.random() < 0.5) {
          const hallwayWp = this.reserveWaypoint(npcId, 'hallway_slot_')
          if (hallwayWp) {
            steps.push({ actionId: 'walk_hallway', animTrigger: 'walk_slow', waypointId: hallwayWp, duration: 0 })
          }
        }
      } else if (roll < 0.92) {
        // ── Early bird: walk directly toward cafeteria, yawn/stretch mid-walk ──
        // Walk to a hallway point first (not straight to cafeteria — path variety)
        const hallwayWp = this.reserveWaypoint(npcId, 'hallway_slot_')
        if (hallwayWp) {
          steps.push({ actionId: 'walk_early', animTrigger: 'walk_slow', waypointId: hallwayWp, duration: 0 })
          // Brief yawn or stretch while walking (keeps them moving, not stuck)
          const expr = Math.random() < 0.5 ? 'yawn' : 'stretch'
          steps.push({ actionId: 'walk_express', animTrigger: expr, waypointId: hallwayWp, duration: 1 + Math.random() * 1.5 })
        }
      } else {
        // ── Loner: walk to hallway corner, brief idle, then cafeteria ──
        const hallwayWp = this.reserveWaypoint(npcId, 'hallway_slot_')
        if (hallwayWp) {
          steps.push({ actionId: 'walk_to_hallway', animTrigger: 'walk_slow', waypointId: hallwayWp, duration: 0 })
          steps.push({ actionId: 'loner_idle', animTrigger: 'lean_wall', waypointId: hallwayWp, duration: 2 + Math.random() * 3 })
        }
      }

      // ─── FINAL: Walk to cafeteria entrance ────────────────────────────────
      const entranceWp = this.reserveWaypoint(npcId, 'cafeteria_entrance_spot_')
      steps.push({
        actionId: 'walk_to_cafeteria_area',
        animTrigger: 'walk_slow',
        waypointId: entranceWp ?? undefined,
        duration: 0,
      })

      // Activity at cafeteria door (~45% chat, grouped up)
      if (Math.random() < 0.45) {
        const chatPartner = this.findPartner(shuffled, paired, npcId)
        steps.push({
          actionId: 'talk_at_cafeteria_door',
          animTrigger: 'talk_standing',
          waypointId: entranceWp ?? undefined,
          duration: 3 + Math.random() * 4,
          socialPartnerId: chatPartner ?? undefined,
        })
      }

      const totalDur = steps.reduce((s, st) => s + st.duration, 0) + 5 // +5s walking estimate
      assignments.push({
        npcId,
        actionId: 'phase1_sequence',
        animTrigger: 'Idle',
        duration: Math.min(totalDur, def.duration),
        actionSequence: steps,
      })
    }

    return assignments
  }

  // ─── Phases 2/5/8: Ordered Cafeteria Flow ────────────────────────────────

  /**
   * Builds cafeteria assignments with the ordered flow:
   * [optional] wait outside talk → counter → grab food → seat → eat → trash → clear tray → [optional] chat
   * Each NPC gets the full sequence with staggered timing for organic dispersal.
   */
  private buildCafeteriaAssignments(npcIds: string[], def: JailPhaseDef): NPCAssignment[] {
    const assignments: NPCAssignment[] = []
    const paired = new Set<string>()
    const usedSeats = new Set<string>()

    // Shuffle for organic stagger
    const shuffled = [...npcIds].sort(() => Math.random() - 0.5)

    for (const npcId of shuffled) {
      if (paired.has(npcId)) continue

      // Organic departure: NPCs don't all rush to the cafeteria at once.
      // The transition prefix inserts a random linger + optional corridor stop before they head in.
      const steps: NPCActionStep[] = [...this.buildTransitionPrefix(npcId)]

      // Optional: Wait outside and talk (20% probability)
      if (Math.random() < 0.20) {
        const partner = this.findPartner(shuffled, paired, npcId)
        const entranceWp = this.reserveWaypoint(npcId, 'cafeteria_entrance_spot_')
        steps.push({
          actionId: 'cafe_wait_outside_talk',
          animTrigger: 'talk_standing',
          waypointId: entranceWp ?? undefined,
          duration: 6 + Math.random(),
          socialPartnerId: partner ?? undefined,
        })
      }

      // 1. Walk to counter (no dwell)
      const counterWp = this.reserveWaypoint(npcId, 'cafeteria_counter_')
      steps.push({
        actionId: 'cafe_walk_to_counter',
        animTrigger: 'Walking',
        waypointId: counterWp ?? undefined,
        duration: 0,
      })

      // 2. Grab food (4-6s)
      steps.push({
        actionId: 'cafe_grab_food',
        animTrigger: 'serve_self',
        waypointId: counterWp ?? undefined,
        duration: 4 + Math.random() * 2,
      })

      // 3. Walk to seat (no dwell)
      const seatWp = this.reserveUnusedWaypoint(npcId, 'cafeteria_seat_', usedSeats)
      steps.push({
        actionId: 'cafe_walk_to_seat',
        animTrigger: 'Walking',
        waypointId: seatWp ?? undefined,
        duration: 0,
      })

      // 4. Sit and eat (10-15s), possibly with social chat
      const eatDuration = 10 + Math.random() * 5
      const hasSeatPartner = Math.random() < 0.40
      if (hasSeatPartner) {
        const partner = this.findPartner(shuffled, paired, npcId)
        steps.push({
          actionId: 'cafe_sit_eat_talk',
          animTrigger: 'sit_eat_talk',
          waypointId: seatWp ?? undefined,
          duration: eatDuration,
          socialPartnerId: partner ?? undefined,
        })
      } else {
        steps.push({
          actionId: 'cafe_sit_eat',
          animTrigger: 'sit_eat',
          waypointId: seatWp ?? undefined,
          duration: eatDuration,
        })
      }

      // 5. Walk to trash with tray (no dwell)
      const trashWp = this.reserveWaypoint(npcId, 'cafeteria_tray_deposit_')
      steps.push({
        actionId: 'cafe_walk_to_trash',
        animTrigger: 'carry_tray',
        waypointId: trashWp ?? undefined,
        duration: 0,
      })

      // 6. Clear tray (3-5s)
      steps.push({
        actionId: 'cafe_clear_tray',
        animTrigger: 'deposit_tray',
        waypointId: trashWp ?? undefined,
        duration: 3 + Math.random() * 2,
      })

      // Optional: Chat after trash (25% probability)
      if (Math.random() < 0.25) {
        const chatPartner = this.findPartner(shuffled, paired, npcId)
        steps.push({
          actionId: 'cafe_talk_after_trash',
          animTrigger: 'talk_standing',
          waypointId: trashWp ?? undefined,
          duration: 6 + Math.random(),
          socialPartnerId: chatPartner ?? undefined,
        })
      }

      const totalDur = steps.reduce((s, st) => s + st.duration, 0) + 8 // +8s walking estimate
      assignments.push({
        npcId,
        actionId: 'cafeteria_sequence',
        animTrigger: 'Idle',
        duration: Math.min(totalDur, def.duration),
        actionSequence: steps,
      })
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

  /**
   * Builds the organic transition prefix steps for a single NPC.
   *
   * Departure profiles (assigned randomly):
   *   Early leaver  (30%)  — moves in 0–5s, no detour
   *   Normal leaver (50%)  — moves in 5–15s, may take a corridor detour
   *   Lingerer      (20%)  — stays 15–20s before moving, often detours
   *
   * Returns an ordered step array to prepend to any phase sequence.
   * The NPC lingers in-place, optionally walks through a corridor waypoint,
   * and optionally stops to chat there before heading to its phase destination.
   */
  private buildTransitionPrefix(npcId: string): NPCActionStep[] {
    const steps: NPCActionStep[] = []

    // Determine departure profile
    const profileRoll = Math.random()
    let lingerTime: number
    let mayDetour: boolean

    if (profileRoll < 0.30) {
      // Early leaver
      lingerTime = Math.random() * 5
      mayDetour  = false
    } else if (profileRoll < 0.80) {
      // Normal leaver
      lingerTime = 5 + Math.random() * 10
      mayDetour  = Math.random() < TRANSITION_DETOUR_PROB
    } else {
      // Lingerer
      lingerTime = 15 + Math.random() * (TRANSITION_LINGER_MAX_S - 15)
      mayDetour  = Math.random() < (TRANSITION_DETOUR_PROB + 0.20)  // linger more likely to detour
    }

    // In-place linger (idle / stretch / yawn)
    if (lingerTime > 1.5) {
      const idleAnims = ['idle', 'idle', 'idle', 'stretch', 'yawn']
      steps.push({
        actionId:    'linger_before_move',
        animTrigger: idleAnims[Math.floor(Math.random() * idleAnims.length)],
        duration:    lingerTime,
      })
    }

    // Optional: corridor / hallway detour before reaching phase zone
    if (mayDetour) {
      const corridorWp = this.reserveWaypoint(npcId, 'corridor_idle_')
      if (corridorWp) {
        // Walk to corridor (walk-only — no dwell)
        steps.push({ actionId: 'walk_to_corridor', animTrigger: 'Walking', waypointId: corridorWp, duration: 0 })
        // Optional: chat stop in corridor
        if (Math.random() < TRANSITION_ENROUTE_CHAT_PROB) {
          steps.push({ actionId: 'corridor_chat_stop', animTrigger: 'talk_standing', waypointId: corridorWp, duration: 4 + Math.random() * 7 })
        } else {
          // Brief idle pause before moving on
          steps.push({ actionId: 'corridor_idle_pause', animTrigger: 'idle', waypointId: corridorWp, duration: 2 + Math.random() * 3 })
        }
      }
    }

    return steps
  }

  // ─── Private: Reassign (Libre Albedrío) ──────────────────────────────────

  private checkReassignInterval(): void {
    const now = Date.now()
    if ((now - this.lastReassignAt) / 1000 < REASSIGN_INTERVAL_S) return

    // Skip reassignment for sequence-based phases — NPCs follow their ordered flow
    if (this.currentPhase === 1 || this.currentPhase === 2 || this.currentPhase === 5 || this.currentPhase === 8) return

    this.lastReassignAt = now
    const def = this.getPhaseDef(this.currentPhase)!
    const changed: NPCAssignment[] = []

    for (const [npcId, timer] of this.npcTimers) {
      // Only reassign NPCs whose action is almost done
      if (timer > 5) continue
      if (Math.random() > REASSIGN_CHANGE_PROB) continue

      let subZone = this.npcSubZones.get(npcId)

      // Libre albedrío: NPCs can switch sub-zones during hora libre (phases 4 and 7).
      // This creates organic cross-zone traffic that camouflages prisoner movements.
      if ((this.currentPhase === 4 || this.currentPhase === 7) && def.subZones && def.subZones.length > 0) {
        if (Math.random() < SUBZONE_CHANGE_PROB) {
          const candidates = def.subZones.filter(z => z !== subZone)
          const newSubZone = candidates[Math.floor(Math.random() * candidates.length)]
          this.npcSubZones.set(npcId, newSubZone)
          subZone = newSubZone
        }
      }

      const actionPool = this.getActionPool(def, subZone)
      const current    = this.npcAssignments.get(npcId)
      // Exclude current action to force variety
      const pool       = actionPool.filter(a => a.actionId !== current?.actionId)
      // Apply personality-weighted selection on reassign
      const personalizedPool = this.personality.applyWeightModifiers(npcId, pool)
      const action     = this.weightedRandom(personalizedPool)
      if (!action) continue

      // Skip social (partner logic is complex for incremental reassign)
      if (action.type === 'SOCIAL') continue

      // Record action for anti-repetition tracking
      this.personality.recordAction(npcId, action.actionId)

      // Boredom trigger: NPC's timer expired → they might get bored
      const profile = this.personality.getProfile(npcId)
      if (profile && timer <= 0 && Math.random() < 0.25) {
        this.personality.triggerMoodEvent(npcId, 'boredom')
      }

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

  /**
   * Like reserveWaypoint, but also excludes waypoints in the `alreadyUsed` set.
   * Used for cafeteria seating to spread NPCs across different seats.
   */
  private reserveUnusedWaypoint(npcId: string, tag: string, alreadyUsed: Set<string>): string | null {
    const pool = WAYPOINT_POOL[tag]
    if (!pool || pool.length === 0) return null

    for (const slot of pool) {
      if (alreadyUsed.has(slot.id)) continue
      const occ = this.waypointOccupants.get(slot.id) ?? new Set()
      if (occ.size < slot.cap) {
        occ.add(npcId)
        this.waypointOccupants.set(slot.id, occ)
        alreadyUsed.add(slot.id)
        return slot.id
      }
    }
    // Fallback: try any slot ignoring alreadyUsed
    return this.reserveWaypoint(npcId, tag)
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
   * Resolves cell-specific waypoint tags (cell_bed_)
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
    // Phases with sub-zones (3 Trabajo, 4 Hora libre, 6 Trabajo, 7 Hora libre): filter by assigned sub-zone
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
