/**
 * Prison Layout — Zone definitions in backend world coordinates.
 * All positions use the same coordinate system as Unity (X, Y, Z).
 * Y = 1.5 throughout (player height above ground).
 * Map bounds: X[-50,50], Z[-50,50].
 *
 * Layout (top-down view, Z+ = north):
 *   Kitchen  [ Z 25–40 ]
 *   Yard     [ Z -15–15 ]
 *   Cells    [ Z -40 – -25 ]
 */

import { Vector3 } from './types.js'

export interface Zone {
  minX: number
  maxX: number
  minZ: number
  maxZ: number
  y:    number
}

export const ZONES = {
  cells:   { minX: -20, maxX: 20, minZ: -40, maxZ: -25, y: 1.5 },
  yard:    { minX: -30, maxX: 30, minZ: -15, maxZ:  15, y: 1.5 },
  kitchen: { minX: -20, maxX: 20, minZ:  25, maxZ:  40, y: 1.5 },
} as const satisfies Record<string, Zone>

export type ZoneName = keyof typeof ZONES

/** Returns a uniformly random point within the given zone. */
export function randomPointInZone(zone: Zone): Vector3 {
  return {
    x: zone.minX + Math.random() * (zone.maxX - zone.minX),
    y: zone.y,
    z: zone.minZ + Math.random() * (zone.maxZ - zone.minZ),
  }
}
