# Manual Network Testing — Test Clients

Two Node.js test clients for verifying the backend network layer is working.

## Quick Start

### Terminal 1: Start Backend
```bash
npm run dev
```

**Expected output**:
```
🎮 Game server running on port 3001
```

### Terminal 2: Run Test

#### Option A: Single Client (Quick Verify)
```bash
node test-client.js
```

**Expected flow**:
1. ✓ Connected to server
2. ✓ Joined room successfully
3. ✓ Received player:state broadcast
4. ✓ Disconnected

**Time**: ~2 seconds

---

#### Option B: Four Clients (Full Test)
```bash
node test-4clients.js
```

**Expected flow**:
1. ✓ 4 clients connect (Guard + 3 Prisoners)
2. ✓ All join same room
3. ✓ Game auto-starts after 2+ players
4. ✓ Players move in circular pattern
5. ✓ NPC positions update
6. ✓ Phase transitions
7. ✓ Game ends (30s test duration)

**Time**: ~30 seconds

**Output**:
```
[PLAYER_0] ✓ Connected to server
[PLAYER_1] ✓ Connected to server
[PLAYER_2] ✓ Connected to server
[PLAYER_3] ✓ Connected to server

[PLAYER_0] ✓ Player joined. Total: 1
[PLAYER_1] ✓ Player joined. Total: 2
[PLAYER_2] ✓ Player joined. Total: 3
[PLAYER_3] ✓ Player joined. Total: 4

[GUARD] 🎮 GAME STARTED! Phase: Active
[PRISONER_1] 🎮 GAME STARTED! Phase: Active
[PRISONER_2] 🎮 GAME STARTED! Phase: Active
[PRISONER_3] 🎮 GAME STARTED! Phase: Active

[GUARD] [TICK] 4/4 alive
[GUARD] [NPC] Updated 20 NPCs (delta compression)
...

═══════════════════════════════════════════
                    TEST SUMMARY
═══════════════════════════════════════════
Game Started:          ✓
Game Ended:            ✓
Players Joined:          4
Player State Updates:   300+
NPC Position Updates:    30+
Chases Initiated:         0
═══════════════════════════════════════════
✓ NETWORK SYNCHRONIZATION WORKING!
═══════════════════════════════════════════
```

---

## What Each Test Verifies

### test-client.js (Single Client)
- ✓ Backend is running and accepting connections
- ✓ Socket.io handshake works
- ✓ Rooms can be joined
- ✓ Events are broadcasted (player:state)
- ✓ Basic game loop is running

**Good for**: Quick sanity check before 4-client test

### test-4clients.js (Full Game)
- ✓ Multiple clients can connect simultaneously
- ✓ Auto-game-start trigger works (2+ players)
- ✓ All players in same room see each other
- ✓ Player movement input is accepted and validated
- ✓ NPC positions are being updated
- ✓ Delta compression is working (NPC count reduced)
- ✓ Phase system is running
- ✓ Game can end

**Good for**: Full integration test of all systems

---

## Troubleshooting

### Backend not running
```
❌ Connection Error: connect ECONNREFUSED 127.0.0.1:3001
```

**Fix**: Start backend in another terminal:
```bash
npm run dev
```

---

### Clients connect but no game starts
Check backend logs for `[GAME-START]` message.

If missing:
1. Verify 2+ clients actually connected
2. Check frontend for console errors
3. Verify Socket.io is emitting `game:start`

---

### Players don't move
Movement is simulated by test clients. Check:
1. `player:move` events are being sent (check `[MOVE]` logs)
2. `player:state` broadcasts are received

---

### NPC positions not updating
NPCs are spawned at game start. Check:
1. `game:start` was emitted (check logs)
2. `npc:positions` events are being broadcast (check every 200ms)

---

## What the Tests Do

### Single Client Test (test-client.js)
```
1. Connect to ws://localhost:3001
2. Join room "test-room-single"
3. Send one player:move event
4. Listen for player:state broadcasts
5. Disconnect
```

### Four Client Test (test-4clients.js)
```
1. Create 4 Socket.io clients
   - Client 0 = Guard
   - Clients 1-3 = Prisoners

2. All join same room "test-room-4p"
   - Staggered by 100ms to simulate real network

3. Listen for game:start
   - Triggered auto when 2+ players joined

4. Each client sends player:move every 100ms
   - Position calculated as circle around origin
   - All clients continuously "moving"

5. Track broadcasts received:
   - player:state (every 50ms tick)
   - npc:positions (every 200ms delta)
   - game:end (when condition met)

6. After 30 seconds:
   - Disconnect all clients
   - Print stats
```

---

## Performance Tips

If test is slow:
1. Check backend CPU usage (`npm run dev` logs)
2. Verify no other heavy processes running
3. Run on native machine (not VM)

If getting many validation errors:
1. Check `[MOVE]` logs in backend — look for rejection reasons
2. Verify movement distances are legal
3. Check map bounds in `backend/src/game/room-manager.ts`

---

## Next Steps

✅ All tests pass?

→ Integrate Socket.io C# client in Unity

**Required in Unity**:
1. `SocketIOUnity` or `socket.io-client-csharp` package
2. Implement movement controller with rubber-band
3. Listen for `player:state` and `npc:positions`
4. Emit `player:move` events

See `NETWORK-IMPLEMENTATION.md` → "Integration with Unity"

---

## Files Reference

- `test-client.js` — Single client test (2s)
- `test-4clients.js` — Full game test (30s)
- `backend/src/index.ts` — Server entry point
- `backend/src/sockets/game.ts` — Socket.io handlers
- `design/debugging-guide.md` — More manual testing scenarios
