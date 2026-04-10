# Sistema de Rutina/Fases + NPC Libre Albedrío

> **Status**: Designed  
> **Author**: Cris + Claude  
> **Last Updated**: 2026-04-08  
> **Cubre sistemas**: #4 Rutina/Fases + #13 NPC Rutina/NavMesh  
> **Implementa Pilar**: "La rutina es la cárcel" — los jugadores prisioneros deben imitar a los NPCs para sobrevivir

---

## 1. Overview

Este sistema gestiona el "reloj" de la partida: la jornada carcelaria dividida en 9 fases, cada una con duración fija, zona activa y comportamientos esperados. Los NPCs prisioneros (17–19 de ellos) siguen la rutina automáticamente usando pathfinding local en Unity. El backend es responsable de **asignar qué acción hace cada NPC** (incluyendo waypoint destino y duración), pero **no emite posiciones**: el movimiento real ocurre en cada instancia del juego via NavMeshAgent.

La novedad arquitectónica central es el **sistema de libre albedrío**: dentro de cada fase, los NPCs tienen un pool de acciones posibles con pesos de probabilidad. El backend sortea periódicamente nuevas acciones, incluyendo interacciones sociales entre pares de NPCs. Esto da variedad orgánica sin que el servidor tenga que transmitir un stream continuo de posiciones.

**Entidades en la partida:**

- 1 jugador guardia (human-controlled, no usa este sistema)
- 2–3 jugadores prisioneros (human-controlled, deben imitar la rutina)
- 17–19 NPCs prisioneros (este sistema los controla)
- **Total: siempre 20 entidades**

---

## 2. Player Fantasy

**Para el jugador prisionero:** La rutina no se siente como un tutorial — se siente como presión social constante. Estás rodeado de NPCs que saben exactamente qué hacer y dónde estar. Si te quedás quieto o vas a la zona equivocada, el contraste te delata. Los NPCs charlan, barren, doblan ropa, juegan cartas — y vos tenés que encajar en esa normalidad mientras planificás la fuga.

**Para el jugador guardia:** Los NPCs le dan cobertura a los prisioneros. La cancha con 18 personajes moviéndose crea ruido visual real. Detectar quién es jugador y quién es NPC requiere atención sostenida, no solo mirar el minimapa. Un NPC que dobla bandeja y se va a su celda a tiempo es ruido. Un "NPC" que se queda parado tres segundos demasiado cerca de la ventilación es señal.

---

## 3. Detailed Rules

### 3.1 Fases de la Jornada


| #   | Fase           | Hora ficticia | Duración real | Zona                    | Sub-zonas                        |
| --- | -------------- | ------------- | ------------- | ----------------------- | -------------------------------- |
| 1   | Inicio         | 06:00         | 30 s          | Celda                   | —                                |
| 2   | Desayuno       | 07:00         | 90 s          | Comedor                 | —                                |
| 3   | Trabajo        | 08:00         | 90 s          | Taller / Lavandería     | Taller, Lavandería               |
| 4   | Hora libre     | 10:00         | 120 s         | Libre (patio/comedor/lavandería) | Patio, Comedor, Lavandería |
| 5   | Almuerzo       | 12:00         | 90 s          | Comedor                 | — (mismo que Desayuno)           |
| 6   | Trabajo        | 14:00         | 120 s         | Taller / Lavandería     | Taller, Lavandería               |
| 7   | Siesta         | 16:00         | 90 s          | Celdas                  | —                                |
| 8   | Cena           | 18:00         | 90 s          | Comedor                 | — (mismo que Desayuno)           |
| 9   | Luces apagadas | 22:00         | 120 s         | Celdas                  | —                                |


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
| Waypoint (ID string)              | ✅ Envía el ID        | ❌ Resuelve ID→Vector3 |
| Pathfinding / movimiento          | ❌ No calcula         | ✅ NavMeshAgent        |
| Animaciones NPC                   | ❌ No controla        | ✅ Animator local      |
| Interacción social (pairing)      | ✅ Empareja NPCs      | ✅ Sincroniza llegada  |
| Zona del jugador (para camuflaje) | ✅ Valida server-side | ✅ Envía posición      |


### 3.3 Catálogo de Acciones por Fase

Cada acción define:

- `actionId` — identificador único string
- `type` — SOLO | SOCIAL | LOOPING | IDLE
- `animation` — trigger del Animator
- `waypointTag` — prefijo de los waypoints válidos para esta acción
- `weight` — probabilidad relativa de selección (mayor = más frecuente)
- `minDuration` / `maxDuration` — rango en segundos

---

#### Fase 1 — Inicio | Celda → Comedor (transición)

> Cada NPC hace spawn parado afuera de su celda. Durante 30 segundos se mueven libremente — charlan con el de al lado, se estiran, bostezan — y gradualmente van caminando hacia el comedor. No hay fila ni orden estricto. Es una transición orgánica al Desayuno.
>
> **Waypoints con posición fija:** `cell_door_exit_` (spawn) y `cafeteria_path_` (entrada al comedor).  
> **Sin waypoint fijo:** acciones `greet_neighbor`, `idle_stretch`, `idle_yawn` — el NPC permanece donde está o navega al Transform de su pareja social.

| ActionId            | Type    | Waypoint                | Animation       | Weight | Duration |
| ------------------- | ------- | ----------------------- | --------------- | ------ | -------- |
| `spawn_at_door`     | IDLE    | `cell_door_exit_01..20` | idle            | 100    | 3–8s     |
| `greet_neighbor`    | SOCIAL  | *(posición del partner)*| talk_standing   | 30     | 5–12s    |
| `idle_stretch`      | IDLE    | *(sin mover)*           | stretch         | 20     | 2–4s     |
| `idle_yawn`         | IDLE    | *(sin mover)*           | yawn            | 15     | 1–3s     |
| `walk_to_cafeteria` | ONESHOT | `cafeteria_path_01..05` | walk_slow       | 35     | 10–20s   |


---

#### Fase 2 — Desayuno | Comedor

#### Fase 5 — Almuerzo | Comedor

#### Fase 8 — Cena | Comedor

> Las tres fases de comedor comparten el mismo pool de acciones.


| ActionId               | Type    | Animation         | WaypointTag                 | Weight | Duration                |
| ---------------------- | ------- | ----------------- | --------------------------- | ------ | ----------------------- |
| `cafe_sit_eat`         | IDLE    | Sit_Eat           | `cafeteria_seat`_           | 45     | 20–50s                  |
| `cafe_walk_to_counter` | LOOPING | Walk + Serve_Self | `cafeteria_counter`_ → seat | 20     | 10–18s                  |
| `cafe_wait_in_line`    | IDLE    | Idle_Queue        | `cafeteria_line`_           | 15     | 8–15s → walk to counter |
| `cafe_talk_seated`     | SOCIAL  | Talk_Seated       | mismo seat + vecino         | 12     | 8–20s                   |
| `cafe_clear_tray`      | LOOPING | Carry_Tray        | `cafeteria_tray_deposit`_   | 8      | 6–12s                   |


> `cafeteria_seat_01..16` — máx 2 ocupantes por mesa de 2, máx 4 por mesa de 4.

---

#### Fase 3 — Trabajo (1er turno) | Taller / Lavandería

> NPCs divididos en dos sub-zonas al inicio de la fase. Permanecen en su sub-zona toda la fase.  
> Distribución: ~9 NPCs taller / ~9 lavandería.

**Sub-zona: Taller**

| ActionId                 | Type    | Animation     | WaypointTag           | Weight | Duration |
| ------------------------ | ------- | ------------- | --------------------- | ------ | -------- |
| `work_use_workbench`     | IDLE    | Work_Bench    | `workshop_bench_`     | 40     | 20–50s   |
| `work_carry_box`         | LOOPING | Carry_Box     | `workshop_shelf_`     | 30     | 12–20s   |
| `work_inspect_equipment` | IDLE    | Inspect       | `workshop_machine_`   | 20     | 10–20s   |
| `work_talk_coworker`     | SOCIAL  | Talk_Standing | `workshop_chat_spot_` | 10     | 8–15s    |

**Sub-zona: Lavandería**

| ActionId               | Type    | Animation          | WaypointTag        | Weight | Duration |
| ---------------------- | ------- | ------------------ | ------------------ | ------ | -------- |
| `laundry_load_washer`  | IDLE    | Load_Machine       | `laundry_washer_`  | 30     | 15–30s   |
| `laundry_fold_clothes` | IDLE    | Fold_Clothes       | `laundry_fold_`    | 35     | 20–40s   |
| `laundry_carry_basket` | LOOPING | Carry_Basket       | `laundry_washer_`  | 25     | 10–18s   |
| `laundry_idle_check`   | IDLE    | Idle_Check_Machine | `laundry_washer_`  | 10     | 5–12s    |

---

#### Fase 4 — Hora libre | Patio / Comedor / Lavandería

> Fase de máxima variedad. Los NPCs eligen libremente entre tres sub-zonas.  
> Distribución: ~6 patio / ~6 comedor / ~6 lavandería (ropa personal).

**Sub-zona: Patio**

| ActionId                  | Type    | Animation     | WaypointTag                | Weight | Duration    |
| ------------------------- | ------- | ------------- | -------------------------- | ------ | ----------- |
| `yard_walk_perimeter`     | LOOPING | Walk          | `yard_perimeter_` (cadena) | 20     | 30–60s loop |
| `yard_sit_bench`          | IDLE    | Sit_Bench     | `yard_bench_`              | 20     | 20–60s      |
| `yard_exercise`           | IDLE    | Exercise      | `yard_exercise_area_`      | 15     | 15–40s      |
| `yard_conversation_group` | SOCIAL  | Talk_Standing | `yard_conversation_spot_`  | 20     | 15–35s      |
| `yard_play_cards`         | SOCIAL  | Sit_Cards     | `yard_card_table_`         | 10     | 30–90s      |
| `yard_lean_wall`          | IDLE    | Lean_Wall     | `yard_wall_lean_`          | 8      | 15–40s      |
| `yard_shadow_boxing`      | IDLE    | Shadowbox     | `yard_exercise_area_`      | 5      | 10–20s      |
| `yard_kick_ball`          | SOCIAL  | Kick          | `yard_ball_spot`           | 2      | 20–40s      |

**Sub-zona: Comedor** *(charlar, no comer)*

| ActionId                | Type   | Animation     | WaypointTag       | Weight | Duration |
| ----------------------- | ------ | ------------- | ----------------- | ------ | -------- |
| `free_cafe_sit_talk`    | SOCIAL | Talk_Seated   | `cafeteria_seat_` | 40     | 15–40s   |
| `free_cafe_sit_idle`    | IDLE   | Sit_Idle      | `cafeteria_seat_` | 35     | 10–30s   |
| `free_cafe_stand_chat`  | SOCIAL | Talk_Standing | `cafeteria_line_` | 25     | 10–25s   |

**Sub-zona: Lavandería** *(ropa personal, mismas acciones que turno de trabajo)*

| ActionId               | Type    | Animation          | WaypointTag       | Weight | Duration |
| ---------------------- | ------- | ------------------ | ----------------- | ------ | -------- |
| `laundry_load_washer`  | IDLE    | Load_Machine       | `laundry_washer_` | 30     | 15–30s   |
| `laundry_fold_clothes` | IDLE    | Fold_Clothes       | `laundry_fold_`   | 35     | 20–40s   |
| `laundry_carry_basket` | LOOPING | Carry_Basket       | `laundry_washer_` | 25     | 10–18s   |
| `laundry_idle_check`   | IDLE    | Idle_Check_Machine | `laundry_washer_` | 10     | 5–12s    |

---

#### Fase 6 — Trabajo (2do turno) | Taller / Lavandería

> Idéntico a Fase 3. Los NPCs pueden ser reasignados a distinta sub-zona que el turno anterior (libre albedrío de fase).

*(Mismo catálogo de acciones que Fase 3 — ver arriba)*

---

#### Fase 7 — Siesta | Celdas

> Cada NPC tiene celda asignada al inicio de la partida. Solo accede a los waypoints de su celda.


| ActionId                | Type   | Animation      | WaypointTag                   | Weight | Duration |
| ----------------------- | ------ | -------------- | ----------------------------- | ------ | -------- |
| `cell_lie_bed`          | IDLE   | Lie_Down       | `cell_XX_bed`_                | 50     | 30–90s   |
| `cell_sit_bed`          | IDLE   | Sit_Bed_Edge   | `cell_XX_bed`_                | 20     | 15–40s   |
| `cell_read_book`        | IDLE   | Read_Book      | `cell_XX_desk`                | 15     | 20–60s   |
| `cell_stare_window`     | IDLE   | Idle_Window    | `cell_XX_window`              | 10     | 10–25s   |
| `cell_whisper_cellmate` | SOCIAL | Whisper_Seated | ambas camas de la misma celda | 5      | 8–20s    |


---

#### Fase 9 — Luces Apagadas | Celdas

> Fase más restrictiva. Sin interacciones sociales. El guardia (jugador) patrulla con linterna.


| ActionId       | Type | Animation | WaypointTag    | Weight | Duration                  |
| -------------- | ---- | --------- | -------------- | ------ | ------------------------- |
| `lights_sleep` | IDLE | Sleep     | `cell_XX_bed`_ | 75     | duración total de la fase |
| `lights_toss`  | IDLE | Toss_Turn | `cell_XX_bed`_ | 25     | 5–12s → vuelve a sleep    |


---

### 3.4 Eventos Socket.io

#### `phase:warning` (servidor → todos) — 10s antes de la transición

```json
{
  "nextPhase": 4,
  "nextPhaseName": "Patio libre",
  "warningInSeconds": 10
}
```

#### `phase:start` (servidor → todos) — al inicio de cada fase

```json
{
  "phase": 4,
  "phaseName": "Patio libre",
  "duration": 120,
  "zone": "patio_exterior",
  "npcAssignments": [
    {
      "npcId": "npc_01",
      "actionId": "yard_walk_perimeter",
      "waypointChain": ["yard_perimeter_01", "yard_perimeter_03", "yard_perimeter_06"],
      "duration": 45,
      "loop": true
    },
    {
      "npcId": "npc_02",
      "actionId": "yard_conversation_group",
      "waypointId": "yard_conversation_spot_02",
      "socialPartnerId": "npc_07",
      "duration": 30
    },
    {
      "npcId": "npc_03",
      "actionId": "work_use_workbench",
      "waypointId": "workshop_bench_02",
      "subZone": "taller",
      "duration": 40
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
      "actionId": "yard_sit_bench",
      "waypointId": "yard_bench_03",
      "duration": 25
    },
    {
      "npcId": "npc_02",
      "actionId": "yard_kick_ball",
      "waypointId": "yard_ball_spot",
      "socialPartnerId": "npc_09",
      "duration": 30
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
  "expectedZone": "patio_exterior",
  "phase": 4,
  "graceSeconds": 5
}
```

---

### 3.5 Estructura de Waypoints en Unity

Los waypoints son **ScriptableObjects** configurados en el Editor. El backend solo conoce los IDs string — nunca coordenadas Vector3.

```csharp
// WaypointRegistry.cs
[CreateAssetMenu(menuName = "Jailbreak/WaypointRegistry")]
public class WaypointRegistry : ScriptableObject
{
    [SerializeField] private List<WaypointEntry> waypoints;
    private Dictionary<string, WaypointEntry> _lookup;

    [System.Serializable]
    public class WaypointEntry
    {
        public string waypointId;          // "yard_bench_03"
        public Transform transform;        // drag desde Scene en Editor
        public string zone;                // "patio_exterior"
        public string subZone;             // "taller", "lavanderia", etc.
        public bool isExclusive;           // solo 1 ocupante a la vez
        public int maxOccupants = 1;       // mesas de cartas = 4
        public string[] validPhases;       // fases donde este WP es usable
        [HideInInspector] public int currentOccupants;
    }

    public WaypointEntry Get(string id) { ... }
    public List<WaypointEntry> GetByZone(string zone) { ... }
    public List<WaypointEntry> GetAvailableForPhase(int phase) { ... }
    public bool Reserve(string id) { ... }   // retorna false si está lleno
    public void Release(string id) { ... }
}
```

```csharp
// NPCBehaviorController.cs
public class NPCBehaviorController : MonoBehaviour
{
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Animator animator;
    [SerializeField] private WaypointRegistry waypointRegistry;

    private NPCActionData currentAction;
    private float actionTimer;
    private bool hasArrived;
    private int chainIndex;

    public void AssignAction(NPCActionData data)
    {
        // Liberar waypoint anterior
        if (currentAction != null)
            waypointRegistry.Release(currentAction.waypointId);

        currentAction = data;
        actionTimer = data.duration;
        hasArrived = false;
        chainIndex = 0;

        // Reservar y navegar
        var entry = waypointRegistry.Get(data.waypointId ?? data.waypointChain[0]);
        agent.SetDestination(entry.transform.position);
    }

    private void Update()
    {
        if (!hasArrived && agent.remainingDistance < 0.3f && !agent.pathPending)
        {
            hasArrived = true;
            OnReachedWaypoint();
        }

        if (hasArrived)
        {
            actionTimer -= Time.deltaTime;
            if (actionTimer <= 0f)
                OnActionComplete();
        }
    }

    private void OnReachedWaypoint()
    {
        animator.SetTrigger(currentAction.animationTrigger);

        // LOOPING: si hay chain, navegar al siguiente
        if (currentAction.loop && currentAction.waypointChain != null)
        {
            chainIndex = (chainIndex + 1) % currentAction.waypointChain.Length;
            var next = waypointRegistry.Get(currentAction.waypointChain[chainIndex]);
            agent.SetDestination(next.transform.position);
            hasArrived = false;
        }
    }

    private void OnActionComplete()
    {
        // El servidor se encarga de reasignar. Si no llega reassign en 3s,
        // el NPC hace idle en su posición actual.
        animator.SetTrigger("Idle");
    }
}
```

### 3.6 Libre Albedrío — Lógica Backend

```
Al inicio de cada fase:
  1. Para cada NPC:
     a. Si la fase tiene sub-zonas (Trabajo) → asignar sub-zona balanceada
     b. Seleccionar acción inicial: weighted random del pool de la fase
     c. Seleccionar waypoint disponible del waypointTag de la acción
     d. Si la acción es SOCIAL → buscar partner disponible compatible
     e. Calcular duration = random(minDuration, maxDuration)
  2. Emitir phase:start con todos los assignments

Cada REASSIGN_INTERVAL segundos (default 25s):
  1. Para cada NPC con actionTimer < 5s (a punto de terminar):
     a. 70% probabilidad de cambiar acción
     b. Si cambia: nuevo weighted random, excluyendo la acción actual
     c. SOCIAL: buscar partner con acción compatible y actionTimer > 10s
     d. Si hay partner disponible: pair ambos NPCs en la misma acción social
  2. Emitir npc:reassign solo con los NPCs que cambian

Restricciones:
  - No asignar waypoints exclusivos ya ocupados
  - En Fase 9 (Luces apagadas): solo acciones IDLE del pool de la fase
  - En Fase 1 (Formación): solo acciones del pool de formación
  - Sub-zona de Trabajo: NPC no cambia de sub-zona durante la fase
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

// Distribución de sub-zonas (Fase 6 — Trabajo)
n_npcs = total_npcs                            // 17–19
taller_count    = floor(n_npcs / 3)            // ~6
lavanderia_count = floor(n_npcs / 3)           // ~6
piso_count      = n_npcs - taller - lavanderia // ~5–7

// Bandwidth estimada (reemplaza npc:positions)
phase_start_payload = n_npcs × (npcId[4B] + actionId[20B] + waypointId[20B] + duration[2B]) 
                    = 19 × 46B ≈ 874B per phase transition (~1 KB cada 90–120s)
npc_reassign_payload = avg_changed_npcs(7) × 46B ≈ 322B cada 25s
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
| **NPC recibe reassign mientras navega**                   | El agente estaba en camino a waypoint A, llega reassign para waypoint B  | Interrumpir navegación inmediatamente. Liberar reserva de A. Navegar a B.                                                                                          |
| **Waypoint exclusivo lleno al asignar**                   | El servidor asignó `yard_bench_03` pero ya hay un NPC ahí                | Unity no reserva el WP. El NPC hace Idle en su posición actual y espera hasta que el WP se libere o llegue el siguiente reassign.                                  |
| **Partner social se desconecta mid-action**               | NPC_02 estaba en `yard_kick_ball` con NPC_07 (jugador) que se desconectó | NPC_02 recibe `Idle` trigger automáticamente al detectar que su partner ya no está activo. Queda disponible para el siguiente reassign.                            |
| **Jugador en zona equivocada**                            | Servidor detecta jugador en patio durante fase de Cena                   | Servidor emite `phase:zone_check` con 5s de gracia. Si el jugador no se mueve a la zona correcta → el guardia recibe alerta (sistema de Alertas #16).              |
| **phase:start llega antes de que NPC termine transición** | NPC está en la puerta de la celda cuando empieza Patio Libre             | NPC recibe el nuevo assignment. NavMeshAgent redirige hacia patio exterior sin importar estado anterior.                                                           |
| **Todos los waypoints de un pool están llenos**           | 19 NPCs quieren `cafeteria_seat`_ pero solo hay 16 seats                 | Los últimos 3 NPCs reciben `cafe_wait_in_line` como fallback. El servidor itera el pool hasta encontrar acción alternativa.                                        |
| **Fase de trabajo: sub-zona sin waypoints libres**        | 7 NPCs en taller pero solo 4 workbenches libres                          | Los NPCs sobrantes reciben `work_carry_box` o `work_inspect_equipment` como fallback hasta que se libere un workbench.                                             |
| **LOOPING action durante phase:start**                    | NPC en medio de carry_box cuando llega nueva fase                        | NPC abandona la acción en el próximo waypoint de la cadena (no teleporta). Luego navega a la nueva zona.                                                           |
| **Cliente nuevo se une mid-partida (reconexión)**         | Se une en Fase 4, necesita saber el estado actual de todos los NPCs      | Al reconectar, el servidor envía un `phase:start` completo con los assignments actuales (no solo los cambios) para resincronizar.                                  |
| **Backend se reinicia mid-partida**                       | Todos los clientes pierden assignments                                   | Al reconectar, Unity mantiene el último assignment conocido por NPC. El servidor emite un `phase:start` completo. Los NPCs continúan desde su último estado local. |


---

## 6. Dependencies


| Sistema                         | Tipo | Dirección | Qué necesita / provee                                                                                                                              |
| ------------------------------- | ---- | --------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| Sincronización de Estado (#18)  | Hard | ← usa     | Emite `phase:start`, `phase:warning`, `npc:reassign` via Socket.io rooms                                                                           |
| NPC State Machine (#12)         | Hard | → provee  | El estado del NPC (IDLE/TRANSITION/ANGRY/HOSTILE) puede interrumpir acciones de rutina. Un NPC en estado HOSTILE ignora sus assignments de rutina. |
| Camuflaje (#3)                  | Hard | → provee  | Expone `currentPhase` y `activeZone` para que el sistema de camuflaje valide si el jugador está en la zona correcta                                |
| Condiciones de Victoria (#11)   | Soft | → informa | `phase:start` de Fase 9 puede ser condición de escape (oscuridad = oportunidad). El sistema de victoria lee la fase activa.                        |
| Iluminación Dinámica (#25)      | Soft | → dispara | Cada `phase:start` triggerea un cambio de iluminación (luz día/noche, intensidad por zona)                                                         |
| Audio Ambiente (#24)            | Soft | → dispara | Cada `phase:start` triggerea un cambio de música/ambiente (eco comedor, ruido taller, silencio nocturno)                                           |
| HUD Presos (#20)                | Soft | → provee  | Expone fase actual, nombre y timer para el HUD                                                                                                     |
| HUD Guardia (#21)               | Soft | → provee  | Expone fase actual y zona activa para el HUD del guardia                                                                                           |
| Alertas de Comportamiento (#16) | Soft | → dispara | Si un jugador está en zona incorrecta, este sistema notifica a #16 para que evalúe si alertar al guardia                                           |


**Nota bidireccional (requerida por reglas del GDD):**  
Este sistema depende de #18 (Sync) para emitir eventos. El GDD de #18 debe actualizar su tabla de eventos para **eliminar** `npc:positions` y **agregar** `phase:start`, `phase:warning`, `npc:reassign` y `phase:zone_check`.

---

## 7. Tuning Knobs


| Knob                          | Default     | Rango seguro    | Si muy bajo                                         | Si muy alto                                           |
| ----------------------------- | ----------- | --------------- | --------------------------------------------------- | ----------------------------------------------------- |
| `reassign_interval`           | 25s         | 10–45s          | NPCs cambien demasiado seguido, se ven inquietos    | Los NPCs se ven robóticos, siempre haciendo lo mismo  |
| `reassign_change_probability` | 0.70        | 0.3–0.9         | Pocos NPCs cambian, comportamiento estático         | Todos cambian a la vez, movimiento caótico en ráfagas |
| `action_min_duration`         | por acción  | 5–30s           | NPCs no completan animaciones completas             | NPCs parecen pegados a su waypoint                    |
| `action_max_duration`         | por acción  | 15–90s          | Reasignaciones muy frecuentes                       | Comportamiento muy predecible                         |
| `waypoint_arrival_threshold`  | 0.3m        | 0.1–1.0m        | NPCs nunca "llegan" a destino (se quedan orbitando) | NPCs ejecutan animación demasiado lejos del waypoint  |
| `social_max_pair_distance`    | 15m         | 5–25m           | Muy pocas interacciones sociales posibles           | NPCs viajan demasiado lejos para socializar (irreal)  |
| `phase_warning_time`          | 10s         | 5–15s           | Jugadores sin tiempo para reaccionar                | Warning demasiado anticipado, pierde tensión          |
| `zone_grace_period`           | 5s          | 3–10s           | Demasiado punitivo con jugadores que cambian zona   | Jugadores pueden ignorar la zona correcta             |
| `work_subzone_split`          | 33%/33%/33% | 20-50% por zona | Una zona queda vacía (visual raro)                  | Una zona queda sobrecargada                           |


---

## 8. Acceptance Criteria


| #     | Criterio                                                                                                      | Cómo verificar                                                                                          |
| ----- | ------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| AC-1  | Al inicio de cada fase, todos los clientes reciben el mismo `phase:start` con assignments para todos los NPCs | Loguear payload en 4 clientes simultáneos → payload idéntico (mismo timestamp y assignments)            |
| AC-2  | Los NPCs navegan a su waypoint asignado usando NavMesh (sin teletransportarse)                                | Observar visualmente que los NPCs caminan hacia sus destinos al inicio de fase                          |
| AC-3  | Los NPCs ejecutan la animación correcta al llegar al waypoint                                                 | NPC asignado a `cafe_sit_eat` → se sienta al llegar al seat, no camina en el aire                       |
| AC-4  | Un waypoint exclusivo no acepta más de 1 NPC simultáneo                                                       | Asignar manualmente 2 NPCs al mismo waypoint exclusivo → el segundo hace Idle                           |
| AC-5  | Los NPCs en LOOPING action completan el ciclo sin interrupciones de navegación                                | NPC con `yard_walk_perimeter` navega A→B→C→A en loop hasta recibir reassign                             |
| AC-6  | `npc:reassign` actualiza solo los NPCs mencionados; los demás continúan su acción                             | Emitir reassign para 3 NPCs → solo esos 3 cambian destino, los 16 restantes no se interrumpen           |
| AC-7  | Bandwidth de NPCs ≤ 50 B/s promedio en partida normal (excluye phase transitions)                             | Wireshark/Network Profiler Unity durante 2 minutos de Fase 4 → medir bytes/s de mensajes `npc:`*        |
| AC-8  | Un cliente que reconecta mid-partida recibe los assignments actuales y los NPCs quedan consistentes           | Desconectar cliente en Fase 3, reconectar en Fase 4 → NPCs en posiciones correctas, sin NPCs "fantasma" |
| AC-9  | En Fase 9, ningún NPC ejecuta una acción SOCIAL                                                               | Loggear todas las acciones asignadas en Fase 9 → ninguna tiene `socialPartnerId`                        |
| AC-10 | Los jugadores prisioneros reciben `phase:zone_check` si están en zona incorrecta 5s después de `phase:start`  | Jugador permanece en comedor cuando empieza Fase 4 (patio) → recibe warning a los 5s                    |
| AC-11 | Los NPCs de Fase 6 (Trabajo) permanecen en su sub-zona asignada toda la fase                                  | Observar visualmente que los NPCs de taller no navegan a lavandería durante la fase                     |
| AC-12 | Los NPCs de Formación (Fase 1) permanecen en su slot salvo acciones de fidget/whisper                         | Verificar que ningún NPC de Formación navega a más de 2m de su `formation_slot_XX`                      |


