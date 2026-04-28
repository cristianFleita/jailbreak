using System;
using UnityEngine;

namespace Jailbreak.Network
{
    // ─── Primitives ──────────────────────────────────────────────────────────
    // Use public fields so Unity JsonUtility can deserialize in WebGL and Editor.

    [Serializable]
    public struct SVector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3 ToUnity() => new Vector3(x, y, z);
        public static SVector3 FromUnity(Vector3 v) => new SVector3 { x = v.x, y = v.y, z = v.z };

        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})";
    }

    [Serializable]
    public struct SQuaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Quaternion ToUnity() => new Quaternion(x, y, z, w);
        public static SQuaternion FromUnity(Quaternion q) => new SQuaternion { x = q.x, y = q.y, z = q.z, w = q.w };
    }

    // ─── Enums ───────────────────────────────────────────────────────────────

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        InGame,
        Reconnecting,
        PostGame
    }

    // ─── Core Entity Data ─────────────────────────────────────────────────────

    [Serializable]
    public class PlayerStateData
    {
        public string id;     // player/user ID (stable across reconnects — same as userId)
        public string userId; // persistent user ID (stable across reconnects)
        public string role;
        public SVector3 position;
        public SQuaternion rotation;
        public SVector3 velocity;
        public string movementState;
        public bool isAlive;
        public float health;
        public string spawnWaypointId; // e.g. "cell_door_exit_17" — resolve via WaypointRegistry
        public string carrying;        // e.g. "food_plate" — null/empty means "nothing carried"

        // ── Authoritative inventory (Ruta 1 / Phase A contract) ──────────
        // heldItemId: stable item id currently in hand (e.g. "route1_wrench").
        // inventorySlots: fixed-length (2 for prisoners). Null entries are empty.
        // Both are set by backend; Unity treats them as read-only truth.
        public string heldItemId;
        public InventorySlotData[] inventorySlots;
    }

    /// <summary>
    /// Authoritative inventory slot contents. Mirrors backend InventorySlotSync.
    /// A null array entry represents an empty slot.
    /// </summary>
    [Serializable]
    public class InventorySlotData
    {
        public string itemId;    // unique instance id (e.g. "route1_cutters_a")
        public string itemType;  // e.g. "route_tool"
        public string itemKind;  // family/kind shared by instances (e.g. "route1_cutters") — used by HUD for icon/name
        public string iconId;    // optional HUD icon hint
    }

    [Serializable]
    public class NPCStateData
    {
        public string id;
        public string type;
        public SVector3 position;
        public SQuaternion rotation;
        public string animState;
        public string spawnWaypointId; // e.g. "cell_door_exit_03"
    }

    [Serializable]
    public class ItemStateData
    {
        public string id;
        public string type;
        public SVector3 position;
        public bool isPickedUp;
        public string pickedUpBy;
    }

    [Serializable]
    public class PhaseData
    {
        public string current;
        public string phaseName;
        public float duration;
        public long startedAt;
        public string activeZone;
    }

    // ─── Event Payloads (Server → Client) ────────────────────────────────────

    [Serializable]
    public class PlayerJoinedPayload
    {
        public string playerId;
        public string role;
        public PlayerStateData[] players;
    }

    [Serializable]
    public class PlayerLeftPayload
    {
        public string playerId;
        public PlayerStateData[] players;
    }

    [Serializable]
    public class PlayerReconnectedPayload
    {
        public string playerId;
        public PlayerStateData[] players;
    }

    [Serializable]
    public class GameReconnectPayload
    {
        public PlayerStateData[] players;
        public NPCStateData[] npcs;
        public ItemStateData[] items;
        public PhaseData phase;
        public int tick;
        public string activeRouteId;        // "route1_ventilation" in MVP
        public JailPhaseSnapshot jailPhase; // populated when jail routine is active
    }

    [Serializable]
    public class PlayerStateUpdate
    {
        public PlayerStateData[] players;
    }

    [Serializable]
    public class NPCPositionUpdate
    {
        public NPCStateData[] npcs;
        public int tick;
    }

    [Serializable]
    public class PhaseChangePayload
    {
        public string phase;
        public string phaseName;
        public float duration;
        public string activeZone;
    }

    [Serializable]
    public class GuardCatchPayload
    {
        public string guardId;
        public string targetId;
        public bool success;
        public bool isPlayer;
        public int guardErrorCount;
        public int guardErrorThreshold;
    }

    [Serializable]
    public class ChaseStartPayload
    {
        public string guardId;
        public string targetId;
    }

    [Serializable]
    public class ChaseEndPayload
    {
        public string reason;
    }

    [Serializable]
    public class ItemPickupPayload
    {
        public string playerId;
        public string itemId;
        public int slot;
    }

    [Serializable]
    public class RiotAvailablePayload
    {
        public int errorsCount;
    }

    [Serializable]
    public class GameEndPayload
    {
        public string winner;
        public string reason;
    }

    [Serializable]
    public class ErrorPayload
    {
        public string message;
    }

    // ─── Auth & Room Lobby Payloads ─────────────────────────────────────────

    [Serializable]
    public class AuthRegisteredPayload
    {
        public string userId;
        public string displayName;
        public string socketId;
    }

    [Serializable]
    public class RoomCreatedPayload
    {
        public string roomId;
        public string hostUserId;
    }

    [Serializable]
    public class RoomPlayerInfo
    {
        public string userId;
        public string displayName;
        public string role;
        public bool isHost;
    }

    [Serializable]
    public class RoomStatePayload
    {
        public string roomId;
        public string hostUserId;
        public string status;
        public RoomPlayerInfo[] players;
    }

    [Serializable]
    public class RoomListEntryPayload
    {
        public string roomId;
        public string hostUserId;
        public string hostDisplayName;
        public string status;
        public int playerCount;
        public int maxPlayers;
        public long createdAt;
        public RoomPlayerInfo[] players;
    }

    [Serializable]
    public class RoomListPayload
    {
        public RoomListEntryPayload[] rooms;
    }

    [Serializable]
    public class RoomPlayerJoinedPayload
    {
        public string userId;
        public string displayName;
        public string role;
        public RoomPlayerInfo[] players;
    }

    [Serializable]
    public class RoomPlayerLeftPayload
    {
        public string userId;
        public string reason;
        public RoomPlayerInfo[] players;
    }

    [Serializable]
    public class RoomKickedPayload
    {
        public string roomId;
        public string reason;
    }

    [Serializable]
    public class RoomDestroyedPayload
    {
        public string roomId;
        public string reason;
    }

    [Serializable]
    public class GameStartPayload
    {
        public PlayerStateData[] players;
        public NPCStateData[] npcs;
        public PhaseData phase;
    }

    // ─── Tutorial Payloads (Fase A backend contract) ───────────────────────

    [Serializable]
    public class TutorialStartPayload
    {
        public float duration;
        public string role;
        public int seed;
        public string[] missions;
        public PlayerStateData[] players;
        /// <summary>Authoritative tutorial NPC routine assignments (3 entries).
        /// Each <c>actionId</c> maps to a scene-authored RoutineTemplate.</summary>
        public NPCAssignmentData[] npcAssignments;
    }

    [Serializable]
    public class TutorialStatePayload
    {
        public float remainingSeconds;
        public string[] completedMissionIds;
    }

    [Serializable]
    public class TutorialMissionCompletePayload
    {
        public string missionId;
    }

    [Serializable]
    public class TutorialEndPayload
    {
        public string nextScene;
    }

    // ─── Event Payloads (Client → Server) ────────────────────────────────────

    [Serializable]
    public class PlayerMovePayload
    {
        public string playerId;
        public SVector3 position;
        public SQuaternion rotation;
        public SVector3 velocity;
        public string movementState;
    }

    [Serializable]
    public class PlayerActionPayload
    {
        public string objectId;
        public string action;
    }

    [Serializable]
    public class PlayerActionBroadcast
    {
        public string playerId;
        public string objectId;
        public string action;
    }

    // ─── Jail Routine / NPC Phase System ─────────────────────────────────────
    // Mirrors backend types.ts: NPCAssignment, PhaseJailStartPayload, etc.

    /// <summary>A single step inside an ordered action sequence (cafeteria flow, Phase 1 chain).</summary>
    [Serializable]
    public class NPCActionStepData
    {
        public string actionId;
        public string animTrigger;
        public string zoneId;                // null = stay in place / use partner position
        public uint   seed;                  // deterministic random point seed
        public float  duration;              // 0 = walk-only (no dwell at destination)
        public string socialPartnerId;       // null for solo steps
    }

    [Serializable]
    public class NPCAssignmentData
    {
        public string   npcId;
        public string   actionId;
        public string   animTrigger;
        public string   zoneId;               // null if using chain
        public uint     seed;
        public uint[]   seedChain;            // non-null for LOOPING chains
        public float    duration;
        public bool     loop;
        public string   socialPartnerId;      // null for solo/idle
        public string   subZone;              // null unless Phase 6
        public float    walkSpeedMult;        // backend assigned walk speed var
        public NPCActionStepData[] actionSequence; // ordered steps (cafeteria, Phase 1)
    }

    [Serializable]
    public class PhaseJailStartPayload
    {
        public int                  phase;
        public string               phaseName;
        public float                duration;
        public string               zone;
        public NPCAssignmentData[]  npcAssignments;
    }

    [Serializable]
    public class PhaseWarningPayload
    {
        public int    nextPhase;
        public string nextPhaseName;
        public float  warningInSeconds;
    }

    [Serializable]
    public class NPCReassignPayload
    {
        public long                 timestamp;
        public NPCAssignmentData[]  assignments;
    }

    [Serializable]
    public class PhaseZoneCheckPayload
    {
        public string playerId;
        public string currentZone;
        public string expectedZone;
        public int    phase;
        public float  graceSeconds;
    }

    /// <summary>Included in game:reconnect when jail routine is active.</summary>
    [Serializable]
    public class JailPhaseSnapshot
    {
        public int                  phase;
        public string               phaseName;
        public string               zone;
        public float                duration;
        public float                elapsed;
        public NPCAssignmentData[]  npcAssignments;
    }

    // ─── NPC Personality & Emergent Behavior ─────────────────────────────

    /// <summary>Emitted when an NPC triggers a spontaneous/emergent behavior.</summary>
    [Serializable]
    public class NPCEmergentData
    {
        public string npcId;
        public string actionId;
        public string animTrigger;
        public string zoneId;
        public uint   seed;
        public float  duration;
        public string targetNpcId;
        public string mood;
    }

    /// <summary>Emitted when an NPC's mood visibly shifts (affects idle animation).</summary>
    [Serializable]
    public class NPCMoodShiftData
    {
        public string npcId;
        public string newMood;
        public string animHint;
    }

    [Serializable]
    public class NPCStateSync
    {
        public string npcId;
        public SVector3 position;
        public SQuaternion rotation;
        public int currentSequenceIndex;
        public string currentActionId;
    }

    [Serializable]
    public class NPCSyncStatePayload
    {
        public NPCStateSync[] npcs;
    }

    // ─── Escape Route System (Ruta 1 / Phase A contract) ────────────────────
    // Mirrors backend types.ts. MVP registers only `route1_ventilation`.

    /// <summary>
    /// Emitted on game start AND on reconnect so clients know which route is
    /// active before any route UI renders. MVP value is always
    /// "route1_ventilation".
    /// </summary>
    [Serializable]
    public class EscapeRouteSelectedPayload
    {
        public string activeRouteId;
    }

    /// <summary>
    /// Single in-progress interaction (clue search, server sabotage, vent
    /// unscrew, escape climb). Progress is 0..1.
    /// </summary>
    [Serializable]
    public class Route1ActiveInteractionData
    {
        public string   objectId;
        public string   action;
        public string   startedByUserId;
        public string[] helperUserIds;
        public float    progress;
    }

    /// <summary>
    /// Ruta 1 checklist + progress snapshot pushed to prisoners only.
    /// Guard audience never receives this event.
    ///
    /// correctServerId is populated only when clueFound = true.
    /// missions values are "locked" | "available" | "in_progress" | "complete".
    /// </summary>
    [Serializable]
    public class EscapeRoute1StatePayload
    {
        public string   routeId;
        public Route1Missions missions;
        public bool     clueFound;
        public string   correctServerId;   // null/empty unless clueFound
        public bool     serverDisabled;
        public VentProgressEntry[] ventProgress;  // serialized from ventProgressById dictionary
        public string[] openVentIds;
        public Route1ActiveInteractionData[] activeInteractions;
        public string[] escapingPlayerIds;
        public string[] escapedPlayerIds;
        public long     updatedAt;
    }

    /// <summary>
    /// Ruta 1 mission checklist. Each field is a status string:
    /// "locked" | "available" | "in_progress" | "complete".
    /// Names match the backend Route1MissionId enum so JsonUtility maps them
    /// directly from the JSON object the server emits.
    /// </summary>
    [Serializable]
    public class Route1Missions
    {
        public string find_cutters;
        public string find_clue;
        public string disable_server;
        public string find_wrench;
        public string open_vent;
        public string escape;
    }

    /// <summary>
    /// One vent's progress. Emitted as an array because JsonUtility cannot
    /// deserialize Record&lt;string, number&gt; dictionaries directly.
    /// Phase D is responsible for translating backend ventProgressById to
    /// this array on the way out.
    /// </summary>
    [Serializable]
    public class VentProgressEntry
    {
        public string ventId;
        public float  progress;
    }

    /// <summary>
    /// Observable world cues pushed to the guard (alarms, metal noise, etc.).
    /// Never includes correct server ID — guard infers from observation.
    /// cue ∈ { server_wrong_alarm, server_correct_power_off,
    ///          vent_unscrew_noise, vent_opened }.
    /// </summary>
    [Serializable]
    public class WorldCuePayload
    {
        public string   cue;
        public string   zone;
        public SVector3 position;
    }

    /// <summary>
    /// Public props state visible to everyone (ventilation on/off, open vents).
    /// </summary>
    [Serializable]
    public class WorldStatePayload
    {
        public bool     ventilationPowered;
        public string[] openVentIds;
    }

    /// <summary>
    /// Authoritative broadcast of a single item's lifecycle state.
    /// state ∈ { spawned, held, stored, dropped, respawning }.
    ///
    /// itemId is the unique instance id ("route1_cutters_a"). itemKind is the
    /// shared family ("route1_cutters") and is what clients should use to look
    /// up prefabs and HUD icons — multiple instances may share the same kind.
    /// </summary>
    [Serializable]
    public class ItemStateBroadcastPayload
    {
        public string   itemId;
        public string   itemType;
        public string   itemKind;
        public string   state;
        public string   holderUserId;
        public string   spawnAreaId;
        public SVector3 position;
    }

    /// <summary>
    /// One spawn area sent up to the backend so it can decide where a
    /// route-critical item appears (Phase C-01 / C-02).
    /// </summary>
    [Serializable]
    public class RouteSpawnAreaData
    {
        public string   spawnAreaId;
        public string   zoneId;
        public SVector3 position;
        public string[] allowedItemIds;
    }

    /// <summary>
    /// Host-only client-to-server payload emitted once per match after the
    /// GameScene has loaded. Backend validates, stores and picks one valid
    /// area per critical item, then broadcasts <c>item:state</c> updates.
    /// </summary>
    [Serializable]
    public class RegisterSpawnAreasPayload
    {
        public RouteSpawnAreaData[] areas;
    }
}
