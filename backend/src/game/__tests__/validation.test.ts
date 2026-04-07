import { describe, it, expect, beforeEach } from 'vitest'
import {
  validatePlayerMovement,
  validateInteractionDistance,
  validateGuardCatch,
  validatePayloadOwnership,
} from '../validation.js'
import { defaultGameConfig } from '../room-manager.js'

describe('Validation', () => {
  describe('validatePlayerMovement', () => {
    it('should allow movement within bounds', () => {
      const oldPos = { x: 0, y: 1.5, z: 0 }
      const newPos = { x: 0.25, y: 1.5, z: 0 } // 0.25m in 0.05s = 5 m/s (legal walk speed)

      const result = validatePlayerMovement(newPos, oldPos, 0.05, defaultGameConfig)

      expect(result.valid).toBe(true)
    })

    it('should reject movement beyond max X bound', () => {
      const oldPos = { x: 0, y: 1.5, z: 0 }
      const newPos = { x: 100, y: 1.5, z: 0 } // beyond maxX=50

      const result = validatePlayerMovement(newPos, oldPos, 0.05, defaultGameConfig)

      expect(result.valid).toBe(false)
      expect(result.reason).toContain('bounds')
    })

    it('should reject movement below min Y bound', () => {
      const oldPos = { x: 0, y: 1.5, z: 0 }
      const newPos = { x: 0, y: -5, z: 0 } // below minY=0

      const result = validatePlayerMovement(newPos, oldPos, 0.05, defaultGameConfig)

      expect(result.valid).toBe(false)
      expect(result.reason).toContain('bounds')
    })

    it('should reject impossible speed (speed hack)', () => {
      const oldPos = { x: 0, y: 1.5, z: 0 }
      const newPos = { x: 50, y: 1.5, z: 0 } // 1000 u/s over 50ms = impossible

      const result = validatePlayerMovement(newPos, oldPos, 0.05, defaultGameConfig)

      expect(result.valid).toBe(false)
      expect(result.reason).toContain('Speed')
    })

    it('should allow legal walking speed', () => {
      const oldPos = { x: 0, y: 1.5, z: 0 }
      const newPos = { x: 0.25, y: 1.5, z: 0 } // 5 u/s (walk speed)

      const result = validatePlayerMovement(newPos, oldPos, 0.05, defaultGameConfig)

      expect(result.valid).toBe(true)
    })
  })

  describe('validateInteractionDistance', () => {
    it('should allow interaction within 2m', () => {
      const playerPos = { x: 0, y: 1.5, z: 0 }
      const objectPos = { x: 1, y: 1.5, z: 0 } // 1m away

      const result = validateInteractionDistance(playerPos, objectPos, 2.0)

      expect(result.valid).toBe(true)
    })

    it('should reject interaction beyond 2m', () => {
      const playerPos = { x: 0, y: 1.5, z: 0 }
      const objectPos = { x: 5, y: 1.5, z: 0 } // 5m away

      const result = validateInteractionDistance(playerPos, objectPos, 2.0)

      expect(result.valid).toBe(false)
      expect(result.reason).toContain('exceeds max')
    })

    it('should allow interaction at exactly 2m', () => {
      const playerPos = { x: 0, y: 1.5, z: 0 }
      const objectPos = { x: 2, y: 1.5, z: 0 }

      const result = validateInteractionDistance(playerPos, objectPos, 2.0)

      expect(result.valid).toBe(true)
    })
  })

  describe('validateGuardCatch', () => {
    it('should allow catch within 1.5m', () => {
      const guardPos = { x: 0, y: 1.5, z: 0 }
      const targetPos = { x: 1, y: 1.5, z: 0 } // 1m away

      const result = validateGuardCatch(guardPos, targetPos, 'walking', 1.5)

      expect(result.valid).toBe(true)
    })

    it('should reject catch beyond 1.5m', () => {
      const guardPos = { x: 0, y: 1.5, z: 0 }
      const targetPos = { x: 5, y: 1.5, z: 0 } // 5m away

      const result = validateGuardCatch(guardPos, targetPos, 'walking', 1.5)

      expect(result.valid).toBe(false)
      expect(result.reason).toContain('exceeds catch range')
    })

    it('should reject catch if target is camuflaged', () => {
      const guardPos = { x: 0, y: 1.5, z: 0 }
      const targetPos = { x: 0.5, y: 1.5, z: 0 } // 0.5m away (in range)

      const result = validateGuardCatch(guardPos, targetPos, 'camuflaged', 1.5)

      expect(result.valid).toBe(false)
      expect(result.reason).toContain('camuflaged')
    })

    it('should allow catch at exactly 1.5m', () => {
      const guardPos = { x: 0, y: 1.5, z: 0 }
      const targetPos = { x: 1.5, y: 1.5, z: 0 }

      const result = validateGuardCatch(guardPos, targetPos, 'idle', 1.5)

      expect(result.valid).toBe(true)
    })

    it('should allow catch when target is idle or walking', () => {
      const guardPos = { x: 0, y: 1.5, z: 0 }
      const targetPos = { x: 1, y: 1.5, z: 0 }

      expect(validateGuardCatch(guardPos, targetPos, 'idle', 1.5).valid).toBe(true)
      expect(validateGuardCatch(guardPos, targetPos, 'walking', 1.5).valid).toBe(true)
      expect(validateGuardCatch(guardPos, targetPos, 'sprinting', 1.5).valid).toBe(true)
    })
  })

  describe('validatePayloadOwnership', () => {
    it('should accept matching IDs', () => {
      const result = validatePayloadOwnership('socket_123', 'socket_123')

      expect(result.valid).toBe(true)
    })

    it('should reject mismatched IDs', () => {
      const result = validatePayloadOwnership('socket_123', 'socket_456')

      expect(result.valid).toBe(false)
      expect(result.reason).toContain('mismatch')
    })
  })
})
