# Movimiento FPS

> **Status**: Designed
> **Author**: Cris + Claude
> **Last Updated**: 2026-04-06
> **Implements Pillar**: Tensión asimétrica

## Overview

El sistema de Movimiento FPS controla toda la locomoción y orientación de cámara de los jugadores (presos y guardia) en primera persona. El jugador interactúa con él de forma activa y constante — es el canal principal de input del juego. Sin este sistema no hay gameplay: no podés patrullar, perseguir, escapar, recoger objetos ni mezclarte entre NPCs. Es el sistema Foundation del que dependen 7 sistemas downstream.

## Player Fantasy

**Presos:** Sentirte vulnerable y expuesto. El FOV estrecho (70°) te obliga a girar la cabeza para ver qué hay detrás tuyo — no sabés si el guardia está a tus espaldas. Cada esquina es un momento de tensión. Cuando corrés, sabés que estás generando ruido y atención, pero a veces no tenés otra opción.

**Guardia:** Sentirte en control del espacio pero abrumado por la cantidad de presos. Tu FOV más amplio (80°) te da mejor visión, pero 20 personas idénticas se mueven frente a vos y cualquiera podría ser un jugador. Cuando perseguís a alguien, la adrenalina sube porque sabés que si te equivocás, pagás caro.

**Referencia:** El movimiento debe sentirse como un FPS táctico lento (más Alien: Isolation que Call of Duty). No es ágil ni fluido — es pesado, deliberado. Cada paso tiene peso.

## Detailed Design

### Core Rules

1. **Movimiento** — Input WASD traduce a vector de dirección relativo a la orientación de la cámara. El CharacterController de Unity aplica el movimiento.
2. **Rotación** — El mouse controla pitch (vertical, clamped ±80°) y yaw (horizontal, sin límite). Sensibilidad configurable.
3. **Sprint** — Mantener Shift multiplica la velocidad por `sprint_multiplier`. Sin límite de stamina (no queremos un sistema de stamina extra). Sprint genera ruido audible para el sistema de Audio 3D (rango 15m).
4. **Crouch** — Toggle con C (o hold, configurable). Reduce la altura del collider a 60%, velocidad a 50%. El jugador agachado es más difícil de distinguir visualmente entre NPCs sentados/agachados.
5. **Gravedad** — Los jugadores están sujetos a gravedad. No hay salto (no hay razón narrativa para saltar en una prisión, y simplifica colisiones).
6. **Colisión** — CharacterController con cápsula. No se puede atravesar paredes, NPCs ni otros jugadores. Empujar a un NPC brevemente no genera sospecha; empujarlo repetidamente sí.
7. **Head bob** — Oscilación vertical sutil al caminar (amplitud 0.02m, frecuencia synced con velocidad). Más pronunciada al correr (amplitud 0.04m). Sin head bob en crouch o quieto.

### States and Transitions

| Estado | Velocidad | Head Bob | Audio | Transición a |
|--------|-----------|----------|-------|-------------|
| **Idle** | 0 m/s | Ninguno | Respiración sutil | Walk, Sprint, Crouch |
| **Walk** | 3.5 m/s | Sutil (0.02m) | Pasos normales (rango 8m) | Idle, Sprint, Crouch, CrouchWalk |
| **Sprint** | 5.5 m/s | Pronunciado (0.04m) | Pasos fuertes (rango 15m) | Walk (soltar Shift), Idle (soltar todo) |
| **Crouch** | 0 m/s | Ninguno | Silencioso | CrouchWalk, Idle (des-crouch) |
| **CrouchWalk** | 1.75 m/s | Mínimo (0.01m) | Pasos muy suaves (rango 3m) | Crouch (soltar WASD), Walk (des-crouch) |

- No se puede sprintar estando agachado (Crouch + Shift = CrouchWalk, no sprint).
- La transición Crouch↔Idle verifica que haya espacio vertical para levantarse (evitar quedarse clavado debajo de mesas).

### Interactions with Other Systems

| Sistema | Dirección | Interfaz |
|---------|-----------|----------|
| **Persecución (2)** | ← recibe | Aplica `guard_sprint_multiplier` (1.20x) o `prisoner_sprint_multiplier` (1.15x) a la velocidad de sprint durante persecución activa |
| **Camuflaje (3)** | → provee | Expone `player.position`, `player.zone`, `player.isMoving`, `player.isSprinting` |
| **Inventario (5)** | → provee | Expone `player.position` y `player.forward` para raycast de interacción (recogida de objetos) |
| **Mecánicas Molestia (8)** | ← recibe | Recibe stun/tropiezo: velocidad → 0 por N segundos, rotación bloqueada |
| **Audio 3D (23)** | → provee | Expone `player.state` (Idle/Walk/Sprint/Crouch/CrouchWalk) para selección de sonido + `player.position` para audio posicional |
| **Sincronización (18)** | ↔ bidireccional | Envía input al servidor cada 50ms. Recibe posición autoritativa. Client prediction local. |
| **NPC Rutina (13)** | → provee | Colisión física con NPCs. Si el jugador empuja NPCs repetidamente → flag `suspicious_push` |

## Formulas

```
velocidad_final = velocidad_base[estado] × multiplicador_persecución × multiplicador_stun

Donde:
  velocidad_base[Idle]       = 0.0 m/s
  velocidad_base[Walk]       = walk_speed           (default: 3.5 m/s)
  velocidad_base[Sprint]     = walk_speed × sprint_multiplier  (default: 3.5 × 1.57 = 5.5 m/s)
  velocidad_base[Crouch]     = 0.0 m/s
  velocidad_base[CrouchWalk] = walk_speed × crouch_speed_multiplier  (default: 3.5 × 0.5 = 1.75 m/s)

  multiplicador_persecución  = 1.0 (normal) | guard_sprint_multiplier (1.20) | prisoner_sprint_multiplier (1.15)
  multiplicador_stun         = 1.0 (normal) | 0.0 (stunned)

head_bob_y = sin(tiempo × velocidad_base[estado] × bob_frequency) × bob_amplitude[estado]

Donde:
  bob_amplitude[Idle]       = 0.0m
  bob_amplitude[Walk]       = 0.02m
  bob_amplitude[Sprint]     = 0.04m
  bob_amplitude[CrouchWalk] = 0.01m
  bob_frequency             = 2π (un ciclo completo por paso)

crouch_height = standing_height × 0.6
  standing_height = 1.8m → crouch_height = 1.08m

audio_range[estado]:
  Idle       = 0m (silencioso)
  Walk       = 8m
  Sprint     = 15m
  Crouch     = 0m
  CrouchWalk = 3m
```

## Edge Cases

| Caso | Qué pasa | Resolución |
|------|----------|------------|
| **Crouch debajo de mesa, intenta levantarse** | Raycast vertical detecta obstáculo arriba | Bloquear transición a Idle/Walk hasta que haya espacio. Feedback visual sutil (ícono de "bloqueado"). |
| **Dos jugadores intentan ocupar el mismo espacio** | CharacterController con cápsula impide superposición | Los jugadores se empujan mutuamente. No hay damage ni penalización. |
| **Jugador contra pared + sprint** | Velocidad efectiva = 0 pero estado = Sprint | Audio de pasos se reproduce igualmente (rango 15m). Intencional: sprintar contra pared delata tu posición. |
| **Stun durante crouch** | `multiplicador_stun = 0` pero jugador agachado | Se queda agachado e inmóvil. No se fuerza a ponerse de pie. Rotación bloqueada. |
| **Sprint en transición entre zonas** | Jugador cruza trigger de zona corriendo | La zona se actualiza inmediatamente. Sistema de Camuflaje detecta si la nueva zona es incorrecta. |
| **Desconexión durante movimiento** | Cliente pierde conexión | Personaje se detiene en última posición autoritativa. Reconexión (19) maneja timeout de 30 seg. |
| **Input simultáneo W+S o A+D** | Vectores opuestos se cancelan | Velocidad = 0, estado = Idle. Comportamiento estándar de CharacterController. |
| **Mouse sensitivity = 0** | Jugador no puede rotar | Clamped a mínimo 0.1. Slider no permite llegar a 0. |

## Dependencies

| Sistema | Tipo | Dirección | Interfaz específica |
|---------|------|-----------|-------------------|
| Persecución (2) | Hard | ← recibe | Modifica `sprint_multiplier` durante chase activo |
| Camuflaje (3) | Hard | → provee | `player.position`, `player.zone`, `player.isMoving`, `player.isSprinting`, `player.isCrouching` |
| Inventario (5) | Hard | → provee | `player.position`, `player.forward` (raycast interacción) |
| Mecánicas Molestia (8) | Soft | ← recibe | Aplica stun: `SetStun(duration)` → velocidad 0, rotación bloqueada |
| Audio 3D (23) | Soft | → provee | `player.movementState`, `player.position`, `player.surfaceType` |
| Sincronización (18) | Hard | ↔ | Input → servidor (50ms). Posición autoritativa → cliente. Client prediction. |
| NPC Rutina (13) | Soft | → provee | Colisión física. Flag `suspicious_push` si empuja NPCs repetidamente. |
| Cámaras Seguridad (15) | Soft | → provee | `player.position` para detección en feed de cámaras |

**Hard** = no funciona sin él. **Soft** = funciona pero con feature reducido.

## Tuning Knobs

| Knob | Default | Rango seguro | Si muy bajo | Si muy alto | Interactúa con |
|------|---------|-------------|-------------|-------------|----------------|
| `walk_speed` | 3.5 m/s | 2.5–4.5 m/s | Sluggish, difícil llegar a zonas a tiempo | NPCs quedan atrás, se nota que sos jugador | `sprint_multiplier`, audio ranges |
| `sprint_multiplier` | 1.57x (→5.5 m/s) | 1.3–2.0x | Sprint casi igual que caminar, inútil para escapar | Demasiado rápido, imposible de atrapar | `guard/prisoner_sprint_multiplier` |
| `crouch_speed_multiplier` | 0.5x (→1.75 m/s) | 0.3–0.7x | Agacharse inútil para moverse | Sin penalización suficiente | `walk_speed` |
| `crouch_height_multiplier` | 0.6x (→1.08m) | 0.5–0.7x | Demasiado bajo, clips con objetos | Apenas se nota la diferencia | Colisión con muebles |
| `mouse_sensitivity` | 2.0 | 0.1–10.0 | Muy lento, frustrante | Incontrolable | — |
| `fov_prisoner` | 70° | 60–80° | Claustrofóbico, mareo | Pierde tensión de no ver atrás | `fov_guard` |
| `fov_guard` | 80° | 70–90° | Guardia tan limitado como preso | Ventaja excesiva de visión | `fov_prisoner` |
| `bob_amplitude_walk` | 0.02m | 0.005–0.05m | Imperceptible | Mareo (motion sickness) | `bob_amplitude_sprint` |
| `bob_amplitude_sprint` | 0.04m | 0.01–0.08m | Imperceptible | Mareo severo | `bob_amplitude_walk` |
| `standing_height` | 1.8m | 1.6–2.0m | Perspectiva baja, no realista | Se ve por encima de NPCs | `crouch_height_multiplier` |

## Visual/Audio Requirements

[To be designed]

## UI Requirements

[To be designed]

## Acceptance Criteria

| # | Criterio | Cómo verificar |
|---|----------|----------------|
| AC-1 | El jugador se mueve en las 4 direcciones (WASD) relativo a la orientación de la cámara | Mantener W y girar mouse — siempre avanza hacia donde mira |
| AC-2 | Sprint aumenta velocidad efectiva y es audible | Debug: Walk=3.5 m/s, Sprint=5.5 m/s. Otro jugador a 15m escucha pasos |
| AC-3 | Crouch reduce altura del collider y velocidad | Agacharse debajo de mesa del comedor. Velocidad = 1.75 m/s. Levantarse debajo de mesa → bloqueado |
| AC-4 | No hay salto | Space no produce ningún efecto |
| AC-5 | Head bob corresponde al estado de movimiento | Walk=sutil, Sprint=pronunciada, Idle/Crouch=sin oscilación |
| AC-6 | FOV difiere por rol | Preso=70°, Guardia=80° |
| AC-7 | La colisión impide atravesar paredes y NPCs | Caminar contra pared/NPC → se detiene, no atraviesa |
| AC-8 | Posición sincronizada entre clientes | Dos jugadores ven al otro moverse con <100ms de delay percibido |
| AC-9 | Stun externo funciona | Jabón → velocidad=0, rotación bloqueada por duración del stun |
| AC-10 | Performance ≤ 0.5ms frame time para movimiento | Profiler Unity, 60 FPS con 4 jugadores + 16 NPCs |

## Open Questions

[To be designed]
