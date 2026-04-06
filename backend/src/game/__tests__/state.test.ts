import { describe, it, expect, beforeEach } from 'vitest'
import {
  createGameRoomState,
  addPlayer,
  removePlayer,
  updatePlayerMovement,
  spawnNPCs,
  computeNPCDelta,
  startGame,
  endGame,
  advanceTick,
  distance,
} from '../state.js'
import { defaultGameConfig } from '../room-manager.js'

describe('State Management', () => {
  describe('createGameRoomState', () => {
    it('should create empty game room state', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)

      expect(state.id).toBe('test-room')
      expect(state.status).toBe('lobby')
      expect(state.players.size).toBe(0)
      expect(state.npcs.size).toBe(0)
      expect(state.items.size).toBe(0)
      expect(state.phase.current).toBe('setup')
      expect(state.tick).toBe(0)
      expect(state.createdAt).toBeLessThanOrEqual(Date.now())
    })
  })

  describe('addPlayer', () => {
    let state: any

    beforeEach(() => {
      state = createGameRoomState('test-room', defaultGameConfig)
    })

    it('should add first player as guard', () => {
      const player = addPlayer(state, 'socket_1', { x: 0, y: 1.5, z: 0 })

      expect(player.id).toBe('socket_1')
      expect(player.role).toBe('guard')
      expect(state.players.size).toBe(1)
    })

    it('should add subsequent players as prisoners', () => {
      addPlayer(state, 'socket_1', { x: 0, y: 1.5, z: 0 })
      const p2 = addPlayer(state, 'socket_2', { x: 5, y: 1.5, z: 5 })

      expect(p2.role).toBe('prisoner')
      expect(state.players.size).toBe(2)
    })

    it('should reject 5th player (max 4)', () => {
      addPlayer(state, 'socket_1', { x: 0, y: 1.5, z: 0 })
      addPlayer(state, 'socket_2', { x: 0, y: 1.5, z: 0 })
      addPlayer(state, 'socket_3', { x: 0, y: 1.5, z: 0 })
      addPlayer(state, 'socket_4', { x: 0, y: 1.5, z: 0 })

      expect(() => {
        addPlayer(state, 'socket_5', { x: 0, y: 1.5, z: 0 })
      }).toThrow('Room is full')
    })

    it('should spawn player at given position', () => {
      const spawnPos = { x: 10, y: 2.0, z: -5 }
      const player = addPlayer(state, 'socket_1', spawnPos)

      expect(player.position).toEqual(spawnPos)
    })

    it('should initialize player with idle state', () => {
      const player = addPlayer(state, 'socket_1', { x: 0, y: 1.5, z: 0 })

      expect(player.movementState).toBe('idle')
      expect(player.isAlive).toBe(true)
      expect(player.velocity).toEqual({ x: 0, y: 0, z: 0 })
    })
  })

  describe('removePlayer', () => {
    it('should remove player from state', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)
      addPlayer(state, 'socket_1', { x: 0, y: 1.5, z: 0 })

      expect(state.players.size).toBe(1)
      removePlayer(state, 'socket_1')
      expect(state.players.size).toBe(0)
    })

    it('should handle removing non-existent player gracefully', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)

      expect(() => {
        removePlayer(state, 'nonexistent')
      }).not.toThrow()
    })
  })

  describe('updatePlayerMovement', () => {
    it('should update player position and movement state', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)
      addPlayer(state, 'socket_1', { x: 0, y: 1.5, z: 0 })

      const newPos = { x: 5, y: 1.5, z: 5 }
      updatePlayerMovement(
        state,
        'socket_1',
        newPos,
        { x: 0, y: 0, z: 0, w: 1 },
        { x: 1, y: 0, z: 1 },
        'walking'
      )

      const player = state.players.get('socket_1')
      expect(player?.position).toEqual(newPos)
      expect(player?.movementState).toBe('walking')
      expect(player?.velocity).toEqual({ x: 1, y: 0, z: 1 })
    })

    it('should not update non-existent player', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)

      expect(() => {
        updatePlayerMovement(
          state,
          'nonexistent',
          { x: 0, y: 0, z: 0 },
          { x: 0, y: 0, z: 0, w: 1 },
          { x: 0, y: 0, z: 0 },
          'idle'
        )
      }).not.toThrow()
    })
  })

  describe('spawnNPCs', () => {
    it('should spawn 20 NPCs by default', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)
      spawnNPCs(state, defaultGameConfig)

      expect(state.npcs.size).toBe(20)
    })

    it('should spawn NPCs within map bounds', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)
      spawnNPCs(state, defaultGameConfig)

      state.npcs.forEach((npc) => {
        const { minX, maxX, minY, maxY, minZ, maxZ } = defaultGameConfig.mapBounds

        expect(npc.position.x).toBeGreaterThanOrEqual(minX)
        expect(npc.position.x).toBeLessThanOrEqual(maxX)
        expect(npc.position.y).toBeGreaterThanOrEqual(minY)
        expect(npc.position.y).toBeLessThanOrEqual(maxY)
        expect(npc.position.z).toBeGreaterThanOrEqual(minZ)
        expect(npc.position.z).toBeLessThanOrEqual(maxZ)
      })
    })

    it('should set first NPC as guard type', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)
      spawnNPCs(state, defaultGameConfig)

      const npcs = Array.from(state.npcs.values())
      expect(npcs[0].type).toBe('guard')
    })

    it('should initialize NPC last broadcast position', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)
      spawnNPCs(state, defaultGameConfig)

      state.npcs.forEach((npc) => {
        expect(npc.lastBroadcastPosition).toEqual(npc.position)
      })
    })
  })

  describe('computeNPCDelta', () => {
    it('should return empty delta when no NPCs moved', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)
      spawnNPCs(state, defaultGameConfig)

      const delta = computeNPCDelta(state, 0.1)

      expect(delta.length).toBe(0)
    })

    it('should return only NPCs that moved >threshold', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)
      spawnNPCs(state, defaultGameConfig)

      // Move first NPC by 0.15m (exceeds 0.1m threshold)
      const firstNpc = Array.from(state.npcs.values())[0]
      firstNpc.position.x += 0.15

      const delta = computeNPCDelta(state, 0.1)

      expect(delta.length).toBe(1)
      expect(delta[0].id).toBe(firstNpc.id)
    })

    it('should update lastBroadcastPosition after delta', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)
      spawnNPCs(state, defaultGameConfig)

      const firstNpc = Array.from(state.npcs.values())[0]
      const oldPos = { ...firstNpc.position }
      firstNpc.position.x += 0.2

      computeNPCDelta(state, 0.1)

      expect(firstNpc.lastBroadcastPosition).toEqual(firstNpc.position)
      expect(firstNpc.lastBroadcastPosition).not.toEqual(oldPos)
    })
  })

  describe('startGame', () => {
    it('should transition to active status', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)

      startGame(state)

      expect(state.status).toBe('active')
      expect(state.phase.current).toBe('active')
    })

    it('should set startedAt timestamp', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)
      const beforeStart = Date.now()

      startGame(state)

      expect(state.startedAt).toBeGreaterThanOrEqual(beforeStart)
      expect(state.startedAt).toBeLessThanOrEqual(Date.now())
    })
  })

  describe('endGame', () => {
    it('should set finished status and winner', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)

      endGame(state, 'guards', 'all_prisoners_caught')

      expect(state.status).toBe('finished')
      expect(state.winner).toBe('guards')
      expect(state.reason).toBe('all_prisoners_caught')
    })

    it('should record end timestamp', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)
      const beforeEnd = Date.now()

      endGame(state, 'prisoners', 'escape')

      expect(state.endedAt).toBeGreaterThanOrEqual(beforeEnd)
    })
  })

  describe('advanceTick', () => {
    it('should increment tick counter', () => {
      const state = createGameRoomState('test-room', defaultGameConfig)

      expect(state.tick).toBe(0)
      advanceTick(state)
      expect(state.tick).toBe(1)
      advanceTick(state)
      expect(state.tick).toBe(2)
    })
  })

  describe('distance', () => {
    it('should calculate euclidean distance', () => {
      const dist = distance({ x: 0, y: 0, z: 0 }, { x: 3, y: 4, z: 0 })

      expect(dist).toBe(5) // 3-4-5 triangle
    })

    it('should handle 3D distances', () => {
      const dist = distance({ x: 0, y: 0, z: 0 }, { x: 1, y: 1, z: 1 })

      expect(dist).toBeCloseTo(Math.sqrt(3), 5)
    })

    it('should return 0 for same position', () => {
      const dist = distance({ x: 5, y: 5, z: 5 }, { x: 5, y: 5, z: 5 })

      expect(dist).toBe(0)
    })
  })
})
