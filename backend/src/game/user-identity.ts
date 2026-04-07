/**
 * User Identity System
 * Manages persistent user IDs that survive socket reconnections.
 * Users send their userId (from localStorage) on connect; the server
 * maps socketId ↔ userId and tracks user status across rooms.
 */

import { randomUUID } from 'crypto'
import { UserProfile, UserStatus } from './types.js'

/**
 * In-memory user registry: userId → UserProfile
 */
const users = new Map<string, UserProfile>()

/**
 * Reverse lookup: socketId → userId
 */
const socketToUser = new Map<string, string>()

/**
 * Registers a user. If userId is provided and already known, updates the
 * socket mapping. Otherwise generates a new userId.
 */
export function registerUser(
  socketId: string,
  existingUserId?: string,
  displayName?: string
): UserProfile {
  // Returning user (has a userId from localStorage)
  if (existingUserId && users.has(existingUserId)) {
    const profile = users.get(existingUserId)!

    // Clean up old socket mapping if they had one
    if (profile.socketId && profile.socketId !== socketId) {
      socketToUser.delete(profile.socketId)
    }

    profile.socketId = socketId
    profile.lastSeenAt = Date.now()
    if (displayName) profile.displayName = displayName

    socketToUser.set(socketId, existingUserId)
    console.log(`[AUTH] Returning user ${existingUserId} (socket ${socketId})`)
    return profile
  }

  // New user
  const userId = existingUserId || randomUUID()
  const profile: UserProfile = {
    userId,
    displayName: displayName || `Player_${userId.slice(0, 6)}`,
    status: 'idle',
    socketId,
    createdAt: Date.now(),
    lastSeenAt: Date.now(),
  }

  users.set(userId, profile)
  socketToUser.set(socketId, userId)
  console.log(`[AUTH] New user ${userId} "${profile.displayName}" (socket ${socketId})`)
  return profile
}

/**
 * Gets a user profile by userId.
 */
export function getUser(userId: string): UserProfile | undefined {
  return users.get(userId)
}

/**
 * Gets a user profile by their current socketId.
 */
export function getUserBySocket(socketId: string): UserProfile | undefined {
  const userId = socketToUser.get(socketId)
  if (!userId) return undefined
  return users.get(userId)
}

/**
 * Gets the userId for a socket.
 */
export function getUserIdBySocket(socketId: string): string | undefined {
  return socketToUser.get(socketId)
}

/**
 * Updates user status.
 */
export function setUserStatus(userId: string, status: UserStatus, roomId?: string): void {
  const profile = users.get(userId)
  if (!profile) return

  profile.status = status
  profile.currentRoomId = roomId
  profile.lastSeenAt = Date.now()
}

/**
 * Handles socket disconnect: keeps the profile but clears socketId.
 * The user can reconnect with the same userId later.
 */
export function handleUserDisconnect(socketId: string): UserProfile | undefined {
  const userId = socketToUser.get(socketId)
  if (!userId) return undefined

  const profile = users.get(userId)
  if (profile) {
    profile.socketId = undefined
    profile.lastSeenAt = Date.now()
  }

  socketToUser.delete(socketId)
  return profile
}

/**
 * Checks if a socket has been authenticated (registered).
 */
export function isAuthenticated(socketId: string): boolean {
  return socketToUser.has(socketId)
}

/**
 * Removes a user entirely (cleanup).
 */
export function removeUser(userId: string): void {
  const profile = users.get(userId)
  if (profile?.socketId) {
    socketToUser.delete(profile.socketId)
  }
  users.delete(userId)
}

/**
 * Gets total registered user count (for monitoring).
 */
export function getUserCount(): number {
  return users.size
}

/**
 * Cleans up stale users who haven't been seen in `maxAge` ms.
 * Call periodically to prevent memory leaks.
 */
export function cleanupStaleUsers(maxAgeMs: number = 3600000): number {
  const now = Date.now()
  let cleaned = 0

  for (const [userId, profile] of users) {
    if (now - profile.lastSeenAt > maxAgeMs && !profile.socketId) {
      removeUser(userId)
      cleaned++
    }
  }

  return cleaned
}
