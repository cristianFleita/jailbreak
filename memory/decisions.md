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
