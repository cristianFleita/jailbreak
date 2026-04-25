/**
 * Route 1 — Ventilation escape route.
 * Authoritative state factory and tuning defaults.
 *
 * Spec: design/gdd/ruta-1-ventilacion-industrial.md
 * Plan: design/gdd/ruta-1-implementation-plan.md (Phase A)
 *
 * Phase A scope: state initialization only. Mission gates, interaction
 * ticking, and event handlers land in Phase D.
 */

import {
  GuardDeskId,
  Route1MissionId,
  Route1MissionStatus,
  Route1State,
  ServerRackId,
  VentId,
} from '../../types.js'

// ─── Tuning defaults (from Phase A plan) ────────────────────────────────────
export const ROUTE1_CONFIG = {
  guardDeskCount: 4,
  serverCount: 12,
  clueSearchSeconds: 3,
  serverDisableSeconds: 15,
  ventOpenSecondsSingle: 25,
  ventOpenSecondsCoop: 12,
  escapeSeconds: 5,
  criticalItemRespawnDelay: 45,
} as const

// ─── Static object-ID enumerations ──────────────────────────────────────────
// These IDs must match scene authoring on the Unity side exactly.
export const GUARD_DESK_IDS: readonly GuardDeskId[] = [
  'guard_desk_1',
  'guard_desk_2',
  'guard_desk_3',
  'guard_desk_4',
]

export const SERVER_IDS: readonly ServerRackId[] = [
  'server_1', 'server_2', 'server_3', 'server_4',
  'server_5', 'server_6', 'server_7', 'server_8',
  'server_9', 'server_10', 'server_11', 'server_12',
]

// All vents are authored in-scene. Actual set is determined by Unity
// registration (2 or 3). Backend initializes progress for registered vents
// in Phase D; the canonical ID space is declared here for type safety.
export const VENT_IDS: readonly VentId[] = ['vent_1', 'vent_2', 'vent_3']

// ─── Initial mission checklist ──────────────────────────────────────────────
const INITIAL_MISSIONS: Record<Route1MissionId, Route1MissionStatus> = {
  find_cutters: 'available',
  find_clue: 'available',
  disable_server: 'locked',   // unlocks once clue found + cutters held
  find_wrench: 'available',
  open_vent: 'locked',        // unlocks once serverDisabled + wrench held
  escape: 'locked',           // unlocks once any vent is open
}

/**
 * Picks a random element from a readonly array. Uses Math.random;
 * determinism is not required for MVP.
 */
function pickRandom<T>(pool: readonly T[]): T {
  return pool[Math.floor(Math.random() * pool.length)]
}

/**
 * Builds a fresh Route1State for a new match.
 * - Randomizes `correctDeskId` and `correctServerId`.
 * - Leaves vent progress empty; Phase C/E will seed it once Unity
 *   registers the vent set for the current scene.
 */
export function createRoute1State(now: number = Date.now()): Route1State {
  return {
    routeId: 'route1_ventilation',
    correctDeskId: pickRandom(GUARD_DESK_IDS),
    correctServerId: pickRandom(SERVER_IDS),
    clueFound: false,
    serverDisabled: false,
    wrongServerAttempts: [],
    ventProgressById: {},
    openVentIds: [],
    activeInteractions: {},
    escapingPlayerIds: [],
    escapedPlayerIds: [],
    missions: { ...INITIAL_MISSIONS },
    updatedAt: now,
  }
}
