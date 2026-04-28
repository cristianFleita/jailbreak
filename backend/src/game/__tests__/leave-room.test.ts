/**
 * Tests for the win-by-leave flow:
 *   - Guard leaves an active match → prisoners win + cleanup
 *   - Last remaining prisoner leaves → guards win + cleanup
 *   - Non-last prisoner leaves → no end (existing leave path keeps running)
 *
 * Covers `evaluateLeaveWinCondition` (pure decision) and `endMatchAndCleanup`
 * (the shared cleanup that the leave path and the natural tick-loop end path
 * both call into).
 */

import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import {
  createRoom,
  getRoom,
  destroyRoom,
  roomExists,
  stopGameLoop,
  evaluateLeaveWinCondition,
  endMatchAndCleanup,
} from '../room-manager.js'
import { addPlayer } from '../state.js'
import type { GameRoom, GameRoomState, PlayerState, Vector3 } from '../types.js'

const HOST = 'host-user-guard'

function spawn(state: GameRoomState, socketId: string, userId: string, role: 'guard' | 'prisoner'): PlayerState {
  const pos: Vector3 = { x: 0, y: 1.5, z: 0 }
  const p = addPlayer(state, socketId, userId, pos)
  p.role = role
  return p
}

interface EmittedEvent {
  target: string
  event: string
  payload: unknown
}

interface FakeSocket {
  id: string
  data: { currentRoomId: string | null }
}

/**
 * Minimal socket.io Server stub that records `io.to(id).emit(...)` calls and
 * supports `io.in(id).socketsLeave(...)` + `io.sockets.sockets.get(...)`.
 * Enough surface to drive endMatchAndCleanup without a real network stack.
 */
function createFakeIo(sockets: FakeSocket[]) {
  const emits: EmittedEvent[] = []
  const leftRoomIds: string[] = []
  const socketMap = new Map<string, FakeSocket>(sockets.map(s => [s.id, s]))

  const io: any = {
    to(id: string) {
      return {
        emit(event: string, payload: unknown) {
          emits.push({ target: id, event, payload })
        },
      }
    },
    in(id: string) {
      return {
        socketsLeave(roomId: string) {
          leftRoomIds.push(roomId)
        },
      }
    },
    sockets: {
      sockets: {
        get: (socketId: string) => socketMap.get(socketId),
      },
    },
  }
  return { io, emits, leftRoomIds }
}

function activeMatch(roomId: string): GameRoom {
  const room = createRoom(roomId, HOST)!
  room.state.status = 'active'
  return room
}

describe('Leave-room win conditions', () => {
  afterEach(() => {
    for (const name of ['leave-active-1', 'leave-active-2', 'leave-active-3', 'leave-active-4', 'leave-cleanup-1']) {
      if (roomExists(name)) {
        const r = getRoom(name)
        if (r) stopGameLoop(r)
        destroyRoom(name)
      }
    }
  })

  describe('evaluateLeaveWinCondition', () => {
    it('returns null while the room is still in lobby', () => {
      const room = createRoom('leave-active-1', HOST)!
      // Status defaults to 'lobby'
      const guard = spawn(room.state, 'sock_g', HOST, 'guard')

      expect(evaluateLeaveWinCondition(room.state, guard)).toBeNull()
    })

    it('flags prisoners win when the guard leaves an active match', () => {
      const room = activeMatch('leave-active-1')
      const guard = spawn(room.state, 'sock_g', HOST, 'guard')
      spawn(room.state, 'sock_p1', 'prisoner-a', 'prisoner')
      spawn(room.state, 'sock_p2', 'prisoner-b', 'prisoner')

      expect(evaluateLeaveWinCondition(room.state, guard)).toEqual({
        winner: 'prisoners',
        reason: 'guard-left',
      })
    })

    it('flags guards win when the LAST remaining prisoner leaves', () => {
      const room = activeMatch('leave-active-2')
      spawn(room.state, 'sock_g', HOST, 'guard')
      const lastPrisoner = spawn(room.state, 'sock_p1', 'prisoner-a', 'prisoner')

      expect(evaluateLeaveWinCondition(room.state, lastPrisoner)).toEqual({
        winner: 'guards',
        reason: 'all-prisoners-left',
      })
    })

    it('returns null when a non-last prisoner leaves — match continues', () => {
      const room = activeMatch('leave-active-3')
      spawn(room.state, 'sock_g', HOST, 'guard')
      const leaving = spawn(room.state, 'sock_p1', 'prisoner-a', 'prisoner')
      spawn(room.state, 'sock_p2', 'prisoner-b', 'prisoner')

      expect(evaluateLeaveWinCondition(room.state, leaving)).toBeNull()
    })
  })

  describe('endMatchAndCleanup', () => {
    it('emits game:end, marks state finished, and destroys the room', () => {
      const room = activeMatch('leave-cleanup-1')
      const guardSock: FakeSocket = { id: 'sock_g', data: { currentRoomId: 'leave-cleanup-1' } }
      const prisonerSock: FakeSocket = { id: 'sock_p1', data: { currentRoomId: 'leave-cleanup-1' } }
      spawn(room.state, guardSock.id, HOST, 'guard')
      spawn(room.state, prisonerSock.id, 'prisoner-a', 'prisoner')

      const { io, emits, leftRoomIds } = createFakeIo([guardSock, prisonerSock])
      endMatchAndCleanup(io, room, 'prisoners', 'guard-left')

      const gameEnd = emits.find(e => e.event === 'game:end')
      expect(gameEnd).toBeDefined()
      expect(gameEnd!.target).toBe('leave-cleanup-1')
      expect(gameEnd!.payload).toEqual({ winner: 'prisoners', reason: 'guard-left' })

      // Match state mutated to finished BEFORE the room is destroyed.
      expect(room.state.status).toBe('finished')
      expect(room.state.winner).toBe('prisoners')
      expect(room.state.reason).toBe('guard-left')

      // Sockets get yanked from the socket.io room and their currentRoomId cleared.
      expect(leftRoomIds).toContain('leave-cleanup-1')
      expect(guardSock.data.currentRoomId).toBeNull()
      expect(prisonerSock.data.currentRoomId).toBeNull()

      // Room registry no longer holds it.
      expect(roomExists('leave-cleanup-1')).toBe(false)
    })

    it('is idempotent — calling twice does not double-emit or throw', () => {
      const room = activeMatch('leave-cleanup-1')
      spawn(room.state, 'sock_g', HOST, 'guard')

      const { io, emits } = createFakeIo([{ id: 'sock_g', data: { currentRoomId: 'leave-cleanup-1' } }])
      endMatchAndCleanup(io, room, 'prisoners', 'guard-left')

      // The second call sees status=finished and short-circuits. The room was
      // also destroyed by the first call, so a second run uses the same room
      // reference but produces no new emits.
      expect(() => endMatchAndCleanup(io, room, 'guards', 'all-prisoners-left'))
        .not.toThrow()
      expect(emits.filter(e => e.event === 'game:end')).toHaveLength(1)
    })
  })
})
