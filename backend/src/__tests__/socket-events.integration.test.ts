import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { createRoom, getRoom, stopGameLoop, destroyRoom } from '../game/room-manager.js'
import {
  handlePlayerMove,
  handlePlayerInteract,
  handleGuardCatch,
  handleGuardMark,
  checkGameEndCondition,
} from '../game/event-handlers.js'
import { addPlayer, spawnNPCs } from '../game/state.js'
import { GameRoomState, Vector3 } from '../game/types.js'
import { Server } from 'socket.io'
import { createServer } from 'http'

const HOST = 'host-user-1'

/** Test helper: adds a player and explicitly sets their role */
function addTestPlayer(state: GameRoomState, socketId: string, userId: string, pos: Vector3, role: 'guard' | 'prisoner') {
  const p = addPlayer(state, socketId, userId, pos)
  p.role = role
  return p
}

describe('Socket Events Integration', () => {
  let io: Server
  let httpServer: any
  let room: any

  beforeEach(() => {
    httpServer = createServer()
    io = new Server(httpServer)
    room = createRoom('event-test-room', HOST)
    room.state.status = 'active'
  })

  afterEach(() => {
    const activeRoom = getRoom('event-test-room')
    if (activeRoom) {
      stopGameLoop(activeRoom)
      destroyRoom('event-test-room')
    }
    io.close()
    httpServer.close()
  })

  describe('player:move Validation', () => {
    it('should accept valid player movement', () => {
      const guard = addTestPlayer(room.state, 'socket_guard', HOST, { x: 0, y: 1.5, z: 0 }, 'guard')

      const payload = {
        playerId: 'socket_guard',
        position: { x: 0.25, y: 1.5, z: 0 }, // Legal movement (5 m/s)
        rotation: { x: 0, y: 0, z: 0, w: 1 },
        velocity: { x: 1, y: 0, z: 0 },
        movementState: 'walking' as const,
      }

      // Should not throw
      expect(() => {
        handlePlayerMove({
          io,
          roomId: 'event-test-room',
          room,
          socketId: 'socket_guard',
          payload,
          timestamp: Date.now(),
        })
      }).not.toThrow()

      // Verify state was updated
      const updatedGuard = room.state.players.get('socket_guard')
      expect(updatedGuard?.movementState).toBe('walking')
    })

    it('should reject out-of-bounds movement', () => {
      const guard = addTestPlayer(room.state, 'socket_guard', HOST, { x: 0, y: 1.5, z: 0 }, 'guard')
      const originalPos = { ...guard.position }

      const payload = {
        playerId: 'socket_guard',
        position: { x: 100, y: 1.5, z: 0 }, // out of bounds
        rotation: { x: 0, y: 0, z: 0, w: 1 },
        velocity: { x: 50, y: 0, z: 0 },
        movementState: 'walking' as const,
      }

      handlePlayerMove({
        io,
        roomId: 'event-test-room',
        room,
        socketId: 'socket_guard',
        payload,
        timestamp: Date.now(),
      })

      // Position should remain unchanged (validation failed)
      expect(guard.position).toEqual(originalPos)
    })
  })

  describe('player:interact Race Condition', () => {
    it('should give item to first player who picks it up', () => {
      const prisoner1 = addPlayer(room.state, 'socket_p1', 'user_p1', { x: 0, y: 1.5, z: 0 })
      const prisoner2 = addPlayer(room.state, 'socket_p2', 'user_p2', { x: 0.1, y: 1.5, z: 0 })

      // Create an item at origin
      const item = {
        id: 'item_123',
        type: 'keycard',
        position: { x: 0, y: 1.5, z: 0 },
        isPickedUp: false,
      }
      room.state.items.set(item.id, item)

      // First pickup attempt
      let broadcastedEvent1 = null
      const originalEmit1 = io.to.bind(io)
      io.to = (roomId: string) => ({
        emit: (event: string, payload: any) => {
          if (event === 'item:pickup') {
            broadcastedEvent1 = { event, payload }
          }
        },
      })

      handlePlayerInteract({
        io,
        roomId: 'event-test-room',
        room,
        socketId: 'socket_p1',
        playerId: 'socket_p1',
        objectId: 'item_123',
        action: 'pickup',
        timestamp: Date.now(),
      })

      expect(broadcastedEvent1).not.toBeNull()
      expect((broadcastedEvent1 as any).payload.playerId).toBe('socket_p1')

      // Second pickup attempt (should fail)
      let broadcastedEvent2 = null
      io.to = (roomId: string) => ({
        emit: (event: string, payload: any) => {
          if (event === 'item:pickup') {
            broadcastedEvent2 = { event, payload }
          }
        },
      })

      // Simulate second player trying to pick up same item
      handlePlayerInteract({
        io,
        roomId: 'event-test-room',
        room,
        socketId: 'socket_p2',
        playerId: 'socket_p2',
        objectId: 'item_123',
        action: 'pickup',
        timestamp: Date.now(),
      })

      // Second pickup should not broadcast (item already taken)
      expect(broadcastedEvent2).toBeNull()
    })
  })

  describe('guard:mark Chase Initiation', () => {
    it('should broadcast chase:start on guard mark', () => {
      const guard = addTestPlayer(room.state, 'socket_guard', HOST, {
        x: 0,
        y: 1.5,
        z: 0,
      }, 'guard')
      const prisoner = addPlayer(room.state, 'socket_p1', 'user_p1', {
        x: 5,
        y: 1.5,
        z: 5,
      })

      let chaseStarted = false
      const originalEmit = io.to.bind(io)
      io.to = (roomId: string) => ({
        emit: (event: string, payload: any) => {
          if (event === 'chase:start') {
            chaseStarted = true
            expect(payload.guardId).toBe('socket_guard')
            expect(payload.targetId).toBe('socket_p1')
          }
        },
      })

      handleGuardMark({
        io,
        roomId: 'event-test-room',
        room,
        socketId: 'socket_guard',
        guardId: 'socket_guard',
        targetId: 'socket_p1',
        timestamp: Date.now(),
      })

      expect(chaseStarted).toBe(true)
    })
  })

  describe('guard:catch Validation', () => {
    it('should catch prisoner within 1.5m', () => {
      const guard = addTestPlayer(room.state, 'socket_guard', HOST, {
        x: 0,
        y: 1.5,
        z: 0,
      }, 'guard')
      const prisoner = addPlayer(room.state, 'socket_p1', 'user_p1', {
        x: 1,
        y: 1.5,
        z: 0,
      }) // 1m away

      expect(prisoner.isAlive).toBe(true)

      let catchBroadcast = false
      const originalEmit = io.to.bind(io)
      io.to = (roomId: string) => ({
        emit: (event: string, payload: any) => {
          if (event === 'guard:catch') {
            catchBroadcast = true
            expect(payload.success).toBe(true)
          }
        },
      })

      handleGuardCatch({
        io,
        roomId: 'event-test-room',
        room,
        socketId: 'socket_guard',
        guardId: 'socket_guard',
        targetId: 'socket_p1',
        timestamp: Date.now(),
      })

      expect(catchBroadcast).toBe(true)
      expect(prisoner.isAlive).toBe(false)
    })

    it('should reject catch beyond 1.5m', () => {
      const guard = addTestPlayer(room.state, 'socket_guard', HOST, {
        x: 0,
        y: 1.5,
        z: 0,
      }, 'guard')
      const prisoner = addPlayer(room.state, 'socket_p1', 'user_p1', {
        x: 10,
        y: 1.5,
        z: 0,
      }) // 10m away

      let errorEmitted = false
      const originalEmit = io.to.bind(io)
      io.to = (roomId: string) => ({
        emit: (event: string, payload: any) => {
          if (event === 'catch:failed') {
            errorEmitted = true
          }
        },
      })

      handleGuardCatch({
        io,
        roomId: 'event-test-room',
        room,
        socketId: 'socket_guard',
        guardId: 'socket_guard',
        targetId: 'socket_p1',
        timestamp: Date.now(),
      })

      expect(errorEmitted).toBe(true)
      expect(prisoner.isAlive).toBe(true) // Still alive
    })

    it('should not catch camuflaged prisoner', () => {
      const guard = addTestPlayer(room.state, 'socket_guard', HOST, {
        x: 0,
        y: 1.5,
        z: 0,
      }, 'guard')
      const prisoner = addPlayer(room.state, 'socket_p1', 'user_p1', {
        x: 1,
        y: 1.5,
        z: 0,
      }) // 1m away, in range
      prisoner.movementState = 'camuflaged'

      let catchBroadcast = false
      const originalEmit = io.to.bind(io)
      io.to = (roomId: string) => ({
        emit: (event: string, payload: any) => {
          if (event === 'guard:catch') {
            catchBroadcast = true
          }
        },
      })

      handleGuardCatch({
        io,
        roomId: 'event-test-room',
        room,
        socketId: 'socket_guard',
        guardId: 'socket_guard',
        targetId: 'socket_p1',
        timestamp: Date.now(),
      })

      expect(catchBroadcast).toBe(false)
      expect(prisoner.isAlive).toBe(true)
    })
  })

  describe('Victory Conditions', () => {
    it('should detect guards win when all prisoners caught', () => {
      const guard = addTestPlayer(room.state, 'socket_guard', HOST, {
        x: 0,
        y: 1.5,
        z: 0,
      }, 'guard')
      const p1 = addPlayer(room.state, 'socket_p1', 'user_p1', { x: 5, y: 1.5, z: 5 })
      const p2 = addPlayer(room.state, 'socket_p2', 'user_p2', { x: -5, y: 1.5, z: -5 })
      const p3 = addPlayer(room.state, 'socket_p3', 'user_p3', { x: 10, y: 1.5, z: 10 })

      expect(checkGameEndCondition(room).winner).toBeNull()

      p1.isAlive = false
      p2.isAlive = false
      p3.isAlive = false

      const result = checkGameEndCondition(room)
      expect(result.winner).toBe('guards')
      expect(result.reason).toBe('all_prisoners_caught')
    })

    it('should not end game if prisoners still alive', () => {
      const guard = addTestPlayer(room.state, 'socket_guard', HOST, {
        x: 0,
        y: 1.5,
        z: 0,
      }, 'guard')
      const p1 = addPlayer(room.state, 'socket_p1', 'user_p1', { x: 5, y: 1.5, z: 5 })

      const result = checkGameEndCondition(room)
      expect(result.winner).toBeNull()
    })
  })
})
