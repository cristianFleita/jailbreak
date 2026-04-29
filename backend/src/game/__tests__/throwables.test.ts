import { describe, expect, it, vi } from 'vitest'
import { defaultGameConfig } from '../room-manager.js'
import { createGameRoomState } from '../state.js'
import { handleThrowableHit, handleThrowableThrow } from '../event-handlers.js'
import type { GameRoom, GameRoomState, PlayerState } from '../types.js'

function makeRoom(id: string): GameRoom {
  const state = createGameRoomState(id, 'host-user', defaultGameConfig)
  state.status = 'active'
  return { state, config: defaultGameConfig }
}

function addPlayer(
  state: GameRoomState,
  socketId: string,
  userId: string,
  role: PlayerState['role']
): PlayerState {
  const player: PlayerState = {
    id: userId,
    userId,
    role,
    position: { x: 0, y: 0, z: 0 },
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
  const except = vi.fn(() => ({ emit }))
  const to = vi.fn(() => ({ emit, except }))
  return { io: { to } as any, to, except, emit }
}

describe('throwable item network handlers', () => {
  it('clears carried state and broadcasts throw payload to other clients', () => {
    const room = makeRoom('throwable-throw-room')
    const prisoner = addPlayer(room.state, 'sock-prisoner', 'user-prisoner', 'prisoner')
    prisoner.carrying = 'food_plate'

    const { io, to, except, emit } = mockIo()

    handleThrowableThrow({
      io,
      roomId: room.state.id,
      room,
      socketId: 'sock-prisoner',
      payload: {
        itemKind: 'food_plate',
        origin: { x: 1, y: 2, z: 3 },
        direction: { x: 0, y: 0, z: 2 },
        force: 12,
      },
      timestamp: 1000,
    })

    expect(prisoner.carrying).toBeNull()
    expect(to).toHaveBeenCalledWith(room.state.id)
    expect(except).toHaveBeenCalledWith('sock-prisoner')
    expect(emit).toHaveBeenCalledWith('throwable:throw', {
      throwerId: 'user-prisoner',
      itemKind: 'food_plate',
      origin: { x: 1, y: 2, z: 3 },
      direction: { x: 0, y: 0, z: 1 },
      force: 12,
    })
  })

  it('accepts container throws for legacy pickable items', () => {
    const room = makeRoom('container-throw-room')
    const prisoner = addPlayer(room.state, 'sock-prisoner', 'user-prisoner', 'prisoner')
    prisoner.carrying = null

    const { io, emit } = mockIo()

    handleThrowableThrow({
      io,
      roomId: room.state.id,
      room,
      socketId: 'sock-prisoner',
      payload: {
        itemKind: 'container',
        origin: { x: 2, y: 1, z: 0 },
        direction: { x: 1, y: 0, z: 0 },
        force: 10,
      },
      timestamp: 1500,
    })

    expect(emit).toHaveBeenCalledWith('throwable:throw', {
      throwerId: 'user-prisoner',
      itemKind: 'container',
      origin: { x: 2, y: 1, z: 0 },
      direction: { x: 1, y: 0, z: 0 },
      force: 10,
    })
  })

  it('emits guard stun when a prisoner reports a hit on a guard', () => {
    const room = makeRoom('throwable-hit-room')
    addPlayer(room.state, 'sock-prisoner', 'user-prisoner', 'prisoner')
    addPlayer(room.state, 'sock-guard', 'user-guard', 'guard')

    const { io, emit } = mockIo()

    handleThrowableHit({
      io,
      roomId: room.state.id,
      room,
      socketId: 'sock-prisoner',
      payload: {
        targetGuardId: 'user-guard',
        itemKind: 'folded_clothes',
        hitPosition: { x: 4, y: 1, z: 2 },
        stunDuration: 3,
      },
      timestamp: 2000,
    })

    expect(emit).toHaveBeenCalledWith('guard:stun', {
      guardId: 'user-guard',
      attackerId: 'user-prisoner',
      itemKind: 'folded_clothes',
      duration: 3,
      hitPosition: { x: 4, y: 1, z: 2 },
    })
  })

  it('rejects hit reports whose target is not a guard', () => {
    const room = makeRoom('throwable-hit-reject-room')
    addPlayer(room.state, 'sock-prisoner-a', 'user-prisoner-a', 'prisoner')
    addPlayer(room.state, 'sock-prisoner-b', 'user-prisoner-b', 'prisoner')

    const { io, emit } = mockIo()

    handleThrowableHit({
      io,
      roomId: room.state.id,
      room,
      socketId: 'sock-prisoner-a',
      payload: {
        targetGuardId: 'user-prisoner-b',
        itemKind: 'clothes_bundle',
        hitPosition: { x: 0, y: 0, z: 0 },
        stunDuration: 3,
      },
      timestamp: 3000,
    })

    expect(emit).not.toHaveBeenCalled()
  })
})
