#!/usr/bin/env node

/**
 * Single Client Test for Jailbreak Game Network
 *
 * Simple test to verify backend is running and socket connection works.
 *
 * Run with: node test-client.js
 * Requires: npm run dev (backend running on localhost:3001)
 */

import { io } from 'socket.io-client'

const BASE_URL = 'http://localhost:3001'
const ROOM_ID = 'test-room-single'

const socket = io(BASE_URL, {
  transports: ['websocket'],
})

let isConnected = false

console.log(`
╔═════════════════════════════════════════════════════════╗
║  Single Client Test — Jailbreak Game Network Layer     ║
║  Backend: ${BASE_URL.padEnd(40)}║
╚═════════════════════════════════════════════════════════╝
`)

socket.on('connect', () => {
  isConnected = true
  console.log(`✓ Connected to server`)
  console.log(`  Socket ID: ${socket.id}`)
  console.log(`\n→ Joining room "${ROOM_ID}"...\n`)

  socket.emit('join-room', ROOM_ID)
})

socket.on('player-joined', (data) => {
  console.log(`✓ Joined room successfully`)
  console.log(`  Role: ${data.role}`)
  console.log(`  Total players: ${data.players.length}\n`)

  // Send test movement
  console.log('→ Sending test movement...\n')
  socket.emit('player:move', {
    playerId: socket.id,
    position: { x: 5, y: 1.5, z: 0 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    velocity: { x: 1, y: 0, z: 0 },
    movementState: 'walking',
  })
})

socket.on('player:state', (data) => {
  console.log(`✓ Received player:state broadcast`)
  console.log(`  Players: ${data.players.length}`)
  console.log(`  Tick loop working: YES\n`)

  // Disconnect after first update
  setTimeout(() => {
    console.log('→ Disconnecting...\n')
    socket.disconnect()
  }, 500)
})

socket.on('game:start', (data) => {
  console.log(`✓ Game started!`)
  console.log(`  Phase: ${data.phase.phaseName}`)
  console.log(`  NPCs spawned: ${data.npcs.length}\n`)
})

socket.on('error', (err) => {
  console.error(`✗ Socket Error: ${err.message}`)
})

socket.on('disconnect', () => {
  console.log(`✓ Disconnected from server`)

  if (isConnected) {
    console.log(`
╔═════════════════════════════════════════════════════════╗
║  ✅ BACKEND IS WORKING                                 ║
║                                                         ║
║  Next: Run 4-client test:                              ║
║  $ node test-4clients.js                               ║
╚═════════════════════════════════════════════════════════╝
`)
  } else {
    console.log(`
❌ BACKEND CONNECTION FAILED

Make sure backend is running:
  $ npm run dev
`)
  }

  process.exit(isConnected ? 0 : 1)
})

socket.on('connect_error', (err) => {
  console.error(`✗ Connection Error: ${err.message}`)
  console.log(`\n❌ Cannot connect to backend`)
  console.log(`Make sure it's running: npm run dev`)
  process.exit(1)
})

// Timeout after 5 seconds
setTimeout(() => {
  console.error(`\n❌ Test timeout — backend not responding`)
  socket.disconnect()
  process.exit(1)
}, 5000)
