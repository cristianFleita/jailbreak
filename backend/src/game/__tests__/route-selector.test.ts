/**
 * Route selector + Route1 state contract tests (Phase A).
 *
 * These guard the Phase A contract so later phases can't silently
 * break the selector, randomization, or mission checklist defaults.
 */

import { describe, it, expect } from 'vitest'
import { createGameRoomState } from '../state.js'
import { defaultGameConfig } from '../room-manager.js'
import {
  initializeRouteState,
  listRegisteredRoutes,
  selectActiveRoute,
} from '../routes/route-registry.js'
import {
  GUARD_DESK_IDS,
  SERVER_IDS,
  createRoute1State,
} from '../routes/route1/state.js'
import type { Route1MissionId } from '../types.js'

describe('Route Selector (Phase A)', () => {
  it('registers only route1_ventilation in MVP', () => {
    expect(listRegisteredRoutes()).toEqual(['route1_ventilation'])
  })

  it('selectActiveRoute always returns route1_ventilation', () => {
    for (let i = 0; i < 20; i++) {
      expect(selectActiveRoute()).toBe('route1_ventilation')
    }
  })

  it('createGameRoomState sets activeRouteId = route1_ventilation', () => {
    const state = createGameRoomState('room-a', 'user-1', defaultGameConfig)
    expect(state.activeRouteId).toBe('route1_ventilation')
  })

  it('initializeRouteState populates Route1State with random desk + server', () => {
    const state = createGameRoomState('room-b', 'user-1', defaultGameConfig)
    initializeRouteState(state)

    expect(state.route1).toBeDefined()
    expect(state.route1?.routeId).toBe('route1_ventilation')
    expect(GUARD_DESK_IDS).toContain(state.route1!.correctDeskId)
    expect(SERVER_IDS).toContain(state.route1!.correctServerId)
  })

  it('randomization produces more than one distinct desk across 40 inits', () => {
    const seen = new Set<string>()
    for (let i = 0; i < 40; i++) {
      const s = createRoute1State()
      seen.add(s.correctDeskId)
    }
    // 4 possible desks — with 40 samples at uniform random, P(all same) ~ 4^-39
    expect(seen.size).toBeGreaterThan(1)
  })

  it('randomization produces more than one distinct server across 40 inits', () => {
    const seen = new Set<string>()
    for (let i = 0; i < 40; i++) {
      const s = createRoute1State()
      seen.add(s.correctServerId)
    }
    expect(seen.size).toBeGreaterThan(1)
  })

  it('initial mission checklist matches design (available + locked gates)', () => {
    const s = createRoute1State()
    const expected: Record<Route1MissionId, string> = {
      find_cutters: 'available',
      find_clue: 'available',
      disable_server: 'locked',
      find_wrench: 'available',
      open_vent: 'locked',
      escape: 'locked',
    }
    expect(s.missions).toEqual(expected)
  })

  it('initial Route1State has no progress, no attempts, no escapes', () => {
    const s = createRoute1State()
    expect(s.clueFound).toBe(false)
    expect(s.serverDisabled).toBe(false)
    expect(s.wrongServerAttempts).toEqual([])
    expect(s.ventProgressById).toEqual({})
    expect(s.openVentIds).toEqual([])
    expect(s.activeInteractions).toEqual({})
    expect(s.escapingPlayerIds).toEqual([])
    expect(s.escapedPlayerIds).toEqual([])
  })
})
