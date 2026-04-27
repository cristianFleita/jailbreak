# Architecture Decisions

## ADR-001: Sincronización de Estado — Authoritative Server + Client Prediction

**Status**: Implemented (Fase 1)  
**Date**: 2026-04-06

### Decision

Implement state sync as **authoritative server** with **client-side prediction**:
- Server (Node.js) is single source of truth; validates all actions
- Clients predict movement locally to avoid perceived input lag
- Clients receive `player:state` every 50ms (20 ticks/sec) for authoritativeposition
- Rubber-band reconciliation: if diff > 1m, client teleports; else lerp correction
- Delta compression: NPCs only sent if moved > 0.1m since last broadcast (5 sends/sec)

### Why

- **Fairness**: Server-side catch validation prevents guard exploits (e.g., catching through walls)
- **Low bandwidth**: Delta compression saves ~80% of NPC traffic; 4 KB/s per client
- **Invisible to player**: Tick rate (20/sec) + interpolation buffer (100ms) = smooth movement at <150ms RTT
- **Scalable**: Single server process handles 4 players + 20 NPCs on Render free tier (<20% CPU)

### Implications

- Fase 2 must implement movement controller that reads `player:state` and applies rubber-band
- All authority checks (catch distance, item pickup, phase transitions) must be server-side
- NPC AI loop runs server-side (not delegated to clients)

### Trade-offs Considered & Rejected

1. **Unity Netcode for GameObjects**: Rejected
   - Requires Unity headless server (complex deploy)
   - Host advantage in asymmetric matches
   - Harder to validate guard catches fairly

2. **Peer-to-peer (host authority)**: Rejected
   - Guard is host → prisoners can't verify catches
   - No fallback if host disconnects
   - Incompatible with 1v3 gameplay

---

## (More decisions to be added as they're made...)

## ADR-002: Ruta 1 Ventilacion Industrial — MVP Cooperativo con HUD Compartido

**Status**: Designed
**Date**: 2026-04-23

### Decision

Implementar la primera ruta de escape como una ruta cooperativa de ventilacion con:
- Inventario de presos de 2 slots.
- Pinzas y llave inglesa pesada como herramientas criticas; la llave ocupa 1 slot y aplica -5% velocidad.
- Plano electrico en Oficina del Guardia que revela el fusible correcto a todos los presos, no al guardia.
- Progreso compartido de Ruta 1 en HUD solo para presos.
- Guardia informado solo por senales observables del mundo: ventilador detenido, apagones, rechinido y reja abierta.
- Dos spawns posibles por herramienta critica y respawn backup anti-softlock.
- Segundo preso acelera la rejilla en MVP; reduccion de ruido y QTEs quedan para post-MVP polish.

### Why

La ruta necesita comunicar progreso cooperativo sin depender de voz externa ni revelar informacion injusta al guardia. El HUD compartido mantiene a los presos coordinados; las senales ambientales mantienen la asimetria y le dan al guardia formas justas de deducir que la fuga esta avanzando.

### Implications

- Backend debe modelar estado de ruta por pasos, no solo "items collected".
- Unity HUD de presos necesita un tracker compacto de Ruta 1.
- Eventos de progreso deben filtrar informacion por rol.
- Sistema de inventario debe respetar 2 slots para presos en gameplay.
- Implementacion MVP evita QTEs y reduccion dinamica de ruido para reducir scope de game jam.

---

## ADR-003: Bloque de Celdas + Fase Final — Celdas Visibles y Recuento Jugable

**Status**: Designed
**Date**: 2026-04-23

### Decision

Reemplazar la antigua fase de "Luces apagadas" por una fase breve de **Encierro / Recuento final** con estas reglas:
- El bloque de celdas mantiene 2 pisos, pero las celdas pasan a tener frente de barrotes o puerta abierta para que el catre se vea desde el pasillo.
- La Fase 9 dura 60 segundos y se centra en lectura visual, no en oscuridad total ni patrulla con linterna.
- Durante hora libre, las celdas usan acciones legibles y baratas de producir: sentarse en el catre, leer, mirar al corredor.
- Durante el recuento final, los NPCs vuelven a su celda, se acomodan y quedan visibles como siluetas en el catre.
- Ruta 2 (tunel) usa esta ventana final desde la celda.
- Ruta 3 (dummy) sigue escapando en Cena, pero se valida durante el recuento final.

### Why

La version anterior convertia la fase final en un chequeo binario y restrictivo que rompia el loop central de lectura social. El recuento visible mantiene la fantasia carcelaria, hace que el guardia siga deduciendo hasta el ultimo minuto y reduce complejidad tecnica/artistica para game jam.

### Implications

- Arte y layout deben favorecer lectura desde pasillo y pasarela, no interiores ocultos.
- La iluminacion final pasa a ser tenue y legible, no negra con linterna.
- El guardia no recibe alerta automatica por catres vacios; debe detectarlos visualmente.
- El dummy gana valor real como engaño y el tunel gana una ventana final clara sin agregar un sistema nocturno aparte.
- La implementacion futura debe alinear cualquier configuracion hardcodeada de fases con el nuevo nombre, duracion y comportamiento de Fase 9.

---

## ADR-004: Ruta 1 Fase A — Inventario y Pickup Autoritativos

**Status**: Implementing
**Date**: 2026-04-23

### Decision

La Fase A de Ruta 1 usa inventario autoritativo en servidor, keyed por `userId`, y pickup confirmado por servidor. `player:interact` acepta acciones legacy (`pickup`, `drop`) y nuevas (`item.pickup`, `item.drop`), pero la mutacion real pasa por `InventorySystem`. El servidor emite `item:pickup`, `item:drop` e `item:state`; Unity espera confirmacion antes de parentar el item localmente.

Los spawn points de herramientas criticas son scene-authored en Unity. Al cargar GameScene, Unity registra `{ itemId, itemType, spawnId, position }` con el backend via `item:register-spawns`. Solo el host muta la seleccion inicial; otros clientes usan el mismo evento para pedir el estado actual. El backend elige un spawn activo por item y guarda esa posicion para validar distancia y sincronizar drops/respawns.

### Why

Evita rollback/duplicacion de estado entre clientes, mantiene reconexion viable y permite que pinzas/llave inglesa sean la base segura para Route1System en Fase B.

### Implications

- `PlayerState.inventory` es el espejo broadcast para HUD/reconexion.
- `ItemState.state/holderUserId` reemplaza el uso directo de `isPickedUp/pickedUpBy`, que queda como compatibilidad.
- Drops por captura deben emitirse como estado de item para que Unity reactive el objeto en mundo.

---

## ADR-005: Ruta 1 Fases B-E — Backlog Operativo Paralelizable

**Status**: Designed
**Date**: 2026-04-23

### Decision

Crear `design/gdd/ruta-1-fases-b-e-tareas.md` como backlog operativo desde Fase B en adelante. La Fase A queda como baseline a verificar, y las Fases B-E se dividen en tickets asignables por dominio: backend/network, Unity interactables, UI Toolkit, world cues/VFX y QA.

### Why

Ruta 1 cruza backend, Unity, UI, escena, reconexion y QA. Separar el trabajo en tickets con dependencias y Definition of Done permite paralelizar sin romper el contrato Unity/backend ni duplicar tareas ya avanzadas en Fase A.

### Implications

- B-CONTRACT-01 debe congelar nombres de eventos/payloads antes de paralelizar fuerte.
- Backend puede avanzar `Route1System` mientras Unity prepara payloads e interactables contra el contrato.
- UI Toolkit puede trabajar en HUD con payloads mockeados despues de C-NET-01.
- QA puede usar los IDs de tickets del documento para validar AC-1 a AC-9.
- Unity puede mover visualmente los prefabs, pero cualquier pickup valido requiere que el backend conozca la posicion activa del item.

---

## ADR-006: Ruta 1 Ventilacion — Spec Unica MVP con Selector de Ruta

**Status**: Designed
**Date**: 2026-04-24

### Decision

Reescribir la documentacion de escapes para que el MVP tenga una sola ruta implementable: `route1_ventilation`. El backend conserva arquitectura de selector de rutas, pero por ahora siempre selecciona Ruta 1. Rutas 2+ quedan como `TODO`.

Ruta 1 queda definida con:
- 4 escritorios en oficina (`guard_desk_1..4`), uno elegido por backend para revelar la pista.
- 12 servidores (`server_1..12`), uno elegido por backend para deshabilitar ventilacion.
- 2/3 conductos (`vent_1..3`) todos validos, con progreso propio.
- Pickup instantaneo con `E`, item en mano, store con `F`.
- Pickables sin barra de progreso; misiones con barra uGUI.
- HUD de presos en UI Toolkit con checklist resumida.

### Why

Los documentos anteriores mezclaban fusibles, escritorios/cajones, duraciones y fases incompatibles. La nueva spec elimina ambiguedad para que backend, Unity, UI y QA trabajen contra el mismo contrato.

### Implications

- `design/GDD.md`, `ruta-1-ventilacion-industrial.md`, `ruta-1-implementation-plan.md` y `ruta-1-fases-b-e-tareas.md` deben tratar ADR-006 como fuente vigente.
- ADR-002/ADR-005 quedan historicos y supersedidos para detalles concretos de Ruta 1.
- Implementacion debe priorizar selector de ruta + inventario autoritativo + spawn areas antes del state machine de Ruta 1.

---

## ADR-007: Ruta 1 Fase B — Herramientas Criticas con Mano/Slots Autoritativos

**Status**: Implemented; refined by ADR-028
**Date**: 2026-04-24

### Decision

Implementar Fase B con el backend como fuente de verdad para las herramientas criticas de Ruta 1:
- `item.pickup` mueve `route1_cutters` / `route1_wrench` a `PlayerState.heldItemId`.
- `item.store` mueve el item en mano a `PlayerState.inventorySlots`; ADR-028 refina el contrato para usar el slot seleccionado cuando el cliente manda `slotIndex`.
- Los presos tienen 2 slots; el guardia mantiene inventario vacio.
- Unity solo parenta/guarda visualmente una herramienta despues de recibir `item:state` / `player:state`.
- `route1_cutters` y `route1_wrench` no se pueden tirar directamente desde la mano; ADR-028 permite soltarlas voluntariamente solo desde un slot seleccionado.
- Captura, abandono explicito o expiracion del timeout de reconexion devuelven herramientas criticas al mundo como `dropped`.

### Why

Evita duplicados entre clientes, resuelve carreras de pickup desde el servidor y mantiene la ruta recuperable si un preso desaparece con una herramienta necesaria. La ventana de reconexion conserva F5 sin softlock permanente.

### Implications

- Prefabs de herramientas de ruta deben tener `NetworkInteractable.networkId` igual al `itemId` estable.
- `AdjustableSpanner.prefab` representa `route1_wrench`; `Pliers.prefab` representa `route1_cutters`.
- El wrapper Unity `NetworkRoutePickable` reemplaza el pickup local puro solo para herramientas de ruta; props legacy siguen usando `PickUpInteractable`.
- Hasta Fase C, el backend puede omitir el rango si el item aun tiene posicion placeholder; cuando existan spawn areas, la validacion de distancia usa la posicion autoritativa.

---

## ADR-008: Ruta 1 — Separar Pickup en Mano de Progreso Legacy

**Status**: Implemented; refined by ADR-028
**Date**: 2026-04-24

### Decision

`item.pickup` para herramientas criticas de Ruta 1 solo actualiza la mano autoritativa y emite `item:state`. No debe llamar `GameManager.onItemPickup` ni alimentar el inventario/progreso legacy. Si un cliente viejo manda `pickup` para una herramienta critica, el backend lo redirige al flujo autoritativo. El progreso de escape debe avanzar en los pasos propios de Ruta 1, no en el pickup visual de la herramienta.

El socket `npc:sync_state` acepta payload JSON string o payload object para tolerar diferencias entre clientes Unity/socket.io.

### Why

Evita logs y side effects duplicados (`[PICKUP]`, `[INVENTORY]`, `[ESCAPE]`) al tomar una herramienta, y elimina errores de parsing cuando Unity ya envia objetos parseados.

### Implications

- En pickup de `route1_wrench` / `route1_cutters` el log esperado es solo `[PICKUP] ... to hand`.
- En store el log esperado sigue siendo `[STORE] ... stored ... in slot N`.
- Si aparece `[ESCAPE] collected item route1_*` durante pickup, hay una llamada legacy nueva que debe removerse.

---

## ADR-009: Pickables — Estado Fisico Separado para Mano vs Mundo

**Status**: Implemented
**Date**: 2026-04-24

### Decision

Los pickables tienen un estado fisico explicito para "en mano": renderers visibles, colliders apagados, `Rigidbody.isKinematic = true`, gravity activada y rotacion congelada. El estado "visible en mundo" sigue siendo separado y puede activar fisica dinamica.

### Why

Un item de Ruta 1 puede recibir varios `item:state` mientras ya esta parentado a la mano. Usar visibilidad de mundo para refrescar un item held reactivaba fisica dinamica y el objeto caia al piso aunque siguiera bajo el hueso de la mano en la jerarquia.

### Implications

- `NetworkRoutePickable` debe usar el estado held, no `SetWorldVisible(true)`, cuando el holder local coincide.
- Los items en mano deben verse como el inspector esperado: kinematic activo, gravity activa, rotacion congelada.

---

## ADR-010: Ruta 1 Fase C — Spawn Areas Backend-Driven

**Status**: Implemented
**Date**: 2026-04-24

### Decision

La ubicacion inicial de `route1_cutters` y `route1_wrench` es decidida por el backend a partir de spawn areas authored en Unity.

- Unity marca cada spawn point con el MonoBehaviour `RouteSpawnArea` (`spawnAreaId`, `zoneId`, `allowedItemIds[]`, posicion).
- Al iniciar la partida (cuando el backend emite `escape:route:selected`), el host escanea los `RouteSpawnArea` de la escena y envia `route:register_spawn_areas` con todos los candidatos validos. Los demas clientes no envian nada.
- El backend valida cada entrada, bloquea la registracion por sala (primer envio valido gana), elige una spawn area por item critico, guarda `spawnAreaId` + `position` en `ItemState` y emite `item:state` a todos.
- `RouteItemRegistry` instancia el prefab correspondiente cuando recibe un `item:state` con posicion autoritativa; adopta instancias pre-colocadas con el mismo `itemId` para permitir hot-reload.
- Anti-softlock: si un preso desconecta/expira o es capturado con una herramienta critica, el backend dropea el item y agenda `scheduleCriticalItemRespawn` a 45s. Pickup valido cancela el timer.

### Why

Evita que la escena dicte posiciones fijas por sesion, mantiene el contrato de item:state como unica fuente de verdad, y garantiza recuperabilidad de la ruta incluso si una herramienta queda inaccesible.

### Implications

- Todos los clientes deben poder resolver el prefab por `itemId` a traves de `RouteItemRegistry.itemPrefabs`; escenas sin registry o con prefabs no mapeados no renderizan las herramientas.
- `route:register_spawn_areas` es host-only. Clientes no-host ignoran el envio.
- La validacion de distancia de pickup (en el backend) ahora usa la posicion autoritativa una vez placed; antes del placement el item permanece en estado `spawned` con posicion placeholder y no debe ser pickable.
- Un re-envio por el mismo host (por ejemplo, tras reload de escena) es no-op mientras la sala siga locked.

---

## ADR-011: Ruta 1 Fase D — Route1System backend autoritativo

**Status**: Implemented
**Date**: 2026-04-24

### Decision

Toda la logica de misiones de Ruta 1 vive en `Route1System` (`backend/src/game/systems/route1-system.ts`), construido por `GameManager` y ticked desde el game loop. El cliente Unity solo dispara start/stop por `player:interact`; el backend valida gates, avanza progreso por dt y resuelve completion.

- Acciones soportadas: `route1.search_clue.start/stop`, `route1.disable_server.start/stop`, `route1.open_vent.start/stop`, `route1.escape.start/stop`.
- Validacion: rol prisoner + alive, IDs en pools canonicos (`GUARD_DESK_IDS`, `SERVER_IDS`, `VENT_IDS`), gate de inventario por `playerHasItem` (mano o slot).
- Un jugador solo puede tener una interaccion activa a la vez; un nuevo `start` reemplaza la anterior. Excepcion: helpers en `open_vent` se anaden al `helperUserIds` del initiator.
- `open_vent` persiste su progreso en `ventProgressById[ventId]` aunque el initiator detenga; un restart resume desde el progreso guardado. El initiator necesita llave francesa; los helpers no.
- Tasa de progreso de `open_vent`: solo = `1 / ventOpenSecondsSingle`, con >=1 helper = `1 / ventOpenSecondsCoop`.
- `escape` usa key `route1.escape:${ventId}:${userId}`; multiples presos pueden escapar por el mismo conducto en paralelo.
- Captura del guardia llama a `cancelPlayerInteractions(userId)` y elimina al preso de `escapingPlayerIds`. Tambien se cancela en disconnect/leave (active game) y al expirar la ventana de reconnect.
- Mision checklist (`find_cutters`, `find_clue`, `disable_server`, `find_wrench`, `open_vent`, `escape`) se recomputa desde inventario + flags despues de cada cambio (`notifyInventoryChanged()` desde event-handlers).
- Broadcasts: `escape:route1:state` solo a sockets prisoner (filtra `correctServerId` salvo cuando `clueFound`); `world:state` publico (`ventilationPowered`, `openVentIds`); `world:cue` solo a guardia (`server_wrong_alarm`, `server_correct_power_off`, `vent_opened`). Wired como callbacks en `room-manager.startGameLoop`.
- Reconnect re-emite `escape:route1:state` (a presos) y `world:state` (a todos) usando `Route1System.buildPrisonerStatePayload()` / `buildWorldStatePayload()`.
- Victory: `VictoryConditionSystem` chequea `state.route1.escapedPlayerIds.length > 0` ANTES de cualquier otra condicion y emite `game:end { winner: 'prisoners', reason: 'escape_route' }`.

### Why

- Mantiene autoridad pura: client envia intencion, server resuelve. Distancia/rango es delegada a Unity (igual que `guard:catch`) porque el backend no tiene posicion del prop.
- Persistir `ventProgressById` desacopla el estado del conducto del jugador concreto que lo trabaja, permitiendo que un nuevo initiator retome la misma barra y que el HUD muestre progreso compartido.
- Filtrar `correctServerId` en el payload prisoner-only evita que el guardia pueda inferir el servidor leyendo trafico de socket o re-emitidos en reconnect.
- Cancelar interacciones en captura/disconnect cierra el caso "escape se cancela si el preso es capturado durante la trepada de 5s" sin un check separado en el guard catch path.

### Implications

- Cualquier nuevo input de Ruta 1 debe pasar por `Route1System` para que la checklist + broadcasts queden coherentes.
- Cambios en inventario que afectan el gate (`find_cutters`, `find_wrench`, `disable_server`, `open_vent`) deben llamar a `notifyInventoryChanged()`. Hoy lo hacen pickup, store, drop por captura, drop por leave y drop por expiracion de reconnect.
- Las posiciones de cue (`world:cue.position`) hoy van vacias; cuando Unity registre props en el backend (futuro) se podra rellenar.
- 36 tests nuevos en `__tests__/route1-system.test.ts` cubren AC-5..AC-13. AC-1..AC-4 ya estaban cubiertos por route-selector.test.ts y spawn-areas.test.ts.

---

## ADR-012: Ruta 1 Fase E — Unity route interactables autoritativos

**Status**: Implemented
**Date**: 2026-04-24

### Decision

Unity implementa las interacciones de Ruta 1 con una base comun `Route1ProgressInteractable` y cuatro componentes concretos: `GuardDeskClueInteractable`, `ServerSabotageInteractable`, `VentUnscrewInteractable` y `VentEscapeInteractable`.

- El cliente envia solo intencion por `player:interact` usando los action IDs de backend (`route1.*.start/stop`).
- La barra uGUI se hidrata desde `escape:route1:state.activeInteractions[].progress`; para conductos, el inicio local arranca desde `ventProgress`.
- El feedback local no decide desk correcto, server correcto ni gates de inventario; si backend rechaza, `game:error` cancela la animacion/barra local.
- El mismo action ID se re-emite por `player:action` para que `RemoteInteractionHandler` pueda reproducir start/stop de animacion en avatares remotos.
- `Route1WorldStateController` escucha `world:state` / `world:cue` para ventilacion apagada, vent visual abierto y audio/logs de alarmas.
- `Route1SceneSetup` crea anchors runtime de fallback con IDs canonicos (`guard_desk_1..4`, `server_1..12`, `vent_1..3`) si la escena aun no tiene `NetworkInteractable` authored para esos IDs.
- Los spawn areas existentes de `GameScene` mantienen sus posiciones, pero sus `spawnAreaId` fueron desduplicados.

### Why

Mantiene el contrato de autoridad de Phase D: Unity no resuelve progreso ni resultado, solo muestra feedback inmediato y sincroniza contra snapshots de backend. La base comun reduce drift entre los cuatro tipos de interaccion y permite que la escena actual sea jugable aunque los props definitivos aun no esten authored a mano.

### Implications

- Los placeholders runtime son una red de seguridad para el jam; cuando level design coloque props definitivos, basta con agregar `NetworkInteractable.networkId` + el interactable correspondiente y `Route1SceneSetup` saltara ese ID.
- Las barras siguen siendo uGUI y usan el `ProgressBar` existente; Phase F puede construir HUD UI Toolkit sin tocar estos prompts/progress bars.
- El guardia sigue sin recibir `escape:route1:state`; su feedback visible/audible llega solo por `world:cue` y `world:state`.

---

## ADR-013: Ruta 1 Fase E — logging and authored visual defaults

**Status**: Implemented
**Date**: 2026-04-24

### Decision

- Route 1 completion milestones log on the backend with `[ROUTE1]` tags: clue found, wrong desk complete, correct server disabled, wrong server alarm, vent opened, and escape.
- Route 1 accepted start/stop interactions also log from `event-handlers.ts`; rejected actions keep warning output and return immediately.
- Movement receive/spawn-grace logs are gated behind `DEBUG_MOVEMENT=1`.
- Unity Route 1 debug logs stay opt-in (`debugLogs = false`), route item pickup debug logs default to false, `InteractionManager.debugDetection` defaults to false, and the per-frame `ProgressAction` animator log was removed.
- `Route1SceneSetup.createDebugVisuals` defaults to false. Runtime fallback anchors remain collider-only unless explicitly enabled for debugging.
- Vent visual code keeps the visual active if `closedVisual` and `openVisual` accidentally reference the same GameObject; authored prefabs should still use separate closed/open visuals when available.

### Why

Playtests need backend signal for Route 1 progress without burying it under movement and progress-bar spam. Runtime fallback anchors are useful for QA but should never create visible blocks in the actual scene unless a developer intentionally enables debug geometry.

### Implications

- To inspect low-level movement, start the backend with `DEBUG_MOVEMENT=1`.
- If level design needs visible fallback route anchors temporarily, enable `Route1SceneSetup.createDebugVisuals` in the scene, then disable it before playtest builds.
- Vent prefabs can safely ship with only a closed visual while the open mesh/animation is still being authored.

---

## ADR-014: Ruta 1 Fase E — separate grille and tunnel interactables

**Status**: Implemented
**Date**: 2026-04-24

### Decision

Vent opening and vent escape are authored as separate Unity interactables.

- `VentilationGrille.prefab` owns only `VentUnscrewInteractable`; it no longer carries `VentEscapeInteractable`.
- When a vent opens, `VentUnscrewInteractable` hides the closed grille visual and can disable assigned grille colliders. No open-grille animation or open visual is required.
- `VentEscapeInteractable` can be placed on a separate conduct/tunnel prefab. It can keep its own visual/colliders hidden until the backend reports that the matching vent id is open.
- `VentEscapeInteractable` listens to both prisoner route snapshots and public `world:state.openVentIds`, so tunnel visibility does not depend on receiving the private route payload at the exact moment the vent opens.
- If `VentEscapeInteractable.tunnelVisual` points at the same root GameObject as the script, it hides the renderers instead of disabling the root. The listener GameObject must stay active so it can receive the later route state update.
- `Route1ProgressInteractable` now supports `routeObjectId`: the Unity `NetworkInteractable.networkId` can be unique for remote visual replay (for example `vent_1_escape`), while backend route actions still target the canonical object id (`vent_1`).
- `RemoteInteractionHandler` chooses the route handler that supports the incoming action, which keeps remote replay correct when multiple route components exist near the same route id.
- Backend vent-open completion is idempotent for cues/logs: once a vent is already in `openVentIds`, repeated completion paths keep progress saved but do not re-emit `VENT_OPENED`.

### Why

The grille is an obstacle-removal object, while the conduct tunnel is the actual escape entry. Splitting them lets each prefab own its own progress bar, collider size, prompt, and visuals without fighting over a single `NetworkInteractable` registry id.

### Implications

- Conduct/tunnel prefabs should stay active in the scene; hide their visual/collider via `VentEscapeInteractable` fields instead of disabling the whole GameObject.
- For a tunnel associated with `vent_1`, use a unique visual/network id such as `vent_1_escape` and set `routeObjectId = vent_1`.
- The grille scene instance can keep `NetworkInteractable.networkId = vent_1` and leave `routeObjectId` empty.
- `Conduct.prefab` defaults to `networkId = vent_1_escape`, `routeObjectId = vent_1`, and disables both its body collider and trigger collider until `vent_1` is open.

---

## ADR-015: Ruta 1 Phase F — UI Toolkit HUD reads authoritative state only

**Status**: Implemented
**Date**: 2026-04-25

### Decision

Phase F implements the in-game HUD with UI Toolkit through `GameHudController` and `GameGUI.uxml`.

- The HUD reads phase data from `phase:start`, local role/inventory from `GameStateManager`, and Ruta 1 checklist/progress from prisoner-only `escape:route1:state`.
- The guard never renders the Ruta 1 checklist, and the HUD does not read or infer `correctServerId` outside the backend-filtered prisoner payload.
- Held item and 2 inventory slots are rendered separately from authoritative `heldItemId` / `inventorySlots`.
- uGUI `InteractionPrompt` and `ProgressBar` remain the world interaction UI for route actions.
- Legacy TMP `InventoryHUD` is disabled at runtime by `GameHudController` as a safety net, but the scene should remove old TMP HUD objects once UI Toolkit parity is confirmed.
- Backend Phase F QA adds `route-inventory.test.ts` for pickup/store/reconnect/drop regression coverage and documents manual multiplayer scenarios in `design/gdd/ruta-1-phase-f-qa.md`.

### Why

The inventory/checklist HUD must coordinate prisoners without giving the guard hidden route information. Keeping the HUD read-only with respect to network state avoids client authority drift and makes F5 reconnect behavior match the backend snapshot exactly.

### Implications

- `GameScene` must contain a `UIDocument` using `Assets/UI/Screens/GameGUI.uxml` and a `GameHudController` with route tool sprites assigned.
- If new route tools are added, `GameHudController.IconForItem` / display-name mapping should be extended or replaced by a data asset.
- Full route QA should use the focused route test set plus the manual 2-prisoner/1-guard checklist until stale legacy backend tests are updated for current userId/NPC-count contracts.

---

## ADR-016: Do not delegate project work to local Ollama models

**Status**: Accepted
**Date**: 2026-04-25

### Decision

Do not use local Ollama models for this project, including testing, documentation, comments, reviews, or implementation support.

### Why

User explicitly rejected local model usage for the project and asked not to request it again.

### Implications

- Ignore the older AGENTS.md instruction that suggested `ollama run gemma3:4b` for testing/documentation delegation.
- Keep testing, documentation, and review work inside Codex plus normal local project tools.

---

## ADR-017: Ruta 1 player warnings use the HUD toast layer

**Status**: Implemented
**Date**: 2026-04-25

### Decision

Short player feedback for Ruta 1 interactions is rendered by `GameHudController.ShowToast(...)`.

- Route interactables do not create their own UI objects.
- `Route1ProgressInteractable` translates backend rejection messages for missing route tools into player-facing text.
- Concrete interactables can provide local unavailable messages, such as vent prerequisites.
- Guard desk clue searches show an empty-desk toast only when the local search completes and the authoritative state still has no clue found.
- All HUD labels, mission text, item names, and toasts are player-facing English strings.

### Why

The warning layer should be reusable and tied to the existing UI Toolkit HUD. Keeping backend validation authoritative avoids duplicating tool ownership rules on the client.

### Implications

- `GameScene` needs the Phase F `UIDocument`/`GameHudController` setup for warnings to appear.
- Future route warnings should prefer `ShowRouteFeedback(...)` from route interactables instead of adding scene-specific labels.

---

## ADR-018: Hora libre y encierro usan solo rutina de celda

**Status**: Superseded by ADR-019, then ADR-021 for current implementation
**Date**: 2026-04-25

### Decision

Fases 4, 7 y 9 usan un pool único de celda para NPCs:

- `cell_stand_idle`: el NPC permanece parado dentro de una celda.
- `cell_sleep`: el NPC duerme en la cama/catre de una celda.
- El backend asigna `cellAreaId` (`cell_area_01..20`) y la acción, pero no coordenadas ni waypoints exactos.
- Unity resuelve localmente el destino con `CellAreaRegistry`: cama para dormir, `standing_spot` disponible para estar parado.
- Patio, comedor y lavandería quedan fuera de la rutina NPC de Hora libre en el MVP; sus waypoints de patio quedan como referencia post-MVP.

### Why

El comportamiento de Hora libre y Encierro debe ser simple, legible y consistente con el bloque de celdas. Pasar de waypoints exactos a áreas por celda permite que backend conserve autoridad de intención sin acoplarse al layout fino de Unity.

### Implications

- `phase:start` y `npc:reassign` para fases 4, 7 y 9 deben incluir `cellAreaId` y `cellTargetKind` en lugar de `waypointId`.
- Unity debe configurar 20 `cell_area_XX` con `bedAnchor` y al menos un `standingSpot`.
- Tests de rutina deben validar que esas fases solo emiten `cell_stand_idle` o `cell_sleep`.
- Documentación actualizada en `design/GDD.md` y `design/gdd/rutina-fases-npc.md`.

## ADR-019: Hora libre usa patio seed-driven y Encierro usa idle de celda

**Status**: Superseded by ADR-021
**Date**: 2026-04-25

### Decision

Implementar el primer corte de rutina NPC con estas reglas:

- Hora libre usa solo acciones simples de patio. El backend emite `zoneId`, `seed`, `animTrigger` y duración por NPC.
- Las zonas Unity requeridas para patio son `yard`, `yard_benches` y `yard_exercise`.
- Encierro usa solo `cell_stand_idle`. El backend asigna una zona de celda estable `cell_area_01..08`, más `seed` e `idle`.
- Las zonas Unity requeridas para celdas son `cell_area_01` hasta `cell_area_08`.
- Acostarse/dormir en cama queda diferido para una iteración posterior.

### Why

La implementación actual aprovecha el contrato ya existente `zoneId + seed`, mantiene al backend como autoridad de intención y deja a Unity resolver puntos exactos por `ZoneRegistry`. Esto evita crear ahora un registry especializado de camas antes de necesitar la interacción de acostarse.

### Implications

- `ZoneRegistry` debe tener las 11 zonas requeridas y avisar en runtime si falta alguna.
- Los tests de rutina validan que Hora libre no emite `actionSequence` ni social pairing, y que Encierro solo emite `cell_stand_idle`.
- ADR-018 queda como diseño descartado para Hora libre; su parte de celda se conserva solo como futuro para `cell_sleep`.

## ADR-020: Ocho áreas de celda con veinte camas

**Status**: Implemented
**Date**: 2026-04-26

### Decision

El bloque de celdas se modela como 8 áreas físicas, no 20 celdas individuales:

- Planta baja: `cell_area_01..04`, con capacidades `2,3,2,3` camas.
- Primer piso: `cell_area_05..08`, con capacidades `2,3,2,3` camas.
- Total: 10 camas abajo + 10 camas arriba = 20 camas.
- El backend asigna NPCs a `cell_area_01..08` respetando esas capacidades.

### Why

La escena real tiene 8 celdas grandes compartidas. Modelar 20 áreas falsas acoplaría backend y Unity a una geometría inexistente, y haría más difícil resolver camas múltiples por celda.

### Implications

- `ZoneRegistry` debe requerir `cell_area_01..08` para Hora libre y Encierro.
- Los tests de rutina validan capacidad por celda, no unicidad de celda por NPC.
- Dormir/acostarse se resuelve en Unity con una cama disponible dentro de la celda asignada (ver ADR-023).

## ADR-021: Hora libre multi-zona con patio simplificado y sleep en celdas

**Status**: Implemented
**Date**: 2026-04-26

### Decision

Hora libre vuelve a ser una fase multi-zona. Los NPCs pueden ir a:

- Patio, usando solo `yard_idle`, `yard_bench_idle`, `yard_exercise`, `yard_shadow_box`, `yard_lean_wall`.
- Celdas, usando `cell_stand_idle` o `cell_sleep` con `cell_area_01..08`.
- Lavandería, usando el flujo secuencial de ropa personal.
- Cocina/comedor, usando `free_cafe_sit_talk`, `free_cafe_sit_idle`, `free_cafe_stand_chat`.

Encierro también puede emitir `cell_stand_idle` o `cell_sleep`. `cell_sleep` viaja por red con `animTrigger = sleep`; Unity resuelve la cama/catre dentro del `cell_area_XX` (ver ADR-023).

### Why

Hora libre debe mantener variedad y tráfico orgánico entre zonas. El cambio real es simplificar el patio a acciones seed-driven por zona, no eliminar cocina, lavandería ni celdas.

### Implications

- El backend mantiene `SUBZONE_CHANGE_PROB = 0.25` para que los NPCs puedan cambiar de sub-zona durante Hora libre.
- Unity debe configurar los nuevos `ZoneRegistry` entries de patio y las 8 áreas de celda: `yard`, `yard_benches`, `yard_exercise`, `cell_area_01..08`.
- Los tests de rutina validan que Hora libre cubre las cuatro sub-zonas y que Encierro puede emitir sleep.

## ADR-022: Encierro dura 60 segundos

**Status**: Implemented
**Date**: 2026-04-26

### Decision

La fase final de backend `phase: 8, name: 'Lockdown'` dura 60 segundos.

### Why

El recuento final debe ser una ventana breve de presión y cierre, no una fase larga de espera.

### Implications

- `phase:start` de Lockdown emite `duration = 60`.
- Al expirar esos 60 segundos, la rutina queda completa y no vuelve a Fase 1.
- Los tests de rutina fijan explícitamente la duración de Lockdown en 60 segundos.

## ADR-023: NPC sleep uses bed interactables inside cell areas

**Status**: Implemented
**Date**: 2026-04-26

### Decision

`cell_sleep` no se resuelve como animación directa. Unity busca un `SleepInteractable` libre dentro del `cell_area_XX`, lo reserva, navega hasta un punto caminable cercano al `SleepAction.sleepPoint` y al llegar ejecuta `SleepInteraction`.

### Why

El comportamiento debe seguir el mismo patrón que mesas, gabinetes y lavandería: el backend envía zona, seed y acción; Unity resuelve el interactable concreto localmente y evita que varios NPCs usen el mismo recurso.

### Implications

- Cada cama/catre debe tener `SleepInteractable` y `SleepAction.sleepPoint` dentro de los bounds de su `cell_area_XX`.
- Si no hay cama disponible, Unity no reproduce una animación falsa de sueño en medio de la celda; cae a idle.
- Al terminar, reasignar o destruir el NPC, se libera la reserva de la cama.

## ADR-024: Lobby room browser usa payload envuelto y broadcasts de disponibilidad

**Status**: Implemented
**Date**: 2026-04-26

### Decision

El browser de salas del lobby usa un contrato nuevo `RoomListPayload` con raíz `{ rooms: [...] }`.

- El backend mantiene el `room:list` legacy como array para no romper el wrapper React existente.
- Unity consume `room:list:response` para refresh manual y `room:list:update` para cambios live.
- La lista pública incluye solo salas joinables: `status === 'lobby'` y `playerCount < maxPlayers`.
- El backend emite `room:list:update` cuando una sala se crea, alguien se une, alguien es kickeado, el host inicia partida, un jugador deja el lobby o la sala se destruye.
- Unity mantiene el flujo en `LobbyScene`, agregando un panel plegable dentro de `LobbyScreen.uxml` en vez de crear una escena nueva.

### Why

Unity `JsonUtility` no deserializa arrays en la raíz de forma cómoda, y el cliente web ya dependía del evento legacy. Separar respuesta legacy y payload envuelto permite agregar el browser sin romper contratos existentes.

### Implications

- Cualquier cliente nuevo debe preferir `room:list:response` / `room:list:update`.
- La lista no muestra partidas activas ni salas llenas; el join por ID sigue existiendo para compatibilidad y casos directos.
- Cambios futuros de metadata visible de sala deben extender `RoomListEntryPayload`, no `RoomStatePayload`.

## ADR-025: Route tool voluntary drop uses `item.throw`

**Status**: Implemented
**Date**: 2026-04-26

### Decision

Las herramientas críticas de Ruta 1 se pueden tirar voluntariamente solo mediante la acción nueva `item.throw`, y solo si ya están guardadas en un slot de inventario.

- El backend valida que el item sea crítico y que el jugador lo tenga guardado en un slot.
- `item.throw` usa `dropInventoryItem(...)`, limpia el slot, marca el `ItemState` como `dropped`, emite `item:state`, cancela interacciones activas de Ruta 1 para ese jugador y recalcula misiones.
- El drop voluntario queda en el mundo en la posición calculada frente al jugador para que otro preso pueda recogerlo.
- No se agenda respawn automático para `item.throw`; los respawns anti-softlock siguen reservados para captura, leave/disconnect o timers existentes.
- La acción legacy `drop` sigue rechazando herramientas críticas para no saltearse el contrato autoritativo de hand/slots.
- Unity usa `HeldItemInput.dropKey = G` para pedir `NetworkRoutePickable.RequestThrow()` solo desde la herramienta de ruta guardada en el slot seleccionado.

### Why

Separar `item.throw` de `drop` permite sumar transferencia voluntaria de herramientas sin reabrir el flujo legacy de inventario, que no actualiza correctamente misiones, HUD, reconexión ni `item:state`. Exigir que la herramienta esté en un slot evita que el input de drop compita con el estado visual de "item en mano" y hace explícita la intención del jugador: primero guardar, luego soltar.

### Implications

- Los prefabs de herramientas de ruta deben conservar `NetworkRoutePickable.itemId` igual al item autoritativo.
- El HUD se actualiza por `player:state` y `item:state`; Unity no mueve localmente la herramienta hasta confirmación del servidor.
- El slot seleccionado se documenta en ADR-028; cualquier cliente nuevo debe mandar `slotIndex` para `item.store` y solo emitir `item.throw` desde slots.

## ADR-026: Match voice uses diegetic proximity audio

**Status**: Designed
**Date**: 2026-04-27

### Decision

La voz en partida sera diegetica, espacial y audible por proximidad para todos los roles. No habra canal privado global entre presos en el modo default. El guardia puede escuchar presos si esta dentro del rango audible, pero no recibe nombre, marcador, subtitulo ni posicion exacta del hablante. La tecla default de push-to-talk sera `V`. Las senales de golpe/tos pasan a post-MVP.

### Why

La comunicacion debe reforzar los pilares de tension asimetrica, engano social y cooperacion bajo presion. Si los presos tienen voz privada global, Ruta 1 se coordina sin riesgo y el guardia pierde una capa de deduccion. Si el guardia escucha todo globalmente, se vuelve omnisciente y castiga demasiado la fantasia de infiltracion.

### Implications

- La voz debe transportarse por WebRTC, un SDK de voz compatible o una capa equivalente; Socket.io solo debe usarse para signaling/control, no para audio crudo.
- Unity/WebGL debe exponer poses de jugadores a la capa de audio para atenuacion espacial.
- Capturados no pueden hablar con presos vivos durante la partida.
- Lobby/post-game pueden usar voz global no espacial para comodidad social.
- El plan de implementacion vive en `design/gdd/voz-diegetica-proximidad.md`.

## ADR-027: Prisoner inventory controls use K/L navigation and G drop

**Status**: Implemented
**Date**: 2026-04-27

### Decision

El jugador preso navega sus slots de inventario con `K` y `L`, y tira voluntariamente herramientas de Ruta 1 con `G`.

- `InventoryInput.previousKey` usa `K`.
- `InventoryInput.nextKey` usa `L`.
- `HeldItemInput.dropKey` usa `G`.
- El prefab `Prisoner.prefab` serializa esos defaults para que la escena no dependa solo de valores de script.

### Why

Las flechas no estaban alineadas con el control deseado para alternar slots, y el drop de herramientas criticas necesitaba quedar cableado en el prefab real del jugador.

### Implications

- Nuevos prefabs de jugador que copien el comportamiento del preso deben incluir `HeldItemInput`, `ItemInventory` e `InventoryInput` con estos bindings.
- El backend sigue siendo autoritativo: `G` solo solicita `item.throw`; la posicion final se confirma por `item:state`.

## ADR-028: Route inventory store/drop uses the selected slot

**Status**: Implemented
**Date**: 2026-04-27

### Decision

El inventario de herramientas de Ruta 1 separa navegacion, guardado y drop por slot.

- `K` y `L` solo cambian el slot seleccionado; no equipan, no guardan y no hacen swap.
- `F` guarda la herramienta en mano en el slot seleccionado.
- `G` no tira herramientas desde la mano; solo tira la herramienta del slot seleccionado.
- Unity envia `slotIndex` en `player:interact` cuando usa `item.store`.
- El backend respeta `slotIndex` si viene presente, y conserva fallback al primer slot libre para clientes viejos.

### Why

El jugador debe poder decidir explicitamente donde guardar una herramienta y desde que slot tirarla, sin que la navegacion de slots tenga efectos secundarios de inventario.

### Implications

- El HUD debe reflejar el slot seleccionado localmente, aunque el contenido del inventario siga viniendo del estado autoritativo del backend.
- Cualquier cliente nuevo que implemente `item.store` deberia mandar `slotIndex`.
- `item.throw` sigue identificando el item por `itemId`; el backend rechaza el throw si ese item no esta almacenado en un slot.

## ADR-029: Pre-match tutorial is a 60s dedicated scene after Start Game

**Status**: Designed
**Date**: 2026-04-27

### Decision

Despues de que el host presiona Start Game, la sala no entra directamente a la partida principal. El estado pasa a una antesala/tutorial de 60 segundos y Unity carga una escena dedicada de tutorial. En esa escena, los jugadores ya tienen rol asignado y ven misiones especificas por rol: presos practican rutina, inventario e interacciones de Ruta 1; guardia practica observacion, captura por foco y el costo de acusar mal.

### Why

El tutorial debe ensenar el juego desde la fantasia central: escapar siguiendo un plan mientras se imita la rutina de NPCs. Al ubicarlo despues de Start Game, todos los jugadores llegan con contexto claro, roles definidos y un timer corto que mantiene ritmo de partida. La escena dedicada permite practicar interacciones sin contaminar inventario, estadisticas, errores del guardia o progreso real de escape.

### Implications

- Backend necesita un estado transitorio de tutorial/antesala con timer autoritativo de 60s.
- Unity debe cargar una escena distinta para tutorial y luego cargar la escena principal de gameplay.
- El tutorial debe resetear inventario, errores, capturas y progreso antes de iniciar la partida real.
- La UI de tutorial debe ser role-specific y no debe revelar informacion de Ruta 1 al guardia.
- TAB queda recomendado como toggle de misiones para presos; el uso de TAB del guardia debe resolverse por contexto si convive con camaras.
