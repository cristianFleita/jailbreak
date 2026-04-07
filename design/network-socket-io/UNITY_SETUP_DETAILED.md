# Unity Socket.io Setup — Complete Step-by-Step Guide

## Problem
You have the scripts but the scene isn't connecting. The issue: **NetworkManager needs to be explicitly told to Connect()**.

## Solution
We added `GameBootstrap.cs` — a simple script that auto-connects on startup.

---

## Step 1: Verify Folder Structure

Make sure all script files exist:
```
Assets/Scripts/
├── GameBootstrap.cs                  ← NEW
├── Network/
│   ├── NetworkTypes.cs               ✓
│   ├── NetworkManager.cs             ✓
│   └── GameStateManager.cs           ✓
├── Player/
│   ├── PlayerNetworkSync.cs          ✓
│   └── RemotePlayerSync.cs           ✓
└── NPC/
    └── NPCNetworkSync.cs             ✓
```

---

## Step 2: Scene Hierarchy (Exact)

### Open your scene: `Assets/Scenes/demo-socket-io.unity`

**Delete everything** and create this exact hierarchy:

```
demo-socket-io (Scene Root)
├── Bootstrap (GameObject)
│   └── [Script] GameBootstrap
├── NetworkManager (GameObject)
│   ├── [Script] NetworkManager
│   └── [Script] GameStateManager
├── Game (GameObject)
│   ├── LocalPlayer (GameObject)
│   │   ├── [Component] CharacterController
│   │   └── [Script] PlayerNetworkSync
│   ├── NPCPool (GameObject)
│   │   └── [Script] NPCNetworkSync
│   └── Camera, Lighting, etc (optional)
```

### Step-by-step creation:

#### 1. Create Bootstrap GameObject
```
Right-click Scene → Create Empty
Name: Bootstrap
Attach Script: GameBootstrap
  └ Room Id field: leave as "demo-room"
```

#### 2. Create NetworkManager GameObject
```
Right-click Scene → Create Empty
Name: NetworkManager
Attach Scripts:
  ├ NetworkManager (no fields to set)
  └ GameStateManager
    └ Remote Player Prefab: (leave empty for now — uses capsules)
```

#### 3. Create Game Folder (just for organization)
```
Right-click Scene → Create Empty
Name: Game
Position: (0, 0, 0)
This is just a folder — add children to it:
```

#### 4. Create LocalPlayer (inside Game)
```
Right-click Game → Create Empty
Name: LocalPlayer
Position: (0, 1.5, 0)
Scale: (1, 1, 1)
Rotation: (0, 0, 0)

Attach Components:
  ├ CharacterController
  │   ├ Radius: 0.3
  │   ├ Height: 1.2
  │   ├ Center: (0, 0.6, 0)
  │   └ Slope Limit: 45
  └ Script: PlayerNetworkSync
    └ Send Interval: 0.05 (50ms)
```

#### 5. Create NPCPool (inside Game)
```
Right-click Game → Create Empty
Name: NPCPool
Position: (0, 0, 0)

Attach Script: NPCNetworkSync
  └ NPC Prefab: (leave empty)
```

#### 6. Add Camera & Lighting (optional, inside Game)
```
Right-click Game → 3D Object → Camera
Position: (0, 2, -5)

Right-click Game → Light → Directional Light
Position: (5, 5, 5)
Rotation: (45, -30, 0)
```

**Final hierarchy should look exactly like this:**
```
demo-socket-io
├── Bootstrap
├── NetworkManager
├── Game
│   ├── LocalPlayer
│   ├── NPCPool
│   ├── Camera
│   └── Light
```

---

## Step 3: Build Settings

### 1. Add Scene to Build
```
File → Build Settings (Ctrl+Shift+B)
Add Open Scenes: Click "Add Open Scenes"
  → Should show: Assets/Scenes/demo-socket-io.unity (at index 0)
```

### 2. Set Active Scene
```
In Build Settings, make sure demo-socket-io is at index 0
(it will be loaded first when the game runs)
```

---

## Step 4: Player Settings

### 1. WebGL Settings
```
Edit → Project Settings → Player
  → Select WebGL icon (top)
  → Other Settings:
    ├ Rendering → Color Space: Linear (or Gamma, doesn't matter)
    ├ Scripting Backend: IL2CPP
    └ IL2CPP Code Generation: Faster (Smaller) builds
```

### 2. WebGL Template (for window.BACKEND_URL)
```
Assets/WebGLTemplates/Default/index.html (or create new)

Add this to the <head>:
<script>
  window.BACKEND_URL = "http://localhost:3001";
</script>
```

---

## Step 5: Verify Scripts Compile

```
In Unity Editor:
  → Console window (bottom right)
  → Should see NO RED ERRORS
  → If there are errors, check:
    ├ Is SocketIOUnity package imported? (Packages/manifest.json has it?)
    ├ Are namespaces correct? (using Jailbreak.Network;)
    └ Run: Assets → Reimport All
```

---

## Step 6: Run in Editor

### Terminal 1: Start Backend
```bash
cd backend
npm run dev
```

**Expected output:**
```
[INIT] Game sockets initialized
Listening on http://localhost:3001
```

### Terminal 2: Run Play Button in Unity
```
Press Play (or Ctrl+P) in Unity Editor
```

**Expected console output (bottom of Unity):**
```
[BOOTSTRAP] Game startup...
[NET] Connecting to http://localhost:3001
[NET] Connected
[NET] State → Connected
[BOOTSTRAP] Connected! Joining room: demo-room
[NET] Joining room demo-room
[GSM] I am: <prisoner or guard>
[NET] State → InGame
[NPC] Initialized
```

If you see these messages, **you're connected!** ✅

---

## Step 7: Verify Socket Events in Console

### Open Browser DevTools (if running as WebGL)
```
Build WebGL: File → Build Settings → Build
Open unity-build/index.html in browser
```

### In Browser Console, you should see WebSocket frames:
```
Network → WS filter
ws://localhost:3001/socket.io/?...

Frames tab should show:
0 CONNECT
2 player-joined [{"playerId":"...", "role":"prisoner/guard", ...}]
2 player:state [{"players":[...]}]  ← Every 50ms
2 npc:positions [{"npcs":[...]}]   ← Every 200ms
```

If you don't see these, the backend isn't sending — check backend logs!

---

## Debugging Checklist

### Problem: Only `[NPC] Initialized` in console

**Solution**: GameBootstrap didn't run
- [ ] Is `Bootstrap` GameObject in the scene?
- [ ] Does it have `GameBootstrap` script attached?
- [ ] Is scene saved and loaded?
- [ ] Press Play again

### Problem: `[NET] Connecting to...` but nothing after

**Solution**: Backend not running or wrong URL
- [ ] Backend running? `npm run dev` in `/backend`?
- [ ] Check backend console for `[INIT] Game sockets initialized`
- [ ] URL correct? Editor fallback is `http://localhost:3001` ✓
- [ ] Firewall blocking port 3001? Try: `nc -zv localhost 3001`

### Problem: `Connected` but no `player-joined` event

**Solution**: JoinRoom didn't fire
- [ ] Check NetworkManager OnConnectedEvent is wired
- [ ] GameBootstrap.OnConnected() should call JoinRoom()
- [ ] Check for errors in stack trace

### Problem: `player-joined` arrives but no LocalPlayer movement

**Solution**: PlayerNetworkSync not running
- [ ] LocalPlayer has CharacterController? (required!)
- [ ] LocalPlayer has PlayerNetworkSync script?
- [ ] SendInterval is 0.05 (50ms)?
- [ ] Check console for `[PNS]` logs

### Problem: NPCs don't spawn

**Solution**: npc:positions not arriving
- [ ] Wait 1-2 seconds after game:start
- [ ] Backend game loop takes 50ms to first broadcast
- [ ] Check backend logs for `[TICK] npc:positions`
- [ ] If no NPCs in backend, check `initializeNPCs()` was called

---

## Quick Test: 4-Client Simulation

### Terminal 1: Backend
```bash
cd backend
npm run dev
```

### Terminal 2: 4 Demo Clients
```bash
cd backend
node test-4clients.js
```

**Watch output**: Should show 4 players connecting, game starting, phase transitions

Then connect Unity in Editor at same time — Unity becomes the 5th client (won't join since max 4)

---

## File Checklist

| File | Created | In Scene |
|------|---------|----------|
| NetworkTypes.cs | ✓ | - |
| NetworkManager.cs | ✓ | ✓ (NetworkManager GO) |
| GameStateManager.cs | ✓ | ✓ (NetworkManager GO) |
| PlayerNetworkSync.cs | ✓ | ✓ (LocalPlayer GO) |
| RemotePlayerSync.cs | ✓ | - (spawned at runtime) |
| NPCNetworkSync.cs | ✓ | ✓ (NPCPool GO) |
| GameBootstrap.cs | ✓ NEW | ✓ (Bootstrap GO) |

---

## Expected Behavior (After Setup)

1. **Press Play in Editor**
   - Bootstrap calls `NetworkManager.Connect()`
   - Console shows: `[NET] Connecting...` → `[NET] Connected`

2. **Backend sends `player-joined`**
   - Console shows: `[GSM] I am: prisoner` (or guard)
   - Console shows: `[NET] State → InGame`

3. **Backend tick loop broadcasts `npc:positions`**
   - Console shows: `[NPC] Spawned guard: npc_guard_001`
   - Console shows: `[NPC] Spawned helper: npc_helper_001` (×18 more)

4. **Backend broadcasts `player:state` every 50ms**
   - Console shows: `[PNS] Connected — movement send loop started`
   - Capsule (LocalPlayer) appears in scene

5. **You see in viewport**
   - 1 capsule (you, white)
   - 20 smaller capsules (NPCs, red/blue)
   - Maybe 1-2 other capsules (remote players, if connected in another tab)

---

## If Still Stuck

1. **Share console output** (copy entire log)
2. **Check backend logs** (does it show players connecting?)
3. **Verify Build Settings** (scene in build, at index 0?)
4. **Delete Library folder** (forces reimport):
   ```bash
   rm -rf unity/JAILBREAK/Library
   # Wait 1 min for reimport
   ```

---

**You're ready!** Press Play and watch the game connect 🚀
