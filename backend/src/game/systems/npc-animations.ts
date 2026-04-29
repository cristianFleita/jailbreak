/**
 * NPC animation catalog — single source of truth.
 *
 * `animTrigger` strings emitted from the backend NPC routine flow into
 * Unity's `NPCBehaviorController.MapTriggerToStateName()` (see
 * unity/JAILBREAK/Assets/Scripts/NPC/NPCBehaviorController.cs), which
 * resolves them to a state inside Character.controller. Centralising the
 * catalog here means:
 *
 *   - Refactors and typos surface in TS instead of as silent fall-through
 *     to the `Idle` default in Unity.
 *   - Tests can assert that every trigger emitted by `JailRoutineSystem`
 *     is canonical (no drift between backend and the Animator).
 *   - The player emote panel maps onto the same Animator states the NPCs
 *     use, so a prisoner emoting "Rummage" looks identical to an NPC at
 *     the laundry pile (the whole point of disguising as an NPC).
 *
 * If you add a trigger here, also wire it in `MapTriggerToStateName`.
 * If a new Animator state is required, add it to Character.controller.
 */

/** Every trigger string the backend may put in NPCAssignment.animTrigger. */
export const NPC_ANIM = {
  // ── Locomotion ──────────────────────────────────────────────
  IDLE: 'idle',
  WALK: 'walk',
  WALKING: 'Walking',
  RUN: 'run',
  CARRY_TRAY: 'carry_tray',

  // ── Idle micro-poses (all resolve to Animator "Idle") ────────
  STRETCH: 'stretch',
  YAWN: 'yawn',
  LEAN_WALL: 'lean_wall',

  // ── Social, standing ────────────────────────────────────────
  TALK_STANDING: 'talk_standing',
  WHISPER: 'whisper',
  SALUTE: 'salute',
  DISMISS: 'dismiss',
  SURPRISE: 'surprise',
  ARGUE: 'argue',

  // ── Social, seated ──────────────────────────────────────────
  TALK_SEATED: 'talk_seated',
  WHISPER_SEATED: 'whisper_seated',

  // ── Sitting / eating ────────────────────────────────────────
  SIT_EAT: 'sit_eat',
  SIT_EAT_TALK: 'sit_eat_talk',
  SIT_IDLE: 'sit_idle',
  SIT_BENCH: 'sit_bench',

  // ── Cell / sleep ────────────────────────────────────────────
  SLEEP: 'sleep',
  LIE_DOWN: 'lie_down',

  // ── Cafeteria / workshop / laundry interactions ─────────────
  SERVE_SELF: 'serve_self',
  DEPOSIT_TRAY: 'deposit_tray',
  RUMMAGING: 'rummaging',
  INSPECT: 'inspect',
  LOAD_MACHINE: 'load_machine',
  STORE_CLOTHES: 'store_clothes',
  WORK_BENCH: 'work_bench',

  // ── Yard exercise / fun ─────────────────────────────────────
  EXERCISE: 'exercise',
  SHADOWBOX: 'shadowbox',
  DANCE: 'dance',
} as const

export type NPCAnimTrigger = typeof NPC_ANIM[keyof typeof NPC_ANIM]

/** Convenience: every trigger as a flat readonly array. */
export const NPC_ANIM_TRIGGERS: readonly NPCAnimTrigger[] = Object.freeze(
  Object.values(NPC_ANIM) as NPCAnimTrigger[]
)

/**
 * Animator state names the player emote panel exposes. Each entry must be
 * a state actually present in Character.controller, and must appear in the
 * range of TRIGGER_TO_STATE so disguise parity holds (a prisoner emoting
 * one of these reproduces a state some NPC routine produces).
 */
export const EMOTE_STATES = [
  'Talking',
  'TellingSecret',
  'Salute',
  'Dismissing',
  'Surprised',
  'Angry',
  'Rummaging',
  'Opening',
  'ButtonPushing',
  'Punching',
  'PushUp',
  'Situps',
  'BicycleCrunch',
  'SillyDancing',
] as const

export type EmoteAnimatorState = typeof EMOTE_STATES[number]

/**
 * Backend trigger → Animator state name. Mirrors
 * NPCBehaviorController.MapTriggerToStateName() exactly. Keep them in sync
 * — the test in `npc-animations.test.ts` asserts every NPC_ANIM trigger
 * has an entry here and every EMOTE_STATE is reachable from at least one
 * trigger.
 */
export const TRIGGER_TO_STATE: Readonly<Record<NPCAnimTrigger, string>> = Object.freeze({
  // locomotion
  idle: 'Idle',
  walk: 'Walking',
  Walking: 'Walking',
  run: 'Running',
  carry_tray: 'Walking',
  // idle micro-poses
  stretch: 'Idle',
  yawn: 'Idle',
  lean_wall: 'Idle',
  // social standing
  talk_standing: 'Talking',
  whisper: 'TellingSecret',
  salute: 'Salute',
  dismiss: 'Dismissing',
  surprise: 'Surprised',
  argue: 'Angry',
  // social seated
  talk_seated: 'SittingTalking',
  whisper_seated: 'TellingSecret',
  // sitting
  sit_eat: 'Sitting',
  sit_eat_talk: 'SittingTalking',
  sit_idle: 'Sitting',
  sit_bench: 'SeatedIdle',
  // cell / sleep
  sleep: 'LayingPose',
  lie_down: 'LyingDown',
  // chores
  serve_self: 'Rummaging',
  deposit_tray: 'Opening',
  rummaging: 'Rummaging',
  inspect: 'Rummaging',
  load_machine: 'Opening',
  store_clothes: 'Opening',
  work_bench: 'ButtonPushing',
  // exercise / fun
  exercise: 'PushUp',
  shadowbox: 'Punching',
  dance: 'SillyDancing',
})

/** True iff `trigger` is a recognised NPC animation trigger. */
export function isCanonicalNPCTrigger(trigger: string): trigger is NPCAnimTrigger {
  return Object.prototype.hasOwnProperty.call(TRIGGER_TO_STATE, trigger)
}
