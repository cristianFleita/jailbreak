# Sistema de Rutina/Fases + NPC Libre Albedrío

> **Status**: Designed  
> **Author**: Cris + Claude  
> **Last Updated**: 2026-04-26
> **Cubre sistemas**: #4 Rutina/Fases + #13 NPC Rutina/NavMesh  
> **Implementa Pilar**: "La rutina es la cárcel" — los jugadores prisioneros deben imitar a los NPCs para sobrevivir

---

## 1. Overview

Este sistema gestiona el "reloj" de la partida: la jornada carcelaria dividida en 9 fases, cada una con duración fija, zona activa y comportamientos esperados. Los NPCs prisioneros (17–19 de ellos) siguen la rutina automáticamente usando pathfinding local en Unity. El backend es responsable de **asignar qué acción hace cada NPC** (incluyendo waypoint destino y duración), pero **no emite posiciones**: el movimiento real ocurre en cada instancia del juego via NavMeshAgent.

La novedad arquitectónica central es el **sistema de libre albedrío**: dentro de cada fase, los NPCs tienen un pool de acciones posibles con pesos de probabilidad. El backend sortea periódicamente nuevas acciones, incluyendo interacciones sociales entre pares de NPCs. Esto da variedad orgánica sin que el servidor tenga que transmitir un stream continuo de posiciones.

**Entidades en la partida:**

- 1 jugador guardia (human-controlled, no usa este sistema)
- 1–3 jugadores prisioneros (human-controlled, deben imitar la rutina)
- 17–19 NPCs prisioneros (este sistema los controla)
- **Total: siempre 20 entidades**

---

## 2. Player Fantasy

**Para el jugador prisionero:** La rutina no se siente como un tutorial — se siente como presión social constante. Estás rodeado de NPCs que saben exactamente qué hacer y dónde estar. Si te quedás quieto o vas a la zona equivocada, el contraste te delata. Los NPCs charlan, trabajan, comen, vuelven a su celda, se quedan parados o duermen — y vos tenés que encajar en esa normalidad mientras planificás la fuga.

**Para el jugador guardia:** Los NPCs le dan cobertura a los prisioneros. La cancha con los personajes moviéndose crea ruido visual real. Detectar quién es jugador y quién es NPC requiere atención sostenida, no solo mirar el minimapa. Un NPC que dobla bandeja y se va a su celda a tiempo es ruido. Un "NPC" que se queda parado tres segundos demasiado cerca de la ventilación es señal.

---

## 3. Detailed Rules

### 3.1 Fases de la Jornada


| #   | Fase           | Hora ficticia | Duración real | Zona                                    | Sub-zonas                          |
| --- | -------------- | ------------- | ------------- | --------------------------------------- | ---------------------------------- |
| 1   | Inicio         | 06:00         | 30 s          | Celda                                   | —                                  |
| 2   | Desayuno       | 06:30         | 90 s          | Comedor                                 | —                                  |
| 3   | Trabajo        | 08:00         | 90 s          | Taller / Lavandería                     | Taller, Lavandería                 |
| 4   | Hora libre     | 09:30         | 120 s         | Patio / Cocina / Lavandería / Celdas   | Patio, Cocina, Lavandería, Celdas  |
| 5   | Almuerzo       | 11:30         | 90 s          | Comedor                                 | — (mismo que Desayuno)             |
| 6   | Trabajo        | 13:00         | 120 s         | Taller / Lavandería                     | Taller, Lavandería                 |
| 7   | Hora libre     | 15:00         | 90 s          | Patio / Cocina / Lavandería / Celdas   | Patio, Cocina, Lavandería, Celdas  |
| 8   | Cena           | 16:30         | 90 s          | Comedor                                 | — (mismo que Desayuno)             |
| 9   | Encierro / Recuento final | 18:00 → 00:00 | 60 s | Celdas | `cell_area_01..08` |


**Reglas de transición:**

- El servidor es responsable del timer. Al expirar una fase, emite `phase:start` con la nueva fase.
- Antes de `phase:start`, emite `phase:warning` 10 segundos antes (silbato de aviso).
- Los NPCs reciben sus assignments en el payload de `phase:start` y navegan a sus destinos inmediatamente.
- Si un NPC está en medio de un LOOPING action, completa el ciclo actual antes de navegar a la nueva zona (máximo 5s de gracia, luego navega de inmediato).

### 3.2 Responsabilidades Backend vs Unity


| Responsabilidad                   | Backend (Node.js)    | Unity (Cliente)       |
| --------------------------------- | -------------------- | --------------------- |
| Timer de fases                    | ✅ Autoritativo       | ❌ Solo muestra        |
| Asignación de acción NPC          | ✅ Sortea y emite     | ❌ Solo ejecuta        |
| Waypoint/Zona (ID string)         | ✅ Envía el ID        | ✅ Resuelve ID→Vector3 |
| Área asignada (`zoneId`)          | ✅ Envía `yard*`, comedor/lavandería o `cell_area_01..08` | ✅ Resuelve punto determinístico por `seed` dentro del área |
| Pathfinding / movimiento          | ❌ No calcula         | ✅ NavMeshAgent        |
| Animaciones NPC                   | ❌ No controla        | ✅ Animator local      |
| Interacción social (pairing)      | ✅ Empareja NPCs      | ✅ Sincroniza llegada  |
| Zona del jugador (para camuflaje) | ✅ Valida server-side | ✅ Envía posición      |


### 3.3 Catálogo de Acciones por Fase

Cada acción define:

- `actionId` — identificador único string
- `type` — SOLO | SOCIAL | LOOPING | IDLE
- `animation` — trigger del Animator
- `waypointTag` — prefijo de los waypoints válidos para acciones legacy/especializadas
- `zoneId` — área lógica que Unity resuelve localmente (`yard`, `yard_benches`, `yard_exercise`, `cafeteria`, `cafeteria_seating`, `zone_laundry_*`, `cell_area_01..08`)
- `seed` — semilla enviada por backend para que Unity elija un punto determinístico dentro de `zoneId`
- `weight` — probabilidad relativa de selección (mayor = más frecuente)
- `minDuration` / `maxDuration` — rango en segundos

---

#### Fase 1 — Inicio | Celda → Comedor (transición)

> Cada NPC hace spawn parado afuera de su celda (`cell_door_exit_XX`). Durante ~30 segundos sucede lo siguiente de forma orgánica y no ordenada:
>
> - **Algunos** se saludan con el vecino de celda y después charlan un momento.
> - **Otros** simplemente caminan hacia la zona de la cafetería sin interacción (caminar no tiene duración propia).
> - **Un grupo** se junta cerca de la entrada de la cafetería y sigue conversando ahí antes de entrar.
> - **Los idle** se estiran o bostezan en su lugar hasta que el timer de la fase los fuerza a moverse.
>
> El resultado visual es una dispersión natural: algunos ya están en la puerta del comedor a los 10s, otros recién salen de sus celdas a los 25s. No hay fila ni orden estricto — es una transición orgánica al Desayuno.
>
> **Waypoints con posición fija:** `cell_door_exit_01..20` (spawn) y `cafeteria_entrance_spot_01..06` (zona de espera en la puerta).  
> **Sin waypoint fijo:** `greet_neighbor`, `talk_standing`, `idle_stretch`, `idle_yawn` — el NPC permanece donde está o navega al Transform de su pareja.  
> **Regla de movimiento:** caminar a un waypoint no tiene duración asignada; el timer solo corre una vez que el NPC llega y ejecuta la acción.


| ActionId                 | Type    | Waypoint                         | Animation     | Weight | Duración                        |
| ------------------------ | ------- | -------------------------------- | ------------- | ------ | ------------------------------- |
| `spawn_at_door`          | IDLE    | `cell_door_exit_01..20`          | idle          | 100    | —                               |
| `greet_neighbor`         | SOCIAL  | *(posición del partner)*         | greet         | 35     | 3–4s                            |
| `talk_standing`          | SOCIAL  | *(posición del partner)*         | talk_standing | 30     | 6–7s                            |
| `idle_stretch`           | IDLE    | *(sin mover)*                    | stretch       | 15     | 2–4s                            |
| `idle_yawn`              | IDLE    | *(sin mover)*                    | yawn          | 10     | 1–3s                            |
| `walk_to_cafeteria_area` | ONESHOT | `cafeteria_entrance_spot_01..06` | walk_slow     | 40     | *(sin duración — solo caminar)* |
| `talk_at_cafeteria_door` | SOCIAL  | `cafeteria_entrance_spot_01..06` | talk_standing | 25     | 6–7s                            |


> **Notas de implementación:**
>
> - `greet_neighbor` y `talk_at_cafeteria_door` se distinguen por el waypoint destino: el primero usa la posición del partner cerca de la celda, el segundo se resuelve en la zona de la puerta del comedor.
> - El backend puede encadenar `walk_to_cafeteria_area` → `talk_at_cafeteria_door` en un mismo NPC al inicio de la fase para lograr el comportamiento "camina hasta la puerta y charla ahí".
> - Al expirar la Fase 1, todos los NPCs reciben el assignment de Desayuno (Fase 2). Los que ya están en la puerta simplemente entran; los que aún están en celdas navegan directamente al counter.

---

#### Fase 2 — Desayuno | Comedor

#### Fase 5 — Almuerzo | Comedor

#### Fase 8 — Cena | Comedor

> Las tres fases de comedor comparten el mismo pool de acciones y el mismo flujo ordenado obligatorio.

**Flujo obligatorio (cada NPC lo recorre en este orden):**

```
[Opcional] Esperar afuera y hablar
       ↓
  Caminar al counter        ← sin duración
       ↓
  Agarrar la comida         ← 4–6s
       ↓
  Caminar a un asiento      ← sin duración
       ↓
  Sentarse a comer          ← 10–15s
   (con posible charla      ← +6–7s si hay partner disponible)
       ↓
  Caminar al depósito       ← sin duración
       ↓
  Tirar la bandeja          ← 3–5s
[Opcional] Charlar al salir ← 6–7s
```

> **Regla de movimiento:** caminar entre puntos (counter → asiento → depósito) no tiene duración asignada. El timer solo corre una vez que el NPC ejecuta la acción en destino.

**Catálogo de acciones:**


| ActionId                 | Type    | Animation             | WaypointTag                     | Weight | Duración                        | Orden en flujo    |
| ------------------------ | ------- | --------------------- | ------------------------------- | ------ | ------------------------------- | ----------------- |
| `cafe_wait_outside_talk` | SOCIAL  | Talk_Standing         | `cafeteria_entrance_spot`_      | 20     | 6–7s                            | Opcional — inicio |
| `cafe_walk_to_counter`   | ONESHOT | Walk                  | `cafeteria_counter_01..06`      | 100    | *(sin duración — solo caminar)* | 1° obligatorio    |
| `cafe_grab_food`         | IDLE    | Serve_Self            | `cafeteria_counter_01..06`      | 100    | 4–6s                            | 2° obligatorio    |
| `cafe_walk_to_seat`      | ONESHOT | Walk                  | `cafeteria_seat_01..16`         | 100    | *(sin duración — solo caminar)* | 3° obligatorio    |
| `cafe_sit_eat`           | IDLE    | Sit_Eat               | `cafeteria_seat_01..16`         | 60     | 10–15s                          | 4° obligatorio    |
| `cafe_sit_eat_talk`      | SOCIAL  | Sit_Eat + Talk_Seated | mismo seat + vecino             | 40     | 10–15s (chat interno 6–7s)      | 4° alternativo    |
| `cafe_walk_to_trash`     | ONESHOT | Walk + Carry_Tray     | `cafeteria_tray_deposit_01..04` | 100    | *(sin duración — solo caminar)* | 5° obligatorio    |
| `cafe_clear_tray`        | IDLE    | Deposit_Tray          | `cafeteria_tray_deposit_01..04` | 100    | 3–5s                            | 6° obligatorio    |
| `cafe_talk_after_trash`  | SOCIAL  | Talk_Standing         | `cafeteria_tray_deposit_01..04` | 25     | 6–7s                            | Opcional — final  |


> **Notas:**
>
> - `cafeteria_seat_01..16` — máx 2 ocupantes por mesa de 2, máx 4 por mesa de 4.
> - El backend encadena el flujo completo en el assignment inicial de la fase (counter → seat → trash). Las acciones opcionales se insertan como pasos intermedios con probabilidad definida por weight.
> - `cafe_sit_eat_talk` requiere partner disponible en asiento adyacente; si no hay partner, se usa `cafe_sit_eat` por defecto.
> - Los tiempos de comer (10–15s) ya contemplan posibles charlas entrelazadas; no se suman por separado.

---

#### Fase 3 — Trabajo (1er turno) | Taller / Lavandería

> NPCs divididos en dos sub-zonas al inicio de la fase. Permanecen en su sub-zona toda la fase.  
> Distribución: ~9 NPCs taller / ~9 lavandería.
> **Nota Técnica:** Ya no usamos *waypoints* predefinidos desde el backend. Ahora asignamos **Zonas de Actividad** (`ZoneId`) y el cliente de Unity resuelve localmente el punto exacto usando su `ZoneRegistry` y NavMesh.

**Sub-zona: Taller**

> Los NPCs eligen libremente entre estas acciones usando el sistema de libre albedrío por zonas.

| ActionId                 | Type    | Animation     | ZoneId               | Weight | Duration |
| ------------------------ | ------- | ------------- | -------------------- | ------ | -------- |
| `work_use_workbench`     | IDLE    | Work_Bench    | `zone_workshop_bench`| 45     | 20–50s   |
| `work_inspect_cabinets`  | IDLE    | Inspect       | `zone_workshop_cab`  | 35     | 10–20s   |
| `work_talk_coworker`     | SOCIAL  | Talk_Standing | `zone_workshop_chat` | 20     | 8–15s    |


**Sub-zona: Lavandería**

> La lavandería usa un **flujo secuencial estricto**, simulando un proceso de trabajo real. El backend envía esto como un `actionSequence`.

**Flujo obligatorio:**
```
  Agarrar ropa del bulto    ← 3–5s (activa prop ropa en mano)
       ↓
  Cargar lavadoras          ← 15–20s (animación de progreso, resulta en ropa doblada)
       ↓
  Dejar ropa en estante     ← 3–5s (animación de depósito)
[Opcional] Charlar          ← 8–15s
```

| ActionId                 | Type    | Animation          | ZoneId              | Weight | Duration | Orden en flujo |
| ------------------------ | ------- | ------------------ | ------------------- | ------ | -------- | -------------- |
| `laundry_grab_clothes`   | ONESHOT | Rummaging          | `zone_laundry_pile` | 100    | 3–5s     | 1° obligatorio |
| `laundry_load_washer`    | IDLE    | Load_Machine       | `zone_laundry_wash` | 100    | 15–20s   | 2° obligatorio |
| `laundry_store_clothes`  | IDLE    | Opening            | `zone_laundry_shelf`| 100    | 3–5s     | 3° obligatorio |
| `laundry_talk_coworker`  | SOCIAL  | Talk_Standing      | `zone_laundry_chat` | 30     | 8–15s    | Opcional       |

---

#### Fase 4 — Hora libre | Patio / Cocina / Lavandería / Celdas

> Fase de máxima variedad. Los NPCs eligen libremente entre cuatro sub-zonas: patio, cocina/comedor, lavandería y celdas.
> Distribución inicial: ~5 patio / ~5 celdas / ~5 lavandería / ~4 cocina.
> A diferencia de Trabajo, los NPCs pueden cambiar de sub-zona durante la fase. En cada `npc:reassign`, un NPC puede recibir una sub-zona distinta y caminar hacia allí.
>
> El patio usa el contrato simplificado nuevo: el backend envía `zoneId`, `seed`, `animTrigger` y duración. Unity resuelve localmente un punto determinístico dentro del área registrada en `ZoneRegistry`.

**Sub-zona: Patio**

| ActionId           | Type | Animation  | ZoneId          | Weight | Duration |
| ------------------ | ---- | ---------- | --------------- | ------ | -------- |
| `yard_idle`        | IDLE | idle       | `yard`          | 35     | 20–45s   |
| `yard_bench_idle`  | IDLE | idle       | `yard_benches`  | 30     | 20–45s   |
| `yard_exercise`    | IDLE | exercise   | `yard_exercise` | 20     | 15–35s   |
| `yard_shadow_box`  | IDLE | shadowbox  | `yard_exercise` | 10     | 10–25s   |
| `yard_lean_wall`   | IDLE | lean_wall  | `yard`          | 5      | 15–30s   |

Zonas Unity requeridas: `yard`, `yard_benches`, `yard_exercise`.

**Sub-zona: Cocina / Comedor** *(quedarse, charlar, no comer)*

| ActionId               | Type   | Animation     | ZoneId               | Weight | Duration |
| ---------------------- | ------ | ------------- | -------------------- | ------ | -------- |
| `free_cafe_sit_talk`   | SOCIAL | talk_seated   | `cafeteria_seating`  | 40     | 15–40s   |
| `free_cafe_sit_idle`   | IDLE   | sit_idle      | `cafeteria_seating`  | 35     | 10–30s   |
| `free_cafe_stand_chat` | SOCIAL | talk_standing | `cafeteria`          | 25     | 10–25s   |

**Sub-zona: Lavandería** *(ropa personal, sigue el flujo estructurado de trabajo)*

| ActionId                 | Type    | Animation      | ZoneId                | Weight | Duration | Orden en flujo |
| ------------------------ | ------- | -------------- | --------------------- | ------ | -------- | -------------- |
| `laundry_grab_clothes`   | ONESHOT | rummaging      | `zone_laundry_pile`   | 100    | 3–5s     | 1° obligatorio |
| `laundry_load_washer`    | IDLE    | load_machine   | `zone_laundry_wash`   | 100    | 15–20s   | 2° obligatorio |
| `laundry_store_clothes`  | IDLE    | store_clothes  | `zone_laundry_shelf`  | 100    | 3–5s     | 3° obligatorio |
| `laundry_talk_coworker`  | SOCIAL  | talk_standing  | `zone_laundry_chat`   | 30     | 8–15s    | Opcional       |

**Sub-zona: Celdas**

| ActionId            | Type | Animation | ZoneId              | Weight | Duration |
| ------------------- | ---- | --------- | ------------------- | ------ | -------- |
| `cell_stand_idle`   | IDLE | idle      | `cell_area_01..08`  | 100    | 25–60s   |
| `cell_sleep`        | IDLE | sleep     | `cell_area_01..08`  | 100    | 25–60s   |

`cell_sleep` ya se envía desde backend. Unity resolverá la cama/catre dentro de la celda en una iteración posterior.


---

#### Fase 6 — Trabajo (2do turno) | Taller / Lavandería

> Idéntico a Fase 3. Los NPCs pueden ser reasignados a distinta sub-zona que el turno anterior (libre albedrío de fase).

*(Mismo catálogo de acciones que Fase 3 — ver arriba)*

---

#### Fase 7 — Hora libre (2da) | Patio / Cocina / Lavandería / Celdas

> Idéntico a Fase 4. Los NPCs pueden elegir o cambiar entre patio, celdas, lavandería y cocina.

*(Mismo catálogo de acciones que Fase 4 — ver arriba)*


---

#### Fase 8 — Encierro / Recuento final | Celdas

> Fase final breve. Sin interacciones sociales. Todos los NPCs vuelven a una celda asignada por backend. El backend puede enviar idle de pie o sleep; Unity implementará después el resolver visual de cama para `cell_sleep`.


| ActionId            | Type | Animation | ZoneId              | Weight | Duration |
| ------------------- | ---- | --------- | ------------------- | ------ | -------- |
| `cell_stand_idle`   | IDLE | idle      | `cell_area_01..08`  | 100    | 25–60s   |
| `cell_sleep`        | IDLE | sleep     | `cell_area_01..08`  | 100    | 25–60s   |

**Zonas Unity requeridas para Encierro:** `cell_area_01` hasta `cell_area_08`.


---

### 3.4 Eventos Socket.io

#### `phase:warning` (servidor → todos) — 10s antes de la transición

```json
{
  "nextPhase": 4,
  "nextPhaseName": "Hora libre",
  "warningInSeconds": 10
}
```

#### `phase:start` (servidor → todos) — al inicio de cada fase

```json
{
  "phase": 4,
  "phaseName": "Hora libre",
  "duration": 120,
  "zone": "Patio / Cocina / Lavandería / Celdas",
  "npcAssignments": [
    {
      "npcId": "npc_01",
      "actionId": "yard_idle",
      "animTrigger": "idle",
      "zoneId": "yard",
      "seed": 331245,
      "duration": 28
    },
    {
      "npcId": "npc_02",
      "actionId": "yard_exercise",
      "animTrigger": "exercise",
      "zoneId": "yard_exercise",
      "seed": 331246,
      "duration": 24
    },
    {
      "npcId": "npc_03",
      "actionId": "cell_sleep",
      "animTrigger": "sleep",
      "zoneId": "cell_area_02",
      "seed": 331247,
      "duration": 41,
      "subZone": "celdas"
    },
    {
      "npcId": "npc_04",
      "actionId": "free_cafe_sit_idle",
      "animTrigger": "idle",
      "zoneId": "cafeteria_seating",
      "seed": 331248,
      "duration": 22,
      "subZone": "cocina"
    },
    {
      "npcId": "npc_05",
      "actionId": "laundry_sequence",
      "animTrigger": "idle",
      "duration": 34,
      "subZone": "lavanderia",
      "actionSequence": [
        {
          "actionId": "laundry_grab_clothes",
          "animTrigger": "rummaging",
          "zoneId": "zone_laundry_pile",
          "seed": 331249,
          "duration": 4
        },
        {
          "actionId": "laundry_load_washer",
          "animTrigger": "load_machine",
          "zoneId": "zone_laundry_wash",
          "seed": 331250,
          "duration": 18
        }
      ]
    }
  ]
}
```

#### `npc:reassign` (servidor → todos) — cada 20–30s (libre albedrío)

```json
{
  "timestamp": 1712567890,
  "assignments": [
    {
      "npcId": "npc_01",
      "actionId": "yard_lean_wall",
      "animTrigger": "lean_wall",
      "zoneId": "yard",
      "seed": 441001,
      "duration": 25
    },
    {
      "npcId": "npc_02",
      "actionId": "cell_stand_idle",
      "animTrigger": "idle",
      "zoneId": "cell_area_05",
      "seed": 441002,
      "duration": 36,
      "subZone": "celdas"
    }
  ]
}
```

> Solo se incluyen los NPCs que cambian de acción. Los que continúan su acción actual no aparecen en el payload.

#### `phase:zone_check` (servidor → jugador específico) — cuando el servidor detecta zona incorrecta

```json
{
  "playerId": "player_02",
  "currentZone": "taller",
  "expectedZone": "free_time",
  "allowedZones": ["patio", "cocina", "lavanderia", "celdas"],
  "phase": 4,
  "graceSeconds": 5
}
```

---

### 3.5 Estructura de Zonas en Unity

El backend solo conoce IDs string y semillas; nunca transmite coordenadas `Vector3`.

Unity configura las áreas en `ZoneRegistry.cs` como `ZoneEntry` con `zoneId` + `BoxCollider`. Para Hora libre y Encierro, `NPCBehaviorController` usa `ZoneRegistry.GetDeterministicPoint(zoneId, seed)` y navega con `NavMeshAgent` a ese punto.

Zonas requeridas:

- Patio: `yard`, `yard_benches`, `yard_exercise`
- Cocina/comedor en Hora libre: `cafeteria`, `cafeteria_seating`
- Lavandería en Hora libre: `zone_laundry_pile`, `zone_laundry_wash`, `zone_laundry_shelf`, `zone_laundry_chat`
- Celdas de Hora libre y Encierro: `cell_area_01` ... `cell_area_08`

```csharp
// Contrato actual para Hora libre y Encierro
public struct NPCActionData
{
    public string npcId;
    public string actionId;
    public string animTrigger;
    public string zoneId;
    public uint seed;
    public float duration;
}

// Resolución local en Unity
var destination = zoneRegistry.GetDeterministicPoint(data.zoneId, data.seed);
agent.SetDestination(destination);
```

Para acostarse/dormir se agregará después un resolver especializado de cama dentro de cada celda. Hasta entonces, `cell_sleep` viaja por red con `animTrigger = sleep`, pero Unity puede resolverlo temporalmente como fallback local.

### 3.6 Transición Orgánica entre Fases

> **Problema que resuelve:** Si todos los NPCs se mueven a su zona de destino en el mismo instante al sonar el silbato, el guardia puede inferir quién es jugador por ser el que tarda en reaccionar o va en dirección distinta. Este sistema elimina esa señal.

#### Perfiles de salida

En fases con flujo secuencial o transición orgánica, el backend puede asignar a cada NPC un `actionSequence` que incluye un **prefijo de transición orgánica** antes de su acción de fase. Hora libre puede usar `actionSequence` para lavandería, cocina/comedor social y prefijos de transición; las acciones directas de patio y celda siguen usando `zoneId` + `seed`.

| Perfil | % NPCs | Linger (in-place) | Detour corredor | Chat en corredor |
|--------|--------|-------------------|-----------------|------------------|
| **Salida temprana** | 30% | 0–5 s | No | — |
| **Salida normal** | 50% | 5–15 s | 40% de prob. | 30% de prob. |
| **Rezagado** | 20% | 15–20 s | 60% de prob. | 30% de prob. |

El linger usa animaciones idle / stretch / yawn para que el NPC se vea "distraído" antes de moverse.

#### Waypoints de corredor

Los NPCs que toman el desvío navegan a un `corridor_idle_XX` (10 slots disponibles en la prisión) antes de continuar hacia su destino de fase. Algunos se detienen a conversar brevemente allí (`corridor_chat_stop`, 4–11 s).

#### Flujo completo de un NPC con desvío

```
phase:start recibido
     │
     ▼
[LINGER 8s]  ← in-place, anim "stretch"
     │
     ▼
[WALK → corridor_idle_04]  ← walk-only, sin duración
     │
     ▼
[CHAT STOP 6s]  ← en el pasillo, anim "talk_standing"
     │
     ▼
[WALK → workshop_bench_02]  ← destino de fase real
     │
     ▼
[WORK_BENCH 35s]  ← acción de fase
```

#### Comportamiento de LOOPING

Las acciones de tipo LOOPING, si se reactivan en un pool futuro, **no reciben prefijo de transición**: su animación de loop ya es visualmente orgánica y meterles un linger los haría comenzar en el lugar equivocado.

#### Libre albedrío por sub-zona (Hora libre y Encierro)

Durante el reassign (cada 20s), los NPCs de Hora libre pueden cambiar entre patio, celdas, lavandería y cocina. En Encierro pueden refrescar `cell_stand_idle` o `cell_sleep` dentro de la misma celda asignada.

```
NPC en Hora libre:
Roll de sub-zona → patio
Roll de acción → yard_exercise
→ Backend emite npc:reassign con zoneId = yard_exercise, seed y animTrigger = exercise
→ Unity resuelve un punto dentro de yard_exercise

NPC en Encierro:
Celda asignada → cell_area_07
→ Backend emite npc:reassign con zoneId = cell_area_07, seed y animTrigger = idle o sleep
→ Unity resuelve idle local ahora; cama/sleep visual queda para iteración posterior
```

---

### 3.7 Libre Albedrío — Lógica Backend

```
Al inicio de cada fase:
  1. Para cada NPC:
     a. Si la fase tiene sub-zonas (Trabajo) → asignar sub-zona balanceada
     b. Si la fase es Hora libre → asignar sub-zona balanceada (`patio`, `celdas`, `lavanderia`, `cocina`)
     c. Si la fase es Encierro → asignar `zoneId` de celda (`cell_area_01..08`) + `seed`
     d. Seleccionar acción inicial: weighted random del pool de la fase
     e. Si la acción es SOCIAL → buscar partner disponible compatible
     f. Calcular duration = random(minDuration, maxDuration)
  2. Emitir phase:start con todos los assignments

Cada REASSIGN_INTERVAL segundos (default 20s):
  1. Para cada NPC con actionTimer < 5s (a punto de terminar):
     a. 80% probabilidad de cambiar acción
     b. Hora libre: 25% de probabilidad de cambiar sub-zona antes de elegir acción
     c. Encierro: refrescar `cell_stand_idle` o `cell_sleep`, manteniendo `cell_area_XX`
     d. Si cambia: nuevo weighted random del pool de su fase/sub-zona, excluyendo acción actual
     e. SOCIAL: skip en reassign (partner logic es complejo para incremental)
  2. Emitir npc:reassign solo con los NPCs que cambian

Restricciones:
  - No asignar waypoints exclusivos ya ocupados
  - En Hora libre: patio usa solo las acciones `yard_*` simplificadas; celdas usa `cell_stand_idle` / `cell_sleep`; lavandería conserva su secuencia; cocina conserva idle/social de comedor
  - En Encierro: solo `cell_stand_idle` o `cell_sleep` con `cell_area_01..08`
  - En Fase 1 (Inicio): solo acciones del pool de inicio
  - Sub-zona de Trabajo: NPC no cambia de sub-zona durante la fase
  - Hora libre: cualquier reassign conserva una sub-zona válida y no usa las acciones viejas complejas de patio
```

---

## 4. Formulas

```
// Selección de acción (weighted random)
total_weight = sum(action.weight for action in phase_pool)
r = random(0, total_weight)
running = 0
for action in phase_pool:
    running += action.weight
    if r < running: return action

// Distribución de sub-zonas (Fase 3/6 — Trabajo)
n_npcs = total_npcs                            // 17–19
taller_count    = floor(n_npcs / 2)            // ~9
lavanderia_count = n_npcs - taller             // ~9

// Hora libre — selección de sub-zona
n_npcs = total_npcs                            // 17–19
for each npc:
  subZone = balanced_random([patio, celdas, lavanderia, cocina])
  if subZone == patio:
    action = weighted_random([yard_idle, yard_bench_idle, yard_exercise, yard_shadow_box, yard_lean_wall])
    zoneId = action.zoneId
    seed = random_uint()
  if subZone == celdas:
    action = weighted_random([cell_stand_idle, cell_sleep])
    zoneId = assigned_cell_area[npc]
    seed = random_uint()
  if subZone == lavanderia:
    actionSequence = [laundry_grab_clothes, laundry_load_washer, laundry_store_clothes, optional(laundry_talk_coworker)]
  if subZone == cocina:
    action = weighted_random([free_cafe_sit_talk, free_cafe_sit_idle, free_cafe_stand_chat])
    zoneId = action.zoneId
    seed = random_uint()

// Asignación de celdas (Encierro)
n_npcs = total_npcs                            // 17–19
cell_areas = [
  cell_area_01(cap=2), cell_area_02(cap=3),
  cell_area_03(cap=2), cell_area_04(cap=3),
  cell_area_05(cap=2), cell_area_06(cap=3),
  cell_area_07(cap=2), cell_area_08(cap=3)
]
for each npc:
  zoneId = preferred_cell_area[npc] ?? first_available_slot(cell_areas)
  action = weighted_random([cell_stand_idle, cell_sleep])
  seed = random_uint()

// Bandwidth estimada (reemplaza npc:positions)
phase_start_payload = n_npcs × (npcId[4B] + actionId[20B] + zoneId[20B] + seed[4B] + duration[2B])
                    = 19 × 50B ≈ 950B per phase transition (~1 KB cada 90–120s)
npc_reassign_payload = avg_changed_npcs(7) × 50B ≈ 350B cada 25s
vs. antiguo npc:positions = 20 × 18B × 5/s = 1800B/s (delta 25%) ≈ ~450B/s

Reducción: de ~450 B/s continuo a ~13 B/s promedio → reducción de 35x

// Distribución de formación (Fase 1)
slot_count = 20
// Slots pre-asignados en setup de partida, no cambian entre jornadas
```

---

## 5. Edge Cases


| Caso                                                      | Qué pasa                                                                 | Resolución                                                                                                                                                         |
| --------------------------------------------------------- | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **NPC recibe reassign mientras navega**                   | El agente estaba en camino a zona A, llega reassign para zona B          | Interrumpir navegación inmediatamente. Resolver nuevo punto por `zoneId` + `seed`. Navegar a B.                                                                    |
| **Waypoint exclusivo lleno al asignar**                   | El servidor asignó `cafeteria_counter_03` pero ya hay un NPC ahí         | Unity no reserva el WP. El NPC hace Idle en su posición actual y espera hasta que el WP se libere o llegue el siguiente reassign.                                  |
| **Partner social se desconecta mid-action**               | NPC_02 estaba en `work_talk_coworker` con NPC_07 que deja de estar activo | NPC_02 recibe `Idle` trigger automáticamente al detectar que su partner ya no está activo. Queda disponible para el siguiente reassign.                            |
| **Jugador en zona equivocada**                            | Servidor detecta jugador en patio durante fase de Cena                   | Servidor emite `phase:zone_check` con 5s de gracia. Si el jugador no se mueve a la zona correcta → el guardia recibe alerta (sistema de Alertas #16).              |
| **phase:start llega antes de que NPC termine transición** | NPC está en la puerta de la celda cuando empieza Hora libre              | NPC recibe el nuevo assignment. NavMeshAgent redirige hacia el `zoneId` nuevo sin importar estado anterior.                                                        |
| **Todos los waypoints de un pool están llenos**           | 19 NPCs quieren `cafeteria_seat`_ pero solo hay 16 seats                 | Los últimos 3 NPCs reciben `cafe_wait_in_line` como fallback. El servidor itera el pool hasta encontrar acción alternativa.                                        |
| **Celda sin área configurada**                            | Unity recibe `cell_stand_idle` o `cell_sleep` con `cell_area_07`, pero falta en `ZoneRegistry` | Unity loguea warning de authoring y deja al NPC en idle local hasta recibir un assignment válido.                                                                  |
| **Zona de patio sin collider**                            | Unity recibe `yard_exercise`, pero falta el `BoxCollider` del área       | Unity loguea warning de authoring y deja al NPC en idle local hasta recibir un assignment válido.                                                                  |
| **Fase de trabajo: sub-zona sin waypoints libres**        | 7 NPCs en taller pero solo 4 workbenches libres                          | Los NPCs sobrantes reciben `work_inspect_cabinets` o idle local como fallback hasta que se libere un workbench.                                                    |
| **LOOPING action durante phase:start**                    | NPC en medio de una acción de loop cuando llega nueva fase               | NPC abandona la acción en el próximo waypoint de la cadena (no teleporta). Luego navega a la nueva zona.                                                           |
| **Cliente nuevo se une mid-partida (reconexión)**         | Se une en Fase 4, necesita saber el estado actual de todos los NPCs      | Al reconectar, el servidor envía un `phase:start` completo con los assignments actuales (no solo los cambios) para resincronizar.                                  |
| **Backend se reinicia mid-partida**                       | Todos los clientes pierden assignments                                   | Al reconectar, Unity mantiene el último assignment conocido por NPC. El servidor emite un `phase:start` completo. Los NPCs continúan desde su último estado local. |


---

## 6. Dependencies


| Sistema                         | Tipo | Dirección | Qué necesita / provee                                                                                                                              |
| ------------------------------- | ---- | --------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| Sincronización de Estado (#18)  | Hard | ← usa     | Emite `phase:start`, `phase:warning`, `npc:reassign` via Socket.io rooms                                                                           |
| NPC State Machine (#12)         | Hard | → provee  | El estado del NPC (IDLE/TRANSITION/ANGRY/HOSTILE) puede interrumpir acciones de rutina. Un NPC en estado HOSTILE ignora sus assignments de rutina. |
| Camuflaje (#3)                  | Hard | → provee  | Expone `currentPhase` y `activeZone` para que el sistema de camuflaje valide si el jugador está en la zona correcta                                |
| Condiciones de Victoria (#11)   | Soft | → informa | `phase:start` de Fase 9 abre la ventana final de escape via celda/recuento. El sistema de victoria lee la fase activa.                             |
| Iluminación Dinámica (#25)      | Soft | → dispara | Cada `phase:start` triggerea un cambio de iluminación. En Fase 9 se baja la luz del pasillo y se mantienen siluetas legibles en celdas.            |
| Audio Ambiente (#24)            | Soft | → dispara | Cada `phase:start` triggerea un cambio de música/ambiente (eco comedor, ruido taller, quietud tensa del recuento final)                            |
| HUD Presos (#20)                | Soft | → provee  | Expone fase actual, nombre y timer para el HUD                                                                                                     |
| HUD Guardia (#21)               | Soft | → provee  | Expone fase actual y zona activa para el HUD del guardia                                                                                           |
| Alertas de Comportamiento (#16) | Soft | → dispara | Si un jugador está en zona incorrecta, este sistema notifica a #16 para que evalúe si alertar al guardia                                           |


**Nota bidireccional (requerida por reglas del GDD):**  
Este sistema depende de #18 (Sync) para emitir eventos. El GDD de #18 debe actualizar su tabla de eventos para **eliminar** `npc:positions` y **agregar** `phase:start`, `phase:warning`, `npc:reassign` y `phase:zone_check`.

---

## 7. Tuning Knobs


| Knob                          | Default     | Rango seguro    | Si muy bajo                                         | Si muy alto                                           |
| ----------------------------- | ----------- | --------------- | --------------------------------------------------- | ----------------------------------------------------- |
| `reassign_interval`           | 20s         | 10–45s          | NPCs cambian demasiado seguido, se ven inquietos    | Los NPCs se ven robóticos, siempre haciendo lo mismo  |
| `reassign_change_probability` | 0.80        | 0.3–0.9         | Pocos NPCs cambian, comportamiento estático         | Todos cambian a la vez, movimiento caótico en ráfagas |
| `action_min_duration`         | por acción  | 5–30s           | NPCs no completan animaciones completas             | NPCs parecen pegados a su waypoint                    |
| `action_max_duration`         | por acción  | 15–90s          | Reasignaciones muy frecuentes                       | Comportamiento muy predecible                         |
| `waypoint_arrival_threshold`  | 0.3m        | 0.1–1.0m        | NPCs nunca "llegan" a destino (se quedan orbitando) | NPCs ejecutan animación demasiado lejos del waypoint  |
| `social_max_pair_distance`    | 15m         | 5–25m           | Muy pocas interacciones sociales posibles           | NPCs viajan demasiado lejos para socializar (irreal)  |
| `phase_warning_time`          | 10s         | 5–15s           | Jugadores sin tiempo para reaccionar                | Warning demasiado anticipado, pierde tensión          |
| `zone_grace_period`           | 5s          | 3–10s           | Demasiado punitivo con jugadores que cambian zona   | Jugadores pueden ignorar la zona correcta             |
| `work_subzone_split`          | 50%/50%     | 30–70% por zona | Una zona queda vacía (visual raro)                  | Una zona queda sobrecargada                           |
| `yard_idle_weight`            | 35          | 20–50           | Demasiado movimiento para una hora libre             | Patio demasiado estático                              |
| `yard_exercise_weight`        | 20          | 10–35           | Poca actividad visible en patio                      | Demasiados NPCs ejercitando a la vez                  |
| `cell_stand_weight`           | 100         | 50–150          | Pocos NPCs quedan legibles de pie                    | Casi nadie duerme en celdas                           |
| `cell_sleep_weight`           | 100         | 50–150          | Casi nadie duerme en celdas                          | Pocos NPCs quedan legibles de pie                     |
| `free_time_subzone_change_prob` | 0.25      | 0.10–0.40       | Poco tráfico entre zonas durante Hora libre          | Demasiado cruce de NPCs entre zonas                   |
| `transition_linger_max`       | 20s         | 10–30s          | Todos se mueven casi igual de rápido (NPC-tell)     | Algunos NPCs llegan muy tarde a la zona              |
| `transition_detour_prob`      | 0.40        | 0.15–0.65       | Poco tráfico en pasillos, ruido visual bajo         | Demasiados NPCs en corredores, congestión             |
| `transition_enroute_chat_prob`| 0.30        | 0.10–0.50       | Los desvíos se ven mecánicos (nadie charla)         | Demasiadas charlas, los NPCs tardan en llegar         |


---

## 8. Acceptance Criteria


| #     | Criterio                                                                                                      | Cómo verificar                                                                                          |
| ----- | ------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| AC-1  | Al inicio de cada fase, todos los clientes reciben el mismo `phase:start` con assignments para todos los NPCs | Loguear payload en 4 clientes simultáneos → payload idéntico (mismo timestamp y assignments)            |
| AC-2  | Los NPCs navegan al `zoneId` asignado usando NavMesh (sin teletransportarse)                                  | Observar visualmente que los NPCs caminan hacia sus destinos al inicio de fase                          |
| AC-3  | Los NPCs ejecutan la animación correcta al llegar al waypoint                                                 | NPC asignado a `cafe_sit_eat` → se sienta al llegar al seat, no camina en el aire                       |
| AC-4  | Un waypoint exclusivo no acepta más de 1 NPC simultáneo                                                       | Asignar manualmente 2 NPCs al mismo waypoint exclusivo → el segundo hace Idle                           |
| AC-5  | Los assignments por zona resuelven destino local sin coordenadas de backend                                   | NPC con `yard_exercise` + `seed` navega a un punto estable dentro de `yard_exercise`                    |
| AC-6  | `npc:reassign` actualiza solo los NPCs mencionados; los demás continúan su acción                             | Emitir reassign para 3 NPCs → solo esos 3 cambian destino, los 16 restantes no se interrumpen           |
| AC-7  | Bandwidth de NPCs ≤ 50 B/s promedio en partida normal (excluye phase transitions)                             | Wireshark/Network Profiler Unity durante 2 minutos de Fase 4 → medir bytes/s de mensajes `npc:`*        |
| AC-8  | Un cliente que reconecta mid-partida recibe los assignments actuales y los NPCs quedan consistentes           | Desconectar cliente en Fase 3, reconectar en Fase 4 → NPCs en posiciones correctas, sin NPCs "fantasma" |
| AC-9  | En Fase 9, todos los NPCs usan solo acciones de celda y quedan visibles desde el pasillo                      | Loggear Fase 9 → solo `cell_stand_idle` o `cell_sleep`, sin `socialPartnerId`, todos con `cell_area_XX` válido |
| AC-10 | Los jugadores prisioneros reciben `phase:zone_check` si están en zona incorrecta 5s después de `phase:start`  | Jugador permanece fuera de las sub-zonas válidas de la fase → recibe warning a los 5s                  |
| AC-11 | Los NPCs de Fase 6 (Trabajo) permanecen en su sub-zona asignada toda la fase                                  | Observar visualmente que los NPCs de taller no navegan a lavandería durante la fase                     |
| AC-12 | En Fases 4 y 7 (Hora libre), los NPCs usan patio simplificado, cocina, lavandería o celdas                   | Loggear `phase:start` y `npc:reassign` → aparecen `yard_*`, `free_cafe_*`, `laundry_*`, `cell_stand_idle`/`cell_sleep`; no aparecen acciones viejas complejas de patio |
| AC-13 | Al sonar el silbato de transición, los NPCs NO se mueven todos a la vez — el movimiento es escalonado        | En el momento exacto de `phase:start`, ≥5 NPCs están aún en su posición anterior (linger activo)        |
| AC-14 | Al menos el 30% de los NPCs pasan por un waypoint de corredor durante una transición de fase                  | Loguear waypoints reservados en `corridor_idle_*` durante 3 transiciones → al menos 6 NPCs usan corredor|

---

## 9. Referencia Completa de Waypoints y Áreas (122 waypoints + 11 zone areas)

> **Fuente canónica:** `Assets/Editor/WaypointRegistryGenerator.cs`
> **Convención de nombres:** Los GameObjects en escena deben tener exactamente este nombre para que `WaypointRegistry` los resuelva automáticamente desde los `waypointRoots`.

---

### 9.1 Celdas — Spawn (20 waypoints)

**Parent sugerido:** `WP_Celdas`
**Fases:** 1

| # | Waypoint ID | Zona | Cap | Fases |
|---|-------------|------|-----|-------|
| 1 | `cell_door_exit_01` | celda | 1 | 1 |
| 2 | `cell_door_exit_02` | celda | 1 | 1 |
| 3 | `cell_door_exit_03` | celda | 1 | 1 |
| 4 | `cell_door_exit_04` | celda | 1 | 1 |
| 5 | `cell_door_exit_05` | celda | 1 | 1 |
| 6 | `cell_door_exit_06` | celda | 1 | 1 |
| 7 | `cell_door_exit_07` | celda | 1 | 1 |
| 8 | `cell_door_exit_08` | celda | 1 | 1 |
| 9 | `cell_door_exit_09` | celda | 1 | 1 |
| 10 | `cell_door_exit_10` | celda | 1 | 1 |
| 11 | `cell_door_exit_11` | celda | 1 | 1 |
| 12 | `cell_door_exit_12` | celda | 1 | 1 |
| 13 | `cell_door_exit_13` | celda | 1 | 1 |
| 14 | `cell_door_exit_14` | celda | 1 | 1 |
| 15 | `cell_door_exit_15` | celda | 1 | 1 |
| 16 | `cell_door_exit_16` | celda | 1 | 1 |
| 17 | `cell_door_exit_17` | celda | 1 | 1 |
| 18 | `cell_door_exit_18` | celda | 1 | 1 |
| 19 | `cell_door_exit_19` | celda | 1 | 1 |
| 20 | `cell_door_exit_20` | celda | 1 | 1 |

---

### 9.2 Comedor (45 waypoints)

**Parent sugerido:** `WP_Comedor`
**Fases:** 1, 2, 4, 5, 7, 8

#### Entrada (6)

| # | Waypoint ID | Cap | Fases |
|---|-------------|-----|-------|
| 1 | `cafeteria_entrance_spot_01` | 3 | 1, 2, 5, 8 |
| 2 | `cafeteria_entrance_spot_02` | 3 | 1, 2, 5, 8 |
| 3 | `cafeteria_entrance_spot_03` | 3 | 1, 2, 5, 8 |
| 4 | `cafeteria_entrance_spot_04` | 3 | 1, 2, 5, 8 |
| 5 | `cafeteria_entrance_spot_05` | 3 | 1, 2, 5, 8 |
| 6 | `cafeteria_entrance_spot_06` | 3 | 1, 2, 5, 8 |

#### Caminos (5)

| # | Waypoint ID | Cap | Fases |
|---|-------------|-----|-------|
| 1 | `cafeteria_path_01` | 4 | 1, 2, 5, 8 |
| 2 | `cafeteria_path_02` | 4 | 1, 2, 5, 8 |
| 3 | `cafeteria_path_03` | 4 | 1, 2, 5, 8 |
| 4 | `cafeteria_path_04` | 4 | 1, 2, 5, 8 |
| 5 | `cafeteria_path_05` | 4 | 1, 2, 5, 8 |

#### Asientos (16)

| # | Waypoint ID | Cap | Fases |
|---|-------------|-----|-------|
| 1 | `cafeteria_seat_01` | 2 | 2, 4, 5, 7, 8 |
| 2 | `cafeteria_seat_02` | 2 | 2, 4, 5, 7, 8 |
| 3 | `cafeteria_seat_03` | 2 | 2, 4, 5, 7, 8 |
| 4 | `cafeteria_seat_04` | 2 | 2, 4, 5, 7, 8 |
| 5 | `cafeteria_seat_05` | 2 | 2, 4, 5, 7, 8 |
| 6 | `cafeteria_seat_06` | 2 | 2, 4, 5, 7, 8 |
| 7 | `cafeteria_seat_07` | 2 | 2, 4, 5, 7, 8 |
| 8 | `cafeteria_seat_08` | 2 | 2, 4, 5, 7, 8 |
| 9 | `cafeteria_seat_09` | 2 | 2, 4, 5, 7, 8 |
| 10 | `cafeteria_seat_10` | 2 | 2, 4, 5, 7, 8 |
| 11 | `cafeteria_seat_11` | 2 | 2, 4, 5, 7, 8 |
| 12 | `cafeteria_seat_12` | 2 | 2, 4, 5, 7, 8 |
| 13 | `cafeteria_seat_13` | 2 | 2, 4, 5, 7, 8 |
| 14 | `cafeteria_seat_14` | 2 | 2, 4, 5, 7, 8 |
| 15 | `cafeteria_seat_15` | 2 | 2, 4, 5, 7, 8 |
| 16 | `cafeteria_seat_16` | 2 | 2, 4, 5, 7, 8 |

#### Counter (6)

| # | Waypoint ID | Cap | Fases |
|---|-------------|-----|-------|
| 1 | `cafeteria_counter_01` | 1 | 2, 5, 8 |
| 2 | `cafeteria_counter_02` | 1 | 2, 5, 8 |
| 3 | `cafeteria_counter_03` | 1 | 2, 5, 8 |
| 4 | `cafeteria_counter_04` | 1 | 2, 5, 8 |
| 5 | `cafeteria_counter_05` | 1 | 2, 5, 8 |
| 6 | `cafeteria_counter_06` | 1 | 2, 5, 8 |

#### Fila (8)

| # | Waypoint ID | Cap | Fases |
|---|-------------|-----|-------|
| 1 | `cafeteria_line_01` | 1 | 2, 4, 5, 7, 8 |
| 2 | `cafeteria_line_02` | 1 | 2, 4, 5, 7, 8 |
| 3 | `cafeteria_line_03` | 1 | 2, 4, 5, 7, 8 |
| 4 | `cafeteria_line_04` | 1 | 2, 4, 5, 7, 8 |
| 5 | `cafeteria_line_05` | 1 | 2, 4, 5, 7, 8 |
| 6 | `cafeteria_line_06` | 1 | 2, 4, 5, 7, 8 |
| 7 | `cafeteria_line_07` | 1 | 2, 4, 5, 7, 8 |
| 8 | `cafeteria_line_08` | 1 | 2, 4, 5, 7, 8 |

#### Depósito de bandejas (4)

| # | Waypoint ID | Cap | Fases |
|---|-------------|-----|-------|
| 1 | `cafeteria_tray_deposit_01` | 2 | 2, 5, 8 |
| 2 | `cafeteria_tray_deposit_02` | 2 | 2, 5, 8 |
| 3 | `cafeteria_tray_deposit_03` | 2 | 2, 5, 8 |
| 4 | `cafeteria_tray_deposit_04` | 2 | 2, 5, 8 |

---

### 9.3 Corredores — Transición (24 waypoints)

**Parent sugerido:** `WP_Corredor`
**Fases:** todas (sin restricción)

| # | Waypoint ID | Cap | Fases |
|---|-------------|-----|-------|
| 1 | `corridor_idle_01` | 1 | todas |
| 2 | `corridor_idle_02` | 1 | todas |
| 3 | `corridor_idle_03` | 1 | todas |
| 4 | `corridor_idle_04` | 1 | todas |
| 5 | `corridor_idle_05` | 1 | todas |
| 6 | `corridor_idle_06` | 1 | todas |
| 7 | `corridor_idle_07` | 1 | todas |
| 8 | `corridor_idle_08` | 1 | todas |
| 9 | `corridor_idle_09` | 1 | todas |
| 10 | `corridor_idle_10` | 1 | todas |
| 11 | `corridor_chat_spot_01` | 2 | todas |
| 12 | `corridor_chat_spot_02` | 2 | todas |
| 13 | `corridor_chat_spot_03` | 2 | todas |
| 14 | `corridor_chat_spot_04` | 2 | todas |
| 15 | `hallway_slot_01` | 1 | todas |
| 16 | `hallway_slot_02` | 1 | todas |
| 17 | `hallway_slot_03` | 1 | todas |
| 18 | `hallway_slot_04` | 1 | todas |
| 19 | `hallway_slot_05` | 1 | todas |
| 20 | `hallway_slot_06` | 1 | todas |
| 21 | `hallway_slot_07` | 1 | todas |
| 22 | `hallway_slot_08` | 1 | todas |
| 23 | `hallway_slot_09` | 1 | todas |
| 24 | `hallway_slot_10` | 1 | todas |

---

### 9.4 Taller (17 waypoints)

**Parent sugerido:** `WP_Taller`
**Fases:** 3, 6

| # | Waypoint ID | Sub-zona | Cap | Fases |
|---|-------------|----------|-----|-------|
| 1 | `workshop_bench_01` | taller | 1 | 3, 6 |
| 2 | `workshop_bench_02` | taller | 1 | 3, 6 |
| 3 | `workshop_bench_03` | taller | 1 | 3, 6 |
| 4 | `workshop_bench_04` | taller | 1 | 3, 6 |
| 5 | `workshop_bench_05` | taller | 1 | 3, 6 |
| 6 | `workshop_bench_06` | taller | 1 | 3, 6 |
| 7 | `workshop_shelf_01` | taller | 1 | 3, 6 |
| 8 | `workshop_shelf_02` | taller | 1 | 3, 6 |
| 9 | `workshop_shelf_03` | taller | 1 | 3, 6 |
| 10 | `workshop_shelf_04` | taller | 1 | 3, 6 |
| 11 | `workshop_machine_01` | taller | 1 | 3, 6 |
| 12 | `workshop_machine_02` | taller | 1 | 3, 6 |
| 13 | `workshop_machine_03` | taller | 1 | 3, 6 |
| 14 | `workshop_machine_04` | taller | 1 | 3, 6 |
| 15 | `workshop_chat_spot_01` | taller | 2 | 3, 6 |
| 16 | `workshop_chat_spot_02` | taller | 2 | 3, 6 |
| 17 | `workshop_chat_spot_03` | taller | 2 | 3, 6 |

---

### 9.5 Lavanderia (16 waypoints)

**Parent sugerido:** `WP_Lavanderia`
**Fases:** 3, 4, 6, 7

| # | Waypoint ID | Sub-zona | Cap | Fases |
|---|-------------|----------|-----|-------|
| 1 | `laundry_washer_01` | lavanderia | 1 | 3, 4, 6, 7 |
| 2 | `laundry_washer_02` | lavanderia | 1 | 3, 4, 6, 7 |
| 3 | `laundry_washer_03` | lavanderia | 1 | 3, 4, 6, 7 |
| 4 | `laundry_washer_04` | lavanderia | 1 | 3, 4, 6, 7 |
| 5 | `laundry_washer_05` | lavanderia | 1 | 3, 4, 6, 7 |
| 6 | `laundry_washer_06` | lavanderia | 1 | 3, 4, 6, 7 |
| 7 | `laundry_fold_01` | lavanderia | 1 | 3, 4, 6, 7 |
| 8 | `laundry_fold_02` | lavanderia | 1 | 3, 4, 6, 7 |
| 9 | `laundry_fold_03` | lavanderia | 1 | 3, 4, 6, 7 |
| 10 | `laundry_fold_04` | lavanderia | 1 | 3, 4, 6, 7 |
| 11 | `laundry_fold_05` | lavanderia | 1 | 3, 4, 6, 7 |
| 12 | `laundry_fold_06` | lavanderia | 1 | 3, 4, 6, 7 |
| 13 | `laundry_dryer_01` | lavanderia | 1 | 3, 4, 6, 7 |
| 14 | `laundry_dryer_02` | lavanderia | 1 | 3, 4, 6, 7 |
| 15 | `laundry_dryer_03` | lavanderia | 1 | 3, 4, 6, 7 |
| 16 | `laundry_dryer_04` | lavanderia | 1 | 3, 4, 6, 7 |

---

### 9.6 Patio (3 zone areas)

**Parent sugerido:** `WP_Patio`
**Fases:** 4, 7

Cada `zoneId` se configura en `ZoneRegistry` con un `BoxCollider` que cubre el área caminable. Unity elige un punto determinístico dentro del área usando el `seed` recibido del backend.

| # | Zone ID | Uso | Fases |
|---|---------|-----|-------|
| 1 | `yard` | Idle general y lean wall | 4, 7 |
| 2 | `yard_benches` | Idle cerca de bancos | 4, 7 |
| 3 | `yard_exercise` | Ejercicio y shadowbox | 4, 7 |

---

### 9.7 Celdas — Interior (8 cell areas / 20 beds)

**Parent sugerido:** `WP_Celdas_Interior`
**Fases:** 4, 7, 9

Cada `cell_area_XX` se configura como un área lógica en `ZoneRegistry` con un `BoxCollider` que cubre el espacio caminable dentro de la celda.

Hay 4 celdas abajo y 4 celdas en primer piso. Las capacidades por piso son `2,3,2,3`, para 10 camas abajo y 10 camas arriba.

En la implementación actual se usa para `cell_stand_idle` y `cell_sleep`. Unity puede resolver ambos a punto de área por ahora; el resolver específico de cama para `cell_sleep` se agregará después.

| # | CellArea ID | Piso | Camas/Cap | Fases |
|---|-------------|------|-----------|-------|
| 1 | `cell_area_01` | Abajo | 2 | 4, 7, 9 |
| 2 | `cell_area_02` | Abajo | 3 | 4, 7, 9 |
| 3 | `cell_area_03` | Abajo | 2 | 4, 7, 9 |
| 4 | `cell_area_04` | Abajo | 3 | 4, 7, 9 |
| 5 | `cell_area_05` | Primer piso | 2 | 4, 7, 9 |
| 6 | `cell_area_06` | Primer piso | 3 | 4, 7, 9 |
| 7 | `cell_area_07` | Primer piso | 2 | 4, 7, 9 |
| 8 | `cell_area_08` | Primer piso | 3 | 4, 7, 9 |

---

### 9.8 Resumen por Parent

| Parent sugerido | Zona | Waypoints / Áreas | Fases principales |
|-----------------|------|-----------|-------------------|
| `WP_Celdas` | celda | 20 | 1 |
| `WP_Comedor` | comedor | 45 | 1, 2, 4, 5, 7, 8 |
| `WP_Corredor` | corredor | 24 | todas |
| `WP_Taller` | trabajo/taller | 17 | 3, 6 |
| `WP_Lavanderia` | lavanderia | 16 | 3, 4, 6, 7 |
| `WP_Patio` | patio | 3 zone areas | 4, 7 |
| `WP_Celdas_Interior` | celdas | 8 cell areas | 4, 7, 9 |
| **Total** | | **122 waypoints + 11 zone areas** | |
