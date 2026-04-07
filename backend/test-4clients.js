#!/usr/bin/env node

/**
 * Manual 4-Client Test for Jailbreak Game Network
 *
 * Simulates:
 * - 1 Guard + 3 Prisoners
 * - Auto-join same room
 * - Auto-start after 2+ players
 * - Player movement every 100ms
 * - Phase transitions
 * - NPC position updates
 * - Game ending
 *
 * Run with: node test-4clients.js
 * Requires: npm run dev (backend running on localhost:3001)
 */

import { io } from 'socket.io-client'

const ROOM_ID = 'test-room-4p'
const BASE_URL = 'http://localhost:3001'
const TEST_DURATION = 70000 // 70 seconds (lobby 30s + game 40s)

const clients = []
const stats = {
  playerJoined: 0,
  playerStates: 0,
  npcPositions: 0,
  chaseStarted: 0,
  gameStarted: false,
  gameEnded: false,
}

function log(playerId, message) {
  const timestamp = new Date().toLocaleTimeString()
  console.log(`[${timestamp}] [${playerId}] ${message}`)
}

function createClient(index) {
  const socket = io(BASE_URL, {
    transports: ['websocket'],
    reconnection: true,
  })

  const playerId = `player_${index}`
  const isGuard = index === 0
  const role = isGuard ? 'GUARD' : `PRISONER_${index}`

  socket.on('connect', () => {
    log(role, `✓ Connected to server (${socket.id})`)
    log(role, `→ Joining room "${ROOM_ID}"...`)
    socket.emit('join-room', ROOM_ID)
  })

  socket.on('player-joined', (data) => {
    stats.playerJoined++
    log(role, `✓ Player joined. Total: ${data.players.length}`)
    if (data.players.length === 1) {
      log(role, `  Role assigned: ${data.role}`)
    }
  })

  socket.on('game:start', (data) => {
    stats.gameStarted = true
    log(role, `🎮 GAME STARTED! Phase: ${data.phase.phaseName}`)
    log(role, `   Players: ${data.players.length}, NPCs: ${data.npcs.length}`)

    // Start moving
    const moveInterval = setInterval(() => {
      if (socket.connected) {
        const time = Date.now() / 1000
        const angle = time + index * (Math.PI * 2 / 4) // Spread players in circle

        socket.emit('player:move', {
          playerId: socket.id,
          position: {
            x: Math.cos(angle) * 15,
            y: 1.5,
            z: Math.sin(angle) * 15,
          },
          rotation: { x: 0, y: angle, z: 0, w: 1 },
          velocity: {
            x: -Math.sin(angle) * 5,
            y: 0,
            z: Math.cos(angle) * 5,
          },
          movementState: index === 0 ? 'walking' : 'walking',
        })
      }
    }, 100)

    socket.on('disconnect', () => clearInterval(moveInterval))
  })

  socket.on('player:state', (data) => {
    stats.playerStates++
    if (stats.playerStates % 20 === 0) {
      // Log every 20 ticks to avoid spam
      const alive = data.players.filter((p) => p.isAlive).length
      log(role, `[TICK] ${alive}/${data.players.length} alive`)
    }
  })

  socket.on('npc:positions', (data) => {
    stats.npcPositions++
    if (stats.npcPositions % 5 === 0) {
      log(role, `[NPC] Updated ${data.npcs.length} NPCs (delta compression)`)
    }
  })

  socket.on('chase:start', (data) => {
    stats.chaseStarted++
    log(role, `🔴 CHASE STARTED: Guard chasing prisoner!`)
  })

  socket.on('guard:catch', (data) => {
    log(role, `💀 PRISONER CAUGHT: ${data.targetId.substring(0, 8)}...`)
  })

  socket.on('phase:change', (data) => {
    log(role, `📍 PHASE CHANGED: → ${data.phaseName}`)
  })

  socket.on('game:end', (data) => {
    stats.gameEnded = true
    log(role, `🏁 GAME OVER! Winner: ${data.winner.toUpperCase()}`)
    log(role, `   Reason: ${data.reason}`)
  })

  socket.on('game:error', (err) => {
    log(role, `❌ Error: ${err.message}`)
  })

  socket.on('disconnect', () => {
    log(role, `⚠️  Disconnected from server`)
  })

  return socket
}

async function main() {
  console.log(`
╔═══════════════════════════════════════════════════════════════╗
║        Jailbreak Game — 4-Client Network Test                ║
║                                                               ║
║  Simulating: 1 Guard + 3 Prisoners                           ║
║  Duration: ${TEST_DURATION / 1000}s (30s lobby + 40s game)                    ║
║  Backend: ${BASE_URL}                             ║
║                                                               ║
║  Timeline:                                                    ║
║    0-30s:  Lobby (auto-start when 2+ players)               ║
║   30-70s:  Active game (movement, NPCs, phases)             ║
║                                                               ║
║  Expected: Game starts ~30s in, runs until test end         ║
╚═══════════════════════════════════════════════════════════════╝
  `)

  // Create 4 clients
  console.log('📱 Spawning 4 clients...\n')
  for (let i = 0; i < 4; i++) {
    const socket = createClient(i)
    clients.push(socket)
    await new Promise((resolve) => setTimeout(resolve, 100)) // Stagger connections
  }

  // Wait for test duration
  await new Promise((resolve) => setTimeout(resolve, TEST_DURATION))

  // Disconnect all and print stats
  console.log(`\n\n╔═══════════════════════════════════════════════════════════════╗`)
  console.log(`║                        TEST SUMMARY                            ║`)
  console.log(`╠═══════════════════════════════════════════════════════════════╣`)
  console.log(`║ Game Started:          ${stats.gameStarted ? '✓' : '✗'}                                  ║`)
  console.log(`║ Game Ended:            ${stats.gameEnded ? '✓' : '✗'}                                  ║`)
  console.log(`║ Players Joined:        ${String(stats.playerJoined).padStart(5)}                               ║`)
  console.log(`║ Player State Updates:  ${String(stats.playerStates).padStart(5)}                               ║`)
  console.log(`║ NPC Position Updates:  ${String(stats.npcPositions).padStart(5)}                               ║`)
  console.log(`║ Chases Initiated:      ${String(stats.chaseStarted).padStart(5)}                               ║`)
  console.log(`╠═══════════════════════════════════════════════════════════════╣`)

  // Success criteria: game started, received many tick updates (20 ticks/sec), and NPC updates
  const minTickUpdates = 100 // At least 100 player state updates across all clients
  const minNpcUpdates = 10 // At least 10 NPC position updates

  if (stats.gameStarted && stats.playerStates > minTickUpdates && stats.npcPositions > minNpcUpdates && stats.gameEnded) {
    console.log(`║ ✓ NETWORK SYNCHRONIZATION WORKING!                          ║`)
  } else {
    console.log(`║ ⚠️  Test incomplete (game may have just started)              ║`)
    if (!stats.gameStarted) console.log(`║    - Game did not start (lobby timeout 30s)                ║`)
    if (stats.playerStates <= minTickUpdates) console.log(`║    - Low tick updates: ${String(stats.playerStates).padStart(3)} < ${minTickUpdates}                    ║`)
    if (stats.npcPositions <= minNpcUpdates) console.log(`║    - Low NPC updates: ${String(stats.npcPositions).padStart(2)} < ${minNpcUpdates}                      ║`)
    if (!stats.gameEnded) console.log(`║    - Game did not end (need longer test)                 ║`)
  }

  console.log(`╚═══════════════════════════════════════════════════════════════╝\n`)

  // Disconnect all clients
  console.log('🔌 Disconnecting all clients...')
  clients.forEach((socket) => socket.disconnect())

  process.exit(0)
}

// Run test
main().catch((err) => {
  console.error('❌ Test failed:', err)
  process.exit(1)
})

// Graceful shutdown on SIGINT
process.on('SIGINT', () => {
  console.log('\n\n⚠️  Shutting down...')
  clients.forEach((socket) => socket.disconnect())
  process.exit(0)
})
