# Ruta 1 — Phase F QA Checklist

> Status: Ready for manual multiplayer pass
> Scope: UI Toolkit HUD, authoritative inventory display, prisoner-only route checklist, backend route regressions.

## Automated Coverage

Run from `backend/`:

```bash
npm test -- src/game/__tests__/route-selector.test.ts src/game/__tests__/spawn-areas.test.ts src/game/__tests__/route-inventory.test.ts src/game/__tests__/route1-system.test.ts
```

Note: the full backend suite currently has older non-route tests that still expect the pre-userId socket contract and old NPC-count behavior. Use the focused command above for Ruta 1 Phase F evidence until those legacy tests are updated.

Focused files:

- `src/game/__tests__/route-selector.test.ts`
- `src/game/__tests__/spawn-areas.test.ts`
- `src/game/__tests__/route-inventory.test.ts`
- `src/game/__tests__/route1-system.test.ts`

Coverage map:

| Scenario | Automated evidence |
|---|---|
| Route selector emits only `route1_ventilation` | `route-selector.test.ts` |
| Desk/server randomization | `route-selector.test.ts` |
| Item spawn areas and anti-softlock respawn | `spawn-areas.test.ts` |
| Pickup to hand, store to slot, pickup race | `route-inventory.test.ts` |
| Reconnect with held/stored item snapshot | `route-inventory.test.ts` |
| Wrong server guard cue | `route1-system.test.ts` |
| Correct server unlocks vents | `route1-system.test.ts` |
| Vent requires wrench on initiator | `route1-system.test.ts` |
| Two prisoners reduce vent time to 12s | `route1-system.test.ts` |
| Capture/disconnect cancels escape interaction | `route1-system.test.ts` |
| Escape victory reason `escape_route` | `route1-system.test.ts` |

## Manual Multiplayer Pass

Use 3 clients: 2 prisoners + 1 guard.

1. Start a room and confirm prisoners see `ROUTE 1: VENTILATION`; guard does not.
2. Confirm prisoner HUD shows held item separately from the 2 inventory slots.
3. Pick up `route1_cutters` with `E`; confirm held item updates after backend state.
4. Store with `F`; confirm slot 1 updates and held item clears.
5. Refresh client with cutters held; confirm held item restores.
6. Refresh client with cutters stored; confirm slot restores.
7. Search a wrong desk; confirm the prisoner sees `Nothing useful here`.
8. Search desks until clue is found; confirm prisoners see `Server N`; guard does not.
9. Try sabotaging a server without `route1_cutters`; confirm the prisoner sees `You need the cutters`.
10. Sabotage wrong server; confirm guard receives only alarm/cue feedback.
11. Sabotage correct server; confirm ventilation world state changes and vents become usable.
12. Try opening a vent without wrench; confirm the prisoner sees `You need the wrench`.
13. Start opening with wrench holder; second prisoner joins same vent; confirm progress is faster than solo.
14. Complete vent open; confirm route checklist marks `Vent`.
15. Start 5s escape and have guard capture prisoner; confirm escape cancels.
16. Complete 5s escape; confirm `game:end` winner prisoners, reason `escape_route`.

## Unity Scene Setup Required

- Add a `UIDocument` to `GameScene` using `Assets/UI/Screens/GameGUI.uxml`.
- Add `GameHudController` to the same GameObject as the `UIDocument`.
- Assign sprites:
  - `cuttersIcon` -> `Assets/Sprites/Pliers.png`
  - `wrenchIcon` -> `Assets/Sprites/AdjustableSpanner.png`
  - `unknownItemIcon` optional fallback
- Keep uGUI `InteractionPrompt` and `ProgressBar` objects active; Phase F HUD does not replace world prompts.
- Remove or disable old TMP `InventoryHUD` objects after confirming the UI Toolkit HUD is present. `GameHudController` also disables legacy `InventoryHUD` components at runtime as a safety net.
