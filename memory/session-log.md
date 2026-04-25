# Session Log
<!-- Claude Code appends to this file at session end -->
## Sessions

### 2026-04-05
- Backend: set `package.json` `"type": "module"` so `module: nodenext` + `verbatimModuleSyntax` treat sources as ESM; updated relative imports to `.js` extensions; added minimal `src/routes/game.ts` (was imported but missing) so `tsc --noEmit` passes.

### 2026-04-06
- **Network Programmer — Fase 1**: Implemented Sincronización de Estado core
  - Created core types: `PlayerState`, `NPCState`, `GameRoomState`, Socket.io payloads (types.ts)
  - State management: `addPlayer`, `updatePlayerMovement`, `spawnNPCs`, `computeNPCDelta`, delta compression (state.ts)
  - Validation layer: speed checks, bounds, interaction distance, catch range, anti-cheat (validation.ts)
  - Room manager: 20 ticks/sec broadcast loop, `player:state` every tick, `npc:positions` delta every 200ms (room-manager.ts)
  - Socket handlers: `join-room`, `player:move`, `guard:mark`, lifecycle (game.ts)
  - System integration contracts: 11 interfaces (system-integrations.ts)
  - Test plan (test-plan.md)
  - Architecture decision: ADR-001 (authoritative server + client prediction)

- **Network Programmer — Fase 2**: Implemented Socket events & game logic
  - Event handlers for: `player:move`, `player:interact`, `guard:catch`, `guard:mark`, `riot:activate` (event-handlers.ts)
  - Validation: payload ownership, distance (2m/1.5m), speed, bounds, camuflage immunity
  - Race condition handling: two clients pickup same item → first wins, second gets error
  - Reconnection system: save slot for 30s, restore with `game:reconnect` snapshot (reconnection.ts)
  - Updated game.ts socket handlers with event dispatch, error handling, win condition checks
  - Event flow documentation (fase-2-event-flow.md)

- **Network Programmer — Fase 3**: Implemented ALL game systems
  - **NPC Behavior System**: Patrol routes + chase; patrol speed 3 u/s, chase speed 6 u/s; 15s timeout
  - **Pursuit System**: Chase state machine; 30m escape radius; mark → chase:start broadcast
  - **Disguise System**: Camuflage immunity; prevents catch while active
  - **Penalty System**: Guard error tracking; riot available after 3 errors
  - **Inventory System**: 4-slot per player; pickup/use/drop; race condition (first-come-first-served)
  - **Escape Routes System**: Track items collected; need 3 items + in escape zone to win
  - **Phase System**: 5 phases (setup, active, lockdown, escape, riot); auto-transition by duration
  - **Victory Condition System**: 3 win paths: catch all prisoners, escape route, riot activation
  - **Game Manager**: Coordinates all systems; main tick entry point; integrates with room-manager
  - **Integration**: GameManager.tick() called every 50ms; returns { shouldEnd, winner, reason }
  - Event handlers notify GameManager on important actions (mark, catch, pickup, move)
  - **Debugging Guide**: Manual testing, 4-client scenarios, perf monitoring, bandwidth checks (design/debugging-guide.md)
  - **TypeScript**: All code compiles cleanly (no errors)
  
- **Network Programmer — Test Suite Complete**: Generated 60 comprehensive tests, vitest configured
  - vitest.config.ts: Vitest configuration with coverage
  - npm scripts: test, test:watch, test:ui
  - **Unit Tests**:
    * state.test.ts: 13 tests (addPlayer, removePlayer, spawnNPCs, delta, distance, phase transitions)
    * validation.test.ts: 10 tests (speed, bounds, interaction distance, catch range, ownership)
    * room-manager.test.ts: 8 tests (createRoom, getOrCreate, destroy, initializeNPCs, lifecycle)
    * game-manager.test.ts: 8 tests (initialization, tick, errors, events, stats)
  - **Integration Tests**:
    * game-loop.integration.test.ts: 6 tests (tick timing, player:state broadcasts, NPC delta, empty delta)
    * socket-events.integration.test.ts: 10 tests (move validation, interact race conditions, catch validation, camuflage immunity, victory conditions)
  - **Performance Tests**:
    * performance.test.ts: 5 tests (bandwidth <5KB/s, tick precision <30ms variance, memory stability, CPU load, delta compression)
  - Test README with running instructions and coverage goals

## Architecture Complete:
- ✅ Full state synchronization (Fase 1)
- ✅ Socket.io events with validation (Fase 2)
- ✅ Game systems integrated (Fase 3)
- ✅ Tick loop + physics + logic
- ✅ Reconnection system
- ✅ All 3 victory conditions
- ✅ **60 tests covering all systems**
- ✅ **Ready for: npm run test, debugging, Unity integration**

### 2026-04-06 (follow-up)
- Backend manual scripts: `test-client.js` and `test-4clients.js` now use `import { io } from 'socket.io-client'` so they run under `"type": "module"` (replacing `require`, which is invalid in ESM).

### 2026-04-23
- **Game Design — Ruta 1 Ventilacion Industrial**:
  - Updated `design/GDD.md` section 5.2 with MVP decisions for the cooperative ventilation route.
  - Added technical implementation spec at `design/gdd/ruta-1-ventilacion-industrial.md`.
  - Documented shared prisoner HUD, role-filtered events, route data model, anti-softlock item rules, tuning knobs, edge cases, and acceptance criteria.
  - Recorded ADR-002 in `memory/decisions.md`.
  - Confirmed local models/Ollama will not be used for this documentation work.
- **Game Design — Celdas + Recuento Final**:
  - Updated `design/GDD.md` to replace "Luces apagadas" with `Encierro / Recuento final`, shorten Phase 9 to 90s, and redefine the endgame around visible cell inspection.
  - Updated `design/gdd/rutina-fases-npc.md` to make free-time cell behavior readable from the corridor and to convert Phase 9 into a visible count/silhouette phase.
  - Updated `design/gdd/systems-index.md` and `design/gdd/npc-autonomy-personality.md` so supporting docs match the new cell-front/barred layout and final-count fantasy.
  - Recorded ADR-003 in `memory/decisions.md`.
- **Unity/Network Programmer — Ruta 1 Fase A continuation**:
  - Wired `InventorySystem` into `event-handlers.ts` for `item.pickup` / `item.drop`, emitting `item:pickup`, `item:drop`, and `item:state`.
  - Added capture drop handling so held inventory returns to the world when a prisoner is caught.
  - Updated socket action typing to `InteractAction` and preserved legacy `pickup`/`drop` aliases.
  - Added Unity network payloads for authoritative inventory and item lifecycle state.
  - Added WebGL/Editor listeners for `item:drop` and `item:state`.
  - Added `NetworkedItemRegistry` and `NetworkedPickupInteractable` to support server-confirmed local pickup and remote item replay.
  - Verified backend TypeScript with bundled Node because local Homebrew Node is missing ICU dylib.
- **Unity/Network Programmer — Ruta 1 Fase A spawn init**:
  - Added Unity-to-backend item spawn registration via `item:register-spawns`.
  - Backend now initializes `route1_cutters` and `route1_wrench` from scene-authored spawn markers and emits active `item:state`.
  - `NetworkedItemRegistry` now supports multiple spawn copies per item (`itemId` + `spawnId`) and hides inactive copies.
  - `GameStateManager` registers local scene spawns shortly after GameScene starts and applies item snapshots/state updates.
  - Verified backend TypeScript and `game-manager.test.ts` using bundled Node.
- **Unity/Network Specialist — Ruta 1 Fases B-E task breakdown**:
  - Read `memory/progress.md` and `design/gdd/ruta-1-implementation-plan.md`.
  - Created `design/gdd/ruta-1-fases-b-e-tareas.md` with assignment-ready tickets from Phase B through Phase E.
  - Captured current Phase A baseline as verification work instead of duplicating implementation tasks.
  - Split follow-up work across backend/network, Unity network payloads, route interactables, UI Toolkit HUD, world cues, edge cases, QA, balance, and performance.
  - Recorded ADR-005 in `memory/decisions.md`.

### 2026-04-24
- **Unity/Network Specialist — Ruta 1 docs reset to MVP spec**:
  - Rewrote `design/gdd/ruta-1-ventilacion-industrial.md` as the current source of truth for `route1_ventilation`.
  - Replaced `design/gdd/ruta-1-implementation-plan.md` with phases A-F: route selector, authoritative inventory, spawn areas, Route1System, Unity interactables, HUD/QA.
  - Replaced `design/gdd/ruta-1-fases-b-e-tareas.md` with task-level work items for the new phases while keeping the filename for compatibility.
  - Updated `design/GDD.md` escape, map, HUD, audio, networking, roadmap, and scope-cut sections to remove old fusible/route2/route3 implementation details.
  - Recorded ADR-006 in `memory/decisions.md`; ADR-006 supersedes older Ruta 1 concrete details.
- **Unity/Network Specialist — Ruta 1 Phase B implementation**:
  - Implemented authoritative `item.pickup` / `item.store` handling for `route1_cutters` and `route1_wrench`, storing ownership by `userId` in `heldItemId` and 2-slot `inventorySlots`.
  - Added `item:state` lifecycle broadcasts for pickup, store, initial route item hydration, reconnect hydration, capture drops, explicit leave drops, and reconnect-timeout recovery.
  - Added Unity `NetworkRoutePickable` wrapper for route tools; local hand/store visuals now wait for backend confirmation.
  - Updated route tool prefabs: `AdjustableSpanner.prefab` = `route1_wrench`, `Pliers.prefab` = `route1_cutters`, both with stable `NetworkInteractable.networkId`.
  - Preserved legacy local pickable behavior for non-route props while blocking voluntary throw/drop for route tools.
  - Verified backend TypeScript with bundled Node and ran `route-selector.test.ts` successfully.
  - Recorded ADR-007 in `memory/decisions.md`.
- **Unity Specialist — Input System compatibility hotfix**:
  - Fixed `InventoryInput` crash caused by `UnityEngine.Input.GetKeyDown` while the project uses the Input System package.
  - Added `InputSystemKey` helper to preserve existing inspector `KeyCode` fields while reading through `Keyboard.current` / `Mouse.current`.
  - Updated `InventoryInput`, `HeldItemInput`, and legacy `PlayerMovement` to avoid all legacy `UnityEngine.Input` reads.
  - Verified with `rg` that no `Input.GetKey*`, `Input.GetAxis*`, or `Input.GetButton*` calls remain under `Assets/Scripts`.
- **Unity Specialist — Route pickable prompt fix**:
  - Fixed route tool prompts not appearing when `AdjustableSpanner.prefab` / `Pliers.prefab` carry both legacy `PickUpInteractable` and `NetworkRoutePickable`.
  - `InteractionManager` now evaluates all `IInteractable` components on a hit collider instead of only the first one returned by Unity.
  - Added throttled `InteractionManager` detection logs and `NetworkRoutePickable` pickup/state logs for debugging prompt, arrow, and backend confirmation flow.
  - Enabled debug flags on `Prisoner 1.prefab`, `AdjustableSpanner.prefab`, and `Pliers.prefab` for current playtesting.
- **Network Programmer — Route pickup log cleanup + NPC sync payload fix**:
  - Stopped route-tool `item.pickup` from calling legacy `GameManager.onItemPickup`; collecting route progress now waits for the proper route/store flow instead of logging `[INVENTORY]` / `[ESCAPE]` immediately on hand pickup.
  - Routed stale legacy `pickup` actions for critical route tools back through the authoritative route pickup path so older components cannot trigger legacy inventory side effects.
  - Updated `npc:sync_state` socket handling to accept either a JSON string or an already-parsed object payload from Unity/socket.io, fixing repeated `"[object Object]" is not valid JSON` errors.
  - Verified backend TypeScript with bundled Node and reran `route-selector.test.ts` successfully.
- **Unity Specialist — Held pickable Rigidbody fix**:
  - Fixed networked route tools falling from the hand after pickup by keeping held pickables in a dedicated held physics state.
  - `PickableItem.SetHeldVisible()` now enables renderers, disables colliders, zeroes dynamic velocity, sets `isKinematic = true`, keeps gravity checked, and freezes rotation while held.
  - `NetworkRoutePickable.ApplyLocalHeld()` no longer calls world visibility for held tools, preventing `SetWorldVisible(true)` from turning the Rigidbody dynamic after backend `item:state` refreshes.
- **Unity Specialist + Network Programmer — Ruta 1 Phase E implementation**:
  - Added Route 1 mission interactables for guard desks, servers, vent opening, and vent escape under `Assets/Scripts/Interactions/Route1`.
  - `Route1ProgressInteractable` sends backend `player:interact` start/stop actions, renders uGUI progress from `escape:route1:state`, cancels on backend error/capture/state removal, and broadcasts animation start/stop for remote replay.
  - Added `Route1WorldStateController` for public ventilation/vent visual state plus guard-facing cue audio hooks.
  - Added `Route1SceneSetup` runtime fallback anchors for canonical scene IDs `guard_desk_1..4`, `server_1..12`, and `vent_1..3` when authored scene objects are missing.
  - Wired `RemoteInteractionHandler` to replay Route 1 interaction animations on remote avatars.
  - Made existing GameScene route spawn area IDs unique: `workshop_wrench_slot_1/2` and `laundry_cutters_slot_1/2`.
  - Verified Unity C# via `msbuild unity/JAILBREAK/Assembly-CSharp.csproj`: build succeeded with existing warnings only.
  - Recorded ADR-012 in `memory/decisions.md`.
- **Unity Specialist + Network Programmer — Ruta 1 Phase E log/visual cleanup**:
  - Added backend `[ROUTE1]` logs for accepted route interactions plus completion milestones: clue found, server disabled, wrong server, vent opened, and escaped.
  - Gated noisy backend movement logs behind `DEBUG_MOVEMENT=1`.
  - Removed Unity progress-bar frame spam and disabled default debug logging on interaction detection, route item pickup, route item prefabs, route world state, and route scene setup.
  - Disabled Route1SceneSetup debug geometry by default so fallback anchors no longer render visible blocks over desks/worktables.
  - Guarded vent visual toggling when closed/open visuals point at the same GameObject so `VentilationGrille.prefab` no longer disappears before the open visual is authored.
  - Verified backend TypeScript with bundled Node, Route1 backend tests (`36/36`), and Unity C# via `msbuild` with existing warnings only.
  - Recorded ADR-013 in `memory/decisions.md`.
- **Unity Specialist — Ruta 1 grille/tunnel split**:
  - Updated `Route1ProgressInteractable` with `routeObjectId` so a tunnel prefab can use a unique `NetworkInteractable.networkId` for remote replay while sending canonical vent ids to the backend.
  - Updated `VentUnscrewInteractable` to hide the closed grille visual and disable assigned grille colliders once the backend reports that the vent is open.
  - Updated `VentEscapeInteractable` so separate conduct/tunnel prefabs can hide their visual and assigned colliders until their `routeObjectId` appears in `openVentIds`.
  - Removed `VentEscapeInteractable` from `VentilationGrille.prefab`; the grille now owns only the 25s unscrew/open interaction.
  - Updated remote route replay to choose the route component matching the broadcast action.
  - Verified Unity C# via `msbuild unity/JAILBREAK/Assembly-CSharp.csproj`: build succeeded with existing warnings only.
  - Recorded ADR-014 in `memory/decisions.md`.
- **Unity Specialist — Conduct prefab activation fix**:
  - Fixed `VentEscapeInteractable` so a `tunnelVisual` assigned to the script's own root hides renderers instead of disabling the GameObject that must receive route state updates.
  - Updated `Conduct.prefab` defaults to `networkId = vent_1_escape`, `routeObjectId = vent_1`, and disabled both body/trigger colliders until the matching vent is open.
  - Verified Unity C# via `msbuild unity/JAILBREAK/Assembly-CSharp.csproj`: build succeeded with existing warnings only.
- **Unity/Backend Specialist — Conduct world-state robustness**:
  - Made `VentEscapeInteractable` also subscribe to public `world:state` and cached world state so conduct visibility/colliders follow `openVentIds` directly.
  - Made backend `open_vent` completion idempotent for world cue/log emission if a duplicated completion reaches an already-open vent.
  - Verified backend TypeScript and `route1-system.test.ts` (`36/36`), plus Unity C# via `msbuild` with existing warnings only.
- **Unity Specialist + Network Programmer — Ruta 1 Phase F HUD and QA**:
  - Implemented UI Toolkit `GameHudController` for phase/timer, local held item, 2-slot authoritative inventory, and prisoner-only Ruta 1 checklist/progress.
  - Reworked `GameGUI.uxml` from static/mock inventory slots into runtime-bound HUD regions for phase, timer, inventory, and route checklist.
  - Added runtime safety disabling for legacy TMP `InventoryHUD` components while preserving uGUI `InteractionPrompt` and `ProgressBar` for world interactions.
  - Added backend `route-inventory.test.ts` covering pickup, store, pickup race, held/stored reconnect snapshots, and critical tool drops.
  - Added `design/gdd/ruta-1-phase-f-qa.md` with focused automated coverage and manual 2-prisoner/1-guard QA checklist.
  - Verified Unity C# via `msbuild unity/JAILBREAK/Assembly-CSharp.csproj`: build succeeded with existing warnings only.
  - Verified backend TypeScript with bundled Node `tsc`.
  - Verified focused Ruta 1 backend tests (`route-selector`, `spawn-areas`, `route-inventory`, `route1-system`): `62/62` passing.
  - Full backend suite still has pre-existing stale failures in old state/room-manager/socket/game-manager tests around userId migration and NPC count expectations.
  - Recorded ADR-015 in `memory/decisions.md`.
- **Project preference update**:
  - User explicitly said not to use local Ollama/local models for project work and not to ask again.
  - Recorded ADR-016 in `memory/decisions.md`.
- **Unity Specialist + Network Programmer — Ruta 1 HUD warning toasts**:
  - Added a reusable `HudToast` UI Toolkit element to `GameGUI.uxml` and `GameHudController.ShowToast(...)` with timed hiding.
  - Routed backend rejection messages through `Route1ProgressInteractable` so missing `route1_cutters` shows `Necesitas las pinzas` and missing `route1_wrench` shows `Necesitas la llave francesa`.
  - Added local unavailable feedback for blocked vent/escape steps and already-completed route objects.
  - Added empty guard-desk feedback: completed clue searches that leave `clueFound=false` show `No hay nada en este escritorio`.
  - Updated `design/gdd/ruta-1-phase-f-qa.md` manual multiplayer checks for warning toasts.
  - Verified Unity C# via `msbuild unity/JAILBREAK/Assembly-CSharp.csproj`: build succeeded with existing warnings only.
  - Recorded ADR-017 in `memory/decisions.md`.
