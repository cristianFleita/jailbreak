/**
 * Prison Layout — Zone-Based Definitions.
 *
 * Defines the logical zones used by the routine system.
 * Bounds have been moved entirely to Unity (ZoneRegistry.cs) 
 * so the backend no longer hardcodes spatial coordinates.
 *
 * Deterministic movement:
 *   - Backend sends zoneId + seed per NPC action
 *   - All clients use the same seeded RNG to generate identical random points inside the zone
 *   - NavMesh.SamplePosition snaps the point to walkable surface
 */

export interface ZoneDefinition {
  id: string
  name: string
  /** Parent zone ID — sub-zones inherit from a parent (e.g. cafeteria_counter ⊂ cafeteria) */
  parentZone?: string
}

export const ZONES: Record<string, ZoneDefinition> = {
  // ── Primary zones ──────────────────────────────────────────────────────
  cells:       { id: 'cells',       name: 'Bloque de Celdas' },
  cafeteria:   { id: 'cafeteria',   name: 'Comedor' },
  workshop:    { id: 'workshop',    name: 'Taller' },
  laundry:     { id: 'laundry',     name: 'Lavandería' },
  yard:        { id: 'yard',        name: 'Patio Exterior' },
  hallway:     { id: 'hallway',     name: 'Pasillo Principal' },
  office:      { id: 'office',      name: 'Oficina del Guardia' },
  electrical:  { id: 'electrical',  name: 'Sala de Electricidad' },

  // ── Sub-zones (finer areas inside primary zones) ───────────────────────
  cafeteria_counter:  { id: 'cafeteria_counter',  name: 'Counter del comedor',   parentZone: 'cafeteria' },
  cafeteria_seating:  { id: 'cafeteria_seating',  name: 'Mesas del comedor',     parentZone: 'cafeteria' },
  cafeteria_trash:    { id: 'cafeteria_trash',    name: 'Depósito de bandejas',  parentZone: 'cafeteria' },
  yard_exercise:      { id: 'yard_exercise',      name: 'Zona de ejercicio',     parentZone: 'yard' },
  yard_benches:       { id: 'yard_benches',       name: 'Bancos del patio',      parentZone: 'yard' },
  workshop_benches:   { id: 'workshop_benches',   name: 'Bancos de trabajo',     parentZone: 'workshop' },
  laundry_machines:   { id: 'laundry_machines',   name: 'Lavadoras',             parentZone: 'laundry' },
}

/**
 * Generate a random seed (non-deterministic — called only on the backend).
 */
export function generateSeed(): number {
  return (Math.random() * 0xFFFFFFFF) >>> 0
}

/**
 * Get all sub-zones for a given parent zone.
 */
export function getSubZones(parentId: string): ZoneDefinition[] {
  return Object.values(ZONES).filter(z => z.parentZone === parentId)
}
