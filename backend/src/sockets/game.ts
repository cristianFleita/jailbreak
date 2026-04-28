import { Server, Socket } from 'socket.io'
import {
  NPCSyncStatePayload, PlayerInteractAction, PlayerMovePayload, Vector3,
  AuthRegisterPayload, RoomCreatePayload, RoomJoinPayload, RoomKickPayload,
  RegisterSpawnAreasPayload, TutorialMissionCompletePayload,
} from '../game/types.js'
import {
  createRoom,
  getRoom,
  destroyRoom,
  roomExists,
  transitionToTutorial,
  stopGameLoop,
  buildRoomPlayersPayload,
  buildRoomStatePayload,
  buildRoomListPayload,
  buildMatchStatus,
  findSocketByUserId,
  listRooms,
  getTutorialManager,
} from '../game/room-manager.js'
import { addPlayer, removePlayer, assignRandomRoles } from '../game/state.js'
import {
  handlePlayerMove,
  clearPlayerMoveTracking,
  handlePlayerInteract,
  handlePlayerAction,
  handleGuardCatch,
  handleGuardMark,
  handleRiotActivate,
  checkGameEndCondition,
  handleNPCSyncState,
  handleRegisterSpawnAreas,
} from '../game/event-handlers.js'
import {
  markPlayerDisconnected,
  restorePlayerConnection,
  consumeExpiredDisconnectedSlot,
  clearRoomSlots,
} from '../game/reconnection.js'
import {
  dropCriticalRouteItemsForPlayer,
  emitAllRouteItemStatesToSocket,
} from '../game/systems/route-inventory.js'
import {
  clearSpawnAreaRegistration,
  scheduleCriticalItemRespawn,
} from '../game/systems/spawn-areas.js'
import type { RouteItemId } from '../game/types.js'
import {
  registerUser,
  getUserBySocket,
  handleUserDisconnect,
  isAuthenticated,
  setUserStatus,
  cleanupStaleUsers,
} from '../game/user-identity.js'

const MIN_PLAYERS_TO_START = 2
const STALE_USER_CLEANUP_INTERVAL = 600000 // 10 minutes

/**
 * Requires the socket to be authenticated before running a handler.
 */
function requireAuth(socket: Socket, cb: () => void): void {
  if (!isAuthenticated(socket.id)) {
    socket.emit('game:error', { code: 'NOT_AUTHENTICATED', message: 'Register first with auth:register' })
    return
  }
  cb()
}

/**
 * Broadcasts the current public room browser state to all connected clients.
 */
function emitRoomListUpdate(io: Server): void {
  io.emit('room:list:update', buildRoomListPayload())
}

/**
 * Sets up all socket event handlers.
 *
 * Flow:
 * 1. Client connects → emits `auth:register` with userId + displayName
 * 2. Server responds with `auth:registered` (userId confirmed or generated)
 * 3. Client can then: `room:create`, `room:join`, `room:list`
 * 4. Host can: `room:kick`, `room:start`
 * 5. All players can: `room:leave`
 * 6. During game: `player:move`, `player:interact`, `guard:mark`, `guard:catch`, `riot:activate`
 */
export function setupGameSockets(io: Server) {
  // Periodic cleanup of stale users (no socket, inactive >1h)
  const cleanupInterval = setInterval(() => {
    const cleaned = cleanupStaleUsers()
    if (cleaned > 0) console.log(`[CLEANUP] Removed ${cleaned} stale user profiles`)
  }, STALE_USER_CLEANUP_INTERVAL)

  ;(io as any).__cleanupInterval = cleanupInterval

  io.on('connection', (socket: Socket) => {
    console.log(`[CONN] Socket connected: ${socket.id}`)

    // Track which room this socket is in
    let currentRoomId: string | null = null

    // ==================================================================
    // AUTH: Register / identify user
    // ==================================================================
    socket.on('auth:register', (payload: AuthRegisterPayload) => {
      try {
        const profile = registerUser(socket.id, payload.userId)

        // If user was in a room, restore context
        if (profile.currentRoomId) {
          const room = getRoom(profile.currentRoomId)
          if (room) {
            currentRoomId = profile.currentRoomId
          } else {
            profile.currentRoomId = undefined
          }
        }

        socket.emit('auth:registered', {
          userId: profile.userId,
          displayName: profile.displayName,
          socketId: socket.id, // client needs this to identify itself in player:state broadcasts
        })

        // If user was mid-game in an active room, emit game:reconnect immediately
        // so the client redirects to GameScene instead of staying on Start/Lobby
        if (profile.currentRoomId) {
          const roomForReconnect = getRoom(profile.currentRoomId)
          if (roomForReconnect && roomForReconnect.state.status === 'active') {
            const reconnectResult = restorePlayerConnection(profile.currentRoomId, profile.userId)
            if (reconnectResult.success && reconnectResult.playerState) {
              roomForReconnect.state.players.set(socket.id, reconnectResult.playerState)
              roomForReconnect.state.playersByUserId.set(profile.userId, reconnectResult.playerState)
            }

            socket.join(profile.currentRoomId)
            currentRoomId = profile.currentRoomId
            setUserStatus(profile.userId, 'in-game', profile.currentRoomId)

            {
              // Emit the active route BEFORE game:reconnect so Unity can cache
              // activeRouteId before any route UI hydrates from the snapshot.
              socket.emit('escape:route:selected', {
                activeRouteId: roomForReconnect.state.activeRouteId,
              })
              emitAllRouteItemStatesToSocket(io, socket.id, roomForReconnect.state)

              const gm = (roomForReconnect as any).gameManager
              // Re-emit Route 1 snapshot so the reconnected client rebuilds
              // its checklist + world props without waiting for the next tick.
              if (gm?.route1) {
                if (reconnectResult.playerState?.role === 'prisoner') {
                  socket.emit('escape:route1:state', gm.route1.buildPrisonerStatePayload())
                }
                socket.emit('world:state', gm.route1.buildWorldStatePayload())
              }
              socket.emit('game:reconnect', {
                players: Array.from(roomForReconnect.state.players.values()),
                npcs: Array.from(roomForReconnect.state.npcs.values()),
                items: Array.from(roomForReconnect.state.items.values()),
                phase: roomForReconnect.state.phase,
                tick: roomForReconnect.state.tick,
                activeRouteId: roomForReconnect.state.activeRouteId,
                jailPhase: gm?.jailRoutine ? {
                  phase:          gm.jailRoutine.getCurrentJailPhase(),
                  phaseName:      gm.jailRoutine.getCurrentPhaseName(),
                  zone:           gm.jailRoutine.getCurrentZone(),
                  duration:       gm.jailRoutine.getCurrentPhaseDuration(),
                  elapsed:        gm.jailRoutine.getCurrentPhaseElapsed(),
                  npcAssignments: gm.jailRoutine.buildReconnectAssignments(),
                } : undefined,
              })
              socket.emit('match:status', buildMatchStatus(roomForReconnect))
            }

            io.to(profile.currentRoomId).emit('player-reconnected', {
              userId: profile.userId,
              players: buildRoomPlayersPayload(roomForReconnect),
            })

            console.log(`[AUTH] ${profile.displayName} auto-reconnected to active game "${profile.currentRoomId}"`)
          }
        }
      } catch (err) {
        console.error(`[ERROR] auth:register: ${err}`)
        socket.emit('game:error', { code: 'AUTH_FAILED', message: String(err) })
      }
    })

    // ==================================================================
    // ROOM: List available rooms
    // ==================================================================
    socket.on('room:list', () => {
      requireAuth(socket, () => {
        // Keep the legacy array event for the React wrapper, and send a
        // JsonUtility-friendly wrapper event for Unity/WebGL.
        socket.emit('room:list', listRooms())
        socket.emit('room:list:response', buildRoomListPayload())
      })
    })

    // ==================================================================
    // ROOM: Resend current room state to the requesting socket only
    // Used by RoomScreenController after scene transition to repopulate the UI
    // ==================================================================
    socket.on('room:get-state', () => {
      requireAuth(socket, () => {
        if (!currentRoomId) {
          socket.emit('game:error', { code: 'NOT_IN_ROOM', message: 'You are not in a room' })
          return
        }
        const room = getRoom(currentRoomId)
        if (!room) {
          socket.emit('game:error', { code: 'ROOM_NOT_FOUND', message: 'Room no longer exists' })
          return
        }
        socket.emit('room:state', buildRoomStatePayload(room))
        console.log(`[ROOM] Re-sent room:state to ${socket.id} for "${currentRoomId}"`)
      })
    })

    // ==================================================================
    // ROOM: Create a new room (caller becomes host)
    // ==================================================================
    socket.on('room:create', (payload: RoomCreatePayload) => {
      requireAuth(socket, () => {
        try {
          const user = getUserBySocket(socket.id)!
          const roomName = payload.roomName.trim()

          if (!roomName || roomName.length > 32) {
            socket.emit('game:error', { code: 'INVALID_ROOM_NAME', message: 'Room name must be 1-32 characters' })
            return
          }

          if (roomExists(roomName)) {
            socket.emit('game:error', { code: 'ROOM_EXISTS', message: `Room "${roomName}" already exists` })
            return
          }

          // Sync closure state if it was cleared externally by a host leaving/kicking
          if (socket.data.currentRoomId === null) currentRoomId = null

          if (currentRoomId) {
            socket.emit('game:error', { code: 'ALREADY_IN_ROOM', message: 'Leave current room first' })
            return
          }

          const room = createRoom(roomName, user.userId)
          if (!room) {
            socket.emit('game:error', { code: 'ROOM_CREATE_FAILED', message: 'Failed to create room' })
            return
          }

          // Add host as first player (guard)
          const spawnPos: Vector3 = { x: 0, y: 1.5, z: 0 }
          addPlayer(room.state, socket.id, user.userId, spawnPos)

          socket.join(roomName)
          currentRoomId = roomName
          setUserStatus(user.userId, 'in-lobby', roomName)

          socket.emit('room:created', {
            roomId: roomName,
            hostUserId: user.userId,
          })

          // Send full room state
          socket.emit('room:state', buildRoomStatePayload(room))
          emitRoomListUpdate(io)

          console.log(`[ROOM] "${roomName}" created by ${user.displayName}`)
        } catch (err) {
          console.error(`[ERROR] room:create: ${err}`)
          socket.emit('game:error', { code: 'ROOM_CREATE_FAILED', message: String(err) })
        }
      })
    })

    // ==================================================================
    // ROOM: Join an existing room
    // ==================================================================
    socket.on('room:join', (payload: RoomJoinPayload) => {
      requireAuth(socket, () => {
        try {
          const user = getUserBySocket(socket.id)!

          // If this socket was kicked, or the room was destroyed by the host,
          // the host's handler cleared socket.data.currentRoomId but couldn't touch 
          // our closure — sync it here before joining.
          if (socket.data.currentRoomId === null) currentRoomId = null

          if (currentRoomId) {
            socket.emit('game:error', { code: 'ALREADY_IN_ROOM', message: 'Leave current room first' })
            return
          }

          const room = getRoom(payload.roomId)
          if (!room) {
            socket.emit('game:error', { code: 'ROOM_NOT_FOUND', message: `Room "${payload.roomId}" not found` })
            return
          }

          if (room.state.players.size >= room.config.maxPlayers) {
            socket.emit('game:error', { code: 'ROOM_FULL', message: 'Room is full' })
            return
          }

          // Check if this user is reconnecting to this room
          const reconnectResult = restorePlayerConnection(payload.roomId, user.userId)
          if (reconnectResult.success && reconnectResult.playerState) {
            room.state.players.set(socket.id, reconnectResult.playerState)
            room.state.playersByUserId.set(user.userId, reconnectResult.playerState)

            socket.join(payload.roomId)
            currentRoomId = payload.roomId
            setUserStatus(user.userId, 'in-game', payload.roomId)

            {
              socket.emit('escape:route:selected', {
                activeRouteId: room.state.activeRouteId,
              })
              emitAllRouteItemStatesToSocket(io, socket.id, room.state)

              const gm2 = (room as any).gameManager
              if (gm2?.route1) {
                if (reconnectResult.playerState?.role === 'prisoner') {
                  socket.emit('escape:route1:state', gm2.route1.buildPrisonerStatePayload())
                }
                socket.emit('world:state', gm2.route1.buildWorldStatePayload())
              }
              socket.emit('game:reconnect', {
                players: Array.from(room.state.players.values()),
                npcs: Array.from(room.state.npcs.values()),
                items: Array.from(room.state.items.values()),
                phase: room.state.phase,
                tick: room.state.tick,
                activeRouteId: room.state.activeRouteId,
                jailPhase: gm2?.jailRoutine ? {
                  phase:          gm2.jailRoutine.getCurrentJailPhase(),
                  phaseName:      gm2.jailRoutine.getCurrentPhaseName(),
                  zone:           gm2.jailRoutine.getCurrentZone(),
                  duration:       gm2.jailRoutine.getCurrentPhaseDuration(),
                  elapsed:        gm2.jailRoutine.getCurrentPhaseElapsed(),
                  npcAssignments: gm2.jailRoutine.buildReconnectAssignments(),
                } : undefined,
              })
              socket.emit('match:status', buildMatchStatus(room))
            }

            io.to(payload.roomId).emit('player-reconnected', {
              userId: user.userId,
              players: buildRoomPlayersPayload(room),
            })
            return
          }

          if (room.state.status !== 'lobby') {
            socket.emit('game:error', { code: 'GAME_IN_PROGRESS', message: 'Game already in progress' })
            return
          }

          // New player joining lobby
          const spawnPos: Vector3 = { x: 0, y: 1.5, z: 0 }
          const player = addPlayer(room.state, socket.id, user.userId, spawnPos)

          socket.join(payload.roomId)
          currentRoomId = payload.roomId
          setUserStatus(user.userId, 'in-lobby', payload.roomId)

          // Broadcast to all in room
          io.to(payload.roomId).emit('room:player-joined', {
            userId: user.userId,
            displayName: user.displayName,
            role: player.role,
            players: buildRoomPlayersPayload(room),
          })

          // Send full room state to the joining player
          socket.emit('room:state', buildRoomStatePayload(room))
          emitRoomListUpdate(io)

          console.log(`[ROOM] ${user.displayName} joined "${payload.roomId}" as ${player.role}`)
        } catch (err) {
          console.error(`[ERROR] room:join: ${err}`)
          socket.emit('game:error', { code: 'JOIN_FAILED', message: String(err) })
        }
      })
    })

    // ==================================================================
    // ROOM: Host kicks a player
    // ==================================================================
    socket.on('room:kick', (payload: RoomKickPayload) => {
      requireAuth(socket, () => {
        try {
          const user = getUserBySocket(socket.id)!

          if (!currentRoomId) {
            socket.emit('game:error', { code: 'NOT_IN_ROOM', message: 'You are not in a room' })
            return
          }

          const room = getRoom(currentRoomId)
          if (!room) return

          // Only host can kick
          if (room.state.hostUserId !== user.userId) {
            socket.emit('game:error', { code: 'NOT_HOST', message: 'Only the host can kick players' })
            return
          }

          if (room.state.status !== 'lobby') {
            socket.emit('game:error', { code: 'GAME_IN_PROGRESS', message: 'Cannot kick during game' })
            return
          }

          // Can't kick yourself
          if (payload.targetUserId === user.userId) {
            socket.emit('game:error', { code: 'CANNOT_KICK_SELF', message: 'Cannot kick yourself' })
            return
          }

          // Find the target's socketId
          const targetSocketId = findSocketByUserId(room, payload.targetUserId)
          if (!targetSocketId) {
            socket.emit('game:error', { code: 'PLAYER_NOT_FOUND', message: 'Player not in room' })
            return
          }

          // Remove from room state
          removePlayer(room.state, targetSocketId)

          // Remove from socket.io room and notify the kicked player
          const targetSocket = io.sockets.sockets.get(targetSocketId)
          if (targetSocket) {
            targetSocket.leave(currentRoomId)
            // Null out the room on the target socket's data bag so its own
            // closure can sync from it on the next room:join attempt.
            targetSocket.data.currentRoomId = null
            targetSocket.emit('room:kicked', { roomId: currentRoomId, reason: 'Kicked by host' })
          }

          setUserStatus(payload.targetUserId, 'idle')

          // Broadcast updated player list
          io.to(currentRoomId).emit('room:player-left', {
            userId: payload.targetUserId,
            reason: 'kicked',
            players: buildRoomPlayersPayload(room),
          })
          emitRoomListUpdate(io)

          console.log(`[ROOM] ${user.displayName} kicked ${payload.targetUserId} from "${currentRoomId}"`)
        } catch (err) {
          console.error(`[ERROR] room:kick: ${err}`)
          socket.emit('game:error', { code: 'KICK_FAILED', message: String(err) })
        }
      })
    })

    // ==================================================================
    // ROOM: Host starts the game
    // ==================================================================
    socket.on('room:start', () => {
      requireAuth(socket, () => {
        try {
          const user = getUserBySocket(socket.id)!

          if (!currentRoomId) {
            socket.emit('game:error', { code: 'NOT_IN_ROOM', message: 'You are not in a room' })
            return
          }

          const room = getRoom(currentRoomId)
          if (!room) return

          if (room.state.hostUserId !== user.userId) {
            socket.emit('game:error', { code: 'NOT_HOST', message: 'Only the host can start the game' })
            return
          }

          if (room.state.status !== 'lobby') {
            socket.emit('game:error', { code: 'GAME_ALREADY_STARTED', message: 'Game already started' })
            return
          }

          if (room.state.players.size < MIN_PLAYERS_TO_START) {
            socket.emit('game:error', {
              code: 'NOT_ENOUGH_PLAYERS',
              message: `Need at least ${MIN_PLAYERS_TO_START} players to start`,
            })
            return
          }

          // Update all players' status
          for (const [_sid, player] of room.state.players) {
            setUserStatus(player.userId, 'in-game', currentRoomId)
          }

          // Randomly assign 1 guard + rest prisoners. Roles must be set before
          // the tutorial fan-out — each player's tutorial:start carries the
          // role-specific mission list.
          assignRandomRoles(room.state)

          // Fase A: Start Game enters the 60s tutorial round, NOT the live
          // match. The tutorial manager auto-transitions to active when it
          // ends. Status goes lobby → tutorial → active.
          transitionToTutorial(io, room)
          emitRoomListUpdate(io)

          console.log(`[ROOM] "${currentRoomId}" started by ${user.displayName} → TUTORIAL`)
        } catch (err) {
          console.error(`[ERROR] room:start: ${err}`)
          socket.emit('game:error', { code: 'START_FAILED', message: String(err) })
        }
      })
    })

    // ==================================================================
    // ROOM: Player leaves room voluntarily
    // ==================================================================
    socket.on('room:leave', () => {
      requireAuth(socket, () => {
        handleLeaveRoom(socket, io, currentRoomId, 'left')
        currentRoomId = null
      })
    })

    // ==================================================================
    // GAMEPLAY: player:move
    // ==================================================================
    socket.on('player:move', (payload: PlayerMovePayload) => {
      if (!currentRoomId) return

      try {
        const room = getRoom(currentRoomId)
        if (!room || (room.state.status !== 'active' && room.state.status !== 'tutorial')) return

        handlePlayerMove({
          io,
          roomId: currentRoomId,
          room,
          socketId: socket.id,
          payload,
          timestamp: Date.now(),
        })

        // The normal game loop is not running while status === 'tutorial',
        // so relay player positions immediately for remote tutorial avatars.
        if (room.state.status === 'tutorial') {
          io.to(currentRoomId).emit('player:state', {
            players: Array.from(room.state.players.values()),
          })
        }
      } catch (err) {
        console.error(`[ERROR] player:move: ${err}`)
      }
    })

    // ==================================================================
    // GAMEPLAY: guard:mark
    // ==================================================================
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

    // ==================================================================
    // GAMEPLAY: player:interact
    // ==================================================================
    socket.on('player:interact', ({
      objectId,
      action,
      slotIndex,
    }: {
      objectId: string
      action: PlayerInteractAction
      slotIndex?: number
    }) => {
      if (!currentRoomId) return

      try {
        const room = getRoom(currentRoomId)
        if (!room || room.state.status !== 'active') return

        const interactUser = getUserBySocket(socket.id)
        handlePlayerInteract({
          io,
          roomId: currentRoomId,
          room,
          socketId: socket.id,
          playerId: interactUser?.userId ?? socket.id,
          objectId,
          action,
          slotIndex,
          timestamp: Date.now(),
        })
      } catch (err) {
        console.error(`[ERROR] player:interact: ${err}`)
      }
    })

    // ==================================================================
    // GAMEPLAY: player:action (sit, stand, other pure animation broadcasts)
    // ==================================================================
    socket.on('player:action', ({ objectId, action }: { objectId: string; action: string }) => {
      if (!currentRoomId) return

      try {
        const room = getRoom(currentRoomId)
        if (!room || room.state.status !== 'active') return

        handlePlayerAction({
          io,
          roomId: currentRoomId,
          room,
          socketId: socket.id,
          objectId,
          action,
          timestamp: Date.now(),
        })
      } catch (err) {
        console.error(`[ERROR] player:action: ${err}`)
      }
    })

    // ==================================================================
    // GAMEPLAY: guard:catch
    // ==================================================================
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

      } catch (err) {
        console.error(`[ERROR] guard:catch: ${err}`)
      }
    })

    // ==================================================================
    // GAMEPLAY: riot:activate
    // ==================================================================
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

    // ==================================================================
    // TUTORIAL: client claims a mission completed (Fase A)
    // ==================================================================
    socket.on('tutorial:mission:complete', (payloadRaw: string | TutorialMissionCompletePayload) => {
      requireAuth(socket, () => {
        if (!currentRoomId) return
        try {
          const room = getRoom(currentRoomId)
          if (!room || room.state.status !== 'tutorial') return

          const payload = typeof payloadRaw === 'string'
            ? JSON.parse(payloadRaw) as TutorialMissionCompletePayload
            : payloadRaw

          const manager = getTutorialManager(room)
          if (!manager) return

          const user = getUserBySocket(socket.id)
          if (!user) return

          manager.markMissionComplete(user.userId, payload.missionId)
        } catch (err) {
          console.error(`[ERROR] tutorial:mission:complete: ${err}`)
        }
      })
    })

    // ==================================================================
    // GAMEPLAY: route:register_spawn_areas (host-only; Phase C-02)
    // ==================================================================
    socket.on('route:register_spawn_areas', (payloadRaw: string | RegisterSpawnAreasPayload) => {
      if (!currentRoomId) return

      try {
        const room = getRoom(currentRoomId)
        if (!room || room.state.status !== 'active') return

        const payload = typeof payloadRaw === 'string'
          ? JSON.parse(payloadRaw) as RegisterSpawnAreasPayload
          : payloadRaw

        handleRegisterSpawnAreas({
          io,
          roomId: currentRoomId,
          room,
          socketId: socket.id,
          payload,
        })
      } catch (err) {
        console.error(`[ERROR] route:register_spawn_areas: ${err}`)
      }
    })

    // ==================================================================
    // GAMEPLAY: npc:sync_state (from Host to sync NPC positions)
    // ==================================================================
    socket.on('npc:sync_state', (payloadRaw: string | NPCSyncStatePayload) => {
      if (!currentRoomId) return

      try {
        const room = getRoom(currentRoomId)
        if (!room || room.state.status !== 'active') return

        const payload = typeof payloadRaw === 'string'
          ? JSON.parse(payloadRaw) as NPCSyncStatePayload
          : payloadRaw

        handleNPCSyncState({
          io,
          roomId: currentRoomId,
          room,
          socketId: socket.id,
          payload,
        })
      } catch (err) {
        console.error(`[ERROR] npc:sync_state: ${err}`)
      }
    })

    // ==================================================================
    // DISCONNECT
    // ==================================================================
    socket.on('disconnect', () => {
      console.log(`[DISC] Socket disconnected: ${socket.id}`)

      const user = getUserBySocket(socket.id)
      handleLeaveRoom(socket, io, currentRoomId, 'disconnected')
      handleUserDisconnect(socket.id)
    })
  })

  console.log('[INIT] Game sockets initialized (auth + room lobby)')
}

// ============================================================================
// Helper: handle player leaving a room (used by room:leave and disconnect)
// ============================================================================

function handleLeaveRoom(
  socket: Socket,
  io: Server,
  roomId: string | null,
  reason: 'left' | 'disconnected'
): void {
  if (!roomId) return

  try {
    const room = getRoom(roomId)
    if (!room) return

    const player = room.state.players.get(socket.id)
    if (!player) return

    const userId = player.userId
    const isHost = userId === room.state.hostUserId
    const gameIsActive = room.state.status === 'active'

    // During an active game: save reconnection slot then remove the stale socket entry
    if (gameIsActive) {
      // Whatever route1 action this player was doing must end immediately —
      // their socket is gone, the world cannot rely on a `*.stop` that will
      // never arrive (see Phase D anti-griefing requirement).
      const gmLeave = (room as any).gameManager
      gmLeave?.route1?.cancelPlayerInteractions(userId)

      if (reason === 'left') {
        dropCriticalRouteItemsForPlayer(
          io,
          roomId,
          room.state,
          player,
          player.position,
          (itemId) => scheduleCriticalItemRespawn(io, roomId, room.state, itemId as RouteItemId)
        )
        gmLeave?.route1?.notifyInventoryChanged()
        setUserStatus(userId, 'idle')
      } else {
        markPlayerDisconnected(roomId, player, room.config.reconnectTimeout)
        scheduleDisconnectedCriticalItemRelease(io, roomId, userId, room.config.reconnectTimeout)
      }

      removePlayer(room.state, socket.id)
      clearPlayerMoveTracking(socket.id)
      socket.leave(roomId)

      // Notify remaining players that this player temporarily disconnected
      io.to(roomId).emit('room:player-left', {
        userId,
        reason,
        players: buildRoomPlayersPayload(room),
      })

      console.log(`[ROOM] ${isHost ? 'Host' : 'Player'} ${userId} ${reason} active game "${roomId}"${reason === 'disconnected' ? ' — slot reserved' : ''}`)
      return
    }

    // Lobby: remove the player from state immediately
    removePlayer(room.state, socket.id)
    clearPlayerMoveTracking(socket.id)
    socket.leave(roomId)
    setUserStatus(userId, 'idle')

    // HOST LEFT lobby → destroy the entire room
    if (isHost) {
      console.log(`[ROOM] Host left "${roomId}" — destroying room`)

      // Notify all remaining players
      io.to(roomId).emit('room:destroyed', { roomId, reason: 'host-left' })

      // Update status for all remaining players
      for (const [sid, p] of room.state.players) {
        setUserStatus(p.userId, 'idle')

        // FIX: Null out the room on the target socket's data bag so its own 
        // closure variable can sync properly on their next room:join/create attempt.
        const targetSocket = io.sockets.sockets.get(sid)
        if (targetSocket) {
          targetSocket.data.currentRoomId = null
        }
      }

      // Force all sockets out of the room
      io.in(roomId).socketsLeave(roomId)

      stopGameLoop(room)
      clearRoomSlots(roomId)
      clearSpawnAreaRegistration(roomId)
      destroyRoom(roomId)
      emitRoomListUpdate(io)
      return
    }

    // Non-host left: broadcast to remaining players
    io.to(roomId).emit('room:player-left', {
      userId,
      reason,
      players: buildRoomPlayersPayload(room),
    })

    // Destroy room if empty
    if (room.state.players.size === 0) {
      console.log(`[ROOM] "${roomId}" is empty, destroying`)
      stopGameLoop(room)
      clearRoomSlots(roomId)
      clearSpawnAreaRegistration(roomId)
      destroyRoom(roomId)
    }

    emitRoomListUpdate(io)
  } catch (err) {
    console.error(`[ERROR] handleLeaveRoom: ${err}`)
  }
}

function scheduleDisconnectedCriticalItemRelease(
  io: Server,
  roomId: string,
  userId: string,
  reconnectTimeoutSeconds: number
): void {
  setTimeout(() => {
    try {
      const expiredPlayer = consumeExpiredDisconnectedSlot(roomId, userId)
      if (!expiredPlayer) return

      const room = getRoom(roomId)
      if (!room || room.state.status !== 'active') return

      const dropped = dropCriticalRouteItemsForPlayer(
        io,
        roomId,
        room.state,
        expiredPlayer,
        expiredPlayer.position,
        (itemId) => scheduleCriticalItemRespawn(io, roomId, room.state, itemId as RouteItemId)
      )

      const gmExpire = (room as any).gameManager
      if (dropped.length > 0) gmExpire?.route1?.notifyInventoryChanged()

      setUserStatus(userId, 'idle')

      if (dropped.length > 0) {
        console.log(`[RECONNECT] Released expired critical route tools for ${userId}: ${dropped.join(', ')}`)
      }
    } catch (err) {
      console.error(`[ERROR] scheduleDisconnectedCriticalItemRelease: ${err}`)
    }
  }, reconnectTimeoutSeconds * 1000 + 100)
}
