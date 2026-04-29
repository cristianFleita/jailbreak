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

import { ActiveRouteId, GameRoomState, RouteItemId, RouteItemKind } from '../types.js'
import { createRoute1State } from './route1/state.js'
import { ensureItemState } from '../systems/route-inventory.js'

/**
 * A route definition is a pure initializer: it mutates a freshly-created
 * room state to wire its authoritative sub-state. Behavior/ticking lands
 * in each route's own System in later phases.
 */
interface RouteDefinition {
  id: ActiveRouteId
  init(state: GameRoomState): void
}

/**
 * Two physical instances per critical kind so a single dropped/lost tool
 * never softlocks the route. Each instance has a unique id; the kind is
 * what mission gates check, so any instance satisfies its requirement.
 */
export interface RouteItemInstance {
  instanceId: RouteItemId
  kind: RouteItemKind
  itemType?: string
}

export const ROUTE1_ITEM_INSTANCES: readonly RouteItemInstance[] = [
  { instanceId: 'route1_cutters_a', kind: 'route1_cutters' },
  { instanceId: 'route1_cutters_b', kind: 'route1_cutters' },
  { instanceId: 'route1_wrench_a',  kind: 'route1_wrench' },
  { instanceId: 'route1_wrench_b',  kind: 'route1_wrench' },
  { instanceId: 'route1_soap_a',    kind: 'route1_soap', itemType: 'prank_item' },
]

const REGISTRY: RouteDefinition[] = [
  {
    id: 'route1_ventilation',
    init: (state) => {
      state.route1 = createRoute1State()
      // Seed authoritative lifecycle for every critical tool instance.
      // Positions are placeholders (0,0,0) until Phase C spawn areas override
      // them via item:state updates. Pickup validation tolerates placeholder
      // positions; Unity is the source of proximity in this phase.
      for (const inst of ROUTE1_ITEM_INSTANCES) {
        ensureItemState(state, inst.instanceId, inst.itemType ?? 'route_tool', undefined, inst.kind)
      }
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
