# Ruta 1 — Implementation Plan

> **Status**: Plan — ready for implementation
> **Companion spec**: `design/gdd/ruta-1-ventilacion-industrial.md`
> **GDD section**: `design/GDD.md` §5.2
> **Authors**: unity-specialist + network-programmer
> **Last Updated**: 2026-04-24

---

## 0. Implementation Target

Implement one complete escape route: `route1_ventilation`.

The backend will use a route-selection architecture so future routes can be added without rewriting room state, but MVP registers only Ruta 1. At match start, the backend selects `route1_ventilation`, chooses the random desk/server, chooses item spawn areas, and broadcasts the selected route and initial item states to Unity.

Core decisions:

- Pickables are instant pickup with `E`, then held in hand.
- `F` stores the held item in backend inventory.
- Pickables do not use progress bars.
- Mission interactions use the existing world uGUI prompt/progress bar.
- All configured vents are valid once opened.
- Mission order is flexible; backend gates each action by requirements.
- Prisoner HUD is a UI Toolkit checklist.

---

## Phase A — Base Contracts and Route Selector

**Goal:** make route state explicit and future-proof without implementing future routes.

### Backend

- Add `activeRouteId` to room state with MVP value `'route1_ventilation'`.
- Add `Route1State` to room state.
- Add `RouteRegistry` or equivalent selector that currently returns only Ruta 1.
- Add route config defaults:
  - `guard_desk_count = 4`
  - `server_count = 12`
  - `clue_search_seconds = 3`
  - `server_disable_seconds = 15`
  - `vent_open_seconds_single = 25`
  - `vent_open_seconds_coop = 12`
  - `escape_seconds = 5`
  - `critical_item_respawn_delay = 45`
- Emit `escape:route:selected` when the match starts or when a client reconnects.
- Extend `player:state` contract to include:
  - `heldItemId?: string`
  - `inventorySlots: (InventorySlotSync | null)[]`

### Unity

- Add matching C# payloads for selected route and inventory/held item.
- Cache `activeRouteId` in `GameStateManager`.

### Done

- A room always has `activeRouteId = 'route1_ventilation'`.
- Reconnected clients receive the selected route before route UI renders.
- Types compile on backend and Unity.

---

## Phase B — Authoritative Inventory and Pickables

**Goal:** integrate existing Unity pickables with backend authority while preserving the current hand/store flow.

### Backend

- Replace socket-id inventory ownership with `userId` ownership.
- Change inventory to 2 slots for prisoners.
- Track held item separately from stored slots:
  - `heldItemId`
  - `inventorySlots`
- Extend `ItemState` with:
  - `itemId`
  - `itemType`
  - `state: 'spawned' | 'held' | 'stored' | 'dropped' | 'respawning'`
  - `holderUserId`
  - `spawnAreaId`
  - `position`
- Add `player:interact` actions:
  - `item.pickup`
  - `item.store`
- `item.pickup` validates range, item availability and empty hand.
- `item.store` validates held item and free inventory slot.
- Critical route tools cannot be voluntarily thrown or dropped in MVP.
- Capture/disconnect with a critical tool returns it to the world or triggers respawn.

### Unity

- Keep existing scripts on `Prisoner 1.prefab`:
  - `HeldItemInput`
  - `ItemInventory`
  - `InventoryInput`
  - `InteractionManager`
- Replace local-only `PickUpInteractable` behavior on route tools with a networked wrapper.
- On `E`, send `item.pickup`; parent to hand only after server confirmation.
- On `F`, send `item.store`; update local inventory only after server confirmation.
- Disable throw/drop behavior for `route1_cutters` and `route1_wrench`.
- Keep old world prompt behavior for nearby pickup prompt.

### Done

- A player can pick up pinzas/llave with `E`, hold them, store with `F`, reconnect and recover state.
- UI Toolkit can render authoritative slots.
- Two players racing for one item results in exactly one owner.

---

## Phase C — Backend-Driven Spawn Areas

**Goal:** let backend decide where route tools appear while Unity owns scene placement.

### Backend

- Add spawn area registration payload:
  - `spawnAreaId`
  - `zoneId`
  - `position`
  - `allowedItemIds`
- Store spawn areas in room state.
- At match start, choose one valid spawn area for:
  - `route1_cutters`
  - `route1_wrench`
- Emit `item:state` for each active item.
- Respawn critical tools after invalid position or softlock delay.

### Unity

- Add scene-authored spawn areas in lavanderia/workshop or other allowed zones.
- Register spawn areas with backend after GameScene loads.
- Instantiate or enable the requested pickable prefab based on `item:state`.
- Use stable IDs; no hierarchy-path fallback for route-critical spawns.

### Done

- Backend controls which item appears in which spawn area.
- Unity visual state matches `item:state` for spawned, held, stored, dropped and respawning.
- No permanent tool loss is possible.

---

## Phase D — Route1System Backend

**Goal:** implement the server-authoritative mission state machine.

### Backend

- Create `Route1System`.
- On init:
  - choose `correctDeskId` from `guard_desk_1..4`
  - choose `correctServerId` from `server_1..12`
  - initialize `ventProgressById`
  - initialize mission checklist state
- Add `player:interact` actions:
  - `route1.search_clue.start`
  - `route1.search_clue.stop`
  - `route1.disable_server.start`
  - `route1.disable_server.stop`
  - `route1.open_vent.start`
  - `route1.open_vent.stop`
  - `route1.escape.start`
  - `route1.escape.stop`
- Tick active interactions using `dt`.
- Validate:
  - clue search requires prisoner and valid desk ID
  - server disable requires prisoner, valid server ID and `route1_cutters`
  - open vent requires disabled server and `route1_wrench` on the initiating player
  - helper on vent must be at the same vent
  - escape requires open vent
- Emit:
  - `escape:route1:state` to prisoners
  - `world:cue` to guard for alarms/noise
  - `world:state` to all for public props
  - `game:end` on successful escape

### Done

- One of four desks reveals the server.
- One of twelve servers disables the route.
- Wrong servers alarm but do not block retry.
- Vents progress independently.
- Escape is vulnerable for 5 seconds and wins on completion.

---

## Phase E — Unity Route Interactables and Scene Setup

**Goal:** connect scene objects to backend route actions.

### New route interactables

- `GuardDeskClueInteractable`
  - Uses `NetworkInteractable`.
  - Sends search clue start/stop.
  - Shows 3s uGUI progress.
- `ServerSabotageInteractable`
  - Uses `NetworkInteractable`.
  - Sends disable server start/stop.
  - Shows 15s uGUI progress.
  - Requires pinzas according to backend; local prompt may hint, server decides.
- `VentUnscrewInteractable`
  - Uses `NetworkInteractable`.
  - Sends open vent start/stop.
  - Shows server-driven vent progress.
  - Supports second-player helper on same vent.
- `VentEscapeInteractable`
  - Uses `NetworkInteractable`.
  - Sends escape start/stop.
  - Shows 5s uGUI progress.
- World prop controllers:
  - server alarm audio
  - ventilation/fan off state
  - vent open visual state

### Scene setup

- Oficina del guardia:
  - 4 desks: `guard_desk_1..4`
- Sala de servidores:
  - 12 servers: `server_1..12`
- Ventilation:
  - 2 or 3 vents: `vent_1..3`
- Spawn areas:
  - stable `spawnAreaId`
  - `zoneId`
  - allowed items list

### Prefab setup

- `AdjustableSpanner.prefab`
  - `itemId = route1_wrench`
  - networked pickup wrapper
- `Pliers.prefab`
  - `itemId = route1_cutters`
  - networked pickup wrapper
- `Prisoner 1.prefab`
  - keep existing pickable/inventory/input stack
  - route tool throw disabled
  - inventory slot count set to 2 for prisoner role

### Done

- Scene IDs match backend contract exactly.
- Progress bars are world uGUI, not UI Toolkit.
- Route objects work in multiplayer with backend confirmation.

---

## Phase F — UI Toolkit HUD and QA

**Goal:** replace old inventory HUD and validate full route.

### UI Toolkit

- Add `GameHudController`.
- Bind:
  - phase + timer
  - held item
  - 2-slot inventory
  - Ruta 1 checklist
- Checklist:
  - Pinzas
  - Pista: Servidor ?
  - Servidor
  - Llave
  - Conducto
  - Escape
- Hide prisoner checklist from guard.
- Keep `InteractionPrompt` and `ProgressBar` in uGUI.
- Remove old TMP inventory HUD only after UI Toolkit parity.

### QA scenarios

- 2 prisoners + 1 guard complete full route.
- F5 reconnect with held item.
- F5 reconnect with stored item.
- Wrong server alarm reaches guard.
- Correct server enables vents.
- Vent requires wrench on initiator.
- Two prisoners on same vent reduce duration to 12s.
- Capture during 5s escape cancels.
- Escape completion emits `game:end` with `reason = 'escape_route'`.
- Critical item cannot be lost permanently.

### Done

- All acceptance criteria in `ruta-1-ventilacion-industrial.md` pass manually or by automated backend tests.
- No duplicate inventory HUD is visible.
- Guard receives only observable cues, never the correct server directly.

---

## Implementation Order

1. Phase A contracts.
2. Phase B inventory/pickables.
3. Phase C spawn areas.
4. Phase D backend route system.
5. Phase E Unity interactables and scene setup.
6. Phase F HUD and QA.

Parallelization after Phase A:

- Backend agent: Phases B-D.
- Unity gameplay agent: Phases B, C, E.
- UI Toolkit agent: Phase F HUD after payloads are stable.
- QA/producer: acceptance checklist and multiplayer scenarios.
