/**
 * Sistema 5: Inventory (Inventario)
 * Manages player inventory: which items each player has.
 * Each player has N inventory slots.
 */

import { GameRoomState } from '../types.js'

export interface InventorySlot {
  itemId: string
  itemType: string
}

export interface PlayerInventory {
  playerId: string
  slots: (InventorySlot | null)[]
  maxSlots: number
}

const MAX_INVENTORY_SLOTS = 4

export class InventorySystem {
  private inventories: Map<string, PlayerInventory> = new Map()

  constructor(private state: GameRoomState) {
    // Initialize empty inventories for all players
    this.state.players.forEach((player) => {
      this.inventories.set(player.id, {
        playerId: player.id,
        slots: Array(MAX_INVENTORY_SLOTS).fill(null),
        maxSlots: MAX_INVENTORY_SLOTS,
      })
    })
  }

  /**
   * Try to pick up an item.
   * Returns: { success, slot } or { success: false, reason }
   */
  tryPickupItem(
    playerId: string,
    itemId: string
  ): { success: boolean; slot?: number; reason?: string } {
    const inventory = this.inventories.get(playerId)
    if (!inventory) {
      return { success: false, reason: 'Player has no inventory' }
    }

    const item = this.state.items.get(itemId)
    if (!item) {
      return { success: false, reason: 'Item not found' }
    }

    // Find first empty slot
    const emptySlot = inventory.slots.findIndex((slot) => slot === null)
    if (emptySlot === -1) {
      return { success: false, reason: 'Inventory full' }
    }

    // Add to inventory
    inventory.slots[emptySlot] = {
      itemId,
      itemType: item.type,
    }

    console.log(`[INVENTORY] ${playerId} picked up ${itemId} in slot ${emptySlot}`)
    return { success: true, slot: emptySlot }
  }

  /**
   * Try to use an item.
   */
  tryUseItem(playerId: string, itemId: string, targetId?: string): { success: boolean; reason?: string } {
    const inventory = this.inventories.get(playerId)
    if (!inventory) {
      return { success: false, reason: 'Player has no inventory' }
    }

    // Find item in inventory
    const slotIndex = inventory.slots.findIndex((slot) => slot?.itemId === itemId)
    if (slotIndex === -1) {
      return { success: false, reason: 'Item not in inventory' }
    }

    // For now, just remove from inventory
    // Real item effects would be handled here
    inventory.slots[slotIndex] = null

    console.log(`[INVENTORY] ${playerId} used ${itemId}`)
    return { success: true }
  }

  /**
   * Drop item from inventory.
   */
  dropItem(playerId: string, itemId: string): { success: boolean; reason?: string } {
    const inventory = this.inventories.get(playerId)
    if (!inventory) {
      return { success: false, reason: 'Player has no inventory' }
    }

    const slotIndex = inventory.slots.findIndex((slot) => slot?.itemId === itemId)
    if (slotIndex === -1) {
      return { success: false, reason: 'Item not in inventory' }
    }

    inventory.slots[slotIndex] = null

    console.log(`[INVENTORY] ${playerId} dropped ${itemId}`)
    return { success: true }
  }

  /**
   * Query: does player have this item?
   */
  hasItem(playerId: string, itemId: string): boolean {
    const inventory = this.inventories.get(playerId)
    if (!inventory) return false

    return inventory.slots.some((slot) => slot?.itemId === itemId)
  }

  /**
   * Get player's inventory.
   */
  getInventory(playerId: string): PlayerInventory | null {
    return this.inventories.get(playerId) ?? null
  }

  /**
   * Get count of specific item type.
   */
  getItemCount(playerId: string, itemType: string): number {
    const inventory = this.inventories.get(playerId)
    if (!inventory) return 0

    return inventory.slots.filter((slot) => slot?.itemType === itemType).length
  }

  /**
   * Add new player inventory.
   */
  addPlayerInventory(playerId: string): void {
    this.inventories.set(playerId, {
      playerId,
      slots: Array(MAX_INVENTORY_SLOTS).fill(null),
      maxSlots: MAX_INVENTORY_SLOTS,
    })
  }
}
