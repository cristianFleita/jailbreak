/**
 * Route Inventory — authoritative hand + 2-slot storage for route-critical tools.
 *
 * Spec: design/gdd/ruta-1-ventilacion-industrial.md
 * Plan: design/gdd/ruta-1-implementation-plan.md (Phase B)
 *
 * Unlike the legacy InventorySystem (4-slot, socket-keyed), this module operates
 * directly on authoritative fields in PlayerState:
 *   - heldItemId        → item currently in hand
 *   - inventorySlots[]  → 2-slot array for prisoners (null = empty)
 *
 * It also mutates ItemState entries in state.items and emits 'item:state'
 * broadcasts so every client can reconstruct the visible lifecycle.
 */

import { Server } from 'socket.io'
import {
  GameRoomState,
  InventorySlotSync,
  ItemState,
  ItemStateBroadcast,
  PlayerState,
  RouteItemId,
  RouteItemLifecycle,
  Vector3,
} from '../types.js'

/**
 * Critical route tools that cannot be voluntarily thrown or dropped in MVP.
 * On capture/disconnect these are returned to the world at the player's last
 * known position so the route cannot softlock.
 */
export const CRITICAL_ROUTE_ITEM_IDS: readonly RouteItemId[] = [
  'route1_cutters',
  'route1_wrench',
]

export function isCriticalRouteItem(itemId: string | null | undefined): boolean {
  if (!itemId) return false
  return CRITICAL_ROUTE_ITEM_IDS.includes(itemId as RouteItemId)
}

// ─── Item lifecycle mutators ────────────────────────────────────────────────

/** Ensure an authoritative ItemState exists for a known route itemId. */
export function ensureItemState(
  state: GameRoomState,
  itemId: RouteItemId,
  itemType: string = 'route_tool',
  initialPosition: Vector3 = { x: 0, y: 0, z: 0 }
): ItemState {
  const existing = state.items.get(itemId)
  if (existing) return existing

  const item: ItemState = {
    id: itemId,
    type: itemType,
    itemId,
    itemType,
    state: 'spawned',
    position: { ...initialPosition },
    isPickedUp: false,
  }
  state.items.set(itemId, item)
  return item
}

export function buildItemStateBroadcast(item: ItemState): ItemStateBroadcast {
  return {
    itemId: (item.itemId ?? item.id) as RouteItemId,
    itemType: (item.itemType ?? item.type) as string,
    state: (item.state ?? 'spawned') as RouteItemLifecycle,
    holderUserId: item.holderUserId,
    spawnAreaId: item.spawnAreaId,
    position: { ...item.position },
  }
}

export function broadcastItemState(
  io: Server,
  roomId: string,
  item: ItemState
): void {
  io.to(roomId).emit('item:state', buildItemStateBroadcast(item))
}

export function broadcastAllRouteItemStates(
  io: Server,
  roomId: string,
  state: GameRoomState
): void {
  for (const item of state.items.values()) {
    if (item.itemId && isCriticalRouteItem(item.itemId)) {
      broadcastItemState(io, roomId, item)
    }
  }
}

export function emitAllRouteItemStatesToSocket(
  io: Server,
  socketId: string,
  state: GameRoomState
): void {
  for (const item of state.items.values()) {
    if (item.itemId && isCriticalRouteItem(item.itemId)) {
      io.to(socketId).emit('item:state', buildItemStateBroadcast(item))
    }
  }
}

// ─── Query helpers ──────────────────────────────────────────────────────────

/** True if the player currently holds the item either in hand or a slot. */
export function playerHasItem(player: PlayerState, itemId: string): boolean {
  if (player.heldItemId === itemId) return true
  const slots = player.inventorySlots ?? []
  return slots.some((s) => s?.itemId === itemId)
}

export function findPlayerWithItem(
  state: GameRoomState,
  itemId: string
): PlayerState | null {
  for (const player of state.playersByUserId.values()) {
    if (playerHasItem(player, itemId)) return player
  }
  return null
}

export function ensurePrisonerInventorySlots(player: PlayerState): void {
  if (player.role !== 'prisoner') return

  const existing = player.inventorySlots ?? []
  if (existing.length === 2) return

  player.inventorySlots = [existing[0] ?? null, existing[1] ?? null]
}

function firstEmptySlotIndex(player: PlayerState): number {
  const slots = player.inventorySlots ?? []
  return slots.findIndex((s) => s === null || s === undefined)
}

function buildSlotSync(item: ItemState): InventorySlotSync {
  return {
    itemId: (item.itemId ?? item.id) as RouteItemId,
    itemType: (item.itemType ?? item.type) as string,
  }
}

// ─── Authoritative operations ───────────────────────────────────────────────

export type PickupResult =
  | { success: true; item: ItemState }
  | { success: false; reason: string }

/**
 * Attempts to pick up `itemId` into `player`'s hand.
 * Rules:
 *  - Player must exist and be a prisoner
 *  - Player's hand must be empty
 *  - Item must exist and be in 'spawned' or 'dropped' state
 */
export function pickupToHand(
  state: GameRoomState,
  player: PlayerState,
  itemId: string
): PickupResult {
  if (player.role !== 'prisoner') {
    return { success: false, reason: 'Only prisoners can pick up route tools' }
  }
  if (player.heldItemId) {
    return { success: false, reason: 'Hand is already full' }
  }
  ensurePrisonerInventorySlots(player)

  const item = state.items.get(itemId)
  if (!item) {
    return { success: false, reason: `Unknown item ${itemId}` }
  }

  const lifecycle = item.state ?? 'spawned'
  if (lifecycle !== 'spawned' && lifecycle !== 'dropped') {
    return { success: false, reason: `Item ${itemId} is ${lifecycle}, not pickable` }
  }

  // Commit
  item.state = 'held'
  item.holderUserId = player.userId
  item.isPickedUp = true
  item.pickedUpBy = player.userId

  player.heldItemId = itemId

  return { success: true, item }
}

export type StoreResult =
  | { success: true; item: ItemState; slotIndex: number }
  | { success: false; reason: string }

/**
 * Moves the player's held item into the first empty inventory slot.
 */
export function storeHeldItem(
  state: GameRoomState,
  player: PlayerState
): StoreResult {
  ensurePrisonerInventorySlots(player)

  if (!player.heldItemId) {
    return { success: false, reason: 'No item in hand' }
  }
  const slots = player.inventorySlots ?? []
  if (slots.length === 0) {
    return { success: false, reason: 'No inventory slots available for this role' }
  }
  const slotIndex = firstEmptySlotIndex(player)
  if (slotIndex < 0) {
    return { success: false, reason: 'Inventory full' }
  }

  const itemId = player.heldItemId
  const item = state.items.get(itemId)
  if (!item) {
    // State desync — clear hand defensively.
    player.heldItemId = null
    return { success: false, reason: `Held item ${itemId} missing from world state` }
  }

  slots[slotIndex] = buildSlotSync(item)
  player.inventorySlots = slots
  player.heldItemId = null

  item.state = 'stored'
  item.holderUserId = player.userId
  item.isPickedUp = true
  item.pickedUpBy = player.userId

  return { success: true, item, slotIndex }
}

/**
 * Returns all items a player currently holds (hand + slots) and clears them.
 * Used on capture / disconnect to drop route tools back into the world.
 */
export function takeAllPlayerItems(player: PlayerState): string[] {
  const itemIds: string[] = []

  if (player.heldItemId) {
    itemIds.push(player.heldItemId)
    player.heldItemId = null
  }

  const slots = player.inventorySlots ?? []
  for (let i = 0; i < slots.length; i++) {
    const slot = slots[i]
    if (slot?.itemId) {
      itemIds.push(slot.itemId)
      slots[i] = null
    }
  }
  player.inventorySlots = slots

  return itemIds
}

/**
 * Clears only critical route tools from a player. This is the capture/abandon
 * path: non-route inventory remains untouched, while tools that can softlock
 * Ruta 1 become available in the world again.
 */
export function takeCriticalRouteItems(player: PlayerState): string[] {
  const itemIds: string[] = []

  if (isCriticalRouteItem(player.heldItemId)) {
    itemIds.push(player.heldItemId!)
    player.heldItemId = null
  }

  const slots = player.inventorySlots ?? []
  for (let i = 0; i < slots.length; i++) {
    const slot = slots[i]
    if (isCriticalRouteItem(slot?.itemId)) {
      itemIds.push(slot!.itemId)
      slots[i] = null
    }
  }
  player.inventorySlots = slots

  return itemIds
}

export function dropCriticalRouteItemsForPlayer(
  io: Server,
  roomId: string,
  state: GameRoomState,
  player: PlayerState,
  position: Vector3 = player.position,
  onItemDropped?: (itemId: RouteItemId) => void
): string[] {
  const itemIds = takeCriticalRouteItems(player)

  for (const itemId of itemIds) {
    const item = dropItemAt(state, itemId, position)
    if (item) {
      broadcastItemState(io, roomId, item)
      onItemDropped?.(itemId as RouteItemId)
    }
  }

  return itemIds
}

/**
 * Drops an item at a world position. Used when a player is captured or
 * disconnects while carrying a critical tool.
 */
export function dropItemAt(
  state: GameRoomState,
  itemId: string,
  position: Vector3
): ItemState | null {
  const item = state.items.get(itemId)
  if (!item) return null

  item.state = 'dropped'
  item.holderUserId = undefined
  item.position = { ...position }
  item.isPickedUp = false
  item.pickedUpBy = undefined
  return item
}
