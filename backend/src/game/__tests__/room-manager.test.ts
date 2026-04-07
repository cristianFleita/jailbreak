import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import {
  createRoom,
  getRoom,
  destroyRoom,
  roomExists,
  stopGameLoop,
  initializeNPCs,
  defaultGameConfig,
} from '../room-manager.js'

const HOST = 'host-user-1'

describe('Room Manager', () => {
  afterEach(() => {
    // Clean up rooms after each test
    for (const name of [
      'test-room-1', 'new-room', 'existing-room', 'config-room',
      'get-room', 'destroy-room', 'interval-room', 'npc-room',
      'custom-npc-room', 'bounds-room', 'stop-room', 'no-loop-room',
      'status-room', 'join-room', 'dup-room',
    ]) {
      const room = getRoom(name)
      if (room) {
        stopGameLoop(room)
        destroyRoom(name)
      }
    }
  })

  describe('createRoom', () => {
    it('should create a new room with default config', () => {
      const room = createRoom('test-room-1', HOST)

      expect(room).not.toBeNull()
      expect(room!.state.id).toBe('test-room-1')
      expect(room!.state.hostUserId).toBe(HOST)
      expect(room!.state.status).toBe('lobby')
      expect(room!.config.tickRate).toBe(20)
    })

    it('should allow custom config override', () => {
      const room = createRoom('test-room-1', HOST, { tickRate: 10 })

      expect(room!.config.tickRate).toBe(10)
      expect(room!.config.npcDeltaThreshold).toBe(defaultGameConfig.npcDeltaThreshold)
    })

    it('should initialize with empty players and NPCs', () => {
      const room = createRoom('test-room-1', HOST)

      expect(room!.state.players.size).toBe(0)
      expect(room!.state.npcs.size).toBe(0)
    })

    it('should return null if room name already exists', () => {
      createRoom('dup-room', HOST)
      const duplicate = createRoom('dup-room', 'other-host')

      expect(duplicate).toBeNull()
    })
  })

  describe('getRoom / roomExists', () => {
    it('should return room if exists', () => {
      const created = createRoom('get-room', HOST)
      const retrieved = getRoom('get-room')

      expect(retrieved).toBe(created)
      expect(roomExists('get-room')).toBe(true)
    })

    it('should return undefined if not exists', () => {
      const result = getRoom('nonexistent-room')

      expect(result).toBeUndefined()
      expect(roomExists('nonexistent-room')).toBe(false)
    })
  })

  describe('destroyRoom', () => {
    it('should remove room from registry', () => {
      createRoom('destroy-room', HOST)
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
      const room = createRoom('interval-room', HOST)!
      room.tickLoopInterval = setInterval(() => {}, 1000)

      destroyRoom('interval-room')
      expect(getRoom('interval-room')).toBeUndefined()
    })
  })

  describe('initializeNPCs', () => {
    it('should spawn NPCs in room', () => {
      const room = createRoom('npc-room', HOST)!
      expect(room.state.npcs.size).toBe(0)

      initializeNPCs(room, 20)
      expect(room.state.npcs.size).toBe(20)
    })

    it('should respect custom NPC count', () => {
      const room = createRoom('custom-npc-room', HOST)!

      initializeNPCs(room, 10)
      expect(room.state.npcs.size).toBe(10)
    })

    it('should spawn NPCs within map bounds', () => {
      const room = createRoom('bounds-room', HOST)!
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
      const room = createRoom('stop-room', HOST)!
      room.tickLoopInterval = setInterval(() => {}, 1000)

      expect(room.tickLoopInterval).toBeDefined()
      stopGameLoop(room)
      expect(room.tickLoopInterval).toBeUndefined()
    })

    it('should handle stopping when no loop running', () => {
      const room = createRoom('no-loop-room', HOST)!

      expect(() => {
        stopGameLoop(room)
      }).not.toThrow()
    })
  })

  describe('Room state transitions', () => {
    it('should start in lobby status', () => {
      const room = createRoom('status-room', HOST)!

      expect(room.state.status).toBe('lobby')
      expect(room.state.startedAt).toBeUndefined()
    })

    it('should track player joins', async () => {
      const room = createRoom('join-room', HOST)!
      const { addPlayer } = await import('../state.js')

      addPlayer(room.state, 'socket_1', HOST, { x: 0, y: 1.5, z: 0 })
      addPlayer(room.state, 'socket_2', 'user_2', { x: 5, y: 1.5, z: 5 })

      expect(room.state.players.size).toBe(2)
      expect(room.state.playersByUserId.size).toBe(2)
    })
  })
})
