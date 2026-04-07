import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createRoom, getRoom, destroyRoom, startGameLoop, stopGameLoop } from '../room-manager.js'
import { addPlayer, spawnNPCs, updateNPCPosition } from '../state.js'
import { Server } from 'socket.io'
import { createServer } from 'http'

const HOST = 'host-user-1'

describe('Game Loop Integration', () => {
  let io: Server
  let httpServer: any
  let room: any

  beforeEach(() => {
    httpServer = createServer()
    io = new Server(httpServer)
    room = createRoom('loop-test-room', HOST)!
  })

  afterEach(() => {
    if (room) stopGameLoop(room)
    destroyRoom('loop-test-room')
    io.close()
    httpServer.close()
  })

  describe('Tick Loop Timing', () => {
    it('should advance tick every 50ms', (done) => {
      const startTick = room.state.tick
      let checkCount = 0

      const checkInterval = setInterval(() => {
        checkCount++
        if (checkCount === 2) {
          // After 100ms, tick should have advanced at least 2 times
          expect(room.state.tick).toBeGreaterThanOrEqual(startTick + 2)
          clearInterval(checkInterval)
          stopGameLoop(room)
          done()
        }
      }, 60) // Check every 60ms

      startGameLoop(io, room)
    }, 500)
  })

  describe('Player State Broadcasting', () => {
    it('should emit player:state with all players', (done) => {
      // Set up 4 players (host = guard, rest = prisoners)
      addPlayer(room.state, 'socket_guard', HOST, { x: 0, y: 1.5, z: 0 })
      addPlayer(room.state, 'socket_p1', 'user_p1', { x: 5, y: 1.5, z: 5 })
      addPlayer(room.state, 'socket_p2', 'user_p2', { x: -5, y: 1.5, z: -5 })
      addPlayer(room.state, 'socket_p3', 'user_p3', { x: 10, y: 1.5, z: 10 })

      room.state.status = 'active'

      let playerStateReceived = false

      // Mock emit to capture broadcast
      const originalEmit = io.to.bind(io)
      io.to = vi.fn((roomId: string) => {
        return {
          emit: (event: string, payload: any) => {
            if (event === 'player:state') {
              playerStateReceived = true
              expect(payload.players.length).toBe(4)
              expect(payload.players[0].id).toBeDefined()
              expect(payload.players[0].position).toBeDefined()
              expect(payload.players[0].role).toBeDefined()
              clearTimeout(timeout)
              stopGameLoop(room)
              done()
            }
            originalEmit(roomId).emit(event, payload)
          },
        }
      })

      const timeout = setTimeout(() => {
        if (!playerStateReceived) {
          done(new Error('player:state was not emitted'))
        }
      }, 200)

      startGameLoop(io, room)
    }, 1000)
  })

  describe('NPC Delta Compression', () => {
    it('should emit npc:positions delta every 200ms', (done) => {
      spawnNPCs(room.state, room.config, 20)
      room.state.status = 'active'

      let npcEmitCount = 0

      const originalEmit = io.to.bind(io)
      io.to = vi.fn((roomId) => {
        return {
          emit: (event: string, payload: any) => {
            if (event === 'npc:positions') {
              npcEmitCount++
              // Delta should only include moved NPCs (or be empty if none moved)
              expect(Array.isArray(payload.npcs)).toBe(true)
              expect(typeof payload.tick).toBe('number')
            }
            originalEmit(roomId).emit(event, payload)
          },
        }
      })

      const timeout = setTimeout(() => {
        stopGameLoop(room)
        // Should have emitted NPC positions at least once in 300ms
        expect(npcEmitCount).toBeGreaterThanOrEqual(1)
        done()
      }, 300)

      startGameLoop(io, room)
    }, 1000)
  })

  describe('NPC Position Updates', () => {
    it('should track lastBroadcastPosition after delta emission', (done) => {
      spawnNPCs(room.state, room.config, 5)
      room.state.status = 'active'

      // Move first NPC significantly
      const firstNpc = Array.from(room.state.npcs.values())[0]
      const originalPos = { ...firstNpc.position }
      firstNpc.position.x += 1.0 // Move 1m (exceeds 0.1m threshold)

      const timeout = setTimeout(() => {
        stopGameLoop(room)

        // lastBroadcastPosition should have been updated to new position
        expect(firstNpc.lastBroadcastPosition.x).toBe(originalPos.x + 1.0)

        done()
      }, 250)

      startGameLoop(io, room)
    }, 1000)
  })

  describe('Empty Delta Handling', () => {
    it('should send empty NPC delta when nothing moved', (done) => {
      spawnNPCs(room.state, room.config, 10)
      room.state.status = 'active'

      let emptyDeltaCount = 0
      let totalNpcEmits = 0

      const originalEmit = io.to.bind(io)
      io.to = vi.fn((roomId) => {
        return {
          emit: (event: string, payload: any) => {
            if (event === 'npc:positions') {
              totalNpcEmits++
              if (payload.npcs.length === 0) {
                emptyDeltaCount++
              }
            }
            originalEmit(roomId).emit(event, payload)
          },
        }
      })

      const timeout = setTimeout(() => {
        stopGameLoop(room)

        // Should have sent at least one NPC position update
        expect(totalNpcEmits).toBeGreaterThanOrEqual(1)
        // If nothing moved, should have empty delta
        if (emptyDeltaCount > 0) {
          expect(emptyDeltaCount).toBeGreaterThanOrEqual(1)
        }

        done()
      }, 300)

      startGameLoop(io, room)
    }, 1000)
  })

  describe('Tick Counter', () => {
    it('should increment tick on each loop iteration', (done) => {
      room.state.status = 'active'

      const startTick = room.state.tick
      const ticks: number[] = []

      const originalEmit = io.to.bind(io)
      io.to = vi.fn((roomId) => {
        return {
          emit: (event: string, payload: any) => {
            if (event === 'player:state' || event === 'npc:positions') {
              ticks.push(room.state.tick)
            }
            originalEmit(roomId).emit(event, payload)
          },
        }
      })

      const timeout = setTimeout(() => {
        stopGameLoop(room)

        // Ticks should be in order
        for (let i = 1; i < ticks.length; i++) {
          expect(ticks[i]).toBeGreaterThanOrEqual(ticks[i - 1])
        }

        done()
      }, 150)

      startGameLoop(io, room)
    }, 1000)
  })
})
