import type { Server, Socket } from 'socket.io'

export interface VoicePeer {
  userId: string
  muted: boolean
  deafened: boolean
}

export interface VoiceJoinPayload {
  roomId: string
  userId?: string
}

export interface VoiceSignalPayload {
  toUserId: string
  fromUserId?: string
  signal: unknown
}

export interface VoiceStatePayload {
  userId?: string
  muted?: boolean
  deafened?: boolean
}

interface VoicePeerRecord extends VoicePeer {
  socketId: string
  joinedAt: number
}

const peersByRoom = new Map<string, Map<string, VoicePeerRecord>>()
const roomBySocket = new Map<string, string>()
const userBySocket = new Map<string, string>()

/**
 * Minimal Socket.io signaling layer for WebRTC voice.
 *
 * The backend never receives raw audio. It only tracks which authenticated
 * user is reachable at which socket and relays SDP / ICE messages inside the
 * current game room.
 */
export function joinVoiceRoom(
  io: Server,
  socket: Socket,
  roomId: string,
  userId: string
): VoicePeer[] {
  leaveVoiceRoom(io, socket, 'rejoin')

  let roomPeers = peersByRoom.get(roomId)
  if (!roomPeers) {
    roomPeers = new Map<string, VoicePeerRecord>()
    peersByRoom.set(roomId, roomPeers)
  }

  const existing = roomPeers.get(userId)
  if (existing && existing.socketId !== socket.id) {
    notifyPeerLeft(io, roomId, userId, 'replaced')
    roomBySocket.delete(existing.socketId)
    userBySocket.delete(existing.socketId)
  }

  const initialPeers = listVoicePeers(roomId).filter(peer => peer.userId !== userId)

  roomPeers.set(userId, {
    userId,
    socketId: socket.id,
    muted: false,
    deafened: false,
    joinedAt: Date.now(),
  })
  roomBySocket.set(socket.id, roomId)
  userBySocket.set(socket.id, userId)

  socket.emit('voice:peers', { peers: initialPeers })
  return initialPeers
}

export function relayVoiceSignal(
  io: Server,
  socket: Socket,
  payload: VoiceSignalPayload
): boolean {
  const roomId = roomBySocket.get(socket.id)
  const fromUserId = userBySocket.get(socket.id)
  if (!roomId || !fromUserId || !payload?.toUserId || payload.signal == null) return false

  const target = peersByRoom.get(roomId)?.get(payload.toUserId)
  if (!target) return false

  io.to(target.socketId).emit('voice:signal', {
    toUserId: payload.toUserId,
    fromUserId,
    signal: payload.signal,
  })
  return true
}

export function updateVoiceState(
  io: Server,
  socket: Socket,
  payload: VoiceStatePayload
): boolean {
  const roomId = roomBySocket.get(socket.id)
  const userId = userBySocket.get(socket.id)
  if (!roomId || !userId) return false

  const peer = peersByRoom.get(roomId)?.get(userId)
  if (!peer) return false

  if (typeof payload.muted === 'boolean') peer.muted = payload.muted
  if (typeof payload.deafened === 'boolean') peer.deafened = payload.deafened

  const statePayload: VoicePeer = {
    userId,
    muted: peer.muted,
    deafened: peer.deafened,
  }

  for (const target of peersByRoom.get(roomId)?.values() ?? []) {
    io.to(target.socketId).emit('voice:state', statePayload)
  }
  return true
}

export function leaveVoiceRoom(
  io: Server,
  socket: Socket,
  reason: string = 'left'
): boolean {
  const roomId = roomBySocket.get(socket.id)
  const userId = userBySocket.get(socket.id)
  if (!roomId || !userId) return false

  const roomPeers = peersByRoom.get(roomId)
  roomPeers?.delete(userId)
  roomBySocket.delete(socket.id)
  userBySocket.delete(socket.id)

  notifyPeerLeft(io, roomId, userId, reason)
  if (roomPeers && roomPeers.size === 0) peersByRoom.delete(roomId)
  return true
}

export function cleanupVoiceRoom(
  io: Server,
  roomId: string,
  reason: string = 'room-closed'
): void {
  const roomPeers = peersByRoom.get(roomId)
  if (!roomPeers) return

  const peers = Array.from(roomPeers.values())
  for (const leaving of peers) {
    for (const target of peers) {
      if (target.userId === leaving.userId) continue
      io.to(target.socketId).emit('voice:peer-left', { userId: leaving.userId, reason })
    }
  }

  for (const peer of peers) {
    roomBySocket.delete(peer.socketId)
    userBySocket.delete(peer.socketId)
  }
  peersByRoom.delete(roomId)
}

export function listVoicePeers(roomId: string): VoicePeer[] {
  const roomPeers = peersByRoom.get(roomId)
  if (!roomPeers) return []

  return Array.from(roomPeers.values())
    .sort((a, b) => a.joinedAt - b.joinedAt)
    .map(peer => ({
      userId: peer.userId,
      muted: peer.muted,
      deafened: peer.deafened,
    }))
}

export function resetVoiceSignalingForTests(): void {
  peersByRoom.clear()
  roomBySocket.clear()
  userBySocket.clear()
}

function notifyPeerLeft(io: Server, roomId: string, userId: string, reason: string): void {
  const roomPeers = peersByRoom.get(roomId)
  if (!roomPeers) return

  for (const peer of roomPeers.values()) {
    io.to(peer.socketId).emit('voice:peer-left', { userId, reason })
  }
}
