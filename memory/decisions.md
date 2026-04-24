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
- La Fase 9 dura 90 segundos y se centra en lectura visual, no en oscuridad total ni patrulla con linterna.
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
