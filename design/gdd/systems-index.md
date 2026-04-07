# Jailbreak — Systems Index

**Fecha:** 6 de abril de 2026  
**Total sistemas:** 25  
**MVP:** 12 sistemas | **Vertical Slice:** 9 sistemas | **Alpha/Polish:** 6 sistemas  

---

## 1. Enumeración de Sistemas

### Gameplay Core

| # | Sistema | Descripción | Origen | Prioridad |
|---|---------|-------------|--------|-----------|
| 1 | Movimiento FPS | Movimiento en primera persona, sprint, colisión con entorno | Explícito | MVP |
| 2 | Sistema de Persecución | Señalar → perseguir → atrapar/perder rastro, con timer y estados | Explícito | MVP |
| 3 | Sistema de Camuflaje | Perder al guardia: zona correcta, NPCs cercanos, cambio de área | Explícito | MVP |
| 4 | Sistema de Rutina/Fases | Timer de fases, transiciones, silbato, zonas activas por fase | Explícito | MVP |
| 5 | Sistema de Inventario | 2 slots, recoger, usar, soltar al ser capturado | Explícito | MVP |
| 6 | Rutas de Escape | 3 rutas (ventilación, túnel, carro) con progreso y acción final | Explícito | MVP (ruta 1), VS (ruta 2), Alpha (ruta 3) |
| 7 | Crafting Simple | Combinar objetos (destornillador+palo, almohada+ropa) | Implícito | MVP |
| 8 | Mecánicas de Molestia | Jabón, bandeja, apagar luz, falsa alarma, señales entre presos | Explícito | VS |
| 9 | Penalizaciones del Guardia | 3 errores escalonados → NPC enojado → zona bloqueada → motín | Explícito | VS |
| 10 | Motín | Activación manual, NPCs rodean al guardia, fin de partida | Explícito | VS |
| 11 | Condiciones de Victoria | Evaluar todas las condiciones de fin de partida por equipo | Explícito | MVP |

### IA y NPCs

| # | Sistema | Descripción | Origen | Prioridad |
|---|---------|-------------|--------|-----------|
| 12 | NPC State Machine | Estados: IDLE → TRANSITION → ANGRY → HOSTILE → RIOT | Explícito | MVP |
| 13 | NPC Rutina/NavMesh | Rutas precalculadas por fase, variación aleatoria, animaciones | Explícito | MVP |
| 14 | NPC Soborno | NPC reacciona a objeto de valor, cambia comportamiento (ruta 3) | Implícito | Alpha |

### Cámaras y Vigilancia

| # | Sistema | Descripción | Origen | Prioridad |
|---|---------|-------------|--------|-----------|
| 15 | Cámaras de Seguridad | 4 cámaras fijas, modo cámara del guardia, feed en HUD, puntos ciegos | Explícito | VS |
| 16 | Alertas de Comportamiento | Detectar zona incorrecta, notificar al guardia sin revelar identidad | Implícito | VS |

### Networking

| # | Sistema | Descripci��n | Origen | Prioridad |
|---|---------|-------------|--------|-----------|
| 17 | Lobby y Matchmaking | Crear/unirse sala, lista jugadores, asignación roles, "listo" — todo en Unity | Explícito | MVP |
| 18 | Sincronización de Estado | Socket.io desde Unity (plugin C#), server-authoritative, client prediction, delta NPCs | Explícito | MVP |
| 19 | Reconexión | 30 seg para reconectarse, personaje queda como NPC | Implícito | Alpha |

### UI/HUD (todo en Unity)

| # | Sistema | Descripción | Origen | Prioridad |
|---|---------|-------------|--------|-----------|
| 20 | HUD Presos | Fase, timer, inventario, alerta persecución, progreso escape, aliados — Unity UI | Explícito | VS |
| 21 | HUD Guardia | Fase, timer, mini-cámaras, errores, tensión motín, alertas — Unity UI | Explícito | VS |
| 22 | Pantallas de UI | Menú, lobby, asignación rol, captura, victoria/derrota, revancha — Unity UI | Explícito | Alpha |

### Audio

| # | Sistema | Descripción | Origen | Prioridad |
|---|---------|-------------|--------|-----------|
| 23 | Audio 3D | Pasos, golpes, tos, excavación, risas — posicional con rangos | Explícito | VS |
| 24 | Audio Ambiente + Stingers | Eco prisión, silbato de fase, stingers de eventos | Explícito | Alpha |

### Infraestructura

| # | Sistema | Descripción | Origen | Prioridad |
|---|---------|-------------|--------|-----------|
| 25 | Iluminación Dinámica | Cambio de luz por fase, linterna nocturna, apagón por interruptor | Implícito | Alpha |

> **Nota:** Unity-React Bridge fue eliminado como sistema. React es solo un wrapper/iframe sin lógica. Unity maneja todo: lobby, gameplay, UI, resultados. La conexión Socket.io es directa desde Unity al backend via plugin C#.

---

## 2. Mapa de Dependencias

### Capa 0 — Foundation (sin dependencias)

```
[1] Movimiento FPS
[4] Rutina/Fases
[18] Sincronización de Estado (Socket.io desde Unity)
```

### Capa 1 — Core (depende solo de Foundation)

```
[5] Inventario          ← [1]
[12] NPC State Machine  ← [4]
[13] NPC Rutina/NavMesh ← [4]
[17] Lobby/Matchmaking  ← [18]
[25] Iluminación        ← [4]
```

### Capa 2 — Feature (depende de Core)

```
[2] Persecución         ← [1], [12], [18]
[3] Camuflaje           ← [1], [13], [4]
[7] Crafting Simple     ← [5]
[8] Mecánicas Molestia  ← [5], [1]
[14] NPC Soborno        ← [12], [5]
[15] Cámaras Seguridad  ← [1], [18]
[16] Alertas Comportam. ← [13], [4], [18]
```

### Capa 3 — Sistemas Compuestos (depende de Feature)

```
[6] Rutas de Escape     ← [5], [7], [4], [3]
[9] Penalizaciones      ← [2], [12]
[10] Motín              ← [9], [12]
[11] Cond. Victoria     ← [6], [2], [10], [4]
[19] Reconexión         ← [18], [17]
```

### Capa 4 — Presentación

```
[20] HUD Presos         ← [5], [4], [2], [6]
[21] HUD Guardia        ← [2], [9], [15], [16]
[22] Pantallas UI       ← [17], [11]
[23] Audio 3D           ← [1], [2], [8]
[24] Audio Ambiente     ← [4], [11]
```

### Grafo visual simplificado

```
                 FOUNDATION
    ┌──────────┬──────────┬──────────┐
   [1]        [4]       [18]
  Movimiento  Rutina    Sync (Unity→Backend)
    │          │ │        │ │
    │    ┌─────┘ │    ┌───┘ │
    │    │       │    │     │
    ▼    ▼       ▼    │     ▼
   [5]  [12]   [13]  │   [17]
   Inv  NPC-SM NPC-Nav│   Lobby
    │    │       │    │     │
    ├────┼───────┼────┘     │
    │    │       │          │
    ▼    ▼       ▼          │
   [7]  [2]    [3]  [15]  [16]  [8]
  Craft Persc  Camo  Cams Alert Molest
    │    │       │     │    │    │
    └────┼───────┘     │    │    │
         │             │    │    │
         ▼             │    │    │
        [6]    [9]─────┘    │    │
       Escape  Penal        │    │
         │      │           │    │
         │     [10]         │    │
         │     Motín        │    │
         │      │           │    │
         ▼      ▼           ▼    ▼
        [11]   [20]  [21]  [23] [24]
        WinCond HUD-P HUD-G Audio Audio
                       │
                      [22]
                    Pantallas UI

    (Todo UI es Unity — React es solo wrapper iframe)
```

---

## 3. Sistemas de Alto Riesgo (Bottlenecks)

| Sistema | Dependientes directos | Nivel de riesgo |
|---------|----------------------|-----------------|
| Movimiento FPS (1) | 7 sistemas | **Crítico** — sin esto no hay juego |
| Rutina/Fases (4) | 7 sistemas | **Crítico** — estructura temporal de toda la partida |
| Sincronización (18) | 5 sistemas | **Crítico** — sin esto no hay multiplayer |
| NPC State Machine (12) | 5 sistemas | Alto — afecta persecución, penalizaciones, motín |
| Inventario (5) | 5 sistemas | Alto — afecta escape, crafting, molestias |

---

## 4. Orden de Diseño Recomendado

Orden de implementación combinando dependencias + prioridad MVP-first:

### MVP — Semana 1 (Foundation + Core)

| Orden | Sistema | Capa | Est. |
|-------|---------|------|------|
| 1 | Movimiento FPS (1) | Foundation | 1 día |
| 2 | Sincronización de Estado (18) | Foundation | 2 días |
| 3 | Sistema de Rutina/Fases (4) | Foundation | 0.5 día |
| 4 | NPC Rutina/NavMesh (13) | Core | 1.5 días |
| 5 | NPC State Machine (12) | Core | 1 día |

**Entregable S1:** 2 jugadores conectados, moviéndose en el mapa con NPCs siguiendo rutina.

### MVP — Semana 2 (Feature + Compuestos)

| Orden | Sistema | Capa | Est. |
|-------|---------|------|------|
| 6 | Inventario (5) | Core | 1.5 días |
| 7 | Persecución (2) | Feature | 2 días |
| 8 | Camuflaje (3) | Feature | 1 día |
| 9 | Crafting Simple (7) | Feature | 0.5 día |
| 10 | Rutas de Escape — Ruta 1: Ventilación (6) | Compuesto | 2 días |
| 11 | Condiciones de Victoria (11) | Compuesto | 1 día |
| 12 | Lobby y Matchmaking (17) | Core | 1 día |

**Entregable S2:** Partida jugable completa con 1 ruta de escape cooperativa.

### Vertical Slice — Semana 3

| Orden | Sistema | Capa | Est. |
|-------|---------|------|------|
| 13 | Penalizaciones del Guardia (9) | Feature | 1 día |
| 14 | Motín (10) | Compuesto | 0.5 día |
| 15 | Mecánicas de Molestia (8) | Feature | 0.5 día |
| 16 | Rutas de Escape — Ruta 2: Túnel (6) | Compuesto | 1.5 días |
| 17 | Cámaras de Seguridad (15) | Feature | 1.5 días |
| 18 | Alertas de Comportamiento (16) | Feature | 0.5 día |
| 19 | HUD Presos (20) | Presentación | 1 día |
| 20 | HUD Guardia (21) | Presentación | 1 día |
| 21 | Audio 3D (23) | Presentación | 1 día |

**Entregable S3:** Juego con 2 rutas, HUD completo, audio, cámaras.

### Alpha/Polish — Últimos 3–4 días

| Orden | Sistema | Capa | Est. |
|-------|---------|------|------|
| 22 | Rutas de Escape — Ruta 3: Carro de Ropa (6) | Compuesto | 1 día |
| 23 | NPC Soborno (14) | Feature | 0.5 día |
| 24 | Iluminación Dinámica (25) | Core | 0.5 día |
| 25 | Pantallas de UI (22) | Presentación | 0.5 día |
| 26 | Audio Ambiente + Stingers (24) | Presentación | 0.5 día |
| 27 | Reconexión (19) | Compuesto | 0.5 día |

**Entregable final:** Juego completo con 3 rutas, polish, UI completa.

---

## 5. Progress Tracker

| # | Sistema | Estado | GDD | Sprint |
|---|---------|--------|-----|--------|
| 1 | Movimiento FPS | Designed | [movimiento-fps.md](movimiento-fps.md) | S1 |
| 2 | Persecución | Not Started | — | S2 |
| 3 | Camuflaje | Not Started | — | S2 |
| 4 | Rutina/Fases | Not Started | — | S1 |
| 5 | Inventario | Not Started | — | S2 |
| 6 | Rutas de Escape | Not Started | — | S2/S3/Polish |
| 7 | Crafting Simple | Not Started | — | S2 |
| 8 | Mecánicas de Molestia | Not Started | — | S3 |
| 9 | Penalizaciones Guardia | Not Started | — | S3 |
| 10 | Motín | Not Started | — | S3 |
| 11 | Condiciones de Victoria | Not Started | — | S2 |
| 12 | NPC State Machine | Not Started | — | S1 |
| 13 | NPC Rutina/NavMesh | Not Started | — | S1 |
| 14 | NPC Soborno | Not Started | — | Polish |
| 15 | Cámaras de Seguridad | Not Started | — | S3 |
| 16 | Alertas de Comportamiento | Not Started | — | S3 |
| 17 | Lobby y Matchmaking | Not Started | — | S2 |
| 18 | Sincronización de Estado | Designed | [sincronizacion-estado.md](sincronizacion-estado.md) | S1 |
| 19 | Reconexión | Not Started | — | Polish |
| 20 | HUD Presos | Not Started | — | S3 |
| 21 | HUD Guardia | Not Started | — | S3 |
| 22 | Pantallas de UI | Not Started | — | Polish |
| 23 | Audio 3D | Not Started | — | S3 |
| 24 | Audio Ambiente + Stingers | Not Started | — | Polish |
| 25 | Iluminación Dinámica | Not Started | — | Polish |

---

## 6. Scope Cuts (si no hay tiempo)

| Prioridad de corte | Sistema | Impacto |
|--------------------|---------|---------|
| Cortar primero | Reconexión (19) | Bajo — pueden reconectarse manualmente |
| Cortar primero | Audio Ambiente (24) | Bajo — juego funciona sin música |
| Cortar segundo | Ruta 3: Carro de Ropa (6) + NPC Soborno (14) | Bajo — 2 rutas son suficientes |
| Cortar segundo | Iluminación Dinámica (26) | Medio — pierde atmósfera nocturna |
| No cortar | Cámaras de Seguridad (15) | Alto — herramienta clave del guardia |
| No cortar | Persecución (2) | Crítico — es la mecánica central |
