using Jailbreak.Network;
using Jailbreak.Player;
using Jailbreak.UI;
using UnityEngine;

namespace Jailbreak.Interactions.Route1
{
    [RequireComponent(typeof(ProgressPointAction))]
    [RequireComponent(typeof(NetworkInteractable))]
    public abstract class Route1ProgressInteractable : MonoBehaviour, IInteractable
    {
        [Header("UI")]
        public ProgressBar progressBar;
        public string progressLabel;
        public string startLabel;
        public string stopLabel = "Stop";

        [Header("Interaction")]
        public KeyCode interactKey = KeyCode.E;
        public int priority;
        public bool prisonersOnly = true;
        public string activeStateTag = "Route1Action";

        [Header("Route Backend")]
        [Tooltip("Canonical backend route object id. Leave empty to use NetworkInteractable.NetworkId.")]
        public string routeObjectId;

        [Header("Debug")]
        public bool debugLogs;

        public KeyCode InteractKey => interactKey;
        public string ActionLabel => IsLocallyBusy ? stopLabel : startLabel;
        public int Priority => priority;
        public Transform Transform => transform;
        public string[] AllowedInStates => new[] { activeStateTag };
        public string BackendObjectId => RouteObjectId;

        public bool CanInteract
        {
            get
            {
                if (LocalActiveInstance != null && LocalActiveInstance != this) return false;
                if (!IsLocalRoleAllowed()) return false;
                if (!IsRouteActiveOrUnknown()) return false;
                if (IsLocallyBusy) return true;
                return IsAvailable(GameStateManager.Instance != null ? GameStateManager.Instance.Route1State : null);
            }
        }

        protected abstract string StartAction { get; }
        protected abstract string StopAction { get; }
        protected abstract string StateAction { get; }
        protected virtual string DefaultProgressLabel => "Working...";
        protected virtual string DefaultStartLabel => "Start";
        protected virtual string DefaultStopLabel => "Stop";
        protected virtual string DefaultAnimatorBool => "isWorkingTable";
        protected virtual float DefaultDurationSeconds => 1f;
        protected virtual int DefaultPriority => 30;

        private static Route1ProgressInteractable LocalActiveInstance;

        private ProgressPointAction progressPointAction;
        private NetworkInteractable networkInteractable;
        private Animator localAnimator;
        private CharacterController localController;
        private InteractionManager localInteractionManager;
        private Transform localActorRoot;
        private bool isLocalActive;
        private bool pendingStart;
        private bool pendingStop;
        private bool ignoreServerActiveUntilClear;
        private bool subscribedNetwork;
        private bool subscribedGameState;

        protected string NetworkId => networkInteractable != null ? networkInteractable.NetworkId : null;
        protected string RouteObjectId => !string.IsNullOrEmpty(routeObjectId) ? routeObjectId : NetworkId;
        protected bool IsLocallyBusy => isLocalActive || pendingStart || pendingStop;

        protected virtual void Awake()
        {
            progressPointAction = GetComponent<ProgressPointAction>();
            networkInteractable = GetComponent<NetworkInteractable>();

            if (string.IsNullOrEmpty(progressLabel)) progressLabel = DefaultProgressLabel;
            if (string.IsNullOrEmpty(startLabel)) startLabel = DefaultStartLabel;
            if (string.IsNullOrEmpty(stopLabel)) stopLabel = DefaultStopLabel;
            if (string.IsNullOrEmpty(activeStateTag)) activeStateTag = $"Route1:{StateAction}";
            if (priority <= 0) priority = DefaultPriority;

            var progressAction = progressPointAction != null ? progressPointAction.ProgressAction : null;
            if (progressAction != null)
            {
                if (string.IsNullOrEmpty(progressAction.animatorBoolName))
                    progressAction.animatorBoolName = DefaultAnimatorBool;
                if (progressAction.duration <= 0f)
                    progressAction.duration = DefaultDurationSeconds;
                progressAction.progress = Mathf.Clamp01(GetInitialProgress(GameStateManager.Instance != null ? GameStateManager.Instance.Route1State : null));
            }
        }

        protected virtual void OnEnable()
        {
            SubscribeIfNeeded();
        }

        protected virtual void Start()
        {
            SubscribeIfNeeded();
            EnsureProgressBar();

            var gsm = GameStateManager.Instance;
            if (gsm != null && gsm.Route1State != null)
                HandleRoute1StateChanged(gsm.Route1State);
        }

        protected virtual void Update()
        {
            SubscribeIfNeeded();
        }

        protected virtual void OnDisable()
        {
            StopLocalVisual(sendNetworkStop: false, broadcastRemoteStop: false);

            var net = NetworkManager.Instance;
            if (net != null && subscribedNetwork)
                net.OnNetworkErrorEvent -= HandleNetworkError;
            subscribedNetwork = false;

            var gsm = GameStateManager.Instance;
            if (gsm != null && subscribedGameState)
            {
                gsm.onRoute1StateChanged.RemoveListener(HandleRoute1StateChanged);
                gsm.onPlayerCaught.RemoveListener(HandlePlayerCaught);
            }
            subscribedGameState = false;
        }

        public void OnInteract(Collider source)
        {
            if (IsLocallyBusy)
            {
                ignoreServerActiveUntilClear = true;
                StopLocalVisual(sendNetworkStop: true, broadcastRemoteStop: true);
                return;
            }

            if (!CanInteract)
            {
                ShowRouteFeedback(UnavailableMessage(GameStateManager.Instance != null ? GameStateManager.Instance.Route1State : null));
                return;
            }
            if (source == null) return;

            var root = source.transform.root;
            if (!StartLocalVisual(root)) return;

            pendingStart = true;
            ignoreServerActiveUntilClear = false;
            SendInteract(StartAction);
            BroadcastRemote(StartAction);
        }

        public void ApplyRemote(Transform remoteRoot, string action)
        {
            if (remoteRoot == null) return;
            if (action != StartAction && action != StopAction) return;

            var animator = remoteRoot.GetComponentInChildren<Animator>();
            if (animator == null) return;

            string boolName = GetAnimatorBoolName();
            var sync = remoteRoot.GetComponent<RemotePlayerSync>();

            if (action == StartAction)
            {
                SnapActorToActionPoint(remoteRoot);
                if (sync != null) sync.enabled = false;
                if (!string.IsNullOrEmpty(boolName))
                    animator.SetBool(boolName, true);
            }
            else
            {
                if (!string.IsNullOrEmpty(boolName))
                    animator.SetBool(boolName, false);
                if (sync != null) sync.enabled = true;
            }
        }

        public bool HandlesRemoteAction(string action)
        {
            return action == StartAction || action == StopAction;
        }

        protected virtual bool IsAvailable(EscapeRoute1StatePayload state) => true;
        protected virtual string UnavailableMessage(EscapeRoute1StatePayload state) => null;

        protected virtual float GetInitialProgress(EscapeRoute1StatePayload state)
        {
            var inter = FindInteractionForLocalUser(state);
            return inter != null ? inter.progress : 0f;
        }

        protected bool IsVentOpen(EscapeRoute1StatePayload state, string ventId)
        {
            if (state == null || state.openVentIds == null || string.IsNullOrEmpty(ventId)) return false;
            foreach (var id in state.openVentIds)
                if (id == ventId) return true;
            return false;
        }

        protected float GetVentProgress(EscapeRoute1StatePayload state, string ventId)
        {
            if (state == null || state.ventProgress == null || string.IsNullOrEmpty(ventId)) return 0f;
            foreach (var entry in state.ventProgress)
                if (entry != null && entry.ventId == ventId) return Mathf.Clamp01(entry.progress);
            return 0f;
        }

        protected virtual void OnServerStateApplied(EscapeRoute1StatePayload state)
        {
        }

        protected virtual void OnLocalInteractionStarted(EscapeRoute1StatePayload state)
        {
        }

        protected virtual void OnLocalInteractionCompletedWithoutState(EscapeRoute1StatePayload state)
        {
        }

        protected void ShowRouteFeedback(string message, float seconds = 2.4f)
        {
            if (string.IsNullOrEmpty(message)) return;
            GameHudController.Instance?.ShowToast(message, seconds);
        }

        private void SubscribeIfNeeded()
        {
            var net = NetworkManager.Instance;
            if (net != null && !subscribedNetwork)
            {
                net.OnNetworkErrorEvent += HandleNetworkError;
                subscribedNetwork = true;
            }

            var gsm = GameStateManager.Instance;
            if (gsm != null && !subscribedGameState)
            {
                gsm.onRoute1StateChanged.AddListener(HandleRoute1StateChanged);
                gsm.onPlayerCaught.AddListener(HandlePlayerCaught);
                subscribedGameState = true;
            }
        }

        private bool StartLocalVisual(Transform actorRoot)
        {
            if (actorRoot == null) return false;

            localActorRoot = actorRoot;
            localAnimator = actorRoot.GetComponentInChildren<Animator>();
            localController = actorRoot.GetComponent<CharacterController>();
            localInteractionManager = actorRoot.GetComponent<InteractionManager>();

            if (localAnimator == null) return false;

            EnsureProgressBar();
            LocalActiveInstance = this;
            isLocalActive = true;

            if (localController != null) localController.enabled = false;
            if (localInteractionManager != null) localInteractionManager.PushState(activeStateTag);

            SnapActorToActionPoint(actorRoot);

            string boolName = GetAnimatorBoolName();
            if (!string.IsNullOrEmpty(boolName))
                localAnimator.SetBool(boolName, true);

            float initial = GetInitialProgress(GameStateManager.Instance != null ? GameStateManager.Instance.Route1State : null);
            SetProgress(initial);
            progressBar?.Show(progressLabel, initial);
            OnLocalInteractionStarted(GameStateManager.Instance != null ? GameStateManager.Instance.Route1State : null);
            DebugRoute($"Started local visual for {StartAction} on visual={NetworkId} route={RouteObjectId}");
            return true;
        }

        private void StopLocalVisual(bool sendNetworkStop, bool broadcastRemoteStop)
        {
            bool wasBusy = IsLocallyBusy;
            if (!wasBusy) return;

            if (sendNetworkStop) SendInteract(StopAction);
            if (broadcastRemoteStop) BroadcastRemote(StopAction);

            string boolName = GetAnimatorBoolName();
            if (localAnimator != null && !string.IsNullOrEmpty(boolName))
                localAnimator.SetBool(boolName, false);

            if (localController != null) localController.enabled = true;
            if (localInteractionManager != null) localInteractionManager.PopState(activeStateTag);

            isLocalActive = false;
            pendingStart = false;
            pendingStop = false;
            if (LocalActiveInstance == this) LocalActiveInstance = null;

            SetProgress(0f);
            progressBar?.Hide();

            localAnimator = null;
            localController = null;
            localInteractionManager = null;
            localActorRoot = null;
            DebugRoute($"Stopped local visual for {StopAction} on visual={NetworkId} route={RouteObjectId}");
        }

        private void HandleRoute1StateChanged(EscapeRoute1StatePayload state)
        {
            OnServerStateApplied(state);

            var inter = FindInteractionForLocalUser(state);
            if (inter != null)
            {
                if (ignoreServerActiveUntilClear) return;

                pendingStart = false;
                pendingStop = false;

                if (!isLocalActive)
                {
                    var localRoot = FindLocalActorRoot();
                    if (localRoot != null)
                        StartLocalVisual(localRoot);
                }

                float progress = Mathf.Clamp01(inter.progress);
                SetProgress(progress);
                progressBar?.Show(progressLabel, progress);
                return;
            }

            ignoreServerActiveUntilClear = false;

            if (isLocalActive && !pendingStart)
            {
                OnLocalInteractionCompletedWithoutState(state);
                StopLocalVisual(sendNetworkStop: false, broadcastRemoteStop: true);
            }
        }

        private void HandleNetworkError(ErrorPayload payload)
        {
            if (!pendingStart && !pendingStop) return;
            DebugRoute($"Backend rejected/errored while pending: {payload?.message ?? "unknown"}");

            if (pendingStart)
                ShowRouteFeedback(FeedbackMessageForBackendError(payload?.message));

            StopLocalVisual(sendNetworkStop: false, broadcastRemoteStop: true);
        }

        private static string FeedbackMessageForBackendError(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;

            var normalized = message.ToLowerInvariant();
            if (normalized.Contains("route1_cutters required")) return "Necesitas las pinzas";
            if (normalized.Contains("route1_wrench required")) return "Necesitas la llave francesa";
            if (normalized.Contains("ventilation must be disabled first")) return "Primero deshabilita la ventilacion";
            if (normalized.Contains("vent is not open yet")) return "Primero abre el conducto";
            return null;
        }

        private void HandlePlayerCaught(string targetId)
        {
            var net = NetworkManager.Instance;
            if (net == null || string.IsNullOrEmpty(targetId)) return;
            if (targetId == net.LocalUserId || targetId == net.LocalPlayerId)
                StopLocalVisual(sendNetworkStop: false, broadcastRemoteStop: true);
        }

        private Route1ActiveInteractionData FindInteractionForLocalUser(EscapeRoute1StatePayload state)
        {
            if (state == null || state.activeInteractions == null) return null;
            var net = NetworkManager.Instance;
            string localUserId = net != null ? net.LocalUserId : null;
            if (string.IsNullOrEmpty(localUserId) && net != null) localUserId = net.LocalPlayerId;
            if (string.IsNullOrEmpty(localUserId)) return null;

            foreach (var inter in state.activeInteractions)
            {
                if (inter == null) continue;
                if (inter.objectId != RouteObjectId) continue;
                if (inter.action != StateAction) continue;
                if (inter.startedByUserId == localUserId) return inter;
                if (inter.helperUserIds == null) continue;
                foreach (var helper in inter.helperUserIds)
                    if (helper == localUserId) return inter;
            }

            return null;
        }

        private bool IsLocalRoleAllowed()
        {
            if (!prisonersOnly) return true;
            var gsm = GameStateManager.Instance;
            if (gsm == null || string.IsNullOrEmpty(gsm.LocalRole)) return true;
            return gsm.LocalRole == "prisoner";
        }

        private bool IsRouteActiveOrUnknown()
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null || string.IsNullOrEmpty(gsm.ActiveRouteId)) return true;
            return gsm.ActiveRouteId == "route1_ventilation";
        }

        private void SendInteract(string action)
        {
            if (string.IsNullOrEmpty(RouteObjectId)) return;
            NetworkManager.Instance?.SendPlayerInteract(RouteObjectId, action);
        }

        private void BroadcastRemote(string action)
        {
            if (string.IsNullOrEmpty(NetworkId)) return;
            NetworkManager.Instance?.SendPlayerAction(NetworkId, action);
        }

        private void SnapActorToActionPoint(Transform actorRoot)
        {
            if (actorRoot == null || progressPointAction == null || progressPointAction.actionPoint == null) return;

            var p = progressPointAction.actionPoint;
            actorRoot.position = new Vector3(p.position.x, actorRoot.position.y, p.position.z);
            actorRoot.rotation = Quaternion.Euler(0f, p.eulerAngles.y, 0f);
        }

        private void SetProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            var progressAction = progressPointAction != null ? progressPointAction.ProgressAction : null;
            if (progressAction != null) progressAction.progress = progress;
            progressBar?.UpdateProgress(progress);
        }

        private string GetAnimatorBoolName()
        {
            var progressAction = progressPointAction != null ? progressPointAction.ProgressAction : null;
            return progressAction != null ? progressAction.animatorBoolName : DefaultAnimatorBool;
        }

        private Transform FindLocalActorRoot()
        {
            if (localActorRoot != null) return localActorRoot;

#if UNITY_2023_1_OR_NEWER
            var managers = Object.FindObjectsByType<InteractionManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var managers = Object.FindObjectsOfType<InteractionManager>();
#endif
            foreach (var manager in managers)
                if (manager != null && manager.enabled)
                    return manager.transform.root;

            return null;
        }

        private void EnsureProgressBar()
        {
            if (progressBar != null) return;
#if UNITY_2023_1_OR_NEWER
            progressBar = Object.FindFirstObjectByType<ProgressBar>(FindObjectsInactive.Include);
#else
            progressBar = Object.FindObjectOfType<ProgressBar>(true);
#endif
        }

        private void DebugRoute(string message)
        {
            if (!debugLogs) return;
            Debug.Log($"[{GetType().Name}] {message}", this);
        }
    }
}
