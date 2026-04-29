/**
 * Route inventory tests (Phase F regression coverage).
 *
 * Covers the authoritative slot state that the UI Toolkit HUD reads:
 * pickup, legacy store, race rejection, reconnect snapshots and capture/abandon drops.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { createGameRoomState } from '../state.js'
import { defaultGameConfig } from '../room-manager.js'
import { handlePlayerInteract, handleThrowableHit } from '../event-handlers.js'
import { initializeRouteState } from '../routes/route-registry.js'
import { Route1System } from '../systems/route1-system.js'
import { clearSpawnAreaRegistration } from '../systems/spawn-areas.js'
import {
  dropCriticalRouteItemsForPlayer,
  pickupToFirstAvailableSlot,
  pickupToHand,
  storeHeldItem,
} from '../systems/route-inventory.js'
import {
  clearRoomSlots,
  markPlayerDisconnected,
  restorePlayerConnection,
} from '../reconnection.js'
import type { GameRoom, GameRoomState, PlayerState } from '../types.js'

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

function makeGameRoom(state: GameRoomState, route1?: Route1System): GameRoom {
  const room: GameRoom = {
    state,
    config: defaultGameConfig,
  }

  if (route1) {
    const roomWithManager = room as GameRoom & { gameManager?: { route1: Route1System } }
    roomWithManager.gameManager = { route1 }
  }

  return room
}

afterEach(() => {
  clearRoomSlots('room-route-inventory-held')
  clearRoomSlots('room-route-inventory-stored')
})

describe('Route inventory (Phase F)', () => {
  it('picks a route tool directly into the first empty prisoner slot', () => {
    const state = makeRoom('room-route-inventory-pickup-slot')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')

    const result = pickupToFirstAvailableSlot(state, prisoner, 'route1_cutters_a')

    expect(result.success).toBe(true)
    expect(result.success && result.slotIndex).toBe(0)
    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots?.[0]?.itemId).toBe('route1_cutters_a')
    expect(prisoner.inventorySlots?.[1]).toBeNull()
    expect(state.items.get('route1_cutters_a')?.state).toBe('stored')
    expect(state.items.get('route1_cutters_a')?.holderUserId).toBe('user-a')
  })

  it('uses the next empty slot when the first slot is occupied on direct pickup', () => {
    const state = makeRoom('room-route-inventory-pickup-slot-next')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')

    pickupToFirstAvailableSlot(state, prisoner, 'route1_cutters_a')
    const result = pickupToFirstAvailableSlot(state, prisoner, 'route1_wrench_a')

    expect(result.success).toBe(true)
    expect(result.success && result.slotIndex).toBe(1)
    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots?.[0]?.itemId).toBe('route1_cutters_a')
    expect(prisoner.inventorySlots?.[1]?.itemId).toBe('route1_wrench_a')
    expect(state.items.get('route1_wrench_a')?.state).toBe('stored')
  })

  it('handles item.pickup as direct first-slot storage through the player interaction handler', () => {
    const state = makeRoom('room-route-inventory-handler-pickup-slot')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    const route1 = new Route1System(state)
    const room = makeGameRoom(state, route1)
    const io = mockIo()

    handlePlayerInteract({
      io,
      roomId: state.id,
      room,
      socketId: 'sock-a',
      playerId: 'user-a',
      objectId: 'route1_cutters_a',
      action: 'item.pickup',
      timestamp: Date.now(),
    })

    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots?.[0]?.itemId).toBe('route1_cutters_a')
    expect(state.items.get('route1_cutters_a')?.state).toBe('stored')
    expect(state.route1?.missions.find_cutters).toBe('complete')
    expect(io.emit).toHaveBeenCalledWith('item:state', expect.objectContaining({
      itemId: 'route1_cutters_a',
      state: 'stored',
    }))
  })

  it('picks a route tool into the authoritative hand', () => {
    const state = makeRoom('room-route-inventory-pickup')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')

    const result = pickupToHand(state, prisoner, 'route1_cutters_a')

    expect(result.success).toBe(true)
    expect(prisoner.heldItemId).toBe('route1_cutters_a')
    expect(prisoner.inventorySlots).toEqual([null, null])
    expect(state.items.get('route1_cutters_a')?.state).toBe('held')
    expect(state.items.get('route1_cutters_a')?.holderUserId).toBe('user-a')
  })

  it('stores the held route tool in the first empty prisoner slot', () => {
    const state = makeRoom('room-route-inventory-store')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')

    pickupToHand(state, prisoner, 'route1_wrench_a')
    const result = storeHeldItem(state, prisoner)

    expect(result.success).toBe(true)
    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots?.[0]?.itemId).toBe('route1_wrench_a')
    expect(prisoner.inventorySlots?.[0]?.itemType).toBe('route_tool')
    expect(prisoner.inventorySlots?.[1]).toBeNull()
    expect(state.items.get('route1_wrench_a')?.state).toBe('stored')
  })

  it('stores the held route tool in the requested prisoner slot', () => {
    const state = makeRoom('room-route-inventory-store-selected')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')

    pickupToHand(state, prisoner, 'route1_wrench_a')
    const result = storeHeldItem(state, prisoner, 1)

    expect(result.success).toBe(true)
    expect(result.success && result.slotIndex).toBe(1)
    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots?.[0]).toBeNull()
    expect(prisoner.inventorySlots?.[1]?.itemId).toBe('route1_wrench_a')
    expect(state.items.get('route1_wrench_a')?.state).toBe('stored')
  })

  it('rejects storing into an occupied requested prisoner slot', () => {
    const state = makeRoom('room-route-inventory-store-occupied')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')

    pickupToHand(state, prisoner, 'route1_cutters_a')
    storeHeldItem(state, prisoner, 1)
    pickupToHand(state, prisoner, 'route1_wrench_a')

    const result = storeHeldItem(state, prisoner, 1)

    expect(result.success).toBe(false)
    expect(result.success ? '' : result.reason).toBe('Inventory slot occupied')
    expect(prisoner.heldItemId).toBe('route1_wrench_a')
    expect(prisoner.inventorySlots?.[1]?.itemId).toBe('route1_cutters_a')
  })

  it('rejects the second prisoner in a pickup race for the same tool', () => {
    const state = makeRoom('room-route-inventory-race')
    const prisonerA = makePlayer(state, 'sock-a', 'user-a')
    const prisonerB = makePlayer(state, 'sock-b', 'user-b')

    const first = pickupToHand(state, prisonerA, 'route1_cutters_a')
    const second = pickupToHand(state, prisonerB, 'route1_cutters_a')

    expect(first.success).toBe(true)
    expect(second.success).toBe(false)
    expect(second.success ? '' : second.reason).toMatch(/held/)
    expect(prisonerA.heldItemId).toBe('route1_cutters_a')
    expect(prisonerB.heldItemId).toBeNull()
    expect(state.items.get('route1_cutters_a')?.holderUserId).toBe('user-a')
  })

  it('preserves a held route tool through the reconnect slot', () => {
    const roomId = 'room-route-inventory-held'
    const state = makeRoom(roomId)
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    pickupToHand(state, prisoner, 'route1_wrench_a')

    markPlayerDisconnected(roomId, prisoner, 30)
    const restored = restorePlayerConnection(roomId, 'user-a')

    expect(restored.success).toBe(true)
    expect(restored.playerState?.heldItemId).toBe('route1_wrench_a')
    expect(restored.playerState?.inventorySlots).toEqual([null, null])
  })

  it('preserves a stored route tool through the reconnect slot', () => {
    const roomId = 'room-route-inventory-stored'
    const state = makeRoom(roomId)
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    pickupToHand(state, prisoner, 'route1_cutters_a')
    storeHeldItem(state, prisoner)

    markPlayerDisconnected(roomId, prisoner, 30)
    const restored = restorePlayerConnection(roomId, 'user-a')

    expect(restored.success).toBe(true)
    expect(restored.playerState?.heldItemId).toBeNull()
    expect(restored.playerState?.inventorySlots?.[0]?.itemId).toBe('route1_cutters_a')
  })

  it('drops all critical route tools from hand and slots on capture or abandon', () => {
    const state = makeRoom('room-route-inventory-drop')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    const io = mockIo()

    pickupToHand(state, prisoner, 'route1_cutters_a')
    storeHeldItem(state, prisoner)
    pickupToHand(state, prisoner, 'route1_wrench_a')

    const dropped = dropCriticalRouteItemsForPlayer(io, state.id, state, prisoner)

    expect(dropped.sort()).toEqual(['route1_cutters_a', 'route1_wrench_a'])
    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots).toEqual([null, null])
    expect(state.items.get('route1_cutters_a')?.state).toBe('dropped')
    expect(state.items.get('route1_wrench_a')?.state).toBe('dropped')
    expect(io.to).toHaveBeenCalledWith(state.id)
  })

  it('rejects item.throw from hand through the player interaction handler', () => {
    const state = makeRoom('room-route-inventory-throw-hand')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    const route1 = new Route1System(state)
    const room = makeGameRoom(state, route1)
    const io = mockIo()

    pickupToHand(state, prisoner, 'route1_cutters_a')
    route1.notifyInventoryChanged()
    expect(state.route1?.missions.find_cutters).toBe('complete')

    handlePlayerInteract({
      io,
      roomId: state.id,
      room,
      socketId: 'sock-a',
      playerId: 'user-a',
      objectId: 'route1_cutters_a',
      action: 'item.throw',
      timestamp: Date.now(),
    })

    const item = state.items.get('route1_cutters_a')
    expect(prisoner.heldItemId).toBe('route1_cutters_a')
    expect(item?.state).toBe('held')
    expect(item?.holderUserId).toBe('user-a')
    expect(state.route1?.missions.find_cutters).toBe('complete')
    expect(io.emit).not.toHaveBeenCalledWith('item:state', expect.anything())
    expect(io.emit).toHaveBeenCalledWith('game:error', expect.objectContaining({
      message: 'Store the item in a slot before dropping it',
    }))
  })

  it('handles item.throw from a stored slot through the player interaction handler', () => {
    const state = makeRoom('room-route-inventory-throw-slot')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    const route1 = new Route1System(state)
    const room = makeGameRoom(state, route1)
    const io = mockIo()

    pickupToHand(state, prisoner, 'route1_wrench_a')
    storeHeldItem(state, prisoner, 1)
    route1.notifyInventoryChanged()
    expect(prisoner.inventorySlots?.[1]?.itemId).toBe('route1_wrench_a')
    expect(state.route1?.missions.find_wrench).toBe('complete')

    handlePlayerInteract({
      io,
      roomId: state.id,
      room,
      socketId: 'sock-a',
      playerId: 'user-a',
      objectId: 'route1_wrench_a',
      action: 'item.throw',
      timestamp: Date.now(),
    })

    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots?.[1]).toBeNull()
    expect(state.items.get('route1_wrench_a')?.state).toBe('dropped')
    expect(state.route1?.missions.find_wrench).toBe('available')
    expect(io.emit).toHaveBeenCalledWith('item:state', expect.objectContaining({
      itemId: 'route1_wrench_a',
      state: 'dropped',
    }))
  })

  it('picks soap directly into a slot and drops it from storage', () => {
    const state = makeRoom('room-route-inventory-soap-slot-drop')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    const room = makeGameRoom(state)
    const io = mockIo()

    handlePlayerInteract({
      io,
      roomId: state.id,
      room,
      socketId: 'sock-a',
      playerId: 'user-a',
      objectId: 'route1_soap_a',
      action: 'item.pickup',
      timestamp: Date.now(),
    })

    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots?.[0]?.itemId).toBe('route1_soap_a')
    expect(state.items.get('route1_soap_a')?.state).toBe('stored')

    handlePlayerInteract({
      io,
      roomId: state.id,
      room,
      socketId: 'sock-a',
      playerId: 'user-a',
      objectId: 'route1_soap_a',
      action: 'item.throw',
      timestamp: Date.now(),
    })

    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots?.[0]).toBeNull()
    expect(state.items.get('route1_soap_a')?.state).toBe('dropped')
    expect(io.emit).toHaveBeenCalledWith('item:state', expect.objectContaining({
      itemId: 'route1_soap_a',
      state: 'dropped',
    }))
  })

  it('consumes dropped soap and broadcasts guard stun on slip', () => {
    const state = makeRoom('room-route-inventory-soap-slip')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    const guard = makePlayer(state, 'sock-guard', 'guard-user', 'guard')
    const room = makeGameRoom(state)
    const io = mockIo()
    const soap = state.items.get('route1_soap_a')!

    soap.state = 'dropped'
    soap.position = { ...guard.position }

    handleThrowableHit({
      io,
      roomId: state.id,
      room,
      socketId: 'sock-a',
      payload: {
        itemId: 'route1_soap_a',
        targetGuardId: 'guard-user',
        itemKind: 'soap',
        hitPosition: { ...guard.position },
        stunDuration: 4,
      },
      timestamp: Date.now(),
    })

    expect(soap.state).toBe('respawning')
    expect(io.emit).toHaveBeenCalledWith('item:state', expect.objectContaining({
      itemId: 'route1_soap_a',
      state: 'respawning',
    }))
    expect(io.emit).toHaveBeenCalledWith('guard:stun', expect.objectContaining({
      guardId: 'guard-user',
      attackerId: prisoner.userId,
      itemKind: 'soap',
      duration: 4,
    }))

    clearSpawnAreaRegistration(state.id)
  })

  it('handles item.store into a requested slot through the player interaction handler', () => {
    const state = makeRoom('room-route-inventory-handler-store-selected')
    const prisoner = makePlayer(state, 'sock-a', 'user-a')
    const route1 = new Route1System(state)
    const room = makeGameRoom(state, route1)
    const io = mockIo()

    pickupToHand(state, prisoner, 'route1_cutters_a')

    handlePlayerInteract({
      io,
      roomId: state.id,
      room,
      socketId: 'sock-a',
      playerId: 'user-a',
      objectId: 'route1_cutters_a',
      action: 'item.store',
      slotIndex: 1,
      timestamp: Date.now(),
    })

    expect(prisoner.heldItemId).toBeNull()
    expect(prisoner.inventorySlots?.[0]).toBeNull()
    expect(prisoner.inventorySlots?.[1]?.itemId).toBe('route1_cutters_a')
    expect(io.emit).toHaveBeenCalledWith('item:state', expect.objectContaining({
      itemId: 'route1_cutters_a',
      state: 'stored',
    }))
  })
})
