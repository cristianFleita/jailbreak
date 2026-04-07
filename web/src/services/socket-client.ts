/**
 * Socket.io client service.
 * Manages connection, authentication (persistent userId via localStorage),
 * and exposes user/room state to window globals for Unity jslib bridge.
 */

import { io, Socket } from 'socket.io-client'

const STORAGE_KEY_USER_ID = 'jailbreak_user_id'
const STORAGE_KEY_DISPLAY_NAME = 'jailbreak_display_name'

// Window globals for Unity jslib bridge
declare global {
  interface Window {
    JAILBREAK_USER_ID?: string
    JAILBREAK_ROOM_ID?: string
    JAILBREAK_SOCKET?: Socket
    JAILBREAK_USER_STATUS?: string
  }
}

export interface SocketClientOptions {
  backendUrl?: string
  displayName?: string
}

export interface RoomPlayer {
  userId: string
  displayName: string
  role: 'prisoner' | 'guard'
  isHost: boolean
}

export interface RoomState {
  roomId: string
  hostUserId: string
  status: string
  players: RoomPlayer[]
}

// ============================================================================
// Persistent userId (localStorage)
// ============================================================================

export function getSavedUserId(): string | null {
  return localStorage.getItem(STORAGE_KEY_USER_ID)
}

export function getSavedDisplayName(): string | null {
  return localStorage.getItem(STORAGE_KEY_DISPLAY_NAME)
}

function saveUserIdentity(userId: string, displayName: string): void {
  localStorage.setItem(STORAGE_KEY_USER_ID, userId)
  localStorage.setItem(STORAGE_KEY_DISPLAY_NAME, displayName)
}

// ============================================================================
// Event callbacks (set by React components or Unity)
// ============================================================================

type EventCallback<T = any> = (data: T) => void

const listeners: Record<string, EventCallback[]> = {}

export function on(event: string, cb: EventCallback): void {
  if (!listeners[event]) listeners[event] = []
  listeners[event].push(cb)
}

export function off(event: string, cb: EventCallback): void {
  if (!listeners[event]) return
  listeners[event] = listeners[event].filter(fn => fn !== cb)
}

function emit(event: string, data?: any): void {
  if (!listeners[event]) return
  for (const cb of listeners[event]) {
    try { cb(data) } catch (e) { console.error(`[socket-client] listener error on ${event}:`, e) }
  }
}

// ============================================================================
// Socket instance
// ============================================================================

let socket: Socket | null = null
let authenticated = false

export function getSocket(): Socket | null {
  return socket
}

export function isConnected(): boolean {
  return socket?.connected ?? false
}

export function isAuth(): boolean {
  return authenticated
}

// ============================================================================
// Connect & authenticate
// ============================================================================

export function connect(options: SocketClientOptions = {}): Socket {
  if (socket?.connected) return socket

  const url = options.backendUrl || window.BACKEND_URL || 'http://localhost:3001'
  const displayName = options.displayName || getSavedDisplayName() || `Player_${Math.random().toString(36).slice(2, 8)}`

  socket = io(url, {
    transports: ['websocket', 'polling'],
    reconnection: true,
    reconnectionAttempts: 10,
    reconnectionDelay: 1000,
  })

  window.JAILBREAK_SOCKET = socket

  // ---- Connection lifecycle ----
  socket.on('connect', () => {
    console.log('[socket-client] Connected:', socket!.id)

    // Authenticate immediately
    const savedUserId = getSavedUserId()
    socket!.emit('auth:register', {
      userId: savedUserId || undefined,
      displayName,
    })
  })

  socket.on('disconnect', (reason) => {
    console.log('[socket-client] Disconnected:', reason)
    authenticated = false
    emit('disconnected', { reason })
  })

  socket.on('connect_error', (err) => {
    console.error('[socket-client] Connection error:', err.message)
    emit('connection-error', { message: err.message })
  })

  // ---- Auth response ----
  socket.on('auth:registered', (data: { userId: string; displayName: string }) => {
    console.log('[socket-client] Authenticated as', data.userId)
    authenticated = true
    saveUserIdentity(data.userId, data.displayName)

    // Expose to Unity jslib
    window.JAILBREAK_USER_ID = data.userId
    window.JAILBREAK_USER_STATUS = 'idle'

    emit('authenticated', data)
  })

  // ---- Room events ----
  socket.on('room:created', (data: { roomId: string; hostUserId: string }) => {
    window.JAILBREAK_ROOM_ID = data.roomId
    emit('room:created', data)
  })

  socket.on('room:state', (data: RoomState) => {
    window.JAILBREAK_ROOM_ID = data.roomId
    emit('room:state', data)
  })

  socket.on('room:player-joined', (data: any) => {
    emit('room:player-joined', data)
  })

  socket.on('room:player-left', (data: any) => {
    emit('room:player-left', data)
  })

  socket.on('room:kicked', (data: any) => {
    window.JAILBREAK_ROOM_ID = undefined
    window.JAILBREAK_USER_STATUS = 'idle'
    emit('room:kicked', data)
  })

  socket.on('room:destroyed', (data: any) => {
    window.JAILBREAK_ROOM_ID = undefined
    window.JAILBREAK_USER_STATUS = 'idle'
    emit('room:destroyed', data)
  })

  socket.on('room:list', (data: RoomState[]) => {
    emit('room:list', data)
  })

  // ---- Game events ----
  socket.on('game:start', (data: any) => {
    window.JAILBREAK_USER_STATUS = 'in-game'
    emit('game:start', data)
  })

  socket.on('game:end', (data: any) => {
    window.JAILBREAK_USER_STATUS = data.winner // 'prisoners' or 'guards'
    emit('game:end', data)
  })

  socket.on('game:reconnect', (data: any) => {
    window.JAILBREAK_USER_STATUS = 'in-game'
    emit('game:reconnect', data)
  })

  socket.on('player:state', (data: any) => emit('player:state', data))
  socket.on('npc:positions', (data: any) => emit('npc:positions', data))

  // ---- Errors ----
  socket.on('game:error', (data: { code?: string; message: string }) => {
    console.warn('[socket-client] Server error:', data)
    emit('error', data)
  })

  return socket
}

// ============================================================================
// Room actions (called by React UI or Unity)
// ============================================================================

export function createRoom(roomName: string): void {
  socket?.emit('room:create', { roomName })
}

export function joinRoom(roomId: string): void {
  socket?.emit('room:join', { roomId })
}

export function leaveRoom(): void {
  socket?.emit('room:leave')
  window.JAILBREAK_ROOM_ID = undefined
  window.JAILBREAK_USER_STATUS = 'idle'
}

export function kickPlayer(targetUserId: string): void {
  socket?.emit('room:kick', { targetUserId })
}

export function startGame(): void {
  socket?.emit('room:start')
}

export function listRooms(): void {
  socket?.emit('room:list')
}

export function getRoomState(): void {
  socket?.emit('room:get-state')
}

// ============================================================================
// Disconnect
// ============================================================================

export function disconnect(): void {
  socket?.disconnect()
  socket = null
  authenticated = false
  window.JAILBREAK_SOCKET = undefined
  window.JAILBREAK_ROOM_ID = undefined
  window.JAILBREAK_USER_STATUS = undefined
}