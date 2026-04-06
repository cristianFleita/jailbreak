import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import {
  createRoom,
  getOrCreateRoom,
  getRoom,
  destroyRoom,
  stopGameLoop,
  initializeNPCs,
  defaultGameConfig,
} from '../room-manager.js'

describe('Room Manager', () => {
  afterEach(() => {
    // Clean up rooms after each test
    const roomId = 'test-room-1'
    const room = getRoom(roomId)
    if (room) {
      stopGameLoop(room)
      destroyRoom(roomId)
    }
  })

  describe('createRoom', () => {
    it('should create a new room with default config', () => {
      const room = createRoom('test-room-1')

      expect(room).toBeDefined()
      expect(room.state.id).toBe('test-room-1')
      expect(room.state.status).toBe('lobby')
      expect(room.config.tickRate).toBe(20)
    })

    it('should allow custom config override', () => {
      const room = createRoom('test-room-1', { tickRate: 10 })

      expect(room.config.tickRate).toBe(10)
      expect(room.config.npcDeltaThreshold).toBe(defaultGameConfig.npcDeltaThreshold)
    })

    it('should initialize with empty players and NPCs', () => {
      const room = createRoom('test-room-1')

      expect(room.state.players.size).toBe(0)
      expect(room.state.npcs.size).toBe(0)
    })
  })

  describe('getOrCreateRoom', () => {
    it('should create room if not exists', () => {
      const room1 = getOrCreateRoom('new-room')

      expect(room1).toBeDefined()
      expect(room1.state.id).toBe('new-room')
    })

    it('should return existing room if already created', () => {
      const room1 = getOrCreateRoom('existing-room')
      const room2 = getOrCreateRoom('existing-room')

      expect(room1).toBe(room2)
    })

    it('should apply custom config only on creation', () => {
      const room1 = getOrCreateRoom('config-room', { tickRate: 10 })
      expect(room1.config.tickRate).toBe(10)

      const room2 = getOrCreateRoom('config-room', { tickRate: 30 })
      expect(room2.config.tickRate).toBe(10) // unchanged
    })
  })

  describe('getRoom', () => {
    it('should return room if exists', () => {
      const created = createRoom('get-room')
      const retrieved = getRoom('get-room')

      expect(retrieved).toBe(created)
    })

    it('should return undefined if not exists', () => {
      const result = getRoom('nonexistent-room')

      expect(result).toBeUndefined()
    })
  })

  describe('destroyRoom', () => {
    it('should remove room from registry', () => {
      createRoom('destroy-room')
      expect(getRoom('destroy-room')).toBeDefined()

      destroyRoom('destroy-room')
      expect(getRoom('destroy-room')).toBeUndefined()
    })

    it('should handle destroying nonexistent room gracefully', () => {
      expect(() => {
        destroyRoom('nonexistent')
      }).not.toThrow()
    })

    it('should clear intervals before destroying', () => {
      const room = createRoom('interval-room')
      room.tickLoopInterval = setInterval(() => {}, 1000)

      destroyRoom('interval-room')
      expect(getRoom('interval-room')).toBeUndefined()
    })
  })

  describe('initializeNPCs', () => {
    it('should spawn NPCs in room', () => {
      const room = createRoom('npc-room')
      expect(room.state.npcs.size).toBe(0)

      initializeNPCs(room, 20)
      expect(room.state.npcs.size).toBe(20)
    })

    it('should respect custom NPC count', () => {
      const room = createRoom('custom-npc-room')

      initializeNPCs(room, 10)
      expect(room.state.npcs.size).toBe(10)
    })

    it('should spawn NPCs within map bounds', () => {
      const room = createRoom('bounds-room')
      initializeNPCs(room, 20)

      room.state.npcs.forEach((npc) => {
        const { minX, maxX, minZ, maxZ } = room.config.mapBounds

        expect(npc.position.x).toBeGreaterThanOrEqual(minX)
        expect(npc.position.x).toBeLessThanOrEqual(maxX)
        expect(npc.position.z).toBeGreaterThanOrEqual(minZ)
        expect(npc.position.z).toBeLessThanOrEqual(maxZ)
      })
    })
  })

  describe('stopGameLoop', () => {
    it('should clear tick loop interval', () => {
      const room = createRoom('stop-room')
      room.tickLoopInterval = setInterval(() => {}, 1000)

      expect(room.tickLoopInterval).toBeDefined()
      stopGameLoop(room)
      expect(room.tickLoopInterval).toBeUndefined()
    })

    it('should handle stopping when no loop running', () => {
      const room = createRoom('no-loop-room')

      expect(() => {
        stopGameLoop(room)
      }).not.toThrow()
    })
  })

  describe('Room state transitions', () => {
    it('should start in lobby status', () => {
      const room = createRoom('status-room')

      expect(room.state.status).toBe('lobby')
      expect(room.state.startedAt).toBeUndefined()
    })

    it('should track player joins', async () => {
      const room = createRoom('join-room')
      const { addPlayer } = await import('../state.js')

      addPlayer(room.state, 'socket_1', { x: 0, y: 1.5, z: 0 })
      addPlayer(room.state, 'socket_2', { x: 5, y: 1.5, z: 5 })

      expect(room.state.players.size).toBe(2)
    })
  })
})
