# Sincronización de Estado

> **Status**: Designed
> **Author**: Cris + Claude
> **Last Updated**: 2026-04-06
> **Implements Pillar**: Infraestructura invisible (el juego debe sentirse local aunque sea online)

## Decisión Arquitectónica

**Socket.io + Node.js autoritativo** (evaluado vs. Unity Netcode for GameObjects).
NGO descartado por: ventaja del host en partidas asimétricas, y complejidad de deploy Unity headless en scope de jam.
Node.js corre la simulación de NPCs, valida capturas y administra el timer de fases.
Unity conecta via plugin **SocketIOUnity** (C#).

## Overview

La Sincronización de Estado es la capa de red del juego: gestiona toda la comunicación entre los clientes Unity y el servidor Node.js via Socket.io. El servidor es **autoritativo** — él decide qué es verdad (posiciones de jugadores, estado de NPCs, timer de fases, inventarios, capturas). Los clientes predicen su propio movimiento localmente para evitar latencia percibida, pero el servidor corrige cualquier divergencia. El jugador nunca interactúa con este sistema directamente — su trabajo es volverse invisible. Si funciona bien, nadie lo nota.

## Player Fantasy

Este sistema es **infraestructura que no se nota**. La fantasía del jugador no viene del sistema en sí, sino de su ausencia perceptible: el guardia parece estar realmente en el mundo cuando dobla una esquina detrás tuyo; los NPCs se mueven de forma fluida aunque vengan del servidor; cuando el guardia te atrapa, la captura se siente justa y no "por lag". El sistema falla si el jugador piensa "esto fue lag" — ese pensamiento rompe la inmersión y destruye la tensión central del juego.

## Detailed Design

### Core Rules

1. **Autoridad del servidor** — El servidor Node.js es la única fuente de verdad. Toda acción que afecte el estado del juego (captura, recoger objeto, cambio de fase, activar motín) es validada en el servidor antes de confirmarse a los clientes.
2. **Client prediction** — El movimiento del jugador local se aplica inmediatamente en el cliente sin esperar confirmación del servidor. El servidor recibe el input y actualiza su estado; si hay divergencia, se aplica reconciliación rubber-band.
3. **Tick rate** — El servidor procesa y emite estado a 20 ticks/seg (cada 50ms). Los clientes envían input cada 50ms. Los renders corren a 60 FPS con interpolación entre ticks.
4. **Interpolación de otros jugadores** — Los otros jugadores se renderizan con ~100ms de delay (2 ticks) para tener siempre dos posiciones entre las que interpolar suavemente.
5. **Delta compression de NPCs** — Los NPCs solo envían posición si cambiaron >0.1m desde el último tick. El servidor envía un array con solo los NPCs que se movieron. Los clientes guardan el último estado conocido de cada NPC.
6. **Eventos autoritativos** — Acciones de gameplay (guard:mark, player:interact, riot:activate) se envían al servidor. El servidor valida y emite el resultado a todos. Los clientes NO aplican el resultado localmente hasta recibir confirmación del servidor.
7. **Rooms de Socket.io** — Cada partida vive en su propia room con ID único. Los mensajes solo se emiten a los clientes de esa room. El servidor mantiene el estado completo de la partida en memoria durante la partida.

### States and Transitions (conexión del cliente)

| Estado | Descripción | Transición a |
|--------|-------------|-------------|
| **Disconnected** | Sin conexión al servidor | Connecting (al iniciar lobby) |
| **Connecting** | Handshake Socket.io en progreso | Connected (éxito) / Disconnected (timeout 5s) |
| **Connected** | En sala de lobby, esperando jugadores | InGame (server emite `game:start`) |
| **InGame** | Partida activa, tick loop corriendo | Reconnecting (pérdida conexión) / PostGame (`game:end`) |
| **Reconnecting** | Pérdida de conexión detectada | InGame (reconexión exitosa en <30s) / Disconnected (timeout) |
| **PostGame** | Partida terminada, esperando revancha | Connected (volver al lobby) / Disconnected (salir) |

### Eventos Socket.io (referencia completa)

| Evento | Dirección | Frecuencia | Payload | Validación servidor |
|--------|-----------|-----------|---------|-------------------|
| `player:move` | Cliente → Servidor | Cada 50ms | `{ playerId, position, rotation, velocity, movementState }` | Velocidad máxima, bounds del mapa |
| `player:interact` | Cliente → Servidor | On-demand | `{ playerId, objectId, action }` | Objeto existe, jugador en rango (≤2m), objeto disponible |
| `guard:mark` | Cliente → Servidor | On-demand | `{ guardId, targetId }` | Guardia existe, target existe, no en error cooldown |
| `guard:catch` | Servidor → Todos | On-demand | `{ guardId, targetId, success, isPlayer }` | — |
| `phase:change` | Servidor → Todos | Cada ~90–120s | `{ phase, phaseName, duration, activeZone }` | — |
| `npc:positions` | Servidor → Todos | Cada 200ms | `{ npcs: [{ id, position, rotation, animState }] }` (solo delta) | — |
| `chase:start` | Servidor → Target | On-demand | `{ guardId, targetId }` | — |
| `chase:end` | Servidor → Todos | On-demand | `{ reason: 'caught'|'lost'|'timeout' }` | — |
| `escape:progress` | Servidor → Todos | On-demand | `{ route, itemsCollected, itemsNeeded, completedBy }` | — |
| `game:end` | Servidor → Todos | On-demand | `{ winner: 'prisoners'|'guard', reason }` | — |
| `riot:available` | Servidor → Presos | On-demand | `{ errorsCount: 3 }` | — |
| `riot:activate` | Cliente → Servidor | On-demand | `{ prisonerId }` | `riot_available === true` |
| `player:state` | Servidor → Todos | Cada 50ms | `{ players: [{ id, position, rotation, movementState, role }] }` | — |
| `item:pickup` | Servidor → Todos | On-demand | `{ playerId, itemId, slot }` | Jugador en rango, item disponible, slot libre |
| `item:use` | Servidor → Todos | On-demand | `{ playerId, itemId, targetId }` | Jugador tiene item, uso válido |

### Interactions with Other Systems

| Sistema | Dirección | Qué recibe / provee |
|---------|-----------|-------------------|
| **Movimiento FPS (1)** | ← recibe input, → provee corrección | `player:move` cada 50ms; devuelve posición autoritativa para reconciliación rubber-band |
| **Persecución (2)** | ↔ | Recibe `guard:mark`; emite `chase:start`, `chase:end`, `guard:catch` |
| **Inventario (5)** | ↔ | Recibe `player:interact` (recoger/usar); emite `item:pickup`, `item:use` |
| **Rutas de Escape (6)** | ↔ | Recibe acciones de escape; emite `escape:progress`, `game:end` |
| **Rutina/Fases (4)** | → provee | Emite `phase:change` a todos según timer del servidor |
| **NPC Rutina (13)** | → provee | Emite `npc:positions` (delta) cada 200ms |
| **Penalizaciones (9)** | → provee | Emite conteo de errores del guardia |
| **Motín (10)** | ↔ | Recibe `riot:activate`; emite `riot:available`, `game:end` |
| **Condiciones Victoria (11)** | → provee | Emite `game:end` cuando se cumple una condición |
| **Lobby (17)** | ← recibe | Gestiona creación de rooms, join, asignación de roles pre-partida |
| **Reconexión (19)** | ← recibe | Maneja timeout y re-join a la room activa |

## Formulas

```
// Client prediction & reconciliación
pos_local_t = pos_local_(t-1) + input_velocity × delta_time

// Rubber-band (ejecutado al recibir posición autoritativa del servidor)
diff = |pos_servidor - pos_local|
if diff < reconciliation_threshold (1.0m):
    pos_local += (pos_servidor - pos_local) × reconciliation_lerp_speed (0.3) por frame
else:
    pos_local = pos_servidor  // teleport instantáneo

// Interpolación de otros jugadores (buffer de 2 ticks = 100ms)
pos_render = lerp(pos_buffer[t-2], pos_buffer[t-1], alpha)
donde alpha = (tiempo_actual - timestamp_buffer[t-2]) / tick_interval (50ms)

// Delta compression NPCs
enviar_npc(id) = true si |pos_actual(id) - pos_ultimo_enviado(id)| > npc_delta_threshold (0.1m)

// Bandwidth estimada por partida (worst case, todos moviéndose)
jugadores_out  = 4 × (position[12B] + rotation[12B] + velocity[12B] + state[1B]) × 20/s = ~2.96 KB/s
npc_out        = 20 NPCs × (id[1B] + pos[12B] + rot[4B] + anim[1B]) × 5/s (delta ~25%) = ~0.9 KB/s
eventos        = ~0.1 KB/s (on-demand, despreciable)
Total estimado = ~4 KB/s por cliente (≈32 Kbps) — bien dentro de límites razonables
```

## Edge Cases

| Caso | Qué pasa | Resolución |
|------|----------|------------|
| **Lag spike >500ms** | Client prediction diverge mucho, rubber-band notorio | Si diff ≥ 1m → teleport. Mostrar indicador de conexión pobre en HUD. |
| **Guardia señala en el mismo frame que el preso se camufla** | Race condition: cliente del guardia ve el target, servidor ya lo marcó como camuflado | Servidor valida en su tick: si el target cumple camuflaje ese tick → persecución nunca inicia. No es un error. |
| **Dos jugadores recogen el mismo objeto simultáneamente** | Ambos clientes predicen el pickup, servidor solo asigna uno | Servidor asigna al primero (timestamp del mensaje). El segundo recibe "ítem ya tomado" y su inventario se revierte. |
| **Servidor cae durante la partida** | Todos los clientes pierden conexión simultáneamente | Todos van a Reconnecting. Si el servidor no vuelve en 30s → Disconnected. La partida se pierde. |
| **Jugador envía `player:move` con velocidad imposible** | Velocidad > `walk_speed × sprint_multiplier × 1.5` (umbral anti-cheat) | Servidor ignora el mensaje, usa última posición válida. El cliente es corregido por rubber-band en el siguiente tick. |
| **Room llena, quinto jugador intenta unirse** | Servidor rechaza la conexión | El quinto recibe "sala llena" y vuelve a Disconnected sin entrar al lobby. |
| **`game:end` llega antes que el último `player:state`** | Cliente puede mostrar posiciones viejas en pantalla final | El cliente congela el estado al recibir `game:end`. No procesa más `player:state` después. |
| **Delta de NPCs vacío (ningún NPC se movió)** | Servidor no envía el tick de NPCs ese frame | El cliente mantiene las últimas posiciones interpoladas. El silencio significa que nada cambió. |

## Dependencies

| Sistema | Tipo | Dirección | Interfaz específica |
|---------|------|-----------|-------------------|
| Movimiento FPS (1) | Hard | ↔ | Recibe `{ position, rotation, velocity, movementState }` cada 50ms; devuelve posición autoritativa |
| Persecución (2) | Hard | ↔ | Recibe `guard:mark`; emite `chase:start/end`, `guard:catch` |
| Camuflaje (3) | Hard | → provee | Emite estado de camuflaje del preso al validar `chase:start` |
| Rutina/Fases (4) | Hard | → provee | Emite `phase:change` según timer interno del servidor |
| Inventario (5) | Hard | ↔ | Recibe `player:interact`; emite `item:pickup/use` |
| Rutas de Escape (6) | Hard | ↔ | Recibe acciones de escape; emite `escape:progress`, contribuye a `game:end` |
| Penalizaciones (9) | Hard | → provee | Emite conteo de errores del guardia, `riot:available` |
| Motín (10) | Hard | ↔ | Recibe `riot:activate`; emite `game:end` |
| Condiciones Victoria (11) | Hard | → provee | Evalúa y emite `game:end` |
| NPC Rutina (13) | Hard | → provee | Provee posiciones NPC para emitir `npc:positions` delta |
| Lobby (17) | Hard | ← recibe | Gestiona rooms pre-partida, asignación de roles |
| Reconexión (19) | Soft | ← recibe | Maneja re-join a room activa en <30s |

**Hard** = no funciona sin él. **Soft** = funciona pero con feature reducido.

## Tuning Knobs

| Knob | Default | Rango seguro | Si muy bajo | Si muy alto |
|------|---------|-------------|-------------|-------------|
| `tick_rate` | 20/s (50ms) | 10–30/s | Movimiento entrecortado (>100ms entre updates) | Carga excesiva del servidor |
| `player_send_rate` | 20/s (50ms) | 10–30/s | Input lag percibido alto | Bandwidth desperdiciado |
| `npc_send_rate` | 5/s (200ms) | 3–10/s | NPCs se ven teletransportarse | Bandwidth excesivo |
| `npc_delta_threshold` | 0.1m | 0.05–0.5m | Demasiados NPCs enviados (sin beneficio de delta) | NPCs con posiciones muy desactualizadas |
| `interpolation_buffer` | 100ms (2 ticks) | 50–200ms | Jitter visible en otros jugadores | Delay excesivo en posiciones de otros |
| `reconciliation_threshold` | 1.0m | 0.5–3.0m | Rubber-band visible en movimiento normal | Cheaters pueden moverse libremente |
| `reconciliation_lerp_speed` | 0.3 | 0.1–0.8 | Corrección muy lenta, posición equivocada por mucho tiempo | Corrección brusca, visual de "salto" |
| `anticheat_speed_multiplier` | 1.5x | 1.2–2.0x | Falsos positivos (jugadores legítimos rechazados) | Permite speed hacks obvios |
| `reconnect_timeout` | 30s | 10–60s | Jugadores desconectados por lag temporario | Personajes zombie por demasiado tiempo |

## Visual/Audio Requirements

[To be designed]

## UI Requirements

[To be designed]

## Acceptance Criteria

| # | Criterio | Cómo verificar |
|---|----------|----------------|
| AC-1 | Cuatro clientes se conectan a la misma room y reciben estado inicial | Abrir 4 instancias, todos ven los mismos 20 personajes en las mismas posiciones |
| AC-2 | Movimiento local es inmediato (no hay input lag percibido) | Input → render local < 16ms (1 frame a 60fps) |
| AC-3 | Posiciones de otros jugadores consistentes entre clientes | Jugador A y B ven a jugador C en la misma posición (±0.2m por interpolation buffer) |
| AC-4 | Delta compression de NPCs funciona correctamente | Solo se envían NPCs que se movieron >0.1m. Clientes mantienen estado correcto entre ticks. |
| AC-5 | Rubber-band no visible en condiciones normales (<150ms RTT) | Con ping simulado de 100ms, movimiento del jugador local se ve suave |
| AC-6 | Rubber-band teleporta correctamente en lag extremo | Simular lag spike de 1s → jugador teletransporta a posición correcta |
| AC-7 | `guard:catch` requiere validación de distancia server-side | Intentar captura a 10m → servidor rechaza. A 1.5m → servidor acepta. |
| AC-8 | Dos clientes recogen el mismo ítem simultáneamente: solo uno lo obtiene | Dos clientes interactúan con el mismo ítem en el mismo tick → uno lo obtiene, el otro recibe rechazo |
| AC-9 | Bandwidth ≤ 5 KB/s por cliente en partida normal | Monitorear en partida de 4 jugadores activos + 20 NPCs moviéndose |
| AC-10 | Servidor Node.js ≤ 20% CPU en Render free tier (partida de 4) | Profiler Node.js o métricas de Render durante partida de prueba |

## Open Questions

[To be designed]
