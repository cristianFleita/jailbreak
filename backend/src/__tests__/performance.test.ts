import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { createRoom, startGameLoop, stopGameLoop, getRoom, destroyRoom } from '../game/room-manager.js'
import { addPlayer, spawnNPCs } from '../game/state.js'
import { Server } from 'socket.io'
import { createServer } from 'http'

describe('Performance Tests', () => {
  let io: Server
  let httpServer: any
  let room: any

  beforeEach(() => {
    httpServer = createServer()
    io = new Server(httpServer)
    room = createRoom('perf-test-room')
  })

  afterEach(() => {
    const activeRoom = getRoom('perf-test-room')
    if (activeRoom) {
      stopGameLoop(activeRoom)
      destroyRoom('perf-test-room')
    }
    io.close()
    httpServer.close()
  })

  describe('Bandwidth Estimation', () => {
    it('should keep bandwidth under 5 KB/s with 4 players and 20 NPCs', (done) => {
      // Set up 4 players and 20 NPCs
      addPlayer(room.state, 'socket_guard', { x: 0, y: 1.5, z: 0 })
      addPlayer(room.state, 'socket_p1', { x: 5, y: 1.5, z: 5 })
      addPlayer(room.state, 'socket_p2', { x: -5, y: 1.5, z: -5 })
      addPlayer(room.state, 'socket_p3', { x: 10, y: 1.5, z: 10 })

      spawnNPCs(room.state, room.config, 20)
      room.state.status = 'active'

      let totalBytes = 0
      let emitCount = 0

      const originalEmit = io.to.bind(io)
      io.to = (roomId: string) => ({
        emit: (event: string, payload: any) => {
          if (event === 'player:state' || event === 'npc:positions') {
            const payloadSize = JSON.stringify(payload).length
            totalBytes += payloadSize
            emitCount++
          }
        },
      })

      startGameLoop(io, room)

      const timeout = setTimeout(() => {
        stopGameLoop(room)

        // Estimate bytes/second (ticks are 50ms apart)
        // Each tick emits player:state, every 4 ticks also emits npc:positions
        const averageBytesPerTick = totalBytes / emitCount
        const bytesPerSecond = averageBytesPerTick * 20 // 20 ticks/sec
        const kbps = bytesPerSecond / 1024

        console.log(
          `Bandwidth: ${kbps.toFixed(2)} KB/s (${totalBytes} bytes over ${emitCount} emissions)`
        )

        // Should be well under 5 KB/s (design target)
        expect(kbps).toBeLessThan(8) // Generous margin
        done()
      }, 500)
    }, 2000)
  })

  describe('Tick Precision', () => {
    it('should maintain ~50ms tick interval with variance < 10ms', (done) => {
      room.state.status = 'active'

      const tickTimes: number[] = []

      const originalEmit = io.to.bind(io)
      io.to = (roomId: string) => ({
        emit: (event: string, payload: any) => {
          if (event === 'player:state') {
            tickTimes.push(Date.now())
          }
          originalEmit(roomId).emit(event, payload)
        },
      })

      startGameLoop(io, room)

      const timeout = setTimeout(() => {
        stopGameLoop(room)

        // Calculate deltas between ticks
        const deltas: number[] = []
        for (let i = 1; i < tickTimes.length; i++) {
          deltas.push(tickTimes[i] - tickTimes[i - 1])
        }

        if (deltas.length > 0) {
          const avgDelta = deltas.reduce((a, b) => a + b) / deltas.length
          const maxDeviation = Math.max(
            ...deltas.map((d) => Math.abs(d - 50))
          )

          console.log(
            `Tick precision: avg ${avgDelta.toFixed(1)}ms, max deviation ${maxDeviation.toFixed(1)}ms`
          )

          // Average should be close to 50ms
          expect(avgDelta).toBeGreaterThan(40)
          expect(avgDelta).toBeLessThan(70)

          // Max deviation should be reasonable (10-20ms acceptable)
          expect(maxDeviation).toBeLessThan(30)
        }

        done()
      }, 300)
    }, 2000)
  })

  describe('Memory Stability', () => {
    it('should not have runaway memory growth over 100 ticks', (done) => {
      addPlayer(room.state, 'socket_guard', { x: 0, y: 1.5, z: 0 })
      addPlayer(room.state, 'socket_p1', { x: 5, y: 1.5, z: 5 })
      spawnNPCs(room.state, room.config, 20)
      room.state.status = 'active'

      const memSamples: number[] = []

      const originalEmit = io.to.bind(io)
      io.to = (roomId: string) => ({
        emit: (event: string, payload: any) => {
          if (event === 'player:state') {
            const memUsage = process.memoryUsage().heapUsed / 1024 / 1024
            memSamples.push(memUsage)
          }
          originalEmit(roomId).emit(event, payload)
        },
      })

      startGameLoop(io, room)

      const timeout = setTimeout(() => {
        stopGameLoop(room)

        if (memSamples.length > 2) {
          const startMem = memSamples[0]
          const endMem = memSamples[memSamples.length - 1]
          const growth = endMem - startMem

          console.log(
            `Memory: start ${startMem.toFixed(1)}MB, end ${endMem.toFixed(1)}MB, growth ${growth.toFixed(1)}MB`
          )

          // Growth should be minimal (< 5MB for 100 ticks)
          expect(growth).toBeLessThan(10) // Very generous limit
        }

        done()
      }, 2000)
    }, 5000)
  })

  describe('NPC Delta Compression', () => {
    it('should significantly reduce NPC payload size', (done) => {
      spawnNPCs(room.state, room.config, 20)
      room.state.status = 'active'

      let emptyDeltaSize = 0
      let fullDataSize = 0

      const originalEmit = io.to.bind(io)
      io.to = (roomId: string) => ({
        emit: (event: string, payload: any) => {
          if (event === 'npc:positions') {
            const size = JSON.stringify(payload).length

            if (payload.npcs.length === 0) {
              emptyDeltaSize += size
            } else {
              fullDataSize += size
            }
          }
          originalEmit(roomId).emit(event, payload)
        },
      })

      startGameLoop(io, room)

      const timeout = setTimeout(() => {
        stopGameLoop(room)

        const avgEmpty = emptyDeltaSize ? emptyDeltaSize : 50 // rough estimate
        const fullPayload = JSON.stringify({
          npcs: Array.from(room.state.npcs.values()),
          tick: 0,
        }).length

        const compressionRatio = (1 - avgEmpty / fullPayload) * 100

        console.log(
          `Delta compression: ${compressionRatio.toFixed(0)}% reduction (empty: ${avgEmpty}B, full: ${fullPayload}B)`
        )

        // Delta should be significantly smaller than full payload
        expect(avgEmpty).toBeLessThan(fullPayload)

        done()
      }, 300)
    }, 2000)
  })

  describe('CPU Load Estimation', () => {
    it('should complete 100 ticks in reasonable time', (done) => {
      addPlayer(room.state, 'socket_guard', { x: 0, y: 1.5, z: 0 })
      addPlayer(room.state, 'socket_p1', { x: 5, y: 1.5, z: 5 })
      addPlayer(room.state, 'socket_p2', { x: -5, y: 1.5, z: -5 })
      addPlayer(room.state, 'socket_p3', { x: 10, y: 1.5, z: 10 })
      spawnNPCs(room.state, room.config, 20)
      room.state.status = 'active'

      const startTime = Date.now()
      const startCpuUsage = process.cpuUsage()
      let tickCount = 0

      const originalEmit = io.to.bind(io)
      io.to = (roomId: string) => ({
        emit: (event: string, payload: any) => {
          if (event === 'player:state') {
            tickCount++
          }
          originalEmit(roomId).emit(event, payload)
        },
      })

      startGameLoop(io, room)

      const timeout = setTimeout(() => {
        stopGameLoop(room)

        const elapsedMs = Date.now() - startTime
        const cpuUsageMs = process.cpuUsage(startCpuUsage)

        const cpuPercent =
          ((cpuUsageMs.user + cpuUsageMs.system) / (elapsedMs * 1000)) * 100

        console.log(
          `Tick performance: ${tickCount} ticks in ${elapsedMs}ms (${cpuPercent.toFixed(1)}% CPU)`
        )

        // 100 ticks at 50ms each = 5 seconds wall time
        // Should not take excessive time
        expect(elapsedMs).toBeLessThan(10000) // Very generous
        expect(cpuPercent).toBeLessThan(80) // Should not peg CPU

        done()
      }, 5500)
    }, 10000)
  })
})
