# Game Progress

## Status: IMPLEMENTATION (Network / Backend)
## Current Sprint: Fase 1 — State Management Core

## Completed:
- [x] Project scaffold
- [x] Agent studio setup
- [x] React app scaffold
- [x] Node backend scaffold (TypeScript compilation fixed)
- [x] Unity project created
- [x] **Sincronización de Estado — Fase 1**:
  - [x] Types & interfaces (`backend/src/game/types.ts`)
  - [x] State management (`backend/src/game/state.ts`)
  - [x] Validation rules (`backend/src/game/validation.ts`)
  - [x] Room manager + tick loop (`backend/src/game/room-manager.ts`)
  - [x] Socket event handlers (`backend/src/sockets/game.ts`)
  - [x] System integration contracts (`backend/src/game/system-integrations.ts`)
  - [x] Test plan (`design/test-plan.md`)

## Completed (Fase 2):
- [x] **Fase 2**: Socket events & handlers
  - [x] Event handlers (event-handlers.ts)
  - [x] Reconnection system (reconnection.ts)
  - [x] Socket handlers (player:move, player:interact, guard:mark, guard:catch, riot:activate)
  - [x] Event flow documentation (fase-2-event-flow.md)

## Completed (Fase 3):
- [x] **Fase 3**: System integrations (all systems implemented)
  - [x] NPC Behavior: patrol/chase with waypoints
  - [x] Pursuit: chase state machine
  - [x] Disguise: camuflage immunity
  - [x] Penalties: guard errors, riot availability
  - [x] Inventory: 4-slot per player
  - [x] Escape Routes: progress tracking, win condition
  - [x] Phases: active → lockdown → escape → riot
  - [x] Victory Conditions: 3 win paths (catch all, escape, riot)
  - [x] Game Manager: coordinator + tick loop integration
  - [x] Debugging Guide: manual testing, scenarios, perf monitoring

## Test Suite Implemented:
- ✅ **60 unit + integration + performance tests**
  - 13 state.test.ts (addPlayer, removePlayer, spawnNPCs, delta, etc)
  - 10 validation.test.ts (speed, bounds, distance, catch, ownership)
  - 8 room-manager.test.ts (create, getOrCreate, destroy, lifecycle)
  - 6 game-loop.integration.test.ts (tick timing, broadcasts, delta)
  - 10 socket-events.integration.test.ts (move, interact, catch, race conditions)
  - 8 game-manager.test.ts (event callbacks, stats, systems)
  - 5 performance.test.ts (bandwidth, tick precision, memory, CPU)
- ✅ Vitest configured + npm scripts
- ✅ Test README with instructions

## Status:
- ✅ **All code complete & compiling**
- ✅ **Test suite ready to run**
- ✅ **Ready for full QA & debugging**

## Ruta 1 — Phase C (Spawn Areas Backend-Driven):
- [x] Backend: `backend/src/game/systems/spawn-areas.ts` (register / place / respawn + anti-softlock timers)
- [x] Backend: `route:register_spawn_areas` socket handler (host-only)
- [x] Backend: pickup cancels pending respawn timer; capture/leave/disc schedules one
- [x] Backend: room destroy cleans registration + timers
- [x] Backend tests: 12 passing in `spawn-areas.test.ts`
- [x] Unity: `RouteSpawnArea.cs` component (scene marker)
- [x] Unity: `RouteItemRegistry.cs` (scans scene, sends registration, instantiates prefabs on item:state)
- [x] Unity: `NetworkManager.SendRegisterSpawnAreas` + jslib binding + DTOs
- [x] ADR-010 recorded in `memory/decisions.md`

## Ruta 1 — Phase F (UI Toolkit HUD and QA):
- [x] Unity: `GameHudController.cs` binds phase timer, local held item, 2 inventory slots, and prisoner-only Ruta 1 checklist from authoritative network state.
- [x] Unity: `GameGUI.uxml` replaced old static/mock inventory layout with runtime-bound UI Toolkit HUD elements.
- [x] Unity: guard role hides Ruta 1 checklist; uGUI world `InteractionPrompt` / `ProgressBar` remain unchanged.
- [x] Backend tests: `route-inventory.test.ts` covers pickup, store, pickup race, reconnect held/stored, and critical item drops.
- [x] QA doc: `design/gdd/ruta-1-phase-f-qa.md` lists focused automated coverage and manual multiplayer pass.
- [x] Verification: Unity `msbuild Assembly-CSharp.csproj` succeeded; focused Ruta 1 backend tests passed `62/62`.
- [x] HUD feedback: route interactables show short toasts for empty desk searches, missing cutters/wrench, and blocked vent/escape steps.
- [ ] Scene setup: add a `UIDocument` in `GameScene`, assign `GameGUI.uxml`, add `GameHudController`, and assign route tool sprites.

## NPC Routine — Hora libre / Encierro:
- [x] Backend: Hora libre emits multi-zone assignments for patio, celdas, lavanderia, and cocina.
- [x] Backend: Patio free-time actions are simplified to `yard_idle`, `yard_bench_idle`, `yard_exercise`, `yard_shadow_box`, and `yard_lean_wall`.
- [x] Backend: Celdas in Hora libre and Encierro emit `cell_stand_idle` or `cell_sleep` with stable `cell_area_01..08` zones and 20-bed capacity distribution.
- [x] Backend: Lockdown/Encierro duration is 60 seconds.
- [x] Unity: `ZoneRegistry` documents and warns for missing new patio/cell routine zones.
- [x] Backend tests: focused routine tests cover multi-zone free time, cell capacities, and sleep lockdown assignments.
- [x] Verification: backend TypeScript, focused `jail-routine.test.ts` (`9/9`), Unity `msbuild Assembly-CSharp.csproj`, and `git diff --check` passed.
- [ ] Scene setup: add `ZoneRegistry` entries for `yard`, `yard_benches`, `yard_exercise`, and `cell_area_01..08`.

## Backlog:
- [ ] Unit tests (state, validation, room-manager)
- [ ] Integration tests (game-loop, socket-events)
- [ ] Performance tests (bandwidth, CPU, memory)
- [ ] Manual testing (4-client match, lag simulation)
- [ ] Backend → Render deployment
