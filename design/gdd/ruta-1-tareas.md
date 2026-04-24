# Ruta 1 — Tareas Especificas por Fase

> **Status**: Backlog operativo para implementacion
> **Fuente**: `design/gdd/ruta-1-implementation-plan.md`
> **Spec**: `design/gdd/ruta-1-ventilacion-industrial.md`
> **Last Updated**: 2026-04-24

---

## Fase A — Contratos base y selector de ruta

### A-01 — Backend route selector

- **Owner sugerido**: network-programmer
- **Archivos/sistemas**: room state, room manager, game manager, shared types.
- **Setup Unity requerido**: ninguno.
- **Tareas**:
  - Definir `activeRouteId`.
  - Registrar solo `route1_ventilation` en MVP.
  - Emitir `escape:route:selected` al iniciar partida y en reconnect.
  - Inicializar `route1` en room state.
- **Dependencias**: ninguna.
- **Definition of Done**:
  - Toda sala activa tiene `activeRouteId = 'route1_ventilation'`.
  - Cliente reconnect recibe ruta activa antes de renderizar HUD.

### A-02 — Route 1 shared contract

- **Owner sugerido**: network-programmer + unity-specialist
- **Archivos/sistemas**: backend `types.ts`, Unity `NetworkTypes.cs`.
- **Setup Unity requerido**: ninguno.
- **Tareas**:
  - Documentar/crear `Route1State`.
  - Documentar/crear `InventorySlotSync`.
  - Agregar `heldItemId` e `inventorySlots` a player state.
  - Agregar payloads para `escape:route:selected`, `escape:route1:state`, `world:cue`, `world:state`, `item:state`.
- **Dependencias**: A-01.
- **Definition of Done**:
  - Backend y Unity compilan con los nuevos payloads.
  - Los nombres de eventos coinciden con la spec.

---

## Fase B — Inventario autoritativo y pickables

### B-01 — Backend inventory hand/slots

- **Owner sugerido**: network-programmer
- **Archivos/sistemas**: inventory system, player state, event handlers, reconnection.
- **Setup Unity requerido**: ninguno.
- **Tareas**:
  - Re-key inventario por `userId`.
  - Cambiar presos a 2 slots.
  - Agregar `heldItemId` separado de slots.
  - Implementar `item.pickup`.
  - Implementar `item.store`.
  - Persistir mano y slots en reconnect.
  - Bloquear drop/throw voluntario de herramientas criticas en backend.
- **Dependencias**: A-02.
- **Definition of Done**:
  - Pickup con mano vacia pone item en `heldItemId`.
  - Store mueve item held al primer slot libre.
  - F5 conserva mano y slots.

### B-02 — Backend item lifecycle

- **Owner sugerido**: network-programmer
- **Archivos/sistemas**: item state, room state, item broadcasts.
- **Setup Unity requerido**: ninguno.
- **Tareas**:
  - Extender `ItemState`.
  - Emitir `item:state` en spawned/held/stored/dropped/respawning.
  - Resolver race condition de dos pickups.
  - Devolver herramienta al mundo si el preso es capturado.
- **Dependencias**: B-01.
- **Definition of Done**:
  - Solo un jugador puede poseer un item.
  - Los clientes pueden reconstruir estado visual usando `item:state`.

### B-03 — Unity networked pickable wrapper

- **Owner sugerido**: unity-specialist
- **Archivos/sistemas**: `Interactions/Pickable`, `NetworkManager`, `GameStateManager`.
- **Setup Unity requerido**:
  - `AdjustableSpanner.prefab` con `itemId = route1_wrench`.
  - `Pliers.prefab` con `itemId = route1_cutters`.
- **Tareas**:
  - Crear wrapper que reemplaza pickup local puro para herramientas de ruta.
  - En `E`, enviar `item.pickup`.
  - Parentar a la mano solo con confirmacion backend.
  - En `F`, enviar `item.store`.
  - Actualizar `ItemInventory` solo con confirmacion backend.
  - Deshabilitar throw/drop voluntario para herramientas criticas.
- **Dependencias**: A-02, B-01.
- **Definition of Done**:
  - El flujo visible sigue siendo `E` mano, `F` slot.
  - No hay duplicado visual entre clientes.

### B-04 — Player prefab inventory setup

- **Owner sugerido**: unity-specialist
- **Archivos/sistemas**: `Prisoner 1.prefab`, local player init.
- **Setup Unity requerido**:
  - Mantener `HeldItemInput`, `ItemInventory`, `InventoryInput`, `InteractionManager`.
  - Setear slot count de preso a 2.
- **Tareas**:
  - Confirmar que remote players no procesan input local.
  - Conectar store key `F` al flujo autoritativo.
  - Exponer held item para HUD UI Toolkit.
- **Dependencias**: B-03.
- **Definition of Done**:
  - Prefab local puede sostener y guardar tools de ruta con backend como fuente de verdad.

---

## Fase C — Spawn areas backend-driven

### C-01 — Unity spawn area components

- **Owner sugerido**: unity-specialist
- **Archivos/sistemas**: escena principal, nuevo componente de spawn area.
- **Setup Unity requerido**:
  - Crear spawn areas con `spawnAreaId`, `zoneId`, allowed items y posicion.
  - Zonas esperadas: lavanderia/workshop u otras aprobadas por layout.
- **Tareas**:
  - Registrar spawn areas al cargar GameScene.
  - Usar IDs estables, no derivados de hierarchy path.
  - Declarar allowed item IDs por area.
- **Dependencias**: A-02.
- **Definition of Done**:
  - Backend recibe todas las spawn areas antes de activar tools.

### C-02 — Backend spawn selection

- **Owner sugerido**: network-programmer
- **Archivos/sistemas**: room manager, route init, item state.
- **Setup Unity requerido**: spawn areas registradas.
- **Tareas**:
  - Guardar spawn areas por room.
  - Elegir una area valida para `route1_cutters`.
  - Elegir una area valida para `route1_wrench`.
  - Emitir `item:state` con `spawnAreaId` y posicion.
  - Implementar respawn anti-softlock.
- **Dependencias**: C-01, B-02.
- **Definition of Done**:
  - Cada partida activa exactamente una pinza y una llave.
  - Item critico perdido vuelve a un estado recuperable.

### C-03 — Unity item activation by backend state

- **Owner sugerido**: unity-specialist
- **Archivos/sistemas**: item registry, prefab activation, GameStateManager.
- **Setup Unity requerido**:
  - Prefabs de pinza/llave disponibles para instanciar o activar.
- **Tareas**:
  - Resolver prefab por `itemId`.
  - Activar item en `spawnAreaId`.
  - Ocultar item cuando esta held/stored/respawning.
  - Reubicar item cuando esta dropped.
- **Dependencias**: C-02, B-03.
- **Definition of Done**:
  - Todos los clientes ven el mismo item en la misma posicion.

---

## Fase D — Route1System backend

### D-01 — Route1 state initialization

- **Owner sugerido**: network-programmer
- **Archivos/sistemas**: `Route1System`, game manager.
- **Setup Unity requerido**: IDs de escena acordados.
- **Tareas**:
  - Elegir `correctDeskId` entre `guard_desk_1..4`.
  - Elegir `correctServerId` entre `server_1..12`.
  - Inicializar `ventProgressById` para vents registrados o configurados.
  - Inicializar checklist de misiones.
- **Dependencias**: A-01, A-02.
- **Definition of Done**:
  - Cada partida tiene desk/server random independientes.

### D-02 — Desk clue interaction

- **Owner sugerido**: network-programmer
- **Archivos/sistemas**: event handlers, Route1System.
- **Setup Unity requerido**: 4 desks con NetworkInteractable.
- **Tareas**:
  - Implementar `route1.search_clue.start/stop`.
  - Validar prisoner, rango y desk ID.
  - Completar en 3s.
  - Solo desk correcto revela `correctServerId`.
  - Emitir state filtrado a presos.
- **Dependencias**: D-01.
- **Definition of Done**:
  - Presos ven servidor correcto; guardia no.

### D-03 — Server sabotage interaction

- **Owner sugerido**: network-programmer
- **Archivos/sistemas**: event handlers, Route1System, world cues.
- **Setup Unity requerido**: 12 servers con NetworkInteractable.
- **Tareas**:
  - Implementar `route1.disable_server.start/stop`.
  - Validar `route1_cutters` en mano o slot.
  - Completar en 15s.
  - Correcto deshabilita ventilacion.
  - Incorrecto emite alarma/ruido al guardia y permite reintento.
- **Dependencias**: D-01, B-01.
- **Definition of Done**:
  - Solo `server_1..12` son aceptados.
  - Wrong server no bloquea la ruta.

### D-04 — Vent open interaction

- **Owner sugerido**: network-programmer
- **Archivos/sistemas**: Route1System tick, event handlers.
- **Setup Unity requerido**: 2/3 vents con NetworkInteractable.
- **Tareas**:
  - Implementar `route1.open_vent.start/stop`.
  - Validar server disabled.
  - Validar `route1_wrench` en iniciador.
  - Trackear helpers por vent.
  - Progreso por vent: 25s solo, 12s con dos presos en el mismo vent.
  - Emitir `world:state` al abrir vent.
- **Dependencias**: D-03.
- **Definition of Done**:
  - Cada vent progresa independientemente.
  - Segundo preso solo acelera si esta en el mismo vent.

### D-05 — Escape interaction and victory

- **Owner sugerido**: network-programmer
- **Archivos/sistemas**: Route1System, victory, guard catch handler.
- **Setup Unity requerido**: vents abiertos pueden iniciar escape.
- **Tareas**:
  - Implementar `route1.escape.start/stop`.
  - Validar vent abierto.
  - Completar en 5s.
  - Cancelar si el preso es capturado.
  - Emitir `game:end` con `reason = 'escape_route'`.
- **Dependencias**: D-04.
- **Definition of Done**:
  - Escape es vulnerable y termina partida al completarse.

---

## Fase E — Unity route interactables y escena

### E-01 — Guard desk interactable

- **Owner sugerido**: unity-specialist
- **Archivos/sistemas**: `Interactions/Route1`.
- **Setup Unity requerido**:
  - 4 desks: `guard_desk_1..4`.
  - Cada desk con `NetworkInteractable`.
- **Tareas**:
  - Crear interactable con progress bar uGUI de 3s.
  - En start enviar `route1.search_clue.start`.
  - En cancel/leave enviar `route1.search_clue.stop`.
  - No decidir localmente cual desk es correcto.
- **Dependencias**: D-02 contract.
- **Definition of Done**:
  - El feedback local no revela si el desk es correcto antes del backend.

### E-02 — Server sabotage interactable

- **Owner sugerido**: unity-specialist
- **Archivos/sistemas**: `Interactions/Route1`.
- **Setup Unity requerido**:
  - 12 servers: `server_1..12`.
  - Cada server con `NetworkInteractable`.
- **Tareas**:
  - Crear interactable con progress bar uGUI de 15s.
  - En start enviar `route1.disable_server.start`.
  - En cancel/leave enviar `route1.disable_server.stop`.
  - Reaccionar a `world:cue` de alarma si corresponde.
- **Dependencias**: D-03 contract.
- **Definition of Done**:
  - El cliente no decide correct/incorrect; solo renderiza resultado.

### E-03 — Vent unscrew interactable

- **Owner sugerido**: unity-specialist
- **Archivos/sistemas**: `Interactions/Route1`.
- **Setup Unity requerido**:
  - 2/3 vents: `vent_1..3`.
  - Cada vent con `NetworkInteractable`.
- **Tareas**:
  - Crear interactable de abrir conducto.
  - En start enviar `route1.open_vent.start`.
  - En stop enviar `route1.open_vent.stop`.
  - Renderizar progreso desde `escape:route1:state`.
  - Mostrar el mismo progreso a ambos presos.
- **Dependencias**: D-04 contract.
- **Definition of Done**:
  - Barra visible coincide con progreso backend.

### E-04 — Vent escape interactable

- **Owner sugerido**: unity-specialist
- **Archivos/sistemas**: `Interactions/Route1`.
- **Setup Unity requerido**:
  - Escape point en cada vent.
- **Tareas**:
  - Habilitar prompt solo si vent esta abierto segun backend.
  - En start enviar `route1.escape.start`.
  - En cancel/capture enviar o reaccionar a stop.
  - Mostrar progress bar de 5s.
- **Dependencias**: D-05 contract.
- **Definition of Done**:
  - Captura durante escape cancela visualmente la accion.

### E-05 — World feedback controllers

- **Owner sugerido**: unity-shaders-vfx + unity-specialist
- **Archivos/sistemas**: route props, audio, lights.
- **Setup Unity requerido**:
  - Audio de alarma servidor incorrecto.
  - Visual de ventilacion apagada.
  - Visual de conducto abierto.
- **Tareas**:
  - Escuchar `world:cue`.
  - Escuchar `world:state`.
  - Reproducir alarma para guardia cuando corresponda.
  - Actualizar props publicos para todos.
- **Dependencias**: D-03, D-04.
- **Definition of Done**:
  - Guardia recibe senales observables, no datos secretos.

---

## Fase F — HUD UI Toolkit y QA

### F-01 — UI Toolkit inventory HUD

- **Owner sugerido**: unity-ui-toolkit
- **Archivos/sistemas**: `GameGUI.uxml`, new HUD controller.
- **Setup Unity requerido**:
  - UIDocument en escena.
  - Iconos para `route1_cutters` y `route1_wrench`.
- **Tareas**:
  - Renderizar held item.
  - Renderizar 2 slots.
  - Leer `inventorySlots` desde `player:state`.
  - Ocultar inventario de guardia si no aplica.
  - Retirar HUD TMP viejo tras paridad.
- **Dependencias**: B-01, B-03.
- **Definition of Done**:
  - No hay doble HUD de inventario.

### F-02 — Route 1 checklist HUD

- **Owner sugerido**: unity-ui-toolkit
- **Archivos/sistemas**: `GameGUI.uxml`, HUD controller.
- **Setup Unity requerido**: payload `escape:route1:state`.
- **Tareas**:
  - Mostrar checklist:
    - Pinzas
    - Pista: Servidor ?
    - Servidor
    - Llave
    - Conducto
    - Escape
  - Mostrar `Servidor N` solo tras pista.
  - Mostrar progreso de acciones activas.
  - Ocultar checklist al guardia.
- **Dependencias**: D-01 to D-05.
- **Definition of Done**:
  - Presos pueden coordinar Ruta 1 sin ver posiciones ni identidades exactas.

### F-03 — Multiplayer QA

- **Owner sugerido**: QA / producer agent
- **Archivos/sistemas**: test plan/manual checklist.
- **Setup Unity requerido**:
  - 2 presos + 1 guardia.
  - Escena con desks, servers, vents y spawn areas.
- **Tareas**:
  - Validar ruta completa.
  - Validar wrong server alarm.
  - Validar reconnect con held item.
  - Validar reconnect con stored item.
  - Validar captura durante escape.
  - Validar anti-softlock de herramientas.
- **Dependencias**: F-01, F-02, fases B-E.
- **Definition of Done**:
  - Todos los AC de la spec estan cubiertos con evidencia manual o tests automatizados.

### F-04 — Backend automated tests

- **Owner sugerido**: network-programmer
- **Archivos/sistemas**: Vitest backend.
- **Setup Unity requerido**: ninguno.
- **Tareas**:
  - Test route selector.
  - Test desk randomization.
  - Test server randomization.
  - Test item pickup/store/reconnect.
  - Test wrong server cue.
  - Test vent progress solo/co-op.
  - Test escape victory.
- **Dependencias**: D-05.
- **Definition of Done**:
  - Backend tests pasan sin depender de Unity.

---

## Paralelizacion recomendada

1. Primero cerrar Fase A completa.
2. Despues correr en paralelo:
   - Backend: B-01, B-02, C-02, D-01..D-05.
   - Unity gameplay: B-03, B-04, C-01, C-03, E-01..E-05.
   - UI Toolkit: F-01/F-02 cuando payloads esten congelados.
   - QA: preparar F-03 desde que exista escena cableada.

## Riesgos principales

- IDs de escena mal escritos rompen la ruta aunque el backend este correcto.
- Reconnect con item en mano necesita estar definido antes del HUD.
- Si Unity decide correct desk/server localmente, se rompe autoridad.
- Si el guardia recibe `correctServerId`, se rompe la asimetria.
- Si los route tools se pueden tirar voluntariamente, aumenta el riesgo de griefing y softlocks.
