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
  assignRandomRoles,
} from '../state.js'
import { defaultGameConfig } from '../room-manager.js'

const HOST_USER_ID = 'host-user-1'

describe('State Management', () => {
  describe('createGameRoomState', () => {
    it('should create empty game room state', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)

      expect(state.id).toBe('test-room')
      expect(state.hostUserId).toBe(HOST_USER_ID)
      expect(state.status).toBe('lobby')
      expect(state.players.size).toBe(0)
      expect(state.playersByUserId.size).toBe(0)
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
      state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
    })

    it('should add player with default prisoner role', () => {
      const player = addPlayer(state, 'socket_1', HOST_USER_ID, { x: 0, y: 1.5, z: 0 })

      expect(player.id).toBe('socket_1')
      expect(player.userId).toBe(HOST_USER_ID)
      expect(player.role).toBe('prisoner') // default — reassigned on game start
      expect(state.players.size).toBe(1)
      expect(state.playersByUserId.size).toBe(1)
    })

    it('should track players in both maps', () => {
      addPlayer(state, 'socket_1', HOST_USER_ID, { x: 0, y: 1.5, z: 0 })
      addPlayer(state, 'socket_2', 'user_2', { x: 5, y: 1.5, z: 5 })

      expect(state.players.size).toBe(2)
      expect(state.playersByUserId.size).toBe(2)
    })

    it('should reject 5th player (max 4)', () => {
      addPlayer(state, 'socket_1', HOST_USER_ID, { x: 0, y: 1.5, z: 0 })
      addPlayer(state, 'socket_2', 'user_2', { x: 0, y: 1.5, z: 0 })
      addPlayer(state, 'socket_3', 'user_3', { x: 0, y: 1.5, z: 0 })
      addPlayer(state, 'socket_4', 'user_4', { x: 0, y: 1.5, z: 0 })

      expect(() => {
        addPlayer(state, 'socket_5', 'user_5', { x: 0, y: 1.5, z: 0 })
      }).toThrow('Room is full')
    })

    it('should spawn player at given position', () => {
      const spawnPos = { x: 10, y: 2.0, z: -5 }
      const player = addPlayer(state, 'socket_1', HOST_USER_ID, spawnPos)

      expect(player.position).toEqual(spawnPos)
    })

    it('should initialize player with idle state', () => {
      const player = addPlayer(state, 'socket_1', HOST_USER_ID, { x: 0, y: 1.5, z: 0 })

      expect(player.movementState).toBe('idle')
      expect(player.isAlive).toBe(true)
      expect(player.velocity).toEqual({ x: 0, y: 0, z: 0 })
    })
  })

  describe('assignRandomRoles', () => {
    it('should assign exactly 1 guard and rest prisoners', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
      addPlayer(state, 's1', 'u1', { x: 0, y: 1.5, z: 0 })
      addPlayer(state, 's2', 'u2', { x: 5, y: 1.5, z: 5 })
      addPlayer(state, 's3', 'u3', { x: -5, y: 1.5, z: -5 })
      addPlayer(state, 's4', 'u4', { x: 10, y: 1.5, z: 10 })

      assignRandomRoles(state)

      const players = Array.from(state.players.values())
      const guards = players.filter(p => p.role === 'guard')
      const prisoners = players.filter(p => p.role === 'prisoner')

      expect(guards.length).toBe(1)
      expect(prisoners.length).toBe(3)
    })

    it('should assign guard to single player', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
      addPlayer(state, 's1', 'u1', { x: 0, y: 1.5, z: 0 })

      assignRandomRoles(state)

      const player = state.players.get('s1')!
      expect(player.role).toBe('guard')
    })
  })

  describe('removePlayer', () => {
    it('should remove player from state', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
      addPlayer(state, 'socket_1', HOST_USER_ID, { x: 0, y: 1.5, z: 0 })

      expect(state.players.size).toBe(1)
      removePlayer(state, 'socket_1')
      expect(state.players.size).toBe(0)
      expect(state.playersByUserId.size).toBe(0)
    })

    it('should handle removing non-existent player gracefully', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)

      expect(() => {
        removePlayer(state, 'nonexistent')
      }).not.toThrow()
    })
  })

  describe('updatePlayerMovement', () => {
    it('should update player position and movement state', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
      addPlayer(state, 'socket_1', HOST_USER_ID, { x: 0, y: 1.5, z: 0 })

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
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)

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
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
      spawnNPCs(state, defaultGameConfig)

      expect(state.npcs.size).toBe(20)
    })

    it('should spawn NPCs within map bounds', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
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


    it('should initialize NPC last broadcast position', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
      spawnNPCs(state, defaultGameConfig)

      state.npcs.forEach((npc) => {
        expect(npc.lastBroadcastPosition).toEqual(npc.position)
      })
    })
  })

  describe('computeNPCDelta', () => {
    it('should return empty delta when no NPCs moved', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
      spawnNPCs(state, defaultGameConfig)

      const delta = computeNPCDelta(state, 0.1)

      expect(delta.length).toBe(0)
    })

    it('should return only NPCs that moved >threshold', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
      spawnNPCs(state, defaultGameConfig)

      // Move first NPC by 0.15m (exceeds 0.1m threshold)
      const firstNpc = Array.from(state.npcs.values())[0]
      firstNpc.position.x += 0.15

      const delta = computeNPCDelta(state, 0.1)

      expect(delta.length).toBe(1)
      expect(delta[0].id).toBe(firstNpc.id)
    })

    it('should update lastBroadcastPosition after delta', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
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
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)

      startGame(state)

      expect(state.status).toBe('active')
      expect(state.phase.current).toBe('active')
    })

    it('should set startedAt timestamp', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
      const beforeStart = Date.now()

      startGame(state)

      expect(state.startedAt).toBeGreaterThanOrEqual(beforeStart)
      expect(state.startedAt).toBeLessThanOrEqual(Date.now())
    })
  })

  describe('endGame', () => {
    it('should set finished status and winner', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)

      endGame(state, 'guards', 'all_prisoners_caught')

      expect(state.status).toBe('finished')
      expect(state.winner).toBe('guards')
      expect(state.reason).toBe('all_prisoners_caught')
    })

    it('should record end timestamp', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)
      const beforeEnd = Date.now()

      endGame(state, 'prisoners', 'escape')

      expect(state.endedAt).toBeGreaterThanOrEqual(beforeEnd)
    })
  })

  describe('advanceTick', () => {
    it('should increment tick counter', () => {
      const state = createGameRoomState('test-room', HOST_USER_ID, defaultGameConfig)

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
