/**
 * Sistema 3: Disguise (Camuflaje)
 * Prisoners can disguise themselves to avoid being caught.
 * Camuflage = movementState 'camuflaged', provides immunity to catch.
 */

import { GameRoomState } from '../types.js'

export class DisguiseSystem {
  private camouflagedPlayers: Set<string> = new Set()

  constructor(private state: GameRoomState) {}

  /**
   * Enable camuflage for a prisoner.
   * Sets movementState to 'camuflaged' which prevents catch attempts.
   */
  setCamuflage(playerId: string, camouflagedFlag: boolean): void {
    const player = this.state.players.get(playerId)
    if (!player || player.role !== 'prisoner') return

    if (camouflagedFlag) {
      player.movementState = 'camuflaged'
      this.camouflagedPlayers.add(playerId)
      console.log(`[DISGUISE] ${playerId} is now camuflaged`)
    } else {
      // Revert to idle
      if (player.movementState === 'camuflaged') {
        player.movementState = 'idle'
      }
      this.camouflagedPlayers.delete(playerId)
      console.log(`[DISGUISE] ${playerId} camuflage broken`)
    }
  }

  /**
   * Query: is this player camouflagedFlag?
   */
  isCamouflagedFlag(playerId: string): boolean {
    return this.camouflagedPlayers.has(playerId)
  }

  /**
   * Break camuflage if prisoner moves (optional mechanic).
   * For now, camuflage lasts as long as movementState is 'camuflaged'.
   */
  onPlayerMove(playerId: string): void {
    // Uncomment if camuflage breaks on any movement:
    // if (this.isCamouflagedFlag(playerId)) {
    //   this.setCamuflage(playerId, false)
    // }
  }
}
