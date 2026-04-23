# Ruta 1: Ventilacion Industrial

> **Status**: Designed for Implementation
> **Author**: Cris + Codex
> **Last Updated**: 2026-04-23
> **Implements Pillar**: Cooperacion bajo presion

## Overview

La Ruta 1 permite a los presos escapar por el conducto de ventilacion del Taller. Para completarla deben leer un plano electrico en la Oficina del Guardia, usar pinzas para cortar el fusible correcto en la Sala de Electricidad, usar una llave inglesa pesada para abrir la rejilla del Taller y trepar al conducto. El servidor es autoritativo: valida objetos, distancia, progreso, fusible correcto y escape final.

## Player Fantasy

Los presos deben sentirse como un equipo improvisado armando una fuga en silencio: uno roba informacion, otro consigue herramientas, otro se expone trabajando la rejilla mientras todos miran de reojo al guardia. El guardia debe sentir que puede leer el mundo, no una barra magica: ventilador detenido, herramientas faltantes, apagones incorrectos y una rejilla cada vez mas sospechosa.

## MVP Scope

- Mantener inventario de presos en **2 slots**.
- La llave inglesa pesada ocupa 1 slot y aplica **-5% velocidad** al portador.
- El segundo preso en la rejilla acelera el progreso, pero **no reduce ruido en MVP**.
- Implementar 2 spawns posibles por herramienta critica, con 1 copia activa por partida.
- Implementar HUD compartido solo para presos.
- Implementar senales ambientales para el guardia, sin HUD de progreso de ruta.

## Core Rules

### Objetos Criticos

| Item ID sugerido | Nombre | Uso | Spawns | Reglas |
|---|---|---|---|---|
| `route1_cutters` | Pinzas/cizallas | Cortar fusibles | Taller principal + Taller backup | Ocupa 1 slot |
| `route1_wrench` | Llave inglesa pesada | Desatornillar rejilla | Electricidad principal + Electricidad backup | Ocupa 1 slot, -5% velocidad |

Solo existe 1 copia activa de cada herramienta por partida. Al iniciar la partida, el servidor elige el spawn principal o backup. Si la herramienta queda en el suelo mas de `critical_item_respawn_delay` o fuera de bounds, el servidor la mueve al spawn backup disponible.

### Plano Electrico

- El plano esta en 1 archivador/cajon aleatorio de la Oficina del Guardia.
- Interaccion: 1s.
- Resultado autoritativo: el servidor marca `planKnown = true` y revela `correctFuseIndex` a todos los presos.
- El guardia no recibe evento de HUD por esta accion.

### Fusibles

- Hay 4 fusibles: `1`, `2`, `3`, `4`.
- `correctFuseIndex` se randomiza al crear la room.
- Cortar cualquier fusible requiere tener `route1_cutters`, estar a rango de interaccion y completar 15s netos.
- Si el fusible es correcto: `fanPowered = false`.
- Si el fusible es incorrecto: se dispara un apagado breve en una zona no critica y el guardia recibe una senal ambiental/alerta suave.
- La herramienta no se consume.

### Rejilla

- Requiere `fanPowered = false`.
- Requiere que el jugador principal tenga `route1_wrench`.
- Duracion base: 25s netos.
- Si hay un segundo preso interactuando en la rejilla, multiplicador de progreso: `2.0x`, equivalente a ~12.5s.
- Si nadie interactua, el progreso decae a `grate_decay_rate`.
- Al llegar a 100%, `grateOpen = true` y se habilita el conducto.

### Escape Final

- Requiere `grateOpen = true`.
- Interaccion en el hueco: 4s ininterrumpibles.
- Durante esos 4s, el preso sigue capturable por el guardia si esta en rango y linea de vision.
- Al completar la animacion, el servidor marca al preso como escapado y dispara condicion de victoria de presos segun la regla global del GDD.

## State Machine

```text
LOCKED
  - planKnown=false, fanPowered=true, grateOpen=false
  -> PLAN_KNOWN cuando un preso lee el plano

PLAN_KNOWN
  - correctFuseIndex visible para presos
  -> FAN_DISABLED cuando se corta el fusible correcto

FAN_DISABLED
  - rejilla puede progresar con llave inglesa
  -> GRATE_OPEN cuando grateProgress >= 1

GRATE_OPEN
  - conducto interactuable
  -> ESCAPING cuando un preso inicia trepada

ESCAPING
  - escapeTimer por preso
  -> ESCAPED cuando escapeTimer >= 4s
```

El estado puede avanzar aunque se saltee la pista del plano: si un preso corta el fusible correcto por azar, `fanPowered` pasa a `false`. El plano sigue siendo la ruta segura porque reduce intentos incorrectos y alertas.

## Data Model

```ts
type Route1State = {
  routeId: 'ventilation_industrial'
  planKnown: boolean
  correctFuseIndex?: 1 | 2 | 3 | 4 // solo se envia a presos si planKnown=true
  fanPowered: boolean
  wrongFuseAttempts: number
  grateProgress: number // 0..1
  grateOpen: boolean
  activeWorkers: {
    wrenchPlayerId?: string
    supportPlayerIds: string[]
  }
  escapingPlayerIds: string[]
  escapedPlayerIds: string[]
  criticalItems: Record<string, {
    itemId: string
    holderPlayerId?: string
    worldPosition?: { x: number; y: number; z: number }
    spawnId: string
    state: 'spawned' | 'held' | 'dropped'
  }>
  updatedAt: number
}
```

## Networking Events

### Client -> Server

Preferir reutilizar `player:interact` para no multiplicar eventos de input. El servidor traduce `objectId + action` a logica de Ruta 1.

| Evento | Payload | Validacion |
|---|---|---|
| `player:interact` | `{ objectId, action: 'route1.read_plan' }` | Preso, objeto correcto, rango <=2m, no capturado |
| `player:interact` | `{ objectId, action: 'item.pickup' }` | Item existe, slot libre, rango <=2m |
| `player:interact` | `{ objectId: 'fuse_1..4', action: 'route1.cut_fuse.start' }` | Tiene pinzas, rango <=2m, fusible disponible |
| `player:interact` | `{ objectId: 'fuse_1..4', action: 'route1.cut_fuse.stop' }` | Es el jugador que esta cortando |
| `player:interact` | `{ objectId: 'workshop_grate', action: 'route1.grate.start' }` | Ventilador apagado, rango <=2m, rol valido |
| `player:interact` | `{ objectId: 'workshop_grate', action: 'route1.grate.stop' }` | Estaba trabajando o sosteniendo |
| `player:interact` | `{ objectId: 'vent_opening', action: 'route1.escape.start' }` | Rejilla abierta, rango <=2m |

### Server -> Prisoners

| Evento | Payload | Uso UI |
|---|---|---|
| `escape:route1:state` | `Route1State` filtrado para presos | Refrescar tracker completo |
| `escape:progress` | `{ route, step, progress, completedBy, updatedAt }` | Compatibilidad con HUD generico |
| `item:pickup` | `{ playerId, itemId, slot }` | Inventario y feedback local |
| `item:drop` | `{ playerId, itemId, position }` | Recuperacion de herramienta |
| `game:end` | `{ winner, reason: 'escape_route' }` | Fin de partida |

### Server -> Guard

El guardia no recibe progreso exacto. Solo recibe eventos que representan informacion observable.

| Evento | Payload | Cuándo |
|---|---|---|
| `world:cue` | `{ cue: 'lights_flicker', zone, intensity }` | Fusible incorrecto |
| `world:cue` | `{ cue: 'fan_stopped', zone: 'workshop' }` | Ventilador apagado, si el guardia esta cerca o en camara del Taller |
| `world:cue` | `{ cue: 'metal_grinding', zone: 'workshop', position }` | Rejilla en progreso, audible por rango |
| `world:cue` | `{ cue: 'grate_clang', zone: 'workshop', position }` | Rejilla abierta |

### Server -> All

| Evento | Payload | Uso |
|---|---|---|
| `world:state` | `{ fanPowered, grateOpen, changedProps }` | Props visuales sincronizados |
| `player:state` | Estado normal de jugadores | Posiciones, captura, animaciones |

## HUD de Presos

La Ruta 1 se muestra como un tracker compacto sobre el inventario, en el bottom-left del HUD. No debe ocupar el centro de pantalla ni tapar el crosshair.

```text
Bottom-left, sobre inventario:

Ruta 1: Ventilacion
[?] Plano       Fusible: ?
[~] Ventilador  ON
[ ] Rejilla     0%
[ ] Conducto
```

Estados visuales:

| Icono | Significado |
|---|---|
| `[ ]` | No iniciado |
| `[~]` | En progreso / requiere atencion |
| `[x]` | Completado |
| `[!]` | Bloqueado o intento incorrecto reciente |
| `[?]` | Informacion desconocida |

Distribucion sugerida:

- Top-center: fase actual y timer, sin cambios.
- Bottom-left linea 1: inventario de 2 slots.
- Bottom-left linea 2-5: tracker de Ruta 1, colapsable si no hubo progreso.
- Bottom-center: prompt contextual, por ejemplo `[E] Leer plano`, `[E] Cortar fusible 3`, `[E] Sostener rejilla`.
- Bordes de pantalla: flechas de aliados existentes, sin mostrar nombres si estan lejos.

Reglas de visibilidad:

- Antes de que alguien interactue con un elemento de Ruta 1, el tracker puede estar colapsado como `Ruta 1: sin datos`.
- Al leer el plano, todos los presos ven `Fusible: N`.
- Al apagar el ventilador, todos los presos ven `Ventilador OFF`.
- La barra de rejilla se comparte con todos los presos para permitir coordinacion.
- No mostrar ubicacion exacta ni identidad del preso que hizo cada paso, salvo feedback local opcional como "Compañero actualizo Ruta 1".

## World Feedback

| Cambio | Feedback presos | Feedback guardia |
|---|---|---|
| Plano leido | HUD revela fusible correcto | Ninguno |
| Fusible incorrecto | HUD marca intento fallido breve | Parpadeo de luces en zona aleatoria |
| Fusible correcto | Ventilador OFF en HUD, aspas se detienen | Aspas detenidas y silencio si observa Taller |
| Rejilla progresando | Barra compartida | Rechinido audible cerca del Taller |
| Rejilla abierta | Conducto abierto en HUD | Reja caida visible + clang audible |

## Edge Cases

| Caso | Resolucion |
|---|---|
| Preso capturado con herramienta | Servidor dropea la herramienta en su posicion. Si queda inaccesible, respawn backup tras delay. |
| Preso desconectado con herramienta | Tratar como capturado para objetos: dropear herramienta y permitir recuperacion. |
| Dos presos intentan recoger la misma herramienta | Servidor asigna al primero validado; el segundo recibe error de item no disponible. |
| Dos presos cortan fusibles a la vez | Solo un corte activo por panel. El segundo recibe error de panel ocupado. |
| Cortan fusible correcto sin leer plano | Valido. Apaga ventilador; `planKnown` puede seguir false. |
| Guardia mira camara del Taller cuando se apaga ventilador | Recibe/ve `fan_stopped` porque es informacion observable. |
| Jugador inicia escape y es capturado durante los 4s | Cancelar escape, sacar de `escapingPlayerIds`, aplicar captura normal. |
| Rejilla llega a 100% y nadie escapa | Queda abierta hasta fin de partida. |
| Todos los presos vivos quedan sin acceso a herramientas | Respawn backup garantiza recuperacion. |

## Tuning Knobs

| Knob | Default | Rango sugerido | Nota |
|---|---:|---:|---|
| `plan_read_duration` | 1s | 0.5-2s | Hit-and-run en oficina |
| `tool_pickup_duration` | 3s | 1.5-4s | Riesgo de robo |
| `fuse_cut_duration` | 15s | 10-20s | Exposicion en sala electrica |
| `grate_unscrew_duration` | 25s | 18-35s | Solo player |
| `grate_support_multiplier` | 2.0x | 1.3-2.5x | Cooperacion MVP |
| `grate_decay_rate` | 0.01/s | 0-0.03/s | Decae 1% por segundo |
| `escape_climb_duration` | 4s | 3-6s | Ventana final de captura |
| `critical_item_respawn_delay` | 45s | 20-60s | Anti-softlock |
| `wrench_move_penalty` | 0.95x | 0.9-1.0x | Mantener simple |

## Dependencies

| Sistema | Dependencia | Uso |
|---|---|---|
| Inventario | Hard | Slots, pickup, drop, item ownership |
| Interacciones | Hard | Barras de progreso y prompts |
| Sincronizacion de Estado | Hard | Eventos autoritativos y broadcasts |
| Captura por Foco | Hard | Captura durante escape final |
| Audio/Props | Medium | Ventilador, rechinido, clang, apagones |
| HUD Presos | Hard | Tracker compartido |
| Rutina/Fases | Soft | Define mejores ventanas para ejecutar la ruta |

## Acceptance Criteria

| # | Criterio | Verificacion |
|---|---|---|
| AC-1 | El plano revela el fusible correcto a todos los presos y no al guardia | 2 clientes presos + 1 guardia; leer plano y comparar HUDs |
| AC-2 | Cortar fusible correcto apaga el ventilador para todos | Ver aspas detenidas y `fanPowered=false` en estado |
| AC-3 | Cortar fusible incorrecto no bloquea la ruta | Luego de fallo, cortar el correcto y continuar |
| AC-4 | La rejilla solo progresa con ventilador apagado y llave inglesa | Intentar antes/despues, con/sin item |
| AC-5 | Segundo preso acelera la rejilla | Medir duracion solo vs cooperativo |
| AC-6 | Progreso de rejilla se comparte en HUD de presos | Dos presos ven mismo porcentaje con diferencia menor a 1 tick visible |
| AC-7 | Herramientas criticas no se pierden permanentemente | Captura/desconexion/drop fuera de bounds provoca recuperacion |
| AC-8 | Escape final mantiene vulnerabilidad de 4s | Guardia puede capturar durante animacion; no puede despues |
| AC-9 | Completar escape dispara victoria de presos | `game:end` con `winner='prisoners'` y `reason='escape_route'` |

## Post-MVP Polish

- Segundo preso reduce rango audible del rechinido.
- QTEs ligeros para cortar fusible y desatornillar sin hacer ruido extra.
- Animaciones especificas para sostener rejilla, trepar y ser atrapado de las piernas.
- Marcas visuales progresivas en pernos de la rejilla.
- Variantes de plano electrico con lectura visual en vez de texto directo.
