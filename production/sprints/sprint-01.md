# Sprint 1 — 2026-04-06 al 2026-04-12

## Sprint Goal
Dos jugadores se conectan online, se mueven en primera persona por el mapa base de la prisión, y 18 NPCs siguen su rutina de fases con NavMesh.

## Capacity
- **Total días:** 7
- **Buffer (20%):** 1.5 días reservados para imprevistos
- **Disponibles:** 5.5 días

---

## Tasks

### Must Have (Critical Path)

| ID | Tarea | Owner | Est. | Dependencias | Criterio de Aceptación |
|----|-------|-------|------|-------------|----------------------|
| S1-01 | Setup Unity WebGL + SocketIOUnity plugin + build pipeline a React | Unity | 0.5d | — | Build WebGL carga en React sin errores |
| S1-02 | Mapa base: bloque celdas + comedor + pasillo + patio (geometría básica, sin arte) | Unity | 1.5d | S1-01 | Las 5 zonas del mapa son navegables con CharacterController |
| S1-03 | Movimiento FPS: WASD + mouse + sprint + crouch (según GDD `movimiento-fps.md`) | Unity | 1d | S1-02 | AC-1 a AC-6 del GDD: movimiento suave, FOV por rol, sin salto |
| S1-04 | Sincronización básica: Socket.io rooms, 2 jugadores se ven moverse en tiempo real | Node.js | 1d | S1-01, S1-03 | AC-1 a AC-3 del GDD sync: 2 clientes conectados, posiciones sync <100ms |
| S1-05 | Sistema de Rutina/Fases: timer server-side, 9 fases, silbato de cambio | Node.js + Unity | 0.5d | S1-04 | Timer avanza, clientes reciben `phase:change`, fase visible en pantalla |
| S1-06 | NPCs: spawn de 18 NPCs con NavMesh, rutina A→B por fase | Unity | 1d | S1-02, S1-05 | 18 NPCs caminan a zona correcta en cada fase, sin quedarse clavados |

**Subtotal Must Have: 5.5 días**

### Should Have

| ID | Tarea | Owner | Est. | Dependencias | Criterio de Aceptación |
|----|-------|-------|------|-------------|----------------------|
| S1-07 | NPC State Machine básica: IDLE + TRANSITION (sin ANGRY/HOSTILE/RIOT aún) | Unity | 0.5d | S1-06 | NPCs transicionan correctamente entre estados con fases |
| S1-08 | Diferenciación de roles al conectar: guardia (FOV 80°) vs. presos (FOV 70°) | Unity + Node.js | 0.5d | S1-04 | Roles asignados al iniciar sesión, FOV correcto por rol |

**Subtotal Should Have: 1d** (entra en el buffer si los Must Have terminan antes)

### Nice to Have

| ID | Tarea | Owner | Est. | Dependencias | Criterio de Aceptación |
|----|-------|-------|------|-------------|----------------------|
| S1-09 | Client prediction + rubber-band reconciliation (según GDD sync) | Unity | 0.5d | S1-04 | AC-5/AC-6: movimiento suave a 100ms ping simulado |
| S1-10 | Delta compression NPCs (solo enviar NPCs que se movieron >0.1m) | Node.js | 0.5d | S1-06 | AC-4 del GDD sync: bandwidth ≤ 5KB/s con 18 NPCs |

---

## Carryover de Sprint Anterior
Ninguno — primer sprint.

---

## Riesgos

| Riesgo | Probabilidad | Impacto | Mitigación |
|--------|-------------|---------|------------|
| SocketIOUnity incompatible con Unity 6 LTS o WebGL | Media | Alto | Testear el plugin en día 1 antes de construir nada encima. Alternativa: NativeWebSocket |
| NavMesh baking problemático con geometría del mapa | Media | Medio | Bake incremental. Zonas simples primero. |
| Performance WebGL con 18 NPCs + 2 jugadores <60fps | Baja | Medio | LOD agresivo desde inicio. Animations simples (blend tree de 2 estados). |
| Render (backend) cold start lento en free tier | Baja | Bajo | Primer deploy en día 2. Usar keep-alive ping si es necesario. |

---

## Dependencias Externas
- Plugin **SocketIOUnity** — verificar compatibilidad Unity 6 antes de S1-01
- Cuenta **Render** configurada para deploy del backend Node.js
- Cuenta **Vercel** configurada para deploy del wrapper React

---

## Definition of Done

- [ ] Todos los Must Have completados (S1-01 a S1-06)
- [ ] Dos jugadores reales (distinta máquina) se conectan y se ven mover
- [ ] 18 NPCs cambian de zona con cada fase sin crashear
- [ ] Build WebGL carga en el wrapper React sin errores en Chrome
- [ ] No hay bugs S1/S2 conocidos en las features entregadas
- [ ] GDDs de Rutina/Fases, NPC Rutina/NavMesh y NPC State Machine escritos antes de implementar S1-05/S1-06/S1-07

---

## Estado de Tareas

| ID | Estado |
|----|--------|
| S1-01 | ⬜ Not Started |
| S1-02 | ⬜ Not Started |
| S1-03 | ⬜ Not Started |
| S1-04 | ⬜ Not Started |
| S1-05 | ⬜ Not Started |
| S1-06 | ⬜ Not Started |
| S1-07 | ⬜ Not Started |
| S1-08 | ⬜ Not Started |
| S1-09 | ⬜ Not Started |
| S1-10 | ⬜ Not Started |
