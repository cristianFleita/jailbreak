/**
 * Reconnection system: allows players to rejoin after temporary disconnects.
 * Players have 30 seconds to reconnect; their slot is held during this time.
 */

import { GameRoomState, PlayerState } from './types.js'

/**
 * Represents a disconnected player's state snapshot.
 */
interface DisconnectedPlayerSlot {
  playerId: string
  playerState: PlayerState
  disconnectTime: number // timestamp
  expiresAt: number // timestamp
}

/**
 * Tracks disconnected players per room.
 * Key: roomId, Value: Map of (playerId → DisconnectedPlayerSlot)
 */
const disconnectedSlots = new Map<string, Map<string, DisconnectedPlayerSlot>>()

/**
 * Called when a player disconnects.
 * Saves their state snapshot for up to 30 seconds.
 */
export function markPlayerDisconnected(
  roomId: string,
  player: PlayerState,
  reconnectTimeout: number = 30
): void {
  if (!disconnectedSlots.has(roomId)) {
    disconnectedSlots.set(roomId, new Map())
  }

  const now = Date.now()
  const slot: DisconnectedPlayerSlot = {
    playerId: player.id,
    playerState: { ...player }, // snapshot
    disconnectTime: now,
    expiresAt: now + reconnectTimeout * 1000,
  }

  disconnectedSlots.get(roomId)!.set(player.id, slot)
  console.log(`[RECONNECT] Saved slot for ${player.id} in room ${roomId} (expires in ${reconnectTimeout}s)`)
}

/**
 * Called when a player socket reconnects.
 * Checks if their slot is still valid and restores their state.
 * Returns their previous PlayerState if valid, null if expired or not found.
 */
export function restorePlayerConnection(
  roomId: string,
  playerId: string
): { success: boolean; playerState?: PlayerState; reason?: string } {
  const slots = disconnectedSlots.get(roomId)
  if (!slots || !slots.has(playerId)) {
    return { success: false, reason: 'No saved slot found' }
  }

  const slot = slots.get(playerId)!
  const now = Date.now()

  // Check if slot has expired
  if (now > slot.expiresAt) {
    slots.delete(playerId)
    return { success: false, reason: 'Reconnection window expired' }
  }

  // Restore and clean up
  const playerState = slot.playerState
  slots.delete(playerId)

  console.log(`[RECONNECT] Restored ${playerId} to room ${roomId}`)
  return { success: true, playerState }
}

/**
 * Cleans up expired slots for a room.
 * Called periodically or when last player leaves.
 */
export function cleanupExpiredSlots(roomId: string): void {
  const slots = disconnectedSlots.get(roomId)
  if (!slots) return

  const now = Date.now()
  let cleanedCount = 0

  for (const [playerId, slot] of slots.entries()) {
    if (now > slot.expiresAt) {
      slots.delete(playerId)
      cleanedCount++
    }
  }

  if (cleanedCount > 0) {
    console.log(`[RECONNECT] Cleaned up ${cleanedCount} expired slots in room ${roomId}`)
  }

  // If room is now empty of both active and disconnected players, remove it
  if (slots.size === 0) {
    disconnectedSlots.delete(roomId)
  }
}

/**
 * Completely clears a room's disconnected slots (called on game end).
 */
export function clearRoomSlots(roomId: string): void {
  disconnectedSlots.delete(roomId)
  console.log(`[RECONNECT] Cleared all slots for room ${roomId}`)
}

/**
 * Returns diagnostic info about active disconnected slots.
 */
export function debugGetDisconnectedSlots(roomId: string): Array<{
  playerId: string
  secondsUntilExpiry: number
}> {
  const slots = disconnectedSlots.get(roomId)
  if (!slots) return []

  const now = Date.now()
  return Array.from(slots.values()).map((slot) => ({
    playerId: slot.playerId,
    secondsUntilExpiry: Math.max(0, Math.round((slot.expiresAt - now) / 1000)),
  }))
}
