# Architecture Decisions

## ADR-001: Sincronización de Estado — Authoritative Server + Client Prediction

**Status**: Implemented (Fase 1)  
**Date**: 2026-04-06

### Decision

Implement state sync as **authoritative server** with **client-side prediction**:
- Server (Node.js) is single source of truth; validates all actions
- Clients predict movement locally to avoid perceived input lag
- Clients receive `player:state` every 50ms (20 ticks/sec) for authoritativeposition
- Rubber-band reconciliation: if diff > 1m, client teleports; else lerp correction
- Delta compression: NPCs only sent if moved > 0.1m since last broadcast (5 sends/sec)

### Why

- **Fairness**: Server-side catch validation prevents guard exploits (e.g., catching through walls)
- **Low bandwidth**: Delta compression saves ~80% of NPC traffic; 4 KB/s per client
- **Invisible to player**: Tick rate (20/sec) + interpolation buffer (100ms) = smooth movement at <150ms RTT
- **Scalable**: Single server process handles 4 players + 20 NPCs on Render free tier (<20% CPU)

### Implications

- Fase 2 must implement movement controller that reads `player:state` and applies rubber-band
- All authority checks (catch distance, item pickup, phase transitions) must be server-side
- NPC AI loop runs server-side (not delegated to clients)

### Trade-offs Considered & Rejected

1. **Unity Netcode for GameObjects**: Rejected
   - Requires Unity headless server (complex deploy)
   - Host advantage in asymmetric matches
   - Harder to validate guard catches fairly

2. **Peer-to-peer (host authority)**: Rejected
   - Guard is host → prisoners can't verify catches
   - No fallback if host disconnects
   - Incompatible with 1v3 gameplay

---

## (More decisions to be added as they're made...)

## ADR-002: Ruta 1 Ventilacion Industrial — MVP Cooperativo con HUD Compartido

**Status**: Designed
**Date**: 2026-04-23

### Decision

Implementar la primera ruta de escape como una ruta cooperativa de ventilacion con:
- Inventario de presos de 2 slots.
- Pinzas y llave inglesa pesada como herramientas criticas; la llave ocupa 1 slot y aplica -5% velocidad.
- Plano electrico en Oficina del Guardia que revela el fusible correcto a todos los presos, no al guardia.
- Progreso compartido de Ruta 1 en HUD solo para presos.
- Guardia informado solo por senales observables del mundo: ventilador detenido, apagones, rechinido y reja abierta.
- Dos spawns posibles por herramienta critica y respawn backup anti-softlock.
- Segundo preso acelera la rejilla en MVP; reduccion de ruido y QTEs quedan para post-MVP polish.

### Why

La ruta necesita comunicar progreso cooperativo sin depender de voz externa ni revelar informacion injusta al guardia. El HUD compartido mantiene a los presos coordinados; las senales ambientales mantienen la asimetria y le dan al guardia formas justas de deducir que la fuga esta avanzando.

### Implications

- Backend debe modelar estado de ruta por pasos, no solo "items collected".
- Unity HUD de presos necesita un tracker compacto de Ruta 1.
- Eventos de progreso deben filtrar informacion por rol.
- Sistema de inventario debe respetar 2 slots para presos en gameplay.
- Implementacion MVP evita QTEs y reduccion dinamica de ruido para reducir scope de game jam.
