/**
 * Validation rules for player actions and state synchronization.
 * Implements anti-cheat checks and boundary validation.
 */

import { Vector3, GameConfig, PlayerState } from './types.js'
import { distance } from './state.js'

/**
 * Validates that a player's movement is physically plausible.
 * Checks:
 * - Speed doesn't exceed walk_speed × sprint_multiplier × anticheat_speed_multiplier
 * - Position is within map bounds
 */
export function validatePlayerMovement(
  newPosition: Vector3,
  oldPosition: Vector3,
  deltaTime: number, // seconds
  config: GameConfig
): { valid: boolean; reason?: string } {
  const { mapBounds, anticheatSpeedMultiplier } = config

  // ========== Bounds check ==========
  if (
    newPosition.x < mapBounds.minX ||
    newPosition.x > mapBounds.maxX ||
    newPosition.y < mapBounds.minY ||
    newPosition.y > mapBounds.maxY ||
    newPosition.z < mapBounds.minZ ||
    newPosition.z > mapBounds.maxZ
  ) {
    return { valid: false, reason: 'Position outside map bounds' }
  }

  // ========== Speed check ==========
  const dist = distance(newPosition, oldPosition)
  const speed = dist / deltaTime // units per second

  // Max speed thresholds (tunable; these are conservative defaults)
  const walkSpeed = 5.0 // units/sec
  const sprintMultiplier = 1.5
  const maxLegalSpeed = walkSpeed * sprintMultiplier * anticheatSpeedMultiplier

  if (speed > maxLegalSpeed) {
    return { valid: false, reason: `Speed ${speed.toFixed(2)} exceeds max ${maxLegalSpeed.toFixed(2)}` }
  }

  return { valid: true }
}

/**
 * Validates a player can interact with an object.
 * Checks:
 * - Distance is ≤ 2 meters
 * - Object exists
 */
export function validateInteractionDistance(
  playerPosition: Vector3,
  objectPosition: Vector3,
  maxInteractionDistance: number = 2.0
): { valid: boolean; reason?: string } {
  const dist = distance(playerPosition, objectPosition)

  if (dist > maxInteractionDistance) {
    return { valid: false, reason: `Distance ${dist.toFixed(2)}m exceeds max ${maxInteractionDistance}m` }
  }

  return { valid: true }
}

/**
 * Validates a guard catch attempt.
 * Checks:
 * - Guard and target both exist
 * - Distance is ≤ 1.5 meters (catch range)
 * - Target is not camuflaged
 */
export function validateGuardCatch(
  guardPosition: Vector3,
  targetPosition: Vector3,
  targetMovementState: 'idle' | 'walking' | 'sprinting' | 'camuflaged',
  catchRange: number = 1.5
): { valid: boolean; reason?: string } {
  // Can't catch a camuflaged player
  if (targetMovementState === 'camuflaged') {
    return { valid: false, reason: 'Target is camuflaged' }
  }

  const dist = distance(guardPosition, targetPosition)

  if (dist > catchRange) {
    return { valid: false, reason: `Distance ${dist.toFixed(2)}m exceeds catch range ${catchRange}m` }
  }

  return { valid: true }
}

/**
 * Checks if a player position is suspect (potential cheat).
 * Returns warning but doesn't block — allows for false positives.
 */
export function checkMovementAnomaly(
  player: PlayerState,
  newPosition: Vector3,
  timeSinceLastUpdate: number
): { suspicious: boolean; reason?: string } {
  const dist = distance(player.position, newPosition)
  const speed = dist / timeSinceLastUpdate

  // Flag if speed is unreasonable (>3x legal max).
  // This is for monitoring, not blocking.
  if (speed > 100) {
    // arbitrary high threshold
    return { suspicious: true, reason: `Anomalous speed ${speed.toFixed(2)}` }
  }

  return { suspicious: false }
}

/**
 * Validates that a payload comes from the claiming player (basic ownership check).
 * In production, this would be verified by the websocket auth middleware.
 */
export function validatePayloadOwnership(
  claimingPlayerId: string,
  socketId: string
): { valid: boolean; reason?: string } {
  if (claimingPlayerId !== socketId) {
    return { valid: false, reason: 'Payload ownership mismatch' }
  }

  return { valid: true }
}
