/**
 * Sistema 6: Escape Routes (Rutas de Escape)
 * Tracks prisoner progress on escape routes.
 * Prisoners win when they reach the escape zone with all required items.
 */

import { GameRoomState } from '../types.js'

export interface EscapeProgress {
  playerId: string
  routeId: string
  collectedItems: Set<string>
  itemsNeeded: number
  hasEscaped: boolean
}

export class EscapeRouteSystem {
  private progressMap: Map<string, EscapeProgress> = new Map()
  private escapeZone = { x: 0, y: 1.5, z: -40, radius: 5 } // escape zone location (tunable)

  constructor(private state: GameRoomState) {
    // Initialize progress for all prisoners
    this.state.players.forEach((player) => {
      if (player.role === 'prisoner') {
        this.progressMap.set(player.id, {
          playerId: player.id,
          routeId: 'main_route',
          collectedItems: new Set(),
          itemsNeeded: 3, // prisoners need 3 items to escape
          hasEscaped: false,
        })
      }
    })
  }

  /**
   * Record that prisoner collected an item.
   */
  recordItemCollection(playerId: string, itemId: string): void {
    const progress = this.progressMap.get(playerId)
    if (!progress) return

    progress.collectedItems.add(itemId)
    console.log(
      `[ESCAPE] ${playerId} collected item ${itemId} (${progress.collectedItems.size}/${progress.itemsNeeded})`
    )
  }

  /**
   * Check if a player is in the escape zone.
   */
  isInEscapeZone(playerId: string): boolean {
    const player = this.state.players.get(playerId)
    if (!player) return false

    const dx = player.position.x - this.escapeZone.x
    const dz = player.position.z - this.escapeZone.z
    const dist = Math.sqrt(dx * dx + dz * dz)

    return dist <= this.escapeZone.radius
  }

  /**
   * Check if prisoner can escape (in zone + has items).
   * Returns: { canEscape, itemsCollected, itemsNeeded }
   */
  checkEscapeCondition(playerId: string): {
    canEscape: boolean
    itemsCollected: number
    itemsNeeded: number
  } {
    const progress = this.progressMap.get(playerId)
    if (!progress) {
      return { canEscape: false, itemsCollected: 0, itemsNeeded: 0 }
    }

    const inZone = this.isInEscapeZone(playerId)
    const hasItems = progress.collectedItems.size >= progress.itemsNeeded

    return {
      canEscape: inZone && hasItems,
      itemsCollected: progress.collectedItems.size,
      itemsNeeded: progress.itemsNeeded,
    }
  }

  /**
   * Mark prisoner as escaped.
   */
  recordEscape(playerId: string): void {
    const progress = this.progressMap.get(playerId)
    if (!progress) return

    progress.hasEscaped = true
    console.log(`[ESCAPE] ${playerId} ESCAPED!`)
  }

  /**
   * Check if all prisoners have escaped.
   */
  allPrisonersEscaped(): boolean {
    const prisoners = Array.from(this.state.players.values()).filter((p) => p.role === 'prisoner')

    if (prisoners.length === 0) return false

    return prisoners.every((prisoner) => {
      const progress = this.progressMap.get(prisoner.id)
      return progress?.hasEscaped ?? false
    })
  }

  /**
   * Get progress for a prisoner.
   */
  getProgress(playerId: string): EscapeProgress | null {
    return this.progressMap.get(playerId) ?? null
  }

  /**
   * Get all escaped prisoners.
   */
  getEscapedPrisoners(): string[] {
    return Array.from(this.progressMap.values())
      .filter((p) => p.hasEscaped)
      .map((p) => p.playerId)
  }

  /**
   * Add prisoner (called when new player joins mid-game).
   */
  addPrisoner(playerId: string): void {
    this.progressMap.set(playerId, {
      playerId,
      routeId: 'main_route',
      collectedItems: new Set(),
      itemsNeeded: 3,
      hasEscaped: false,
    })
  }
}
