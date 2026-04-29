using System.Collections.Generic;
using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Plays a prop loop while an interaction is active.
    /// Supports local ProgressAction coroutines, remote player:action broadcasts,
    /// Route1 local visuals, and NPC/script-driven action-point loops.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProgressInteractableLoopingSfx : MonoBehaviour
    {
        [Header("Audio")]
        public LoopingSfx loop;

        [Header("Local Progress")]
        public ProgressAction progressAction;
        public bool listenToProgressAction = true;

        [Header("Networked Actions")]
        public NetworkInteractable networkInteractable;
        public bool listenToPlayerActionBroadcasts = true;
        public string[] startActions =
        {
            "startWork",
            "startLeaveFood",
            "startLoadWasher",
            "route1.search_clue.start",
            "route1.disable_server.start",
        };
        public string[] stopActions =
        {
            "stopWork",
            "stopLeaveFood",
            "leaveFood",
            "stopLoadWasher",
            "loadWasher",
            "route1.search_clue.stop",
            "route1.disable_server.stop",
        };

        [Header("Debug")]
        public bool debugLogs;

        private readonly HashSet<string> remotePlayers = new HashSet<string>();
        private readonly HashSet<int> externalOwners = new HashSet<int>();
        private bool localActive;
        private bool subscribedNetwork;
        private bool subscribedProgress;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeProgress();
            SubscribeNetworkIfReady();
        }

        private void Update()
        {
            if (!subscribedNetwork) SubscribeNetworkIfReady();
        }

        private void OnDisable()
        {
            UnsubscribeProgress();
            UnsubscribeNetwork();
            localActive = false;
            remotePlayers.Clear();
            externalOwners.Clear();
            RefreshLoop();
        }

        public void PlayLocal()
        {
            localActive = true;
            RefreshLoop();
        }

        public void StopLocal()
        {
            localActive = false;
            RefreshLoop();
        }

        public void PlayExternal(Object owner)
        {
            externalOwners.Add(OwnerKey(owner));
            RefreshLoop();
        }

        public void StopExternal(Object owner)
        {
            externalOwners.Remove(OwnerKey(owner));
            RefreshLoop();
        }

        public static ProgressInteractableLoopingSfx FindForActionPoint(Transform actionPoint)
        {
            if (actionPoint == null) return null;

            var direct = actionPoint.GetComponent<ProgressInteractableLoopingSfx>();
            if (direct != null) return direct;

            var parent = actionPoint.parent;
            return parent != null ? parent.GetComponentInChildren<ProgressInteractableLoopingSfx>(true) : null;
        }

        private void ResolveReferences()
        {
            if (loop == null) loop = GetComponent<LoopingSfx>();
            if (progressAction == null) progressAction = GetComponentInParent<ProgressAction>();
            if (networkInteractable == null) networkInteractable = GetComponentInParent<NetworkInteractable>();
        }

        private void SubscribeProgress()
        {
            if (subscribedProgress || !listenToProgressAction || progressAction == null) return;
            progressAction.Started += PlayLocal;
            progressAction.Stopped += StopLocal;
            subscribedProgress = true;
        }

        private void UnsubscribeProgress()
        {
            if (!subscribedProgress || progressAction == null) return;
            progressAction.Started -= PlayLocal;
            progressAction.Stopped -= StopLocal;
            subscribedProgress = false;
        }

        private void SubscribeNetworkIfReady()
        {
            if (subscribedNetwork || !listenToPlayerActionBroadcasts) return;
            var net = NetworkManager.Instance;
            if (net == null) return;
            net.OnPlayerActionEvent += HandlePlayerAction;
            subscribedNetwork = true;
        }

        private void UnsubscribeNetwork()
        {
            if (!subscribedNetwork) return;
            var net = NetworkManager.Instance;
            if (net != null) net.OnPlayerActionEvent -= HandlePlayerAction;
            subscribedNetwork = false;
        }

        private void HandlePlayerAction(PlayerActionBroadcast data)
        {
            if (data == null || networkInteractable == null) return;
            if (data.objectId != networkInteractable.NetworkId) return;

            string key = string.IsNullOrEmpty(data.playerId) ? "remote" : data.playerId;
            if (Matches(startActions, data.action))
            {
                remotePlayers.Add(key);
                if (debugLogs) Debug.Log($"[ProgressLoopSfx] start {data.action} on {data.objectId}", this);
                RefreshLoop();
            }
            else if (Matches(stopActions, data.action))
            {
                remotePlayers.Remove(key);
                if (debugLogs) Debug.Log($"[ProgressLoopSfx] stop {data.action} on {data.objectId}", this);
                RefreshLoop();
            }
        }

        private void RefreshLoop()
        {
            if (loop == null) return;

            bool shouldPlay = localActive || remotePlayers.Count > 0 || externalOwners.Count > 0;
            if (shouldPlay) loop.Play();
            else loop.Stop();
        }

        private static bool Matches(string[] actions, string action)
        {
            if (actions == null || string.IsNullOrEmpty(action)) return false;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i] == action) return true;
            return false;
        }

        private static int OwnerKey(Object owner)
        {
            return owner != null ? owner.GetInstanceID() : 0;
        }
    }
}
