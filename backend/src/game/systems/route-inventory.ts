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
 * Critical route tools that use the authoritative hand/slot lifecycle.
 * They can be voluntarily thrown through `item.throw`; legacy `drop` remains
 * blocked so old clients cannot bypass the route inventory contract.
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

function resolveStoreSlotIndex(player: PlayerState, requestedSlotIndex?: number): number {
  const slots = player.inventorySlots ?? []

  if (requestedSlotIndex === undefined || requestedSlotIndex === null) {
    return firstEmptySlotIndex(player)
  }

  if (!Number.isInteger(requestedSlotIndex)) return -1
  if (requestedSlotIndex < 0 || requestedSlotIndex >= slots.length) return -1
  return requestedSlotIndex
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
 * Moves the player's held item into a requested inventory slot, or the first
 * empty slot when no slot is requested by an older client.
 */
export function storeHeldItem(
  state: GameRoomState,
  player: PlayerState,
  requestedSlotIndex?: number
): StoreResult {
  ensurePrisonerInventorySlots(player)

  if (!player.heldItemId) {
    return { success: false, reason: 'No item in hand' }
  }
  const slots = player.inventorySlots ?? []
  if (slots.length === 0) {
    return { success: false, reason: 'No inventory slots available for this role' }
  }
  const slotIndex = resolveStoreSlotIndex(player, requestedSlotIndex)
  if (slotIndex < 0) {
    const reason = requestedSlotIndex === undefined || requestedSlotIndex === null
      ? 'Inventory full'
      : 'Invalid inventory slot'
    return { success: false, reason }
  }
  if (slots[slotIndex] !== null && slots[slotIndex] !== undefined) {
    return { success: false, reason: 'Inventory slot occupied' }
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

/**
 * Removes a single itemId from the player's hand or any inventory slot.
 * Returns true if anything was cleared. Used by the voluntary throw flow
 * (mission gates re-evaluate via `findPlayerWithItem`, so dropping the
 * cutters/wrench rolls back `find_cutters` / `find_wrench` automatically).
 */
export function removeItemFromInventory(
  player: PlayerState,
  itemId: string
): boolean {
  if (!itemId) return false
  let mutated = false

  if (player.heldItemId === itemId) {
    player.heldItemId = null
    mutated = true
  }

  const slots = player.inventorySlots ?? []
  for (let i = 0; i < slots.length; i++) {
    if (slots[i]?.itemId === itemId) {
      slots[i] = null
      mutated = true
    }
  }
  player.inventorySlots = slots

  return mutated
}

/**
 * Voluntary single-item drop. Removes the item from the player's hand or
 * slot, marks the world ItemState as `dropped` at `position`, and broadcasts
 * `item:state`. Returns the updated ItemState, or null if the player did not
 * actually hold the item.
 *
 * Callers should follow up with `notifyRoute1InventoryChanged(room)` so the
 * mission checklist rolls back immediately instead of waiting for the next
 * tick that mutates an active interaction.
 */
export function dropInventoryItem(
  io: Server,
  roomId: string,
  state: GameRoomState,
  player: PlayerState,
  itemId: string,
  position: Vector3 = player.position
): ItemState | null {
  if (!removeItemFromInventory(player, itemId)) return null

  const item = dropItemAt(state, itemId, position)
  if (item) broadcastItemState(io, roomId, item)
  return item
}
