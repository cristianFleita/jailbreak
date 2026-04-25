/**
 * Route inventory tests (Phase F regression coverage).
 *
 * Covers the authoritative hand/slot state that the UI Toolkit HUD reads:
 * pickup, store, race rejection, reconnect snapshots and capture/abandon drops.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { createGameRoomState } from '../state.js'
import { defaultGameConfig } from '../room-manager.js'
import { initializeRouteState } from '../routes/route-registry.js'
import {
  dropCriticalRouteItemsForPlayer,
  pickupToHand,
  storeHeldItem,
} from '../systems/route-inventory.js'
import {
  clearRoomSlots,
  markPlayerDisconnected,
  restorePlayerConnection,
} from '../reconnection.js'
import type { GameRoomState, PlayerState } from '../types.js'

function makeRoom(id: string): GameRoomState {
  const state = createGameRoomState(id, 'host-user', defaultGameConfig)
  initializeRouteState(state)
  return state
}

function makePlayer(
  state: GameRoomState,
  socketId: string,
  userId: string,
  role: PlayerState['role'] = 'prisoner'
): PlayerState {
  const player: PlayerState = {
    id: userId,
    userId,
    role,
    position: { x: 2, y: 0, z: 3 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    velocity: { x: 0, y: 0, z: 0 },
    movementState: 'idle',
    isAlive: true,
    carrying: null,
    heldItemId: null,
    inventorySlots: role === 'prisoner' ? [null, null] : [],
  }
  state.players.set(socketId, player)
  state.playersByUserId.set(userId, player)
  return player
}

function mockIo() {
  const emit = vi.fn()
  const to = vi.fn(() => ({ emit }))
  return { to, emit } as any
}

afterEach(() => {
  clearRoomSlots('room-route-inventory-held')
  clearRoomSlots('room-route-inventory-stored')
})

describe('Route inventory (Phase F)', () => {
  it('picks a route tool into the authoritative hand', () => {
    const state = makeRoom('room-route-inventory-pickup')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')

    const result = pickupToHand(state, prisoner, 'route1_cutters')

    expect(result.success).toBe(true)
    expect(prisoner.heldItemId).toBe('route1_cutters')
    expect(prisoner.inventorySlots).toEqual([null, null])
    expect(state.items.get('route1_cutters')?.state).toBe('held')
    expect(state.items.get('route1_cutters')?.holderUserId).toBe('user-a')
  })

  it('stores the held route tool in the first empty prisoner slot', () => {
    const state = makeRoom('room-route-inventory-store')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')

    pickupToHand(state, prisoner, 'route1_wrench')
    const result = storeHeldItem(state, prisoner)

    expect(result.success).toBe(true)
    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots?.[0]?.itemId).toBe('route1_wrench')
    expect(prisoner.inventorySlots?.[0]?.itemType).toBe('route_tool')
    expect(prisoner.inventorySlots?.[1]).toBeNull()
    expect(state.items.get('route1_wrench')?.state).toBe('stored')
  })

  it('rejects the second prisoner in a pickup race for the same tool', () => {
    const state = makeRoom('room-route-inventory-race')
    const prisonerA = makePlayer(state, 'sock-a', 'user-a')
    const prisonerB = makePlayer(state, 'sock-b', 'user-b')

    const first = pickupToHand(state, prisonerA, 'route1_cutters')
    const second = pickupToHand(state, prisonerB, 'route1_cutters')

    expect(first.success).toBe(true)
    expect(second.success).toBe(false)
    expect(second.success ? '' : second.reason).toMatch(/held/)
    expect(prisonerA.heldItemId).toBe('route1_cutters')
    expect(prisonerB.heldItemId).toBeNull()
    expect(state.items.get('route1_cutters')?.holderUserId).toBe('user-a')
  })

  it('preserves a held route tool through the reconnect slot', () => {
    const roomId = 'room-route-inventory-held'
    const state = makeRoom(roomId)
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    pickupToHand(state, prisoner, 'route1_wrench')

    markPlayerDisconnected(roomId, prisoner, 30)
    const restored = restorePlayerConnection(roomId, 'user-a')

    expect(restored.success).toBe(true)
    expect(restored.playerState?.heldItemId).toBe('route1_wrench')
    expect(restored.playerState?.inventorySlots).toEqual([null, null])
  })

  it('preserves a stored route tool through the reconnect slot', () => {
    const roomId = 'room-route-inventory-stored'
    const state = makeRoom(roomId)
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    pickupToHand(state, prisoner, 'route1_cutters')
    storeHeldItem(state, prisoner)

    markPlayerDisconnected(roomId, prisoner, 30)
    const restored = restorePlayerConnection(roomId, 'user-a')

    expect(restored.success).toBe(true)
    expect(restored.playerState?.heldItemId).toBeNull()
    expect(restored.playerState?.inventorySlots?.[0]?.itemId).toBe('route1_cutters')
  })

  it('drops all critical route tools from hand and slots on capture or abandon', () => {
    const state = makeRoom('room-route-inventory-drop')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    const io = mockIo()

    pickupToHand(state, prisoner, 'route1_cutters')
    storeHeldItem(state, prisoner)
    pickupToHand(state, prisoner, 'route1_wrench')

    const dropped = dropCriticalRouteItemsForPlayer(io, state.id, state, prisoner)

    expect(dropped.sort()).toEqual(['route1_cutters', 'route1_wrench'])
    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots).toEqual([null, null])
    expect(state.items.get('route1_cutters')?.state).toBe('dropped')
    expect(state.items.get('route1_wrench')?.state).toBe('dropped')
    expect(io.to).toHaveBeenCalledWith(state.id)
  })
})
