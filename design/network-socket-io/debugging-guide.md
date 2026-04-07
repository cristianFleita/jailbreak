# Debugging & Testing Guide — Sincronización de Estado

> **Status**: Ready for testing  
> **Date**: 2026-04-06  
> **All Phases**: 1, 2, 3 complete

## Quick Start

### Prerequisites

```bash
# Terminal 1: Start backend
cd backend
npm run dev

# Terminal 2: Run tests (when ready)
npm run test

# Terminal 3: Unity client or Socket.io test client
# (See below for client testing)
```

**Backend runs on**: `http://localhost:3001`  
**Socket.io endpoint**: `ws://localhost:3001`

---

## Architecture Overview

```
Client (Unity)
     ↓ Socket.io
     ↓
Backend (Node.js)
     ├─ Event Handlers (event-handlers.ts)
     │   └─ Validate, update state
     ├─ Tick Loop (20/sec in room-manager.ts)
     │   ├─ GameManager.tick()
     │   │   ├─ NPC Behavior (patrol/chase)
     │   │   ├─ Pursuit State
     │   │   ├─ Phase Transitions
     │   │   ├─ Escape Routes
     │   │   ├─ Victory Conditions
     │   │   └─ Return: { shouldEnd, winner, reason }
     │   ├─ Broadcast: player:state, npc:positions (delta)
     │   └─ Emit: game:end (if shouldEnd)
     └─ Reconnection (hold slot 30s)
```

---

## Testing Approaches

### 1. **Manual Testing with Socket.io Client**

Use a simple JavaScript client to connect and test events.

**File**: `backend/test-client.js` (create this):

```javascript
const io = require('socket.io-client')

const socket = io('http://localhost:3001', {
  transports: ['websocket'],
})

socket.on('connect', () => {
  console.log('✓ Connected:', socket.id)
  
  // Join room
  socket.emit('join-room', 'test-room-1')
})

socket.on('player-joined', (data) => {
  console.log('✓ Player joined:', data)
})

socket.on('player:state', (data) => {
  console.log(`[TICK ${data.players.length} players]`)
})

socket.on('game:start', (data) => {
  console.log('✓ Game started:', data.phase.phaseName)
})

socket.on('game:end', (data) => {
  console.log('✓ Game ended:', data.winner, data.reason)
})

socket.on('error', (err) => {
  console.error('✗ Error:', err)
})

// Simulate player input
setTimeout(() => {
  console.log('\n→ Sending player:move...')
  socket.emit('player:move', {
    playerId: socket.id,
    position: { x: 5, y: 1.5, z: 0 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    velocity: { x: 1, y: 0, z: 0 },
    movementState: 'walking',
  })
}, 2000)

setTimeout(() => {
  console.log('\n→ Disconnecting...')
  socket.disconnect()
}, 5000)
```

**Run**:
```bash
node test-client.js
```

**Expected Output**:
```
✓ Connected: [socket-id]
✓ Player joined: { playerId, role: prisoner, players: [...] }
[TICK 1 players]
[TICK 1 players]
...
→ Sending player:move...
[TICK 1 players]
...
→ Disconnecting...
```

---

### 2. **Multi-Client Testing (4 Clients)**

Test with 4 connected clients (1 guard, 3 prisoners).

**File**: `backend/test-4clients.js`:

```javascript
const io = require('socket.io-client')

const clients = []
const roomId = 'test-room-4p'

function createClient(index) {
  const socket = io('http://localhost:3001', {
    transports: ['websocket'],
  })

  socket.on('connect', () => {
    console.log(`[P${index}] Connected: ${socket.id}`)
    socket.emit('join-room', roomId)
  })

  socket.on('player-joined', (data) => {
    console.log(`[P${index}] Joined. Role: ${data.players[data.players.length - 1]?.role}`)
  })

  socket.on('game:start', (data) => {
    console.log(`[P${index}] Game started! Phase: ${data.phase.phaseName}`)

    // Start sending movements every 100ms
    const moveInterval = setInterval(() => {
      socket.emit('player:move', {
        playerId: socket.id,
        position: {
          x: Math.cos(Date.now() / 1000 + index) * 10,
          y: 1.5,
          z: Math.sin(Date.now() / 1000 + index) * 10,
        },
        rotation: { x: 0, y: 0, z: 0, w: 1 },
        velocity: {
          x: Math.cos(Date.now() / 1000 + index),
          y: 0,
          z: Math.sin(Date.now() / 1000 + index),
        },
        movementState: 'walking',
      })
    }, 100)

    socket.on('disconnect', () => clearInterval(moveInterval))
  })

  socket.on('game:end', (data) => {
    console.log(`[P${index}] GAME OVER: ${data.winner} wins (${data.reason})`)
  })

  socket.on('guard:catch', (data) => {
    console.log(`[P${index}] Guard caught ${data.targetId}`)
  })

  return socket
}

// Create 4 clients
console.log('Starting 4-client test...\n')
for (let i = 0; i < 4; i++) {
  const socket = createClient(i)
  clients.push(socket)
}

// Keep alive for 30 seconds
setTimeout(() => {
  console.log('\nDisconnecting all clients...')
  clients.forEach((c) => c.disconnect())
  process.exit(0)
}, 30000)
```

**Run**:
```bash
node test-4clients.js
```

**Expected Output** (should auto-start after 2-3 clients join):
```
[P0] Connected: socket_123
[P0] Joined. Role: guard
[P1] Connected: socket_456
[P1] Joined. Role: prisoner
[P2] Connected: socket_789
[P2] Joined. Role: prisoner
[P3] Connected: socket_999
[P3] Joined. Role: prisoner

[P0] Game started! Phase: Active
[P1] Game started! Phase: Active
...
(30s passes, phase transitions, game runs)
...
[P0] GAME OVER: guards wins (escape_timeout)
```

---

### 3. **GameManager Direct Testing**

Test GameManager directly without Socket.io.

**File**: `backend/src/game/systems/__tests__/game-manager.test.ts`:

```typescript
import { describe, it, expect, beforeEach } from 'vitest'
import { GameManager } from '../game-manager'
import { createGameRoomState } from '../../state'
import { defaultGameConfig } from '../../room-manager'
import { GameRoom } from '../../types'

describe('GameManager', () => {
  let gameManager: GameManager
  let room: GameRoom

  beforeEach(() => {
    const state = createGameRoomState('test-room', defaultGameConfig)
    room = { state, config: defaultGameConfig }
    gameManager = new GameManager(room)
  })

  it('should not end game immediately', () => {
    const result = gameManager.tick()
    expect(result.shouldEnd).toBe(false)
  })

  it('should track guard errors', () => {
    gameManager.penalty.recordGuardError('guard_1', 'false_accusation')
    expect(gameManager.penalty.getGuardErrorCount('guard_1')).toBe(1)
  })

  it('should make riot available after 3 errors', () => {
    gameManager.penalty.recordGuardError('guard_1', 'error_1')
    gameManager.penalty.recordGuardError('guard_1', 'error_2')
    gameManager.penalty.recordGuardError('guard_1', 'error_3')
    expect(gameManager.penalty.isRiotAvailable()).toBe(true)
  })

  it('should transition phases', () => {
    let phase = gameManager.phases.getCurrentPhase()
    expect(phase).toBe('active')
    // Fast-forward 130 seconds
    // (Would need to mock Date.now() or wait real time)
  })
})
```

---

### 4. **Logs & Debugging Output**

Backend logs all important events prefixed with `[TAG]`.

**Key tags to watch**:

```
[CONN]       — Connection events
[JOIN]       — Player join
[MOVE]       — Movement validation
[CATCH]      — Guard catch attempt
[CHASE]      — Chase started
[PICKUP]     — Item pickup
[INVENTORY]  — Inventory updates
[ESCAPE]     — Escape route progress
[PHASE]      — Phase transitions
[TICK]       — Game tick updates
[GAME-END]   — End game condition
[PURSUIT]    — Pursuit system
[PENALTY]    — Guard errors
[NPC]        — NPC behavior
```

**Run backend with verbose logging**:
```bash
cd backend
npm run dev  # Uses nodemon + tsx, shows all console.log
```

---

### 5. **Specific Test Scenarios**

#### Scenario A: All Prisoners Caught (Guards Win)

```javascript
// After game starts:
// 1. Move all 3 prisoners to center (x: 0, z: 0)
// 2. Guard marks each prisoner
// 3. Guard moves within 1.5m and emits guard:catch
// Expected: game:end { winner: 'guards', reason: 'all_prisoners_caught' }
```

#### Scenario B: Escape (Prisoners Win)

```javascript
// After game starts:
// 1. All 3 prisoners collect 3 items each (move to item positions)
// 2. All prisoners move to escape zone (x: 0, y: 1.5, z: -40)
// 3. Escape condition triggered
// Expected: game:end { winner: 'prisoners', reason: 'escape_route' }
```

#### Scenario C: Riot (Prisoners Win)

```javascript
// Requires PenaltySystem to make riot available
// 1. Guard makes 3 errors (not yet tracked in events; manual call for now)
// 2. Prisoner emits riot:activate
// Expected: game:end { winner: 'prisoners', reason: 'riot_activated' }
```

#### Scenario D: Phase Transitions

```javascript
// Watch logs as game progresses:
// [PHASE] Transitioned to lockdown (duration: 60s)
// [PHASE] Transitioned to escape (duration: 300s)
// If no escape by end of escape phase:
// [PHASE] Transitioned to (game:end)
```

#### Scenario E: Reconnection

```javascript
// Client 1: joins room, game starts
// (5s pass)
// Client 1: socket.disconnect()
// (Server: [RECONNECT] Saved slot for client_1 in room_x (expires in 30s)
// (15s pass)
// Client 1: socket reconnects, emits join-room same roomId
// Server: [RECONNECT] Restored client_1 to room_x
// Client receives: game:reconnect { players, npcs, items, phase, tick }
// Client resumes playing at same position
```

---

## Performance Monitoring

### Check Bandwidth

Add this to tick loop (room-manager.ts):

```typescript
let bytesSent = 0
let lastCheck = Date.now()

// In tick loop, measure payload sizes:
const playerStateBytes = JSON.stringify(playerStatePayload).length
const npcBytes = JSON.stringify(npcPayload).length
bytesSent += playerStateBytes + npcBytes

if (Date.now() - lastCheck > 1000) {
  const bytesPerSecond = bytesSent
  const kbps = (bytesPerSecond / 1024).toFixed(2)
  console.log(`[PERF] ${kbps} KB/s`)
  bytesSent = 0
  lastCheck = Date.now()
}
```

**Expected**: < 5 KB/s for 4 players + 20 NPCs

### Check CPU

```bash
# Terminal 1: Run backend
npm run dev

# Terminal 2: Monitor CPU with Node inspect
node --inspect dist/index.js
# Then open chrome://inspect in Chrome DevTools
```

**Expected**: < 20% CPU on Render free tier during 4-player match

### Check Memory

```typescript
// Add to game-manager tick:
const memUsage = process.memoryUsage()
console.log(`[PERF] Heap: ${(memUsage.heapUsed / 1024 / 1024).toFixed(1)} MB`)
```

**Expected**: Stable, no leak > 10 MB/min

---

## Known Limitations & TODOs

### Phase 1 (State Management)
- ✅ Types, state, validation
- ✅ Tick loop (player:state, npc:positions)

### Phase 2 (Socket Events)
- ✅ Event handlers with validation
- ✅ Reconnection system
- ⚠️ Riot availability hardcoded (true)
- ⚠️ No item spawning (manual creation)

### Phase 3 (System Integrations)
- ✅ NPC behavior (patrol + chase)
- ✅ Pursuit state machine
- ✅ Phase transitions
- ✅ Escape route tracking
- ✅ Victory conditions
- ⚠️ Guard errors: need event hook to record errors
- ⚠️ Camuflage: implemented, not tested
- ⚠️ Inventory: basic slots, no item effects

---

## Debugging Checklist

- [ ] Backend compiles (`npm run build`)
- [ ] Backend runs (`npm run dev`)
- [ ] Single client can join (`test-client.js`)
- [ ] 4 clients auto-start game
- [ ] Game emits `player:state` every 50ms
- [ ] Game emits `npc:positions` every 200ms (delta)
- [ ] NPC positions update in logs
- [ ] Guard mark → chase:start broadcast
- [ ] Guard catch with validation
- [ ] Phase transitions in logs
- [ ] Reconnection saves slot (30s)
- [ ] Victory condition: all prisoners caught
- [ ] Victory condition: escape route
- [ ] Bandwidth < 5 KB/s
- [ ] CPU < 20%

---

## Next Steps

1. **Unit Tests**: Implement test files listed in `test-plan.md`
2. **Integration Tests**: 4-client match with event logging
3. **Client Integration**: Wire Unity Socket.io client
4. **E2E Testing**: Full match cycle with victory conditions
5. **Performance Tuning**: Bandwidth optimization, phase timing
6. **Polish**: UI, networking error handling, reconnection UX
