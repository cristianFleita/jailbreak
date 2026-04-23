# Sistema de Autonomia NPC: Personalidad y Comportamiento Emergente

> **Status**: Implemented  
> **Author**: Cris + Claude  
> **Last Updated**: 2026-04-14  
> **Cubre sistemas**: #14 NPC Personality + Emergent Behavior  
> **Depende de**: #4 Rutina/Fases, #13 NPC Rutina/NavMesh  
> **Implementa Pilar**: "Cada NPC es unico" — los NPCs deben ser indistinguibles de jugadores humanos

---

## 1. Overview

Este sistema agrega una capa de personalidad y comportamiento emergente sobre el sistema de rutina/fases existente (sistema #4/#13). Cada NPC recibe al inicio de la partida un **arquetipo** (troublemaker, loner, social, worker, athletic, nervous, schemer) y un **estado emocional dinamico** (mood) que cambia en respuesta a eventos del juego.

**Problema que resuelve:** Sin este sistema, todos los NPCs siguen la misma distribucion de acciones con los mismos pesos. Un guardia experimentado puede detectar que ningun NPC se detiene a mirar la pared, o que todos los NPCs cambian de accion al mismo ritmo. Esto delata a los jugadores que se salen del patron.

**Solucion arquitectonica:**

1. **NPCPersonalitySystem** (backend): Modifica los pesos de seleccion de acciones en `jail-routine.ts` antes del `weightedRandom()`. Cada NPC tiene multiplicadores unicos basados en su arquetipo y mood actual.

2. **Acciones emergentes**: Cada 8 segundos, cada NPC tiene una probabilidad de disparar una accion espontanea (fidget, whisper, taunt, pushup) que interrumpe brevemente la accion asignada. Estas acciones tienen requisitos de mood/archetype y cooldowns.

3. **Mood dinamico**: El estado emocional de cada NPC cambia basado en: proximidad del guardia, presenciar una captura, aburrimiento por repeticion, cambio de fase, nivel de riot.

**Entidades afectadas:** Los 17-19 NPCs prisioneros. El guardia NPC no usa este sistema.

---

## 2. Player Fantasy

**Para el jugador prisionero:** Los NPCs ya no son todos iguales. El tipo grande siempre esta haciendo flexiones. El nervioso no para de caminar en circulos. El social no deja de hablar. Esto te da modelos de comportamiento para imitar — si te ves como el tipo que se sienta solo en la esquina, el guardia no sospecha. Tambien ves NPCs provocando al guardia, lo que te da ventanas para moverte.

**Para el jugador guardia:** Ahora detectar jugadores es mas dificil. Un NPC puede detenerse a mirar una pared (es un loner), otro puede estar caminando rapido sin razon (es nervioso). Las provocaciones de los troublemaker son distracciones reales — tienes que decidir si ignorar al NPC que te esta gritando o seguir vigilando al sospechoso. Los NPCs ya no son un fondo estatico; son ruido activo que te obliga a prestar atencion real.

---

## 3. Detailed Rules

### 3.1 Arquetipos (Asignados al inicio, inmutables)

| Arquetipo     | Distribucion | Acciones preferidas                          | Acciones evitadas            |
|---------------|-------------|----------------------------------------------|------------------------------|
| troublemaker  | ~15%        | Provocar guardia, shadowbox, kick, pelear    | Trabajo, idle                |
| loner         | ~15%        | Leer, mirar ventana, caminar solo, sentarse  | Acciones sociales            |
| social        | ~15%        | Conversar, jugar cartas, saludar, chismear   | Estar solo, idle             |
| worker        | ~15%        | Workbench, cargar cajas, inspeccionar        | Socializar, ejercicio        |
| athletic      | ~15%        | Flexiones, shadowbox, correr perimetro       | Sentarse, acostarse, idle    |
| nervous       | ~15%        | Fidget, caminar nervioso, mirar ventana      | Provocar guardia, ejercicio  |
| schemer       | ~10%        | Susurrar, inspeccionar, mirar ventana, leer  | Provocar guardia             |

**Regla de distribucion:** Para 20 NPCs: ~3 de cada tipo + 2 schemers. Se shufflea al inicio para que no haya patrones predecibles por posicion de celda.

### 3.2 Sistema de Mood (Dinamico, cambia durante partida)

| Mood       | Efecto en comportamiento                                | Trigger principal             |
|------------|--------------------------------------------------------|-------------------------------|
| calm       | Baseline — sin modificacion de pesos                   | Decay natural (30s)           |
| bored      | Busca variedad, cambia acciones mas rapido             | Repetir misma accion          |
| agitated   | Camina rapido, shadowbox, puede provocar guardia       | Guardia cerca 5s+, captura    |
| nervous    | Fidget, busca grupos, evita guardia                    | Guardia cerca, ver captura    |
| rebellious | Desbloquea acciones anti-guardia, confrontacional      | Troublemaker + guardia cerca  |
| social     | Busca compania activamente                             | Social archetype + interaccion|
| tired      | Prefiere sentarse, moverse lento                       | Decay natural en fases tardias|

**Transiciones de mood:**

```
calm ──(guardia cerca)──> nervous (para nervous archetype)
calm ──(guardia cerca)──> rebellious (para troublemaker, 60%)
calm ──(guardia cerca)──> agitated (para troublemaker, 40%)
calm ──(aburrimiento)──> bored
calm ──(interaccion social)──> social (para social archetype)
bored ──(decay)──> calm
bored ──(troublemaker)──> agitated (30%)
nervous ──(guardia se va)──> calm
agitated ──(cambio fase)──> calm (40%)
rebellious ──(decay)──> agitated/calm
any ──(riot brewing)──> rebellious (troublemaker), nervous (nervous), agitated (otros 40%)
```

### 3.3 Modificacion de Pesos

Los pesos de accion se modifican multiplicativamente:

```
peso_final = peso_base * modificador_arquetipo * modificador_mood * anti_repeticion
```

- **modificador_arquetipo**: Lookup por actionId prefix → type. Ej: `troublemaker.shadowbox = 2.0`
- **modificador_mood**: Mismo lookup. Ej: `rebellious.anti_guard = 3.0`
- **anti_repeticion**: Si la accion esta en las ultimas 5 del NPC → `*0.3`
- **moodIntensity**: Escala el efecto del mood: `1.0 + (modifier - 1.0) * intensity`

### 3.4 Acciones Emergentes

Cada 8 segundos (con jitter +-30%), cada NPC evalua si dispara una accion emergente. Estas son breves interrupciones (1.5-10s) que no requieren waypoint — el NPC se detiene en su posicion actual, ejecuta la animacion, y luego retoma lo que estaba haciendo.

| Accion             | Tipo            | Duracion | Mood requerido        | Archetype requerido       | Probabilidad base | Cooldown |
|--------------------|-----------------|----------|-----------------------|---------------------------|--------------------|----------|
| block_path         | anti_guard      | 4s       | rebellious, agitated  | —                         | 15%                | 60s      |
| taunt_guard        | anti_guard      | 3s       | rebellious            | troublemaker              | 20%                | 45s      |
| argue_guard        | anti_guard      | 6s       | rebellious, agitated  | —                         | 10%                | 90s      |
| fake_fight         | anti_guard      | 5s       | rebellious, agitated  | troublemaker, athletic    | 8%                 | 120s     |
| commotion          | anti_guard      | 4s       | rebellious            | —                         | 12%                | 90s      |
| pushups            | environmental   | 8s       | bored, agitated, calm | athletic, troublemaker    | 20%                | 40s      |
| stretch_random     | environmental   | 4s       | —                     | —                         | 15%                | 20s      |
| check_window       | environmental   | 6s       | nervous, bored        | —                         | 12%                | 30s      |
| kick_wall          | environmental   | 3s       | agitated, rebellious  | —                         | 10%                | 45s      |
| inspect_something  | environmental   | 5s       | bored, calm           | schemer, loner            | 15%                | 35s      |
| pace_nervously     | environmental   | 6s       | nervous, agitated     | —                         | 18%                | 25s      |
| sit_floor          | environmental   | 10s      | tired, bored          | —                         | 10%                | 60s      |
| whisper_nearby     | social_reactive | 4s       | nervous, social       | schemer, social, nervous  | 18%                | 30s      |
| fist_bump          | social_reactive | 2s       | calm, social, rebellious | social, troublemaker, athletic | 20%          | 20s      |
| nod_greeting       | social_reactive | 1.5s     | —                     | —                         | 25%                | 15s      |
| look_around        | social_reactive | 3s       | nervous, calm         | —                         | 15%                | 20s      |
| crack_knuckles     | self_expression | 2s       | —                     | troublemaker, athletic    | 18%                | 25s      |
| fidget             | self_expression | 3s       | nervous, bored        | —                         | 22%                | 15s      |
| sigh               | self_expression | 2s       | bored, tired          | —                         | 15%                | 20s      |
| shadow_punch       | self_expression | 4s       | agitated, bored, rebellious | athletic, troublemaker | 15%              | 30s      |
| lean_think         | self_expression | 5s       | calm, bored           | schemer, loner            | 12%                | 30s      |

**Guardia nearby requerido:** `block_path`, `taunt_guard`, `argue_guard` solo se activan si hay un guardia a menos de 15 unidades.

### 3.5 Acciones en Catalogo de Fases (Nuevas)

Acciones agregadas al catalogo de fases 4 y 7 (hora libre):

| Accion             | Zona  | Tipo   | Animacion    | Peso base | Duracion    |
|--------------------|-------|--------|--------------|-----------|-------------|
| yard_pushups       | patio | IDLE   | PushUp       | 8         | 10-25s      |
| yard_pace_nervous  | patio | IDLE   | pace         | 5         | 8-20s       |
| yard_stare_wall    | patio | IDLE   | idle_window  | 4         | 10-30s      |
| yard_whisper_secret| patio | SOCIAL | whisper      | 6         | 8-15s       |
| yard_argue_loud    | patio | SOCIAL | argue        | 3         | 6-12s       |
| yard_sit_floor     | patio | IDLE   | sit_floor    | 4         | 12-30s      |
| cell_read_book     | celdas| IDLE   | read_book    | 20        | 15-40s      |
| cell_idle_window   | celdas| IDLE   | idle_window  | 15        | 10-30s      |

### 3.6 Preferencias Sociales

Cada NPC tiene 2-3 NPCs "amigos" con los que prefiere interactuar. Esto crea subgrupos organicos — siempre ves a los mismos NPCs juntandose. Los jugadores pueden usar esto para mimetizarse con un grupo NPC.

### 3.7 Anti-Repeticion

Las ultimas 5 acciones de cada NPC se guardan en `actionHistory`. Si una accion esta en el historial, su peso se multiplica por 0.3. Esto fuerza variedad sin eliminar completamente la posibilidad de repetir.

---

## 4. Formulas

### 4.1 Peso Final de Accion

```
peso_final = max(0.1, peso_base * M_arch * M_mood * M_rep)
```

Variables:
- `peso_base`: Weight definido en el catalogo de fases (1-100)
- `M_arch`: Multiplicador del arquetipo para esa accion (0.2-2.5, default 1.0)
- `M_mood`: Multiplicador del mood para esa accion (0.1-3.0, default 1.0)
- `M_rep`: Factor anti-repeticion (0.3 si esta en historial, 1.0 si no)
- Floor: 0.1 (nunca se elimina completamente una accion)

**Ejemplo:** NPC troublemaker (rebellious mood) evaluando `shadowbox`:
```
peso_base = 5
M_arch(troublemaker, shadowbox) = 2.0
M_mood(rebellious, shadowbox) = 1.8
M_rep = 1.0 (no en historial)
moodIntensity = 0.7

M_arch_scaled = 1.0 + (2.0 - 1.0) * 0.7 = 1.7
M_mood_scaled = 1.0 + (1.8 - 1.0) * 0.7 = 1.56

peso_final = max(0.1, 5 * 1.7 * 1.56 * 1.0) = 13.26
```

vs NPC loner (calm mood) evaluando `shadowbox`:
```
M_arch(loner, shadowbox) = 1.0 (no tiene modifier)
M_mood(calm, shadowbox) = 1.0
peso_final = max(0.1, 5 * 1.0 * 1.0 * 1.0) = 5.0
```

### 4.2 Probabilidad de Accion Emergente

```
P_trigger = P_base * M_mood_match * M_intensity
```

Variables:
- `P_base`: Probabilidad base definida en el catalogo (0.08-0.25)
- `M_mood_match`: 2.0 si el mood actual esta en `requiresMood`, 1.0 si no
- `M_intensity`: `0.5 + moodIntensity * 0.5` (range 0.5-1.0)

**Ejemplo:** NPC nervous (mood=nervous, intensity=0.8) evaluando `fidget`:
```
P_base = 0.22
M_mood_match = 2.0 (nervous esta en requiresMood)
M_intensity = 0.5 + 0.8 * 0.5 = 0.9
P_trigger = 0.22 * 2.0 * 0.9 = 0.396 (39.6% por check)
```

### 4.3 Frecuencia Efectiva de Emergentes

Con check cada ~8s y probabilidad tipica de 10-20%:
- NPC promedio: 1 emergente cada 40-80 segundos
- NPC en mood fuerte: 1 emergente cada 20-40 segundos

---

## 5. Edge Cases

| Caso | Que pasa |
|------|----------|
| Todos los NPCs quedan en mismo mood | Mood decay (30s) los devuelve a calm gradualmente. La intensidad inicial es aleatoria (0.4-0.8) |
| Guard desconecta durante emergent anti-guard | La accion emergente se completa normalmente. No depende de la existencia del guardia |
| NPC recibe reassign durante emergent action | Unity: la accion emergente tiene prioridad hasta que termine su timer. El reassign se aplica despues |
| NPC tiene cooldown en todas las acciones emergentes | No dispara ninguna emergente, vuelve a intentar en el proximo check (8s) |
| Todos los waypoints del tipo requerido estan ocupados | La accion emergente se ejecuta in-place (no requiere waypoint). Las acciones de catalogo si fallan waypoint → idle fallback |
| Mood oscillation rapida (guard entra y sale) | guardProximityTimer se resetea al salir el guardia. Se requieren 5s continuos de proximidad |
| NPC con archetype=nervous recibe riot_brewing | Se pone nervioso (no rebellious). Solo troublemaker se pone rebellious |
| Reconnect mid-game | El personality system se reconstruye desde el state del room. Los profiles se regeneran (los moods se resetean a defaults por archetype, lo cual es aceptable) |

---

## 6. Dependencies

### Este sistema depende de:
- **#4 Rutina/Fases** (`jail-routine.ts`): El personality system modifica los pesos antes de `weightedRandom()`. Sin el sistema de fases, no hay acciones que modificar.
- **#13 NPC Rutina/NavMesh** (`npc-behavior.ts`): Las acciones emergentes necesitan que el NPC tenga NavMeshAgent activo para poder pausar y retomar movimiento.
- **Room Manager** (`room-manager.ts`): Wirea los callbacks de `onEmergentAction` y `onMoodShift` para emitir por socket.

### Sistemas que dependen de este:
- **Capture / Guard Pressure System**: Puede consultar mood de un NPC para decidir si un NPC se queda quieto, se aparta o interfiere visualmente cuando el guardia intenta cerrar una captura por foco.
- **Victory Conditions**: Las acciones anti-guard contribuyen al riot meter.
- **UI/HUD**: El mood hint permite que el UI muestre indicadores sutiles del estado emocional (futuro).

### Bidireccional:
- **Guard Catch System**: Cuando un guardia captura a un jugador, `NPCPersonalitySystem.onPlayerCaught()` notifica a NPCs cercanos → mood shift.
- **Riot System**: Cuando riot meter sube, `NPCPersonalitySystem.onRiotBrewing()` prepara troublemakers.

---

## 7. Tuning Knobs

| Knob | Valor actual | Rango seguro | Afecta |
|------|-------------|-------------|--------|
| `SPONTANEOUS_CHECK_INTERVAL` | 8s | 5-15s | Frecuencia de checks emergentes. Menor = mas emergentes pero mas CPU |
| `GUARD_PROXIMITY_THRESHOLD` | 5s | 3-10s | Cuanto tiempo el guardia debe estar cerca para trigger mood. Menor = reacciones mas rapidas |
| `GUARD_NEARBY_DISTANCE` | 15 units | 10-25 | Radio de deteccion del guardia. Mayor = mas NPCs reaccionan |
| `ACTION_HISTORY_MAX` | 5 | 3-8 | Acciones recordadas para anti-repeticion. Mayor = mas variedad forzada |
| `MOOD_MATCH_MULTIPLIER` | 2.0 | 1.5-3.0 | Boost de probabilidad emergente cuando mood coincide. Mayor = mas emergentes |
| `MOOD_DECAY_INTERVAL` | 30s | 20-60s | Tiempo antes de decay natural a calm. Mayor = moods duran mas |
| `REASSIGN_CHANGE_PROB` | 0.80 | 0.5-0.95 | Probabilidad de cambiar accion en reassign. Menor = NPCs mas consistentes |
| `moodIntensity` (por NPC) | 0.4-0.8 | 0.2-1.0 | Cuanto afecta el mood al peso. 0 = mood no importa, 1 = efecto maximo |
| `emergent.cooldownSeconds` | varies | 15-120s | Per-action cooldown. Menor = repeticion mas frecuente del emergente |
| `emergent.probability` | 0.08-0.25 | 0.05-0.40 | Probabilidad base por check. Mayor de 0.30 genera ruido excesivo |

**Perillas de equilibrio rapido:**
- **Mas inmersion, menos gameplay:** Subir `SPONTANEOUS_CHECK_INTERVAL` a 5s, `MOOD_MATCH_MULTIPLIER` a 3.0
- **Mas gameplay, menos ruido:** Subir `SPONTANEOUS_CHECK_INTERVAL` a 15s, bajar probabilidades emergentes a 0.05-0.10
- **Guardia facil:** Bajar `GUARD_NEARBY_DISTANCE` a 10, subir `MOOD_DECAY_INTERVAL` a 60s (NPCs reaccionan menos)
- **Guardia dificil:** Subir `GUARD_NEARBY_DISTANCE` a 25, bajar `MOOD_DECAY_INTERVAL` a 20s (NPCs reaccionan mucho)

---

## 8. Acceptance Criteria

### Funcionales

- [ ] AC-1: Al iniciar partida, cada NPC tiene un archetype y mood asignado. Log muestra `[NPC-PERSONALITY] Profiles initialized:` con distribucion variada.
- [ ] AC-2: Dos NPCs con diferentes archetypes (ej: athletic vs loner) en la misma fase y zona eligen acciones diferentes con frecuencia medible. Athletic elige `exercise`/`shadowbox` >3x mas que loner.
- [ ] AC-3: Cuando el guardia se acerca (<15u) a un NPC troublemaker por >5s, el NPC cambia a mood `rebellious` o `agitated`. Log muestra `[NPC-MOOD]`.
- [ ] AC-4: NPCs en mood `rebellious` disparan acciones anti-guard (taunt, argue, block_path). Log muestra `[NPC-EMERGENT]`.
- [ ] AC-5: NPCs en mood `nervous` disparan fidget/pace. Log muestra `[NPC-EMERGENT]`.
- [ ] AC-6: Las acciones emergentes interrumpen la accion actual por su duracion y luego el NPC retoma la accion previa.
- [ ] AC-7: Los cooldowns previenen repeticion excesiva de emergentes. Un NPC no puede hacer `taunt_guard` mas de 1 vez cada 45s.
- [ ] AC-8: El mood decae naturalmente a `calm` despues de 30s sin trigger.
- [ ] AC-9: La anti-repeticion funciona: un NPC que acaba de hacer `yard_exercise` tiene 70% menos probabilidad de elegirlo en el proximo reassign.
- [ ] AC-10: Las nuevas acciones del catalogo (pushups, pace_nervous, stare_wall, whisper_secret, argue_loud, sit_floor, read_book, idle_window) aparecen en assignments de fases 4 y 7.

### De integracion

- [ ] AC-11: El evento `npc:emergent` llega a Unity y el NPC ejecuta la animacion correspondiente.
- [ ] AC-12: El evento `npc:mood_shift` llega a Unity y el NPC idle muestra el `animHint` correcto.
- [ ] AC-13: Backend compila sin errores (`npx tsc --noEmit` exitoso).
- [ ] AC-14: El sistema no degrada performance: tick loop se mantiene <5ms con 20 NPCs + personality system.

### De experiencia

- [ ] AC-15: En una partida de 4 jugadores, el guardia no puede distinguir NPCs de jugadores en las primeras 2 fases (validacion subjetiva en playtest).
- [ ] AC-16: Al observar el patio durante hora libre, se ven NPCs con comportamientos distintos (algunos hacen ejercicio, otros charlan, otros caminan solos, alguno provoca al guardia si esta cerca).

---

## Apendice A: Lista Completa de Animaciones para el Animator

### Estados requeridos en el Animator Controller

Estos son los **13 estados unicos** que debe tener el Animator Controller del NPC.
El backend envia un `animTrigger` (string), Unity lo mapea a uno de estos estados via `CrossFade()`.

| # | Estado Animator    | Descripcion                              | Tipo de movimiento |
|---|-------------------|------------------------------------------|--------------------|
| 1 | `Idle`            | Parado sin hacer nada                    | Estacionario       |
| 2 | `Walking`         | Caminando (NavMesh controla velocidad)   | Locomotion         |
| 3 | `Salute`          | Saludo con mano / gesto de reconocimiento| Estacionario       |
| 4 | `Talking`         | Hablando de pie (gestos con manos)       | Estacionario       |
| 5 | `Sitting`         | Sentado generico (silla, piso, cama)     | Estacionario       |
| 6 | `SittingTalking`  | Sentado hablando con alguien             | Estacionario       |
| 7 | `SeatedIdle`      | Sentado quieto (banca, leyendo)          | Estacionario       |
| 8 | `TellingSecret`   | Susurrando / contando secreto            | Estacionario       |
| 9 | `Rummaging`       | Buscando / inspeccionando algo           | Estacionario       |
| 10| `Opening`         | Abriendo/cargando (puerta, maquina)      | Estacionario       |
| 11| `ButtonPushing`   | Trabajando en mesa/maquina               | Estacionario       |
| 12| `PushUp`          | Haciendo flexiones / ejercicio           | Estacionario       |
| 13| `Punching`        | Golpeando / shadowboxing                 | Estacionario       |
| 14| `Attack`          | Patada / movimiento agresivo             | Estacionario       |
| 15| `LyingDown`       | Acostado (despierto, moviéndose)         | Estacionario       |
| 16| `LayingPose`      | Acostado dormido (sin movimiento)        | Estacionario       |

### Mapeo completo: Backend trigger → Animator State

Cada fila es un trigger que el backend puede enviar. Usa esta tabla para configurar el `MapTriggerToStateName()` en `NPCBehaviorController.cs` (ya esta implementado).

**Core (locomotion)**

| Backend trigger  | Animator State | Usado en                    |
|------------------|----------------|-----------------------------|
| `idle`           | `Idle`         | Todas las fases, fallback   |
| `walk`           | `Walking`      | Transiciones, movimiento    |
| `walk_slow`      | `Walking`      | Fase 1, transiciones        |
| `Walking`        | `Walking`      | Cafeteria flow, looping     |

**Social (interacciones entre NPCs)**

| Backend trigger   | Animator State   | Usado en                           |
|-------------------|------------------|------------------------------------|
| `Salute`          | `Salute`         | Fase 1 greet, fist bump            |
| `talk_standing`   | `Talking`        | Todas las fases (social parado)    |
| `talk_seated`     | `SittingTalking` | Fase 4/7 cafeteria charla          |
| `whisper`         | `TellingSecret`  | Emergent, yard_whisper_secret      |
| `whisper_seated`  | `TellingSecret`  | Fase 4/7 celdas                    |
| `argue`           | `Talking`        | Emergent, yard_argue_loud          |
| `nod`             | `Salute`         | Emergent nod_greeting              |
| `fist_bump`       | `Salute`         | Emergent fist_bump                 |

**Expresiones idle (sin desplazamiento)**

| Backend trigger   | Animator State | Usado en                          |
|-------------------|----------------|-----------------------------------|
| `stretch`         | `Idle`         | Fase 1, transiciones, emergent    |
| `yawn`            | `Idle`         | Fase 1, transiciones, emergent    |
| `sigh`            | `Idle`         | Emergent sigh                     |
| `fidget`          | `Idle`         | Emergent fidget (mood nervous)    |
| `look_around`     | `Idle`         | Emergent, fase 1 corridor         |
| `lean_think`      | `Idle`         | Emergent (schemer/loner)          |
| `lean_wall`       | `Idle`         | Fase 4/7 patio, fase 1 loner     |
| `crack_knuckles`  | `Idle`         | Emergent (troublemaker/athletic)  |
| `pace`            | `Walking`      | Emergent pace, yard_pace_nervous  |
| `idle_window`     | `Idle`         | Fase 4/7, emergent check_window   |
| `idle_queue`      | `Idle`         | Cafeteria espera                  |
| `idle_check`      | `Idle`         | Lavanderia                        |

**Sentado**

| Backend trigger  | Animator State   | Usado en                         |
|------------------|------------------|----------------------------------|
| `sit_eat`        | `Sitting`        | Fase 2/5/8 cafeteria             |
| `sit_eat_talk`   | `SittingTalking` | Fase 2/5/8 cafeteria social      |
| `sit_bench`      | `SeatedIdle`     | Fase 4/7 patio                   |
| `sit_cards`      | `Sitting`        | Fase 4/7 patio (jugar cartas)    |
| `sit_idle`       | `Sitting`        | Fase 4/7 comedor                 |
| `sit_bed_edge`   | `Sitting`        | Fase 4/7/9 celdas                |
| `sit_floor`      | `Sitting`        | Emergent, fase 4/7 patio         |
| `read_book`      | `SeatedIdle`     | Fase 4/7 celdas                  |

**Trabajo / Interaccion con objetos**

| Backend trigger   | Animator State  | Usado en                        |
|-------------------|-----------------|---------------------------------|
| `serve_self`      | `Rummaging`     | Fase 2/5/8 cafeteria counter    |
| `inspect`         | `Rummaging`     | Fase 3/6 taller, emergent       |
| `deposit_tray`    | `Opening`       | Fase 2/5/8 cafeteria trash      |
| `load_machine`    | `Opening`       | Fase 3/4/6/7 lavanderia         |
| `work_bench`      | `ButtonPushing` | Fase 3/6 taller                 |
| `fold_clothes`    | `Idle`          | Fase 3/4/6/7 lavanderia         |
| `carry_tray`      | `Walking`       | Fase 2/5/8 cafeteria → trash    |
| `carry_box`       | `Walking`       | Fase 3/6 taller (looping)       |
| `carry_basket`    | `Walking`       | Fase 3/4/6/7 lavanderia (loop)  |

**Atletico / Combate**

| Backend trigger   | Animator State | Usado en                         |
|-------------------|----------------|----------------------------------|
| `exercise`        | `PushUp`       | Fase 4/7 patio                   |
| `PushUp`          | `PushUp`       | Emergent pushups, fase 4/7       |
| `shadowbox`       | `Punching`     | Fase 4/7 patio                   |
| `shadow_punch`    | `Punching`     | Emergent (athletic/troublemaker) |
| `fight_stance`    | `Punching`     | Emergent fake_fight              |
| `kick`            | `Attack`       | Fase 4/7 patio, emergent         |
| `kick_wall`       | `Attack`       | Emergent kick_wall               |

**Anti-guardia**

| Backend trigger   | Animator State | Usado en                          |
|-------------------|----------------|-----------------------------------|
| `block_stance`    | `Idle`         | Emergent block_path               |
| `taunt`           | `Talking`      | Emergent taunt_guard              |
| `yell`            | `Talking`      | Emergent commotion                |

**Acostado / Dormir**

| Backend trigger   | Animator State | Usado en                        |
|-------------------|----------------|---------------------------------|
| `lie_down`        | `LyingDown`    | Fase 4/7 celdas                 |
| `toss_turn`       | `LyingDown`    | Fase 9 luces apagadas           |
| `sleep`           | `LayingPose`   | Fase 9 luces apagadas           |

### Prioridad de implementacion

Si no se pueden animar todos los estados de una vez, esta es la prioridad:

1. **Criticos** (el juego se ve roto sin estos): `Idle`, `Walking`, `Sitting`, `LyingDown`, `LayingPose`
2. **Importantes** (se nota la falta): `Talking`, `Salute`, `PushUp`, `Punching`
3. **Polish** (mejoran inmersion): `SittingTalking`, `SeatedIdle`, `TellingSecret`, `Rummaging`, `Opening`, `ButtonPushing`, `Attack`

Los triggers que mapean a `Idle` o `Walking` funcionan automaticamente sin animaciones custom. Solo necesitan un estado con nombre diferente si quieres animaciones distintas (ej: `stretch` podria tener su propia animacion en vez de usar `Idle`).

---

## Apendice B: Setup Requerido

### Backend
1. El archivo `npc-personality.ts` ya esta en `backend/src/game/systems/`
2. Se integra automaticamente via `jail-routine.ts` (constructor crea la instancia)
3. Los callbacks se wirean en `room-manager.ts`
4. No se requiere configuracion adicional

### Unity
1. **NPCBehaviorController.cs**: Ya actualizado con `PlayEmergentAction()` y `ApplyMoodHint()`
2. **NPCNetworkSync.cs**: Ya escucha `npc:emergent` y `npc:mood_shift`
3. **NetworkManager.cs**: Ya tiene events y socket handlers registrados
4. **NetworkTypes.cs**: Ya tiene `NPCEmergentData` y `NPCMoodShiftData`
5. **Animator Controller**: Configurar los 16 estados del Apendice A. Ver prioridad de implementacion.

### Waypoints (Unity Scene)
No se requieren waypoints nuevos. Las acciones emergentes se ejecutan in-place (sin navegacion). Las acciones nuevas del catalogo usan tags existentes (`yard_exercise_area_`, `yard_wall_lean_`, etc.).
