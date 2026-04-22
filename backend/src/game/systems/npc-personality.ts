/**
 * Sistema de Personalidad y Comportamiento Emergente de NPCs
 *
 * Cada NPC recibe un perfil de personalidad al inicio de la partida:
 *   - Archetype: sesga los pesos de acciones (troublemaker prefiere provocar, etc.)
 *   - Mood: estado emocional dinámico que cambia con eventos del juego
 *   - Social preferences: NPCs con quienes prefiere interactuar
 *   - Action history: evita repetir las mismas acciones
 *
 * El sistema NO reemplaza jail-routine.ts — lo complementa:
 *   - jail-routine selecciona acciones con weightedRandom()
 *   - npc-personality modifica los pesos antes de la selección
 *   - npc-personality puede inyectar acciones emergentes entre reassigns
 *
 * Backend authority: todas las decisiones de personalidad son server-side.
 * Unity solo recibe el resultado (assignment o emergent payload).
 */

import {
  GameRoomState,
  NPCArchetype,
  NPCMood,
  NPCProfile,
  EmergentAction,
  NPCEmergentPayload,
  NPCMoodShiftPayload,
  MoodTrigger,
  Vector3,
} from '../types.js'

// ─── Tuning Knobs ─────────────────────────────────────────────────────────────

/** Seconds between spontaneous action checks per NPC */
const SPONTANEOUS_CHECK_INTERVAL = 8

/** Seconds of guard proximity before mood shifts to nervous/agitated */
const GUARD_PROXIMITY_THRESHOLD  = 5

/** Distance (units) to consider guard "nearby" */
const GUARD_NEARBY_DISTANCE      = 15

/** Max action history length (prevents repetition) */
const ACTION_HISTORY_MAX         = 5

/** Base probability multiplier for emergent actions when mood matches */
const MOOD_MATCH_MULTIPLIER      = 2.0

/** Seconds before mood naturally decays toward calm */
const MOOD_DECAY_INTERVAL        = 30

/** Cooldown tracker: npcId:actionId → next available timestamp */
const emergentCooldowns = new Map<string, number>()

// ─── Archetype Weight Modifiers ───────────────────────────────────────────────

/**
 * Each archetype applies multipliers to action categories.
 * Values > 1.0 increase preference, < 1.0 decrease.
 * Keys match ActionDef.type or specific actionId prefixes.
 */
const ARCHETYPE_WEIGHTS: Record<NPCArchetype, Record<string, number>> = {
  troublemaker: {
    SOCIAL:     1.3,
    IDLE:       0.6,
    anti_guard: 2.5,   // loves provoking
    exercise:   1.4,
    shadowbox:  2.0,
    kick:       1.8,
    work:       0.5,
    lean_wall:  1.5,
  },
  loner: {
    SOCIAL:      0.3,
    IDLE:        1.5,
    LOOPING:     1.4,   // walks perimeter alone
    lean_wall:   2.0,
    sit_bench:   1.6,
    exercise:    1.2,
    work:        1.0,
    read_book:   2.0,
    idle_window: 1.8,
  },
  social: {
    SOCIAL:          2.0,
    IDLE:            0.7,
    talk_standing:   1.8,
    talk_seated:     1.8,
    sit_eat_talk:    1.5,
    greet_neighbor:  2.0,
    conversation:    2.0,
    play_cards:      1.5,
    kick_ball:       1.3,
  },
  worker: {
    IDLE:             0.5,
    work_bench:       2.0,
    carry_box:        1.8,
    inspect:          1.5,
    load_machine:     1.8,
    fold_clothes:     1.6,
    carry_basket:     1.5,
    SOCIAL:           0.6,
    exercise:         0.8,
  },
  athletic: {
    exercise:     2.5,
    shadowbox:    2.0,
    PushUp:       2.0,
    kick:         1.5,
    LOOPING:      1.8,  // perimeter running
    sit_bench:    0.4,
    IDLE:         0.5,
    lie_down:     0.3,
    lean_wall:    0.6,
  },
  nervous: {
    IDLE:          1.4,
    SOCIAL:        0.8,
    fidget:        2.5,
    pace:          2.0,
    idle_window:   2.0,
    lean_wall:     1.5,
    whisper:       1.8,
    exercise:      0.5,
    anti_guard:    0.2,  // too scared to provoke
  },
  schemer: {
    whisper:        2.5,
    SOCIAL:         1.2,
    lean_wall:      1.8,
    idle_window:    2.0,
    LOOPING:        1.3,
    inspect:        1.5,
    IDLE:           1.0,
    anti_guard:     0.8,
    read_book:      1.5,
  },
}

// ─── Mood Weight Modifiers ────────────────────────────────────────────────────

/**
 * Mood applies an additional layer of weight modification.
 * Stacks multiplicatively with archetype modifiers.
 */
const MOOD_WEIGHTS: Record<NPCMood, Record<string, number>> = {
  calm: {},  // no modifications — baseline
  bored: {
    IDLE:       0.5,   // restless, wants variety
    SOCIAL:     1.4,
    LOOPING:    1.3,
    exercise:   1.3,
    kick:       1.5,
  },
  agitated: {
    IDLE:        0.4,
    exercise:    1.5,
    shadowbox:   2.0,
    pace:        2.0,
    anti_guard:  1.8,
    kick:        1.5,
    SOCIAL:      0.7,
  },
  nervous: {
    SOCIAL:      1.3,   // safety in numbers
    IDLE:        0.6,
    fidget:      2.0,
    pace:        1.8,
    lean_wall:   0.5,   // doesn't want to be alone
    anti_guard:  0.1,
    whisper:     1.5,
  },
  rebellious: {
    anti_guard:  3.0,
    shadowbox:   1.8,
    kick:        1.5,
    SOCIAL:      1.2,
    IDLE:        0.3,
    work:        0.4,
    exercise:    1.3,
  },
  social: {
    SOCIAL:          2.0,
    talk_standing:   1.8,
    talk_seated:     1.5,
    play_cards:      1.5,
    IDLE:            0.5,
  },
  tired: {
    IDLE:        1.5,
    sit_bench:   2.0,
    lie_down:    2.0,
    lean_wall:   1.5,
    sleep:       1.8,
    exercise:    0.2,
    LOOPING:     0.4,
    SOCIAL:      0.7,
    anti_guard:  0.3,
  },
}

// ─── Emergent Action Catalog ──────────────────────────────────────────────────

/**
 * Acciones emergentes: comportamientos no programados que un NPC puede
 * disparar espontáneamente basándose en su mood, archetype y contexto.
 *
 * Estas acciones interrumpen brevemente la acción asignada y luego
 * el NPC vuelve a su comportamiento normal.
 */
const EMERGENT_ACTIONS: EmergentAction[] = [
  // ─── Anti-Guard Actions ───────────────────────────────────────────────
  {
    actionId: 'emergent_block_path',
    animTrigger: 'block_stance',
    type: 'anti_guard',
    duration: 4,
    requiresMood: ['rebellious', 'agitated'],
    guardProximityRequired: true,
    cooldownSeconds: 60,
    probability: 0.15,
  },
  {
    actionId: 'emergent_taunt_guard',
    animTrigger: 'taunt',
    type: 'anti_guard',
    duration: 3,
    requiresMood: ['rebellious'],
    requiresArchetype: ['troublemaker'],
    guardProximityRequired: true,
    cooldownSeconds: 45,
    probability: 0.20,
  },
  {
    actionId: 'emergent_argue_guard',
    animTrigger: 'argue',
    type: 'anti_guard',
    duration: 6,
    requiresMood: ['rebellious', 'agitated'],
    guardProximityRequired: true,
    cooldownSeconds: 90,
    probability: 0.10,
  },
  {
    actionId: 'emergent_fake_fight',
    animTrigger: 'fight_stance',
    type: 'anti_guard',
    duration: 5,
    requiresMood: ['rebellious', 'agitated'],
    requiresArchetype: ['troublemaker', 'athletic'],
    cooldownSeconds: 120,
    probability: 0.08,
  },
  {
    actionId: 'emergent_commotion',
    animTrigger: 'yell',
    type: 'anti_guard',
    duration: 4,
    requiresMood: ['rebellious'],
    cooldownSeconds: 90,
    probability: 0.12,
  },

  // ─── Environmental Interactions ───────────────────────────────────────
  {
    actionId: 'emergent_pushups',
    animTrigger: 'PushUp',
    type: 'environmental',
    duration: 8,
    requiresMood: ['bored', 'agitated', 'calm'],
    requiresArchetype: ['athletic', 'troublemaker'],
    cooldownSeconds: 40,
    probability: 0.20,
  },
  {
    actionId: 'emergent_stretch_random',
    animTrigger: 'stretch',
    type: 'environmental',
    duration: 4,
    cooldownSeconds: 20,
    probability: 0.15,
  },
  {
    actionId: 'emergent_check_window',
    animTrigger: 'idle_window',
    type: 'environmental',
    duration: 6,
    waypointTag: 'yard_wall_lean_',
    requiresMood: ['nervous', 'bored'],
    cooldownSeconds: 30,
    probability: 0.12,
  },
  {
    actionId: 'emergent_kick_wall',
    animTrigger: 'kick',
    type: 'environmental',
    duration: 3,
    requiresMood: ['agitated', 'rebellious'],
    cooldownSeconds: 45,
    probability: 0.10,
  },
  {
    actionId: 'emergent_inspect_something',
    animTrigger: 'inspect',
    type: 'environmental',
    duration: 5,
    requiresMood: ['bored', 'calm'],
    requiresArchetype: ['schemer', 'loner'],
    cooldownSeconds: 35,
    probability: 0.15,
  },
  {
    actionId: 'emergent_pace_nervously',
    animTrigger: 'pace',
    type: 'environmental',
    duration: 6,
    requiresMood: ['nervous', 'agitated'],
    cooldownSeconds: 25,
    probability: 0.18,
  },
  {
    actionId: 'emergent_sit_floor',
    animTrigger: 'sit_floor',
    type: 'environmental',
    duration: 10,
    requiresMood: ['tired', 'bored'],
    cooldownSeconds: 60,
    probability: 0.10,
  },

  // ─── Social Reactive Actions ──────────────────────────────────────────
  {
    actionId: 'emergent_whisper_nearby',
    animTrigger: 'whisper',
    type: 'social_reactive',
    duration: 4,
    requiresMood: ['nervous', 'social'],
    requiresArchetype: ['schemer', 'social', 'nervous'],
    cooldownSeconds: 30,
    probability: 0.18,
  },
  {
    actionId: 'emergent_fist_bump',
    animTrigger: 'fist_bump',
    type: 'social_reactive',
    duration: 2,
    requiresMood: ['calm', 'social', 'rebellious'],
    requiresArchetype: ['social', 'troublemaker', 'athletic'],
    cooldownSeconds: 20,
    probability: 0.20,
  },
  {
    actionId: 'emergent_nod_greeting',
    animTrigger: 'nod',
    type: 'social_reactive',
    duration: 1.5,
    cooldownSeconds: 15,
    probability: 0.25,
  },
  {
    actionId: 'emergent_look_around',
    animTrigger: 'look_around',
    type: 'social_reactive',
    duration: 3,
    requiresMood: ['nervous', 'calm'],
    cooldownSeconds: 20,
    probability: 0.15,
  },

  // ─── Self Expression ──────────────────────────────────────────────────
  {
    actionId: 'emergent_crack_knuckles',
    animTrigger: 'crack_knuckles',
    type: 'self_expression',
    duration: 2,
    requiresArchetype: ['troublemaker', 'athletic'],
    cooldownSeconds: 25,
    probability: 0.18,
  },
  {
    actionId: 'emergent_fidget',
    animTrigger: 'fidget',
    type: 'self_expression',
    duration: 3,
    requiresMood: ['nervous', 'bored'],
    cooldownSeconds: 15,
    probability: 0.22,
  },
  {
    actionId: 'emergent_sigh',
    animTrigger: 'sigh',
    type: 'self_expression',
    duration: 2,
    requiresMood: ['bored', 'tired'],
    cooldownSeconds: 20,
    probability: 0.15,
  },
  {
    actionId: 'emergent_shadow_punch',
    animTrigger: 'shadow_punch',
    type: 'self_expression',
    duration: 4,
    requiresArchetype: ['athletic', 'troublemaker'],
    requiresMood: ['agitated', 'bored', 'rebellious'],
    cooldownSeconds: 30,
    probability: 0.15,
  },
  {
    actionId: 'emergent_lean_think',
    animTrigger: 'lean_think',
    type: 'self_expression',
    duration: 5,
    requiresArchetype: ['schemer', 'loner'],
    requiresMood: ['calm', 'bored'],
    cooldownSeconds: 30,
    probability: 0.12,
  },
]

// ─── NPCPersonalitySystem ─────────────────────────────────────────────────────

export class NPCPersonalitySystem {
  private profiles = new Map<string, NPCProfile>()

  // Callbacks set by GameManager
  onEmergentAction!: (payload: NPCEmergentPayload) => void
  onMoodShift!:      (payload: NPCMoodShiftPayload) => void

  constructor(private state: GameRoomState) {
    this.initializeProfiles()
  }

  // ─── Public API ───────────────────────────────────────────────────────

  /** Called every tick (50ms). tickDelta in seconds. */
  update(tickDelta: number): void {
    for (const [npcId, profile] of this.profiles) {
      this.updateGuardProximity(npcId, profile, tickDelta)
      this.updateMoodDecay(profile, tickDelta)
      this.updateSpontaneousTimer(npcId, profile, tickDelta)
    }
  }

  /** Get the profile for an NPC. */
  getProfile(npcId: string): NPCProfile | undefined {
    return this.profiles.get(npcId)
  }

  /** Get all profiles (for reconnection snapshot). */
  getAllProfiles(): NPCProfile[] {
    return Array.from(this.profiles.values())
  }

  /**
   * Apply personality-based weight modifiers to an action pool.
   * Called by jail-routine before weightedRandom selection.
   *
   * Returns a new array with modified weights — does NOT mutate input.
   */
  applyWeightModifiers<T extends { actionId: string; type: string; weight: number }>(
    npcId: string,
    actions: T[]
  ): T[] {
    const profile = this.profiles.get(npcId)
    if (!profile) return actions

    const archetypeWeights = ARCHETYPE_WEIGHTS[profile.archetype]
    const moodWeights      = MOOD_WEIGHTS[profile.mood]

    return actions.map(action => {
      let modifier = 1.0

      // Archetype modifier: check actionId prefix and type
      modifier *= this.lookupModifier(archetypeWeights, action.actionId, action.type)

      // Mood modifier: same lookup, stacks multiplicatively
      modifier *= this.lookupModifier(moodWeights, action.actionId, action.type)

      // Scale by mood intensity (0.0 = no effect, 1.0 = full effect)
      modifier = 1.0 + (modifier - 1.0) * profile.moodIntensity

      // Anti-repetition: reduce weight if action was recently done
      if (profile.actionHistory.includes(action.actionId)) {
        modifier *= 0.3
      }

      return { ...action, weight: Math.max(0.1, action.weight * modifier) }
    })
  }

  /** Record that an NPC performed an action (for anti-repetition). */
  recordAction(npcId: string, actionId: string): void {
    const profile = this.profiles.get(npcId)
    if (!profile) return

    profile.actionHistory.push(actionId)
    if (profile.actionHistory.length > ACTION_HISTORY_MAX) {
      profile.actionHistory.shift()
    }
  }

  /** Trigger a mood change from an external event. */
  triggerMoodEvent(npcId: string, trigger: MoodTrigger): void {
    const profile = this.profiles.get(npcId)
    if (!profile) return
    this.applyMoodTransition(profile, trigger)
  }

  /** Batch trigger: guard entered a zone — all NPCs in that zone react. */
  onGuardEnteredZone(guardPosition: Vector3): void {
    for (const [npcId, profile] of this.profiles) {
      const npc = this.state.npcs.get(npcId)
      if (!npc) continue

      const dist = this.distance(npc.position, guardPosition)
      if (dist < GUARD_NEARBY_DISTANCE) {
        this.applyMoodTransition(profile, 'guard_nearby')
      }
    }
  }

  /** Batch trigger: a player was caught — nearby NPCs react. */
  onPlayerCaught(catchPosition: Vector3): void {
    for (const [npcId, profile] of this.profiles) {
      const npc = this.state.npcs.get(npcId)
      if (!npc) continue

      const dist = this.distance(npc.position, catchPosition)
      if (dist < 20) {
        this.applyMoodTransition(profile, 'caught_witness')
      }
    }
  }

  /** Batch trigger: new phase started. */
  onPhaseChanged(): void {
    for (const [, profile] of this.profiles) {
      this.applyMoodTransition(profile, 'phase_change')
    }
  }

  /** Batch trigger: riot meter is high. */
  onRiotBrewing(): void {
    for (const [, profile] of this.profiles) {
      this.applyMoodTransition(profile, 'riot_brewing')
    }
  }

  // ─── Private: Initialization ──────────────────────────────────────────

  private initializeProfiles(): void {
    const archetypes: NPCArchetype[] = [
      'troublemaker', 'loner', 'social', 'worker',
      'athletic', 'nervous', 'schemer',
    ]

    const npcIds = Array.from(this.state.npcs.keys()).sort()

    // Distribute archetypes ensuring variety — weighted distribution
    // For 20 NPCs: ~3 troublemaker, ~3 social, ~3 worker, ~3 athletic,
    //              ~3 loner, ~3 nervous, ~2 schemer
    const distribution = this.buildArchetypeDistribution(npcIds.length, archetypes)

    npcIds.forEach((npcId, index) => {
      const archetype = distribution[index]

      // Social preferences: 2-3 random NPCs from the pool
      const otherIds = npcIds.filter(id => id !== npcId)
      const socialPrefs = this.pickRandom(otherIds, 2 + Math.floor(Math.random() * 2))

      // Starting mood based on archetype tendency
      const startingMood = this.getStartingMood(archetype)

      this.profiles.set(npcId, {
        npcId,
        archetype,
        mood: startingMood,
        moodIntensity: 0.4 + Math.random() * 0.4,  // 0.4–0.8
        actionHistory: [],
        socialPreference: socialPrefs,
        lastMoodChange: Date.now(),
        guardProximityTimer: 0,
        spontaneousTimer: SPONTANEOUS_CHECK_INTERVAL * (0.5 + Math.random()),
      })
    })

    console.log('[NPC-PERSONALITY] Profiles initialized:')
    for (const [npcId, p] of this.profiles) {
      console.log(`  ${npcId}: ${p.archetype} (mood=${p.mood}, intensity=${p.moodIntensity.toFixed(2)})`)
    }
  }

  private buildArchetypeDistribution(count: number, archetypes: NPCArchetype[]): NPCArchetype[] {
    const result: NPCArchetype[] = []

    // Weighted distribution — some archetypes more common
    const weights: Record<NPCArchetype, number> = {
      troublemaker: 3,
      loner:        3,
      social:       3,
      worker:       3,
      athletic:     3,
      nervous:      3,
      schemer:      2,
    }

    // Fill base distribution
    for (const [arch, w] of Object.entries(weights) as [NPCArchetype, number][]) {
      for (let i = 0; i < w; i++) result.push(arch)
    }

    // Shuffle
    for (let i = result.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1))
      ;[result[i], result[j]] = [result[j], result[i]]
    }

    // Trim or extend to match NPC count
    while (result.length < count) {
      result.push(archetypes[Math.floor(Math.random() * archetypes.length)])
    }

    return result.slice(0, count)
  }

  private getStartingMood(archetype: NPCArchetype): NPCMood {
    const moodMap: Record<NPCArchetype, NPCMood[]> = {
      troublemaker: ['calm', 'bored', 'agitated'],
      loner:        ['calm', 'calm', 'bored'],
      social:       ['social', 'calm', 'calm'],
      worker:       ['calm', 'calm', 'calm'],
      athletic:     ['calm', 'bored', 'calm'],
      nervous:      ['nervous', 'calm', 'calm'],
      schemer:      ['calm', 'calm', 'bored'],
    }
    const options = moodMap[archetype]
    return options[Math.floor(Math.random() * options.length)]
  }

  // ─── Private: Guard Proximity ─────────────────────────────────────────

  private updateGuardProximity(npcId: string, profile: NPCProfile, tickDelta: number): void {
    const npc = this.state.npcs.get(npcId)
    if (!npc) return

    // Find guard player
    let guardNearby = false
    for (const [, player] of this.state.players) {
      if (player.role === 'guard') {
        const dist = this.distance(npc.position, player.position)
        if (dist < GUARD_NEARBY_DISTANCE) {
          guardNearby = true
          break
        }
      }
    }

    if (guardNearby) {
      profile.guardProximityTimer += tickDelta
      if (profile.guardProximityTimer >= GUARD_PROXIMITY_THRESHOLD) {
        // Guard has been nearby for a while — trigger mood shift
        if (profile.mood === 'calm' || profile.mood === 'bored') {
          this.applyMoodTransition(profile, 'guard_nearby')
          profile.guardProximityTimer = 0  // reset so it doesn't spam
        }
      }
    } else {
      if (profile.guardProximityTimer > 0) {
        profile.guardProximityTimer = 0
        if (profile.mood === 'nervous') {
          this.applyMoodTransition(profile, 'guard_left')
        }
      }
    }
  }

  // ─── Private: Mood Transitions ────────────────────────────────────────

  private applyMoodTransition(profile: NPCProfile, trigger: MoodTrigger): void {
    const oldMood = profile.mood
    let newMood   = profile.mood

    // Transition table: [currentMood, trigger] → newMood
    switch (trigger) {
      case 'guard_nearby':
        if (profile.archetype === 'troublemaker') {
          newMood = Math.random() < 0.6 ? 'rebellious' : 'agitated'
        } else if (profile.archetype === 'nervous') {
          newMood = 'nervous'
        } else {
          newMood = Math.random() < 0.5 ? 'nervous' : profile.mood
        }
        break

      case 'guard_left':
        if (profile.mood === 'nervous') newMood = 'calm'
        if (profile.mood === 'rebellious') newMood = Math.random() < 0.5 ? 'agitated' : 'calm'
        break

      case 'caught_witness':
        if (profile.archetype === 'nervous') {
          newMood = 'nervous'
        } else if (profile.archetype === 'troublemaker') {
          newMood = Math.random() < 0.7 ? 'rebellious' : 'agitated'
        } else {
          newMood = Math.random() < 0.4 ? 'nervous' : 'agitated'
        }
        break

      case 'phase_change':
        // Mild mood shift — phases are transitions
        if (profile.mood === 'rebellious' || profile.mood === 'agitated') {
          newMood = Math.random() < 0.4 ? 'calm' : profile.mood
        }
        break

      case 'social_interaction':
        if (profile.archetype === 'social') {
          newMood = 'social'
        } else if (profile.mood === 'nervous') {
          newMood = Math.random() < 0.3 ? 'calm' : 'nervous'
        }
        break

      case 'boredom':
        if (profile.mood === 'calm') newMood = 'bored'
        if (profile.mood === 'bored' && profile.archetype === 'troublemaker') {
          newMood = Math.random() < 0.3 ? 'agitated' : 'bored'
        }
        break

      case 'riot_brewing':
        if (profile.archetype === 'troublemaker') {
          newMood = 'rebellious'
        } else if (profile.archetype === 'nervous') {
          newMood = 'nervous'
        } else {
          newMood = Math.random() < 0.4 ? 'agitated' : profile.mood
        }
        break

      case 'time_decay':
        // Natural decay toward calm
        if (profile.mood !== 'calm') {
          newMood = Math.random() < 0.3 ? 'calm' : profile.mood
        }
        break
    }

    if (newMood !== oldMood) {
      profile.mood = newMood
      profile.lastMoodChange = Date.now()

      // Adjust intensity on mood change
      profile.moodIntensity = Math.min(1.0, profile.moodIntensity + 0.1)

      const animHint = this.getMoodIdleAnim(newMood)
      this.onMoodShift?.({
        npcId: profile.npcId,
        newMood,
        animHint,
      })

    }
  }

  private updateMoodDecay(profile: NPCProfile, tickDelta: number): void {
    const elapsed = (Date.now() - profile.lastMoodChange) / 1000
    if (elapsed >= MOOD_DECAY_INTERVAL && profile.mood !== 'calm') {
      this.applyMoodTransition(profile, 'time_decay')
      // Decay intensity slightly
      profile.moodIntensity = Math.max(0.2, profile.moodIntensity - 0.05)
    }
  }

  private getMoodIdleAnim(mood: NPCMood): string {
    switch (mood) {
      case 'nervous':    return 'fidget'
      case 'agitated':   return 'pace'
      case 'rebellious': return 'crack_knuckles'
      case 'bored':      return 'sigh'
      case 'tired':      return 'yawn'
      case 'social':     return 'look_around'
      default:           return 'idle'
    }
  }

  // ─── Private: Spontaneous Emergent Actions ────────────────────────────

  private updateSpontaneousTimer(npcId: string, profile: NPCProfile, tickDelta: number): void {
    profile.spontaneousTimer -= tickDelta
    if (profile.spontaneousTimer > 0) return

    // Reset timer with some randomness
    profile.spontaneousTimer = SPONTANEOUS_CHECK_INTERVAL * (0.7 + Math.random() * 0.6)

    // Try to trigger an emergent action
    const action = this.tryEmergentAction(npcId, profile)
    if (action) {
      this.recordAction(npcId, action.actionId)

      // Set cooldown
      const cooldownKey = `${npcId}:${action.actionId}`
      emergentCooldowns.set(cooldownKey, Date.now() + action.cooldownSeconds * 1000)

      this.onEmergentAction?.({
        npcId,
        actionId: action.actionId,
        animTrigger: action.animTrigger,
        duration: action.duration,
        mood: profile.mood,
      })
    }
  }

  private tryEmergentAction(npcId: string, profile: NPCProfile): EmergentAction | null {
    const now = Date.now()
    const candidates = EMERGENT_ACTIONS.filter(action => {
      // Mood requirement
      if (action.requiresMood && !action.requiresMood.includes(profile.mood)) return false

      // Archetype requirement
      if (action.requiresArchetype && !action.requiresArchetype.includes(profile.archetype)) return false

      // Guard proximity requirement
      if (action.guardProximityRequired && profile.guardProximityTimer < 1) return false

      // Cooldown check
      const cooldownKey = `${npcId}:${action.actionId}`
      const cooldownUntil = emergentCooldowns.get(cooldownKey) ?? 0
      if (now < cooldownUntil) return false

      return true
    })

    if (candidates.length === 0) return null

    // Roll probability for each candidate, pick first that passes
    // Shuffle first so it's not always the same order
    const shuffled = [...candidates].sort(() => Math.random() - 0.5)

    for (const action of shuffled) {
      let prob = action.probability

      // Boost probability if mood matches strongly
      if (action.requiresMood?.includes(profile.mood)) {
        prob *= MOOD_MATCH_MULTIPLIER
      }

      // Scale by mood intensity
      prob *= (0.5 + profile.moodIntensity * 0.5)

      if (Math.random() < prob) {
        return action
      }
    }

    return null
  }

  // ─── Private: Weight Lookup ───────────────────────────────────────────

  /**
   * Looks up a weight modifier by trying:
   * 1. Exact actionId match (e.g. "shadowbox")
   * 2. ActionId prefix match (e.g. "work" matches "work_bench")
   * 3. ActionType match (e.g. "SOCIAL")
   */
  private lookupModifier(
    weights: Record<string, number>,
    actionId: string,
    actionType: string
  ): number {
    // Exact match
    if (weights[actionId] !== undefined) return weights[actionId]

    // Prefix match — check if any key is a prefix of actionId
    for (const [key, value] of Object.entries(weights)) {
      if (actionId.startsWith(key)) return value
    }

    // Type match
    if (weights[actionType] !== undefined) return weights[actionType]

    return 1.0  // no modifier
  }

  // ─── Private: Utilities ───────────────────────────────────────────────

  private distance(a: Vector3, b: Vector3): number {
    const dx = a.x - b.x
    const dy = a.y - b.y
    const dz = a.z - b.z
    return Math.sqrt(dx * dx + dy * dy + dz * dz)
  }

  private pickRandom<T>(arr: T[], count: number): T[] {
    const shuffled = [...arr].sort(() => Math.random() - 0.5)
    return shuffled.slice(0, count)
  }
}
