/**
 * Route Registry — selector-based architecture for escape routes.
 *
 * MVP registers only `route1_ventilation`. The registry exists so
 * future routes (Ruta 2+) can be added without rewriting room
 * initialization, reconnection, or event handling.
 *
 * Spec: design/gdd/ruta-1-ventilacion-industrial.md
 * Plan: design/gdd/ruta-1-implementation-plan.md (Phase A)
 */

import { ActiveRouteId, GameRoomState } from '../types.js'
import { createRoute1State } from './route1/state.js'

/**
 * A route definition is a pure initializer: it mutates a freshly-created
 * room state to wire its authoritative sub-state. Behavior/ticking lands
 * in each route's own System in later phases.
 */
interface RouteDefinition {
  id: ActiveRouteId
  init(state: GameRoomState): void
}

const REGISTRY: RouteDefinition[] = [
  {
    id: 'route1_ventilation',
    init: (state) => {
      state.route1 = createRoute1State()
    },
  },
  // Route 2 — TODO (not implementable in MVP)
  // Route 3 — TODO (not implementable in MVP)
]

/**
 * Selects the active route for a new match.
 * MVP always resolves to `route1_ventilation`; future logic can randomize
 * or gate selection on room config without changing callers.
 */
export function selectActiveRoute(): ActiveRouteId {
  return REGISTRY[0].id
}

/**
 * Returns every registered route ID. Kept in case UI or debug tooling
 * needs to iterate available routes.
 */
export function listRegisteredRoutes(): ActiveRouteId[] {
  return REGISTRY.map((r) => r.id)
}

/**
 * Initializes authoritative state for the selected route on the given
 * room state. Caller is responsible for having assigned `activeRouteId`.
 * Safe to call multiple times — each route's init should be idempotent.
 */
export function initializeRouteState(state: GameRoomState): void {
  const def = REGISTRY.find((r) => r.id === state.activeRouteId)
  if (!def) {
    throw new Error(
      `[ROUTE] Unknown activeRouteId "${state.activeRouteId}" — registry keys: ${listRegisteredRoutes().join(', ')}`
    )
  }
  def.init(state)
}
