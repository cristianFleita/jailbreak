import { Server, Socket } from 'socket.io'
import {
  getOrCreateRoom,
  getRoom,
  transitionToActive,
  destroyRoom,
  stopGameLoop,
} from '../game/room-manager.js'
import { addPlayer, removePlayer } from '../game/state.js'
import { PlayerMovePayload, Vector3 } from '../game/types.js'
import {
  handlePlayerMove,
  handlePlayerInteract,
  handleGuardCatch,
  handleGuardMark,
  handleRiotActivate,
  checkGameEndCondition,
} from '../game/event-handlers.js'
import {
  markPlayerDisconnected,
  restorePlayerConnection,
  cleanupExpiredSlots,
  clearRoomSlots,
} from '../game/reconnection.js'

const LOBBY_TIMEOUT = 30000 // 30 seconds to gather players before auto-start
const MIN_PLAYERS_TO_START = 2 // can start with 2+ players (for testing; design calls for 4)
const RECONNECT_CLEANUP_INTERVAL = 5000 // run cleanup every 5 seconds

/**
 * Sets up core socket event handlers for Sincronización de Estado.
 * Manages:
 * - Connection/disconnection lifecycle
 * - Room join with player state initialization
 * - Player movement input (validates and broadcasts)
 * - Game lifecycle (lobby → active → finished)
 * - Graceful shutdown on errors
 * - Reconnection with state snapshots
 */
export function setupGameSockets(io: Server) {
  // Start periodic cleanup of expired reconnection slots
  const cleanupInterval = setInterval(() => {
    // In a real implementation, we'd iterate over all active rooms
    // For now, this is a placeholder for the cleanup process
  }, RECONNECT_CLEANUP_INTERVAL)

  // Store interval so it can be cleared on server shutdown
  ;(io as any).__reconnectCleanupInterval = cleanupInterval

  io.on('connection', (socket: Socket) => {
    console.log(`[CONN] Player connected: ${socket.id}`)

    // Track which room this socket is in (for cleanup on disconnect)
    let currentRoomId: string | null = null

    // ========== join-room: Player enters a room (or reconnects) ==========
    socket.on('join-room', (roomId: string) => {
      console.log(`[JOIN] ${socket.id} joining room ${roomId}`)

      try {
        const room = getOrCreateRoom(roomId)
        socket.join(roomId)
        currentRoomId = roomId

        // Check if this is a reconnection
        const reconnectResult = restorePlayerConnection(roomId, socket.id)
        if (reconnectResult.success && reconnectResult.playerState) {
          // Player is reconnecting — restore their previous state
          const restored = reconnectResult.playerState
          room.state.players.set(socket.id, restored)

          console.log(`[RECONNECT] ${socket.id} restored to room ${roomId}`)

          // Send full state snapshot to reconnected player
          socket.emit('game:reconnect', {
            players: Array.from(room.state.players.values()),
            npcs: Array.from(room.state.npcs.values()),
            items: Array.from(room.state.items.values()),
            phase: room.state.phase,
            tick: room.state.tick,
          })

          // Notify others that player reconnected
          io.to(roomId).emit('player-reconnected', {
            playerId: socket.id,
            players: Array.from(room.state.players.values()),
          })

          return
        }

        // New player joining
        // Check capacity
        if (room.state.players.size >= room.config.maxPlayers) {
          socket.emit('error', { message: 'Room is full' })
          return
        }

        // Spawn at center
        const spawnPos: Vector3 = {
          x: 0,
          y: 1.5,
          z: 0,
        }

        const player = addPlayer(room.state, socket.id, spawnPos)
        console.log(`[JOIN] ${socket.id} spawned as ${player.role}`)

        // Broadcast to all in room
        io.to(roomId).emit('player-joined', {
          playerId: socket.id,
          role: player.role,
          players: Array.from(room.state.players.values()),
        })

        // Auto-start game after timeout if min players reached
        if (room.state.status === 'lobby' && room.state.players.size >= MIN_PLAYERS_TO_START) {
          setTimeout(() => {
            const r = getRoom(roomId)
            if (r && r.state.status === 'lobby') {
              console.log(`[AUTO-START] Room ${roomId} auto-starting with ${r.state.players.size} players`)
              transitionToActive(io, r)
            }
          }, LOBBY_TIMEOUT)
        }
      } catch (err) {
        console.error(`[ERROR] join-room: ${err}`)
        socket.emit('error', { message: String(err) })
      }
    })

    // ========== player:move: Client sends movement input ==========
    socket.on('player:move', (payload: PlayerMovePayload) => {
      if (!currentRoomId) return

      try {
        const room = getRoom(currentRoomId)
        if (!room || room.state.status !== 'active') return

        handlePlayerMove({
          io,
          roomId: currentRoomId,
          room,
          socketId: socket.id,
          payload,
          timestamp: Date.now(),
        })
      } catch (err) {
        console.error(`[ERROR] player:move: ${err}`)
      }
    })

    // ========== guard:mark: Guard marks a prisoner (initiates chase) ==========
    socket.on('guard:mark', ({ targetId }: { targetId: string }) => {
      if (!currentRoomId) return

      try {
        const room = getRoom(currentRoomId)
        if (!room || room.state.status !== 'active') return

        handleGuardMark({
          io,
          roomId: currentRoomId,
          room,
          socketId: socket.id,
          guardId: socket.id,
          targetId,
          timestamp: Date.now(),
        })
      } catch (err) {
        console.error(`[ERROR] guard:mark: ${err}`)
      }
    })

    // ========== player:interact: Player interacts with items ==========
    socket.on('player:interact', ({ objectId, action }: { objectId: string; action: 'pickup' | 'use' | 'drop' }) => {
      if (!currentRoomId) return

      try {
        const room = getRoom(currentRoomId)
        if (!room || room.state.status !== 'active') return

        handlePlayerInteract({
          io,
          roomId: currentRoomId,
          room,
          socketId: socket.id,
          playerId: socket.id,
          objectId,
          action,
          timestamp: Date.now(),
        })
      } catch (err) {
        console.error(`[ERROR] player:interact: ${err}`)
      }
    })

    // ========== guard:catch: Guard attempts to catch a prisoner ==========
    socket.on('guard:catch', ({ targetId }: { targetId: string }) => {
      if (!currentRoomId) return

      try {
        const room = getRoom(currentRoomId)
        if (!room || room.state.status !== 'active') return

        handleGuardCatch({
          io,
          roomId: currentRoomId,
          room,
          socketId: socket.id,
          guardId: socket.id,
          targetId,
          timestamp: Date.now(),
        })

        // Check if game has ended
        const endCondition = checkGameEndCondition(room)
        if (endCondition.winner) {
          io.to(currentRoomId).emit('game:end', {
            winner: endCondition.winner,
            reason: endCondition.reason,
          })
          console.log(`[GAME-END] Winner: ${endCondition.winner}, reason: ${endCondition.reason}`)
        }
      } catch (err) {
        console.error(`[ERROR] guard:catch: ${err}`)
      }
    })

    // ========== riot:activate: Prisoner activates riot ==========
    socket.on('riot:activate', () => {
      if (!currentRoomId) return

      try {
        const room = getRoom(currentRoomId)
        if (!room || room.state.status !== 'active') return

        handleRiotActivate({
          io,
          roomId: currentRoomId,
          room,
          socketId: socket.id,
          prisonerId: socket.id,
          timestamp: Date.now(),
        })
      } catch (err) {
        console.error(`[ERROR] riot:activate: ${err}`)
      }
    })

    // ========== disconnect: Player leaves (or temporarily loses connection) ==========
    socket.on('disconnect', () => {
      console.log(`[DISC] Player disconnected: ${socket.id}`)

      if (!currentRoomId) return

      try {
        const room = getRoom(currentRoomId)
        if (!room) return

        const player = room.state.players.get(socket.id)
        if (!player) return

        // If game is active, save player slot for reconnection
        if (room.state.status === 'active') {
          markPlayerDisconnected(currentRoomId, player, room.config.reconnectTimeout)
          // Don't remove from state yet — they have reconnectTimeout seconds to rejoin
        } else {
          // Lobby or game finished — remove immediately
          removePlayer(room.state, socket.id)
        }

        // Broadcast to remaining players
        io.to(currentRoomId).emit('player-left', {
          playerId: socket.id,
          players: Array.from(room.state.players.values()),
        })

        // Clean up room if empty
        if (room.state.players.size === 0) {
          console.log(`[CLEANUP] Room ${currentRoomId} is empty, destroying`)
          stopGameLoop(room)
          clearRoomSlots(currentRoomId)
          destroyRoom(currentRoomId)
        }
      } catch (err) {
        console.error(`[ERROR] disconnect cleanup: ${err}`)
      }
    })
  })

  console.log('[INIT] Game sockets initialized')
}