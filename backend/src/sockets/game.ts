import { Server, Socket } from 'socket.io'

/**
 * Represents a game room.
 * @property id - The unique ID of the room.
 * @property players - An array of socket IDs currently in the room.
 * @property state - The mutable state data for the game in the room.
 */
interface Room {
  id: string
  players: string[]
  state: Record<string, unknown>
}

const rooms = new Map<string, Room>()

/**
 * Sets up the core socket event handlers for managing game rooms and player connections.
 * Listens for `connection`, `join-room`, `game-event`, and `disconnect` events
 * to maintain room state and broadcast game events to all players in a room.
 * @param io - The Socket.IO server instance used to listen for connections.
 */
export function setupGameSockets(io: Server) {
  io.on('connection', (socket: Socket) => {
    console.log(`Player connected: ${socket.id}`)

    socket.on('join-room', (roomId: string) => {
      socket.join(roomId)
      if (!rooms.has(roomId)) {
        rooms.set(roomId, { id: roomId, players: [], state: {} })
      }
      const room = rooms.get(roomId)!
      room.players.push(socket.id)
      io.to(roomId).emit('player-joined', { playerId: socket.id, players: room.players })
    })

    socket.on('game-event', ({ roomId, event }: { roomId: string; event: unknown }) => {
      socket.to(roomId).emit('game-event', event)
    })

    socket.on('disconnect', () => {
      rooms.forEach((room) => {
        room.players = room.players.filter(id => id !== socket.id)
      })
      console.log(`Player disconnected: ${socket.id}`)
    })
  })
}