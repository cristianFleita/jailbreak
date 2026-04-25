# Ruta 1: Ventilacion

> **Status**: Designed for Implementation
> **Author**: Cris + Codex
> **Last Updated**: 2026-04-24
> **Implements Pillar**: Cooperacion bajo presion

## Overview

Ruta 1 es la unica ruta de escape implementable para el MVP. El backend selecciona una ruta activa al iniciar la partida; por ahora esa seleccion siempre resuelve a `route1_ventilation`. A partir de esa seleccion, el backend decide donde aparecen las herramientas criticas, que escritorio contiene la pista, cual de los 12 servidores deshabilita el ventilador y que progreso tienen los conductos.

Los presos deben encontrar pinzas, infiltrarse en la oficina del guardia para descubrir el servidor correcto, sabotearlo en la sala de servidores, conseguir una llave francesa, abrir un conducto de ventilacion y completar una trepada final vulnerable. El guardia no recibe HUD de progreso de ruta: solo puede leer senales del mundo como alarmas, ruido, ventilador apagado y conductos abiertos.

## Player Fantasy

Los presos deben sentirse como un equipo improvisado que arma una fuga en silencio, con pequenas tareas que pueden repartirse y ejecutarse en cualquier orden razonable. El guardia debe sentir que esta leyendo una prision viva: herramientas que desaparecen, alarmas por errores, ruido de metal y una ventilacion que deja de sonar.

## Core Rules

### Ruta activa

- El backend tiene arquitectura de selector de rutas.
- En MVP solo existe `route1_ventilation`.
- Rutas futuras quedan como `TODO`; no deben bloquear ni modificar esta ruta.
- Al iniciar partida, el backend inicializa `Route1State`, elige `correctDeskId`, `correctServerId` y activa los spawns de herramientas.

### Misiones

Las misiones se muestran como checklist resumida para presos, pero el backend no fuerza una secuencia lineal. Los gates tecnicos validan si una accion puede completarse.

| Mission ID | Nombre HUD | Condicion de completitud |
|---|---|---|
| `find_cutters` | Buscar pinzas | `route1_cutters` esta en mano o inventario de cualquier preso |
| `find_clue` | Buscar pista | Se completo la interaccion en el escritorio correcto |
| `disable_server` | Deshabilitar servidor | Se completo sabotaje del servidor correcto |
| `find_wrench` | Buscar llave francesa | `route1_wrench` esta en mano o inventario de cualquier preso |
| `open_vent` | Abrir ventilacion | Al menos un conducto llego a 100% |
| `escape` | Escapar | Un preso completa la trepada final |

### Herramientas criticas

| Item ID | Nombre | Uso | Regla |
|---|---|---|---|
| `route1_cutters` | Pinzas | Sabotear servidor | Deben estar en mano o slot de inventario |
| `route1_wrench` | Llave francesa | Iniciar apertura de conducto | Debe estar en mano o slot del jugador que inicia |

- Los pickables se agarran instantaneamente con `E`.
- Al agarrar un pickable queda en la mano.
- Con `F` se guarda en el primer slot disponible.
- Los presos tienen 2 slots.
- Las herramientas criticas no se dropean ni se tiran voluntariamente en MVP.
- Si un preso es capturado o desconecta con una herramienta critica, el backend la devuelve al mundo o la respawnea para evitar softlocks.
- El backend persiste `heldItemId` e `inventorySlots`; al refrescar con F5 se reconstruye mano + inventario.

### Spawn areas

- Unity coloca spawn areas de ruta con `spawnAreaId`, `zoneId`, posicion y lista de items permitidos.
- El backend decide que spawn area activa cada herramienta al iniciar la partida.
- Para Ruta 1 se activa una copia de `route1_cutters` y una copia de `route1_wrench`.
- Ejemplo de zonas esperadas: lavanderia y workshop.
- El cliente Unity instancia o habilita el prefab indicado por `item:state`.

### Oficina del guardia

- Hay 4 escritorios con IDs estables:
  - `guard_desk_1`
  - `guard_desk_2`
  - `guard_desk_3`
  - `guard_desk_4`
- El backend elige `correctDeskId` al iniciar la partida.
- Cada escritorio usa una interaccion con barra de progreso uGUI.
- Duracion: 3 segundos.
- Solo el escritorio correcto completa `find_clue`.
- Al encontrar la pista, los presos reciben el `correctServerId`; el guardia no recibe esa informacion por HUD.

### Sala de servidores

- Hay 12 servidores con IDs estables:
  - `server_1` a `server_12`
- El backend elige `correctServerId` al iniciar la partida.
- Sabotear un servidor requiere `route1_cutters`.
- La interaccion usa barra de progreso uGUI.
- Duracion: 15 segundos.
- Si el servidor es correcto:
  - `serverDisabled = true`
  - `ventilationPowered = false`
  - se habilita abrir cualquier conducto configurado
- Si el servidor es incorrecto:
  - no bloquea la ruta
  - se registra intento fallido
  - se emite alarma/ruido como `world:cue` para el guardia
  - los presos pueden reintentar otro servidor

### Conductos de ventilacion

- La escena tiene 2 o 3 conductos configurados.
- IDs esperados: `vent_1`, `vent_2`, `vent_3` si existen tres.
- Todos los conductos configurados son validos; no hay conducto correcto secreto.
- Cada conducto tiene progreso propio.
- Abrir un conducto requiere:
  - servidor correcto deshabilitado
  - `route1_wrench` en mano o slot del preso que inicia la interaccion
- La interaccion usa barra de progreso uGUI.
- Duracion con 1 preso: 25 segundos.
- Duracion con 2 presos en el mismo conducto: 12 segundos efectivos.
- El segundo preso puede ayudar sin llave, pero debe interactuar con el mismo conducto.
- Al llegar a 100%, el conducto queda abierto y habilita escape final desde ese conducto.

### Escape final

- Requiere un conducto abierto.
- La interaccion usa barra de progreso uGUI.
- Duracion: 5 segundos.
- Durante esos 5 segundos el preso sigue capturable.
- Si el guardia captura al preso antes de completar, el escape se cancela.
- Al completar, el backend marca al preso como escapado y emite `game:end` con `winner = 'prisoners'` y `reason = 'escape_route'`.

## State Model

```ts
type ActiveRouteId = 'route1_ventilation'

type Route1MissionId =
  | 'find_cutters'
  | 'find_clue'
  | 'disable_server'
  | 'find_wrench'
  | 'open_vent'
  | 'escape'

type Route1State = {
  routeId: 'route1_ventilation'
  correctDeskId: 'guard_desk_1' | 'guard_desk_2' | 'guard_desk_3' | 'guard_desk_4'
  correctServerId: `server_${1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12}`
  clueFound: boolean
  serverDisabled: boolean
  wrongServerAttempts: string[]
  ventProgressById: Record<string, number>
  openVentIds: string[]
  activeInteractions: Record<string, {
    objectId: string
    action: string
    startedByUserId: string
    helperUserIds: string[]
    progress: number
  }>
  escapingPlayerIds: string[]
  escapedPlayerIds: string[]
  missions: Record<Route1MissionId, 'locked' | 'available' | 'in_progress' | 'complete'>
  updatedAt: number
}
```

El payload enviado a presos puede incluir `correctServerId` solo si `clueFound = true`. El guardia nunca recibe ese campo por canales de HUD.

## Inventory and Item Model

```ts
type InventorySlotSync = {
  itemId: 'route1_cutters' | 'route1_wrench' | string
  itemType: 'route_tool' | string
  iconId?: string
}

type ItemState = {
  itemId: string
  itemType: string
  state: 'spawned' | 'held' | 'stored' | 'dropped' | 'respawning'
  holderUserId?: string
  spawnAreaId?: string
  position: { x: number; y: number; z: number }
}

type PlayerState = {
  heldItemId?: string
  inventorySlots: (InventorySlotSync | null)[]
}
```

Las herramientas cuentan como disponibles para una accion si el backend las ve en `heldItemId` o en `inventorySlots`.

## Networking Events

### Client -> Server

Todas las acciones usan `player:interact` para mantener un unico canal de input.

| Action | Object ID | Uso |
|---|---|---|
| `item.pickup` | `route1_cutters` / `route1_wrench` | Pedir pickup autoritativo y poner item en mano |
| `item.store` | item ID | Guardar item held en slot de inventario |
| `route1.search_clue.start` | `guard_desk_1..4` | Empezar busqueda de pista |
| `route1.search_clue.stop` | `guard_desk_1..4` | Cancelar busqueda |
| `route1.disable_server.start` | `server_1..12` | Empezar sabotaje |
| `route1.disable_server.stop` | `server_1..12` | Cancelar sabotaje |
| `route1.open_vent.start` | `vent_1..3` | Empezar o ayudar a abrir conducto |
| `route1.open_vent.stop` | `vent_1..3` | Dejar de abrir conducto |
| `route1.escape.start` | `vent_1..3` | Empezar escape final |
| `route1.escape.stop` | `vent_1..3` | Cancelar escape final |

### Server -> Clients

| Event | Audiencia | Uso |
|---|---|---|
| `escape:route:selected` | Todos | Informar `activeRouteId` |
| `escape:route1:state` | Presos | Checklist, progreso, pista encontrada |
| `world:cue` | Guardia | Alarma, ruido, metal, conducto abierto |
| `world:state` | Todos | Props publicos: ventilacion, conductos abiertos |
| `item:state` | Todos | Spawn, held, stored, dropped, respawn |
| `player:state` | Todos / filtrado normal | Incluye `heldItemId` e `inventorySlots` |
| `game:end` | Todos | Victoria por escape |

## HUD Requirements

El HUD de presos se implementa en UI Toolkit. El prompt contextual y la barra de progreso del mundo se mantienen en uGUI.

Checklist resumida:

```text
Ruta 1: Ventilacion
[ ] Pinzas
[ ] Pista: Servidor ?
[ ] Servidor
[ ] Llave
[ ] Conducto
[ ] Escape
```

Reglas:

- Mostrar icono del item en el slot cuando el backend confirme `inventorySlots`.
- Mostrar item en mano como estado local/autoritative separado del slot.
- Si `clueFound = true`, mostrar `Servidor N`.
- Mostrar progreso solo en pasos con duracion: pista, servidor, conducto, escape.
- No mostrar al guardia la checklist ni el servidor correcto.

## Unity Setup Requirements

### Player prefab

`unity/JAILBREAK/Assets/Prefabs/Characters/Prisoner 1.prefab` mantiene:

- `HeldItemInput`
- `ItemInventory`
- `InventoryInput`
- `InteractionManager`

Ajustes esperados:

- `ItemInventory` queda en 2 slots para presos.
- `F` guarda el item held mediante evento autoritativo `item.store`.
- Throw/drop voluntario se deshabilita para herramientas criticas de ruta.

### Pickable prefabs

- `unity/JAILBREAK/Assets/Prefabs/Props/AdjustableSpanner.prefab`
  - `itemId = route1_wrench`
  - representa la llave francesa
- `unity/JAILBREAK/Assets/Prefabs/Props/Pliers.prefab`
  - `itemId = route1_cutters`
  - representa las pinzas

Ambos reemplazan pickup local puro por wrapper networked que espera confirmacion del backend.

### Scene objects

- 4 escritorios: `guard_desk_1..4`
- 12 servidores: `server_1..12`
- 2 o 3 conductos: `vent_1..3`
- Spawn areas con IDs estables, `zoneId`, posicion y allowed items.
- Todos los objetos de mision usan `NetworkInteractable` para ID estable.

## Edge Cases

| Caso | Resolucion |
|---|---|
| Preso desconecta con item en mano | Backend conserva `heldItemId` para F5 o devuelve item si abandona definitivamente |
| Preso desconecta con item guardado | Backend conserva `inventorySlots` |
| Preso capturado con herramienta critica | Herramienta vuelve al mundo o respawnea segun anti-softlock |
| Dos presos agarran el mismo item | Backend confirma al primero; el segundo recibe error |
| Dos presos buscan escritorios distintos | Solo el escritorio correcto revela pista |
| Servidor incorrecto | Alarma para guardia, ruta sigue disponible |
| Dos presos abren conductos distintos | Cada conducto progresa por separado; solo cuenta cooperacion si estan en el mismo conducto |
| Preso inicia escape y es capturado | Escape se cancela, captura normal |
| Se abre un conducto y nadie escapa | Conducto queda abierto hasta fin de partida |

## Tuning Knobs

| Knob | Default | Nota |
|---|---:|---|
| `guard_desk_count` | 4 | Escritorios en oficina |
| `server_count` | 12 | Servidores en sala electrica |
| `vent_count` | 2-3 | Configurado por escena |
| `clue_search_seconds` | 3 | Barra de escritorio |
| `server_disable_seconds` | 15 | Barra de sabotaje |
| `vent_open_seconds_single` | 25 | Un preso |
| `vent_open_seconds_coop` | 12 | Dos presos en mismo conducto |
| `escape_seconds` | 5 | Ventana final vulnerable |
| `critical_item_respawn_delay` | 45 | Anti-softlock |

## Acceptance Criteria

| # | Criterio | Verificacion |
|---|---|---|
| AC-1 | Backend selecciona `route1_ventilation` al iniciar partida | `escape:route:selected` recibido por clientes |
| AC-2 | Backend activa una pinza y una llave en spawn areas validas | `item:state` para ambos items |
| AC-3 | Pickup con `E` pone item en mano, `F` lo guarda en slot | UI Toolkit refleja mano y slot tras confirmacion |
| AC-4 | Reconexion F5 conserva mano e inventario | Reconnect con item held/stored |
| AC-5 | Solo 1 de 4 escritorios revela la pista | Interactuar todos los desks en test |
| AC-6 | La pista revela `correctServerId` a presos y no al guardia | Comparar payloads prisoner/guard |
| AC-7 | Solo 1 de 12 servidores deshabilita ventilacion | Correcto avanza, incorrecto alarma |
| AC-8 | Sabotaje requiere pinzas | Intento sin item falla |
| AC-9 | Abrir conducto requiere servidor deshabilitado y llave en iniciador | Intentos antes/despues, con/sin item |
| AC-10 | Dos presos en el mismo conducto reducen duracion a 12s | Test multiplayer con timestamps |
| AC-11 | Todos los conductos abiertos son validos para escapar | Probar `vent_1..3` configurados |
| AC-12 | Escape dura 5s y puede cancelarse por captura | Captura durante trepada cancela |
| AC-13 | Completar escape termina partida | `game:end` con `reason = 'escape_route'` |
