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
        public string id;
        public string role;
        public SVector3 position;
        public SQuaternion rotation;
        public SVector3 velocity;
        public string movementState;
        public bool isAlive;
        public float health;
    }

    [Serializable]
    public class NPCStateData
    {
        public string id;
        public string type;
        public SVector3 position;
        public SQuaternion rotation;
        public string animState;
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
}
