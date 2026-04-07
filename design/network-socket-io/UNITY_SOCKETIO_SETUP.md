# Unity Socket.io Integration — Setup Guide

## What Was Created

✅ **7 C# Scripts** (in `unity/JAILBREAK/Assets/Scripts/`)
- `Network/NetworkTypes.cs` — All data types + event payloads
- `Network/NetworkManager.cs` — Socket.io client (Singleton)
- `Network/GameStateManager.cs` — Game state tracker + remote player spawn
- `Player/PlayerNetworkSync.cs` — Local player: send movement, rubber-band reconciliation
- `Player/RemotePlayerSync.cs` — Remote players: interpolation buffer
- `NPC/NPCNetworkSync.cs` — NPC pool: spawn/update from server deltas

✅ **Updated `Packages/manifest.json`**
- Added SocketIOUnity: `https://github.com/itisnajim/SocketIOUnity.git`

---

## Scene Setup (in Unity Editor)

### 1. Add NetworkManager
1. Create empty GameObject: `"NetworkManager"`
2. Add scripts:
   - `NetworkManager.cs`
   - `GameStateManager.cs` (same GameObject)
3. Set **DontDestroyOnLoad**: ✓ (handled in code)

### 2. Add NPCPool
1. Create empty GameObject: `"NPCPool"`
2. Add script: `NPCNetworkSync.cs`
3. Keep the NPC Prefab field empty (uses capsules by default)

### 3. Setup LocalPlayer
1. Create empty GameObject: `"LocalPlayer"`
2. Add components:
   - `CharacterController` (Unity built-in)
   - `PlayerNetworkSync.cs`
3. Position: (0, 1.5, 0) — matches server spawn position
4. Set up a simple movement controller to test (optional for now)

### 4. (Optional) Create RemotePlayer Prefab
If you want custom remote player models:
1. Create a prefab with your model
2. In GameStateManager Inspector → assign to "Remote Player Prefab"
3. If left empty, uses colored capsules (red=guard, blue=prisoner)

---

## Before You Test

### Backend Running?
```bash
cd backend
npm run dev
```

Expected output:
```
[INIT] Game sockets initialized
Listening on http://localhost:3001
```

### Check the React Build Path
The host page needs to set `window.BACKEND_URL` so the jslib can read it. In your React index.html:

```html
<script>
  window.BACKEND_URL = "http://localhost:3001";
</script>
```

Or Unity will fall back to `http://localhost:3001` in Editor mode automatically.

---

## Testing Workflow

### 1. Build WebGL
```bash
# In Unity Editor
Build > Build WebGL
```

Outputs to: `unity/JAILBREAK/web/public/unity-build/index.html`

### 2. Run Frontend (React)
```bash
cd frontend
npm run dev
```

### 3. Test in Browser
1. Open `http://localhost:5173` (Vite frontend)
2. Open browser DevTools → Console
3. Should see:
   ```
   [NET] Connecting to http://localhost:3001
   [NET] Connected
   [NET] State → Connected
   [NET] Joining room room-1
   ```

### 4. Verify Socket.io Connection
DevTools → Network → WS filter:
- Should see WebSocket connection to `ws://localhost:3001/socket.io/?...`
- Frames should show:
  - `0 (CONNECT)`
  - `2 (EVENT) player-joined`
  - `2 (EVENT) player:state` (every 50ms)
  - `2 (EVENT) npc:positions` (every 200ms)

---

## Debug Log Prefixes

| Prefix | Component |
|--------|-----------|
| `[NET]` | NetworkManager |
| `[GSM]` | GameStateManager |
| `[PNS]` | PlayerNetworkSync (local player) |
| `[NPC]` | NPCNetworkSync |

All logged to Unity Console for easy debugging.

---

## Known Issues & Workarounds

### SocketIOUnity Package Not Found
After editing `manifest.json`, Unity may take 30s to download the package.
- Check Window → TextMesh Pro (any import dialog) first
- Wait for import to complete
- If still missing, try: Refresh Package Manager → Window → Package Manager → Packages: In Project

### Compilation Errors about SocketIOClient
Make sure the package downloaded completely:
- `Packages/manifest.json` shows `com.itisnajim.socketiounity`
- `Packages/packages-lock.json` includes the git URL
- Project > Settings > Editor > Domain Reload may need toggling

### Can't Connect to Backend
1. Verify backend is running: `npm run dev` in `backend/`
2. Check firewall: `http://localhost:3001` accessible?
3. In Editor, falls back to `http://localhost:3001` automatically
4. In WebGL builds, check `window.BACKEND_URL` is set in host page

### No Player Visible
- Did you add `CharacterController` to LocalPlayer GameObject?
- Is scene named "SampleScene" and in Build Settings?
- Check GameStateManager console logs for spawn messages

---

## Next Steps

### Immediate (1 hour)
1. ✅ Scripts created
2. ⏳ Scene setup (GameObject + components)
3. ⏳ Build WebGL
4. ⏳ Test browser connection

### Short Term (2 hours)
1. ⏳ Implement movement input (WASD, space) in CharacterController
2. ⏳ Test: move local player → see server correction
3. ⏳ Open 2nd tab → verify remote player appears and interpolates

### Medium Term (next session)
1. ⏳ Hook input for guard:mark and player:interact
2. ⏳ Visual feedback for phase changes
3. ⏳ End-game UI
4. ⏳ Sound effects for catches/game end

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│ NetworkManager (Singleton, DontDestroyOnLoad)           │
│ ├─ Socket.io client (SocketIOUnity)                    │
│ ├─ Events: OnPlayerStateEvent, OnNPCPositionsEvent,... │
│ └─ Methods: SendPlayerMove(), SendGuardMark(), etc.   │
└────────────┬────────────────────────────────────────────┘
             │
             ├─────────────────────────────────────┐
             ↓                                     ↓
    ┌─────────────────────────┐      ┌──────────────────────┐
    │ GameStateManager        │      │ PlayerNetworkSync    │
    │ ├─ Player roster        │      │ (LocalPlayer)        │
    │ ├─ Spawn remote players │      │ ├─ SendMovementLoop  │
    │ └─ Track phase/status   │      │ └─ RubberBand       │
    └─────────┬───────────────┘      └──────────────────────┘
              │
        ┌─────┴──────┬──────────┐
        ↓            ↓          ↓
   Remote Player  Remote Player NPCNetworkSync
   (RemotePlayerSync, RemotePlayerSync)  (NPCPool)
   └─ Interpolation buffer     └─ Lerp to delta updates
```

---

## File Locations

```
unity/JAILBREAK/
├── Assets/
│   └── Scripts/
│       ├── Network/
│       │   ├── NetworkTypes.cs
│       │   ├── NetworkManager.cs
│       │   └── GameStateManager.cs
│       ├── Player/
│       │   ├── PlayerNetworkSync.cs
│       │   └── RemotePlayerSync.cs
│       └── NPC/
│           └── NPCNetworkSync.cs
└── Packages/
    └── manifest.json (updated with SocketIOUnity)
```

---

**Status**: All code written. Ready for scene configuration and testing! 🚀
