# Sistema de Captura por Foco

> **Status**: Approved for Jam MVP  
> **Author**: Cris + Codex  
> **Last Updated**: 2026-04-21  
> **Implements Pillar**: Tensión asimétrica, engaño social  
> **Supersedes**: El sistema previo de `marcado + acusación`

## Overview

El sistema de Captura por Foco simplifica el loop del guardia a una sola acción: **acercarse al sospechoso y sostener el foco de captura durante 0.5 segundos**. Si el foco se completa sobre un preso jugador, hay captura. Si se completa sobre un NPC, el guardia comete un error y recibe la penalización correspondiente. Si el foco se rompe antes, no pasa nada y el guardia debe reposicionarse.

## Player Fantasy

- **Guardia**: "Te tengo cerca. Si sostengo el foco un instante más, te saco de la ronda."
- **Preso**: "No dejes que se acomode. Rompé línea, metete en la masa, hacelo dudar."

## Detailed Design

### Core Rules

1. El guardia debe estar a **2.0m o menos** para intentar capturar.
2. La captura requiere mantener el input y la mira sobre el mismo target durante **0.5 segundos**.
3. El target debe seguir visible mientras dura el foco.
4. Si el foco se rompe antes de completarse, la captura se cancela sin penalización.
5. Si el foco se completa:
   - target `player` -> captura,
   - target `npc` -> error del guardia.
6. No hay sistema de marca persistente ni segunda acción de acusación en el MVP.
7. El foco es el anti-missclick principal de la jam.

### States and Transitions

```text
PATROL
  -> (target visible, en rango, hold 0.5s) -> FOCUS_CAPTURE

FOCUS_CAPTURE
  -> (distance > 2.0m) -> PATROL
  -> (lost_los) -> PATROL
  -> (input_release) -> PATROL
  -> (target_switch) -> PATROL
  -> (0.5s completos, target = prisoner) -> CAPTURE_RESOLVED
  -> (0.5s completos, target = npc) -> ERROR_RESOLVED

CAPTURE_RESOLVED
  -> PATROL

ERROR_RESOLVED
  -> PATROL
```

### Interactions with Other Systems

| Sistema | Interacción |
|---------|-------------|
| Movimiento FPS | Determina si el guardia logra acercarse y mantener rango |
| Rutina/Fases | El guardia sigue leyendo comportamientos para decidir a quién acercarse |
| NPCs | Crean ruido visual y bloquean líneas limpias de foco |
| Inventario / Distracciones | Dificultan que el guardia llegue a rango o sostenga el foco |
| Penalizaciones del Guardia | Se activan solo si completa una captura sobre un NPC |
| Victoria/Derrota | Capturar a todos los presos sigue siendo condición de victoria del guardia |

## Backend Spec

### Server Responsibilities

- Ser la autoridad de:
  - rol del emisor,
  - validez del `entityId`,
  - distancia de captura,
  - resultado final de captura o error,
  - penalizaciones y condiciones de victoria.
- Mantener soporte para `entityId` de tipo `player` o `npc`.
- No modelar estado persistente de marca en el MVP.

### Suggested Backend Validation

#### `guard:catch`

Payload sugerido:

```ts
interface GuardCatchAttemptPayload {
  entityId: string
  entityType: 'player' | 'npc'
}
```

Validar:
- el emisor existe y es guardia,
- `entityId` existe,
- guardia y target están a `<= capture_range`,
- target sigue siendo válido al momento de resolver.

Si pasa:
- target `player` -> capturar y emitir resultado exitoso,
- target `npc` -> registrar error y emitir resultado fallido.

Si falla:
- emitir `catch:failed` solo al guardia.

### Backend Notes

- Para la jam, el backend **no valida el foco continuo de 0.5s**; Unity se encarga de enviar el intento solo al completar el foco local.
- El backend sí valida rango e identidad para evitar capturas imposibles.
- Esto reduce muchísimo complejidad respecto al sistema con marca persistente.

## Unity Spec

### Guard Client Responsibilities

- Resolver qué personaje está bajo el centro de la mira mediante raycast local.
- Verificar que el target esté a `<= 2.0m`.
- Mientras el guardia mantiene el input de captura sobre el mismo collider:
  - llenar una barra o radial de foco durante `0.5s`,
  - resetear el foco si cambia el target,
  - resetear el foco si el target sale de rango,
  - resetear el foco si se pierde visión,
  - resetear el foco si el jugador suelta el input.
- Cuando el foco llega a `0.5s`, emitir `guard:catch`.

### Prisoner Client Responsibilities

- No requieren HUD persistente de marca en el MVP.
- Al recibir captura exitosa sobre sí mismo:
  - mostrar pantalla de capturado,
  - cortar input,
  - pasar a espectador si aplica.

### UI Guidance

- Guardia:
  - crosshair más notorio,
  - radial/barra de foco en el centro,
  - prompt contextual solo cuando hay target válido a rango.
- Preso:
  - sin timer de marca,
  - feedback fuerte solo al ser capturado.

## Network Contract

### Client -> Server

| Evento | Payload | Quién lo emite |
|--------|---------|----------------|
| `guard:catch` | `{ entityId, entityType }` | Guardia |

### Server -> Clients

| Evento | Payload | Destino |
|--------|---------|---------|
| `guard:catch:result` | `{ guardId, entityId, success, isPlayer }` | Todos |
| `catch:failed` | `{ reason }` | Solo guardia |

## Formulas

```text
capture_focus_time            = 0.5s
capture_range                 = 2.0m
capture_focus_break_tolerance = 0.1s
```

## Edge Cases

| Caso | Resolución |
|------|------------|
| El guardia cambia de target a mitad del foco | El foco se resetea |
| El target sale de rango a mitad del foco | El foco se resetea |
| El guardia suelta click antes de 0.5s | El foco se resetea |
| Hay varios cuerpos superpuestos | Unity usa raycast al collider visible; no hay auto-lock mágico |
| El target desaparece antes de llegar el evento al servidor | `catch:failed` |
| El guardia captura a un NPC | Error + penalización escalonada |

## Tuning Knobs

| Parámetro | Efecto de subirlo | Efecto de bajarlo |
|-----------|-------------------|-------------------|
| `capture_focus_time` | Más counterplay del preso | Capturas más inmediatas |
| `capture_range` | Guardia más fuerte en espacios abiertos | Guardia necesita compromiso más claro |
| `capture_focus_break_tolerance` | Más tolerante al jitter | Más estricto y preciso |

## Acceptance Criteria

1. El guardia solo necesita una acción para intentar capturar.
2. La captura no ocurre por click instantáneo; requiere foco continuo.
3. Si el foco se rompe, no hay resultado.
4. Si el target es preso, la captura funciona.
5. Si el target es NPC, se cuenta como error.
6. Unity muestra progreso de foco claro al guardia.
7. No existe estado persistente de marca o acusación en el MVP.

## Open Questions

- ¿Queremos un pequeño feedback visual/audio en el preso cuando está a punto de ser capturado, o lo dejamos invisible para mantener simpleza?
- ¿Agregamos una micro-recuperación de 0.25s tras cada intento resuelto o dejamos spam controlado solo por el foco?
