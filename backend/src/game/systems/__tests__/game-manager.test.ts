import { describe, it, expect, beforeEach } from 'vitest'
import { GameManager } from '../game-manager.js'
import { createGameRoomState } from '../../state.js'
import { defaultGameConfig } from '../../room-manager.js'
import { GameRoom } from '../../types.js'

describe('GameManager', () => {
  let gameManager: GameManager
  let room: GameRoom

  beforeEach(() => {
    const state = createGameRoomState('test-room', defaultGameConfig)
    room = { state, config: defaultGameConfig }
    gameManager = new GameManager(room)
  })

  describe('Initialization', () => {
    it('should initialize all systems', () => {
      expect(gameManager.npcBehavior).toBeDefined()
      expect(gameManager.pursuit).toBeDefined()
      expect(gameManager.disguise).toBeDefined()
      expect(gameManager.penalty).toBeDefined()
      expect(gameManager.inventory).toBeDefined()
      expect(gameManager.escapeRoutes).toBeDefined()
      expect(gameManager.phases).toBeDefined()
      expect(gameManager.victory).toBeDefined()
    })
  })

  describe('Game Tick', () => {
    it('should not end game immediately', () => {
      const result = gameManager.tick()

      expect(result.shouldEnd).toBe(false)
      expect(result.winner).toBeUndefined()
    })

    it('should return structured tick result', () => {
      const result = gameManager.tick()

      expect(result).toHaveProperty('shouldEnd')
      expect(typeof result.shouldEnd).toBe('boolean')
    })
  })

  describe('Guard Errors and Riot', () => {
    it('should track guard errors', () => {
      gameManager.penalty.recordGuardError('guard_1', 'false_accusation')

      expect(gameManager.penalty.getGuardErrorCount('guard_1')).toBe(1)
    })

    it('should make riot available after 3 errors', () => {
      gameManager.penalty.recordGuardError('guard_1', 'error_1')
      gameManager.penalty.recordGuardError('guard_1', 'error_2')

      expect(gameManager.penalty.isRiotAvailable()).toBe(false)

      gameManager.penalty.recordGuardError('guard_1', 'error_3')

      expect(gameManager.penalty.isRiotAvailable()).toBe(true)
    })
  })

  describe('Event Callbacks', () => {
    it('should handle guard mark', async () => {
      const { addPlayer, spawnNPCs } = await import('../../state.js')

      // Must spawn NPCs for pursuit system to work
      spawnNPCs(room.state, room.config, 20)

      const guard = addPlayer(room.state, 'guard_1', { x: 0, y: 1.5, z: 0 })
      const prisoner = addPlayer(room.state, 'prisoner_1', {
        x: 10,
        y: 1.5,
        z: 10,
      })

      // Should not throw
      expect(() => {
        gameManager.onGuardMark('guard_1', 'prisoner_1')
      }).not.toThrow()

      // Pursuit should be active
      expect(gameManager.pursuit.isBeingChased('prisoner_1')).toBe(true)
    })

    it('should handle guard catch', async () => {
      const { addPlayer, spawnNPCs } = await import('../../state.js')

      // Spawn NPCs for pursuit system
      spawnNPCs(room.state, room.config, 20)

      addPlayer(room.state, 'guard_1', { x: 0, y: 1.5, z: 0 })
      const prisoner = addPlayer(room.state, 'prisoner_1', {
        x: 10,
        y: 1.5,
        z: 10,
      })

      gameManager.onGuardMark('guard_1', 'prisoner_1')
      gameManager.onGuardCatch('prisoner_1')

      // Pursuit should end
      expect(gameManager.pursuit.isBeingChased('prisoner_1')).toBe(false)
    })

    it('should handle item pickup', async () => {
      const { addPlayer } = await import('../../state.js')

      const prisoner = addPlayer(room.state, 'prisoner_1', { x: 0, y: 1.5, z: 0 })

      // Add prisoner to escape routes (since they were added after gameManager init)
      gameManager.escapeRoutes.addPrisoner('prisoner_1')

      const item = {
        id: 'item_keycard',
        type: 'keycard',
        position: { x: 0, y: 1.5, z: 0 },
        isPickedUp: false,
      }
      room.state.items.set(item.id, item)

      expect(() => {
        gameManager.onItemPickup('prisoner_1', 'item_keycard')
      }).not.toThrow()

      const progress = gameManager.escapeRoutes.getProgress('prisoner_1')
      expect(progress?.collectedItems.has('item_keycard')).toBe(true)
    })

    it('should handle riot activation', () => {
      gameManager.onRiotActivated()

      expect(gameManager.phases.getCurrentPhase()).toBe('riot')
    })
  })

  describe('Game Stats', () => {
    it('should provide game statistics', () => {
      const stats = gameManager.getGameStats()

      expect(stats).toHaveProperty('victoryStats')
      expect(stats).toHaveProperty('guardErrors')
      expect(stats).toHaveProperty('activePursuits')
      expect(stats).toHaveProperty('escapedPrisoners')
      expect(stats).toHaveProperty('currentPhase')
      expect(stats).toHaveProperty('phaseTimeRemaining')
    })

    it('should return correct prisoner counts', async () => {
      const { addPlayer } = await import('../../state.js')

      addPlayer(room.state, 'guard_1', { x: 0, y: 1.5, z: 0 })
      const p1 = addPlayer(room.state, 'prisoner_1', { x: 5, y: 1.5, z: 5 })
      const p2 = addPlayer(room.state, 'prisoner_2', { x: -5, y: 1.5, z: -5 })

      // Add prisoners to escape routes
      gameManager.escapeRoutes.addPrisoner('prisoner_1')
      gameManager.escapeRoutes.addPrisoner('prisoner_2')

      const stats = gameManager.getGameStats()

      expect(stats.victoryStats.totalPrisoners).toBe(2)
      expect(stats.victoryStats.alivePrisoners).toBe(2)
      expect(stats.victoryStats.caughtPrisoners).toBe(0)
    })
  })
})
