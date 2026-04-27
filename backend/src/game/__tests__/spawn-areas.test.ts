/**
 * Spawn Area + backend-driven placement tests (Phase C).
 *
 * Covers:
 *   - C-02 registration validation (reject bad entries, lock once)
 *   - C-02 random spawn selection per itemId
 *   - Anti-softlock respawn triggering on dropped state
 *   - Pickup cancels pending respawn
 */

import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { createGameRoomState } from '../state.js'
import { defaultGameConfig } from '../room-manager.js'
import { initializeRouteState } from '../routes/route-registry.js'
import {
  registerSpawnAreas,
  placeCriticalItemsInSpawnAreas,
  respawnCriticalItem,
  scheduleCriticalItemRespawn,
  cancelPendingRespawn,
  clearSpawnAreaRegistration,
  hasSpawnAreasRegistered,
} from '../systems/spawn-areas.js'
import type { GameRoomState, SpawnAreaRegistration } from '../types.js'

function mockIo() {
  const emit = vi.fn()
  const to = vi.fn(() => ({ emit }))
  return { to, emit } as any
}

function makeRoom(id: string): GameRoomState {
  const state = createGameRoomState(id, 'host-user', defaultGameConfig)
  initializeRouteState(state)
  return state
}

const AREAS_A: SpawnAreaRegistration[] = [
  {
    spawnAreaId: 'laundry_cutters_slot',
    zoneId: 'laundry',
    position: { x: 1, y: 0, z: 2 },
    allowedItemIds: ['route1_cutters'],
  },
  {
    spawnAreaId: 'workshop_wrench_slot',
    zoneId: 'workshop',
    position: { x: 5, y: 0, z: -3 },
    allowedItemIds: ['route1_wrench'],
  },
  {
    spawnAreaId: 'shared_slot',
    zoneId: 'workshop',
    position: { x: 8, y: 0, z: 0 },
    allowedItemIds: ['route1_cutters', 'route1_wrench'],
  },
]

describe('Spawn Areas (Phase C)', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe('registerSpawnAreas', () => {
    it('accepts a well-formed registration and locks the room', () => {
      const state = makeRoom('room-a')
      const io = mockIo()
      const result = registerSpawnAreas(state, { areas: AREAS_A })

      expect(result.skipped).toBe(false)
      expect(result.rejected).toEqual([])
      expect(result.accepted).toHaveLength(3)
      expect(hasSpawnAreasRegistered(state)).toBe(true)
      expect(state.spawnAreas!.size).toBe(3)

      clearSpawnAreaRegistration('room-a')
    })

    it('rejects entries missing required fields', () => {
      const state = makeRoom('room-b')
      const result = registerSpawnAreas(state, {
        areas: [
          { spawnAreaId: '', zoneId: 'z', position: { x: 0, y: 0, z: 0 }, allowedItemIds: ['route1_wrench'] },
          { spawnAreaId: 'ok', zoneId: 'z', position: { x: 0, y: 0, z: 0 }, allowedItemIds: [] },
          {
            spawnAreaId: 'good',
            zoneId: 'z',
            position: { x: 1, y: 2, z: 3 },
            allowedItemIds: ['route1_cutters'],
          },
        ],
      })
      expect(result.accepted).toHaveLength(1)
      expect(result.rejected.length).toBeGreaterThanOrEqual(2)
      clearSpawnAreaRegistration('room-b')
    })

    it('rejects non-finite positions', () => {
      const state = makeRoom('room-c')
      const result = registerSpawnAreas(state, {
        areas: [
          {
            spawnAreaId: 'bad',
            zoneId: 'z',
            position: { x: Number.NaN, y: 0, z: 0 },
            allowedItemIds: ['route1_cutters'],
          },
        ],
      })
      expect(result.accepted).toHaveLength(0)
      expect(result.rejected[0]).toMatch(/non-finite/)
      clearSpawnAreaRegistration('room-c')
    })

    it('skips re-registration once locked', () => {
      const state = makeRoom('room-d')
      registerSpawnAreas(state, { areas: AREAS_A })
      const second = registerSpawnAreas(state, { areas: AREAS_A })
      expect(second.skipped).toBe(true)
      clearSpawnAreaRegistration('room-d')
    })

    it('returns empty on missing areas field without locking', () => {
      const state = makeRoom('room-e')
      const result = registerSpawnAreas(state, {})
      expect(result.accepted).toHaveLength(0)
      expect(result.skipped).toBe(false)
      expect(hasSpawnAreasRegistered(state)).toBe(false)
      clearSpawnAreaRegistration('room-e')
    })
  })

  describe('placeCriticalItemsInSpawnAreas', () => {
    it('assigns one spawn area per critical item and broadcasts item:state', () => {
      const state = makeRoom('room-place')
      const io = mockIo()
      registerSpawnAreas(state, { areas: AREAS_A })
      const placed = placeCriticalItemsInSpawnAreas(io, 'room-place', state)

      expect(placed).toEqual(expect.arrayContaining([
        'route1_cutters_a', 'route1_cutters_b',
        'route1_wrench_a',  'route1_wrench_b',
      ]))

      const cutters = state.items.get('route1_cutters_a')!
      const wrench = state.items.get('route1_wrench_a')!

      expect(cutters.state).toBe('spawned')
      expect(cutters.spawnAreaId).toBeDefined()
      expect(['laundry_cutters_slot', 'shared_slot']).toContain(cutters.spawnAreaId)

      expect(wrench.state).toBe('spawned')
      expect(['workshop_wrench_slot', 'shared_slot']).toContain(wrench.spawnAreaId)

      expect(io.to).toHaveBeenCalledWith('room-place')
      expect(io.emit).toHaveBeenCalled()

      clearSpawnAreaRegistration('room-place')
    })

    it('does not reposition held or stored items', () => {
      const state = makeRoom('room-keep')
      const io = mockIo()
      const cutters = state.items.get('route1_cutters_a')!
      cutters.state = 'held'
      cutters.holderUserId = 'some-user'

      registerSpawnAreas(state, { areas: AREAS_A })
      placeCriticalItemsInSpawnAreas(io, 'room-keep', state)

      expect(cutters.state).toBe('held')
      expect(cutters.holderUserId).toBe('some-user')
      clearSpawnAreaRegistration('room-keep')
    })
  })

  describe('respawnCriticalItem', () => {
    it('moves a dropped item to a new spawn area', () => {
      const state = makeRoom('room-respawn')
      const io = mockIo()
      registerSpawnAreas(state, { areas: AREAS_A })
      placeCriticalItemsInSpawnAreas(io, 'room-respawn', state)

      const cutters = state.items.get('route1_cutters_a')!
      cutters.state = 'dropped'

      const ok = respawnCriticalItem(io, 'room-respawn', state, 'route1_cutters_a')
      expect(ok).toBe(true)
      expect(cutters.state).toBe('spawned')
      expect(cutters.spawnAreaId).toBeDefined()
      clearSpawnAreaRegistration('room-respawn')
    })

    it('refuses to respawn a held item', () => {
      const state = makeRoom('room-refuse')
      const io = mockIo()
      registerSpawnAreas(state, { areas: AREAS_A })

      const cutters = state.items.get('route1_cutters_a')!
      cutters.state = 'held'
      const ok = respawnCriticalItem(io, 'room-refuse', state, 'route1_cutters_a')
      expect(ok).toBe(false)
      clearSpawnAreaRegistration('room-refuse')
    })
  })

  describe('scheduleCriticalItemRespawn + cancelPendingRespawn', () => {
    it('fires respawn after delay when item stays dropped', () => {
      const state = makeRoom('room-timer')
      const io = mockIo()
      registerSpawnAreas(state, { areas: AREAS_A })
      placeCriticalItemsInSpawnAreas(io, 'room-timer', state)

      const wrench = state.items.get('route1_wrench_a')!
      wrench.state = 'dropped'
      wrench.spawnAreaId = undefined

      scheduleCriticalItemRespawn(io, 'room-timer', state, 'route1_wrench_a', 5)
      expect(wrench.state).toBe('dropped')

      vi.advanceTimersByTime(5_000 + 50)
      expect(wrench.state).toBe('spawned')
      expect(wrench.spawnAreaId).toBeDefined()
      clearSpawnAreaRegistration('room-timer')
    })

    it('cancelPendingRespawn prevents the timer from firing', () => {
      const state = makeRoom('room-cancel')
      const io = mockIo()
      registerSpawnAreas(state, { areas: AREAS_A })
      placeCriticalItemsInSpawnAreas(io, 'room-cancel', state)

      const wrench = state.items.get('route1_wrench_a')!
      wrench.state = 'dropped'

      scheduleCriticalItemRespawn(io, 'room-cancel', state, 'route1_wrench_a', 5)
      cancelPendingRespawn('room-cancel', 'route1_wrench_a')
      vi.advanceTimersByTime(10_000)
      // Still dropped — timer never ran respawn
      expect(wrench.state).toBe('dropped')
      clearSpawnAreaRegistration('room-cancel')
    })

    it('skips firing if item was picked up before delay expired', () => {
      const state = makeRoom('room-pickup')
      const io = mockIo()
      registerSpawnAreas(state, { areas: AREAS_A })
      placeCriticalItemsInSpawnAreas(io, 'room-pickup', state)

      const wrench = state.items.get('route1_wrench_a')!
      wrench.state = 'dropped'
      scheduleCriticalItemRespawn(io, 'room-pickup', state, 'route1_wrench_a', 3)

      // Simulate a pickup before the timer fires
      wrench.state = 'held'
      wrench.holderUserId = 'some-prisoner'
      vi.advanceTimersByTime(5_000)

      expect(wrench.state).toBe('held')
      clearSpawnAreaRegistration('room-pickup')
    })
  })
})
