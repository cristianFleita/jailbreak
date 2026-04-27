using System;
using System.Collections.Generic;
using Jailbreak.Network;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Jailbreak.UI
{
    /// <summary>
    /// UI Toolkit HUD for the in-game scene.
    /// Reads only authoritative network state: phase events, local inventory,
    /// and the prisoner-only Ruta 1 payload.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameHudController : MonoBehaviour
    {
        public static GameHudController Instance { get; private set; }

        [Header("References")]
        [SerializeField] private UIDocument uiDocument;

        [Header("Route Tool Icons")]
        [SerializeField] private Sprite cuttersIcon;
        [SerializeField] private Sprite wrenchIcon;
        [SerializeField] private Sprite unknownItemIcon;

        [Header("Visibility")]
        [SerializeField] private bool hideInventoryForGuard = true;
        [SerializeField] private bool disableLegacyTmpInventoryHud = true;

        private const string Route1Id = "route1_ventilation";
        private const float ToastDefaultSeconds = 2.4f;

        private VisualElement _root;
        private VisualElement _inventoryPanel;
        private VisualElement _route1Panel;
        private VisualElement _routeChecklist;
        private VisualElement _heldItemIcon;
        private VisualElement _toastPanel;
        private VisualElement _guardErrorPanel;
        private Label _route1ToggleHint;
        private Label _phaseLabel;
        private Label _timeLabel;
        private Label _roleLabel;
        private Label _heldItemLabel;
        private Label _toastLabel;
        private Label _guardErrorLabel;
        private bool _route1Collapsed;

        private HudSlot _slot0;
        private HudSlot _slot1;

        private readonly Dictionary<string, MissionRow> _missions = new();

        private GameStateManager _gsm;
        private NetworkManager _net;
        private ItemInventory _localInventory;
        private bool _gsmBound;
        private bool _netBound;
        private bool _localInventoryBound;
        private bool _legacyHudDisabled;

        private string _phaseName;
        private string _phaseZone;
        private int _phaseNumber;
        private float _phaseDuration;
        private float _phaseStartedAtRealtime;
        private string _lastRenderedRole;
        private float _nextSlowRefresh;
        private float _toastHideAtRealtime;
        private int _guardErrorCount;
        private int _guardErrorThreshold = 3;

        private void OnEnable()
        {
            Instance = this;
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            CacheElements();
            BuildMissionRows();
            TryBindSystems();
            TryBindLocalInventory();
            DisableLegacyInventoryHud();
            RefreshAll();
        }

        private void OnDisable()
        {
            UnbindSystems();
            UnbindLocalInventory();
            if (Instance == this) Instance = null;
        }

        private void ToggleRoute1Collapsed()
        {
            _route1Collapsed = !_route1Collapsed;
            ApplyRoute1CollapsedState();
        }

        private void ApplyRoute1CollapsedState()
        {
            if (_routeChecklist != null)
                _routeChecklist.style.display = _route1Collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (_route1ToggleHint != null)
                _route1ToggleHint.text = _route1Collapsed ? "[TAB] SHOW" : "[TAB] HIDE";
        }

        private void Update()
        {
            TryBindSystems();
            TryBindLocalInventory();
            RefreshTimer();
            RefreshToast();
            HandleRoute1ToggleInput();

            if (Time.unscaledTime < _nextSlowRefresh) return;
            _nextSlowRefresh = Time.unscaledTime + 0.5f;

            var role = _gsm != null ? _gsm.LocalRole : null;
            if (_lastRenderedRole != role)
                RefreshAll();
        }

        private void HandleRoute1ToggleInput()
        {
            if (!WasTabPressedThisFrame()) return;
            if (_route1Panel == null || _route1Panel.style.display == DisplayStyle.None) return;
            ToggleRoute1Collapsed();
        }

        private static bool WasTabPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null) return keyboard.tabKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Tab);
#else
            return false;
#endif
        }

        private void CacheElements()
        {
            if (uiDocument == null) return;

            _root = uiDocument.rootVisualElement;
            if (_root == null) return;

            _phaseLabel = _root.Q<Label>("PhaseLabel") ?? _root.Q<Label>("FaseLbl");
            _timeLabel = _root.Q<Label>("TimeLabel") ?? _root.Q<Label>("TimeLbl");
            _roleLabel = _root.Q<Label>("RoleLabel");
            _inventoryPanel = _root.Q<VisualElement>("InventoryPanel") ?? _root.Q<VisualElement>("Inventory");
            _route1Panel = _root.Q<VisualElement>("Route1Panel");
            _routeChecklist = _root.Q<VisualElement>("RouteChecklist");
            _heldItemIcon = _root.Q<VisualElement>("HeldItemIcon");
            _heldItemLabel = _root.Q<Label>("HeldItemLabel");
            _toastPanel = _root.Q<VisualElement>("HudToast");
            _toastLabel = _root.Q<Label>("HudToastLabel");
            _guardErrorPanel = _root.Q<VisualElement>("GuardErrorPanel");
            _guardErrorLabel = _root.Q<Label>("GuardErrorLabel");
            _route1ToggleHint = _root.Q<Label>("Route1ToggleHint");
            ApplyRoute1CollapsedState();

            _slot0 = new HudSlot(
                _root.Q<VisualElement>("InventorySlot0"),
                _root.Q<VisualElement>("InventorySlot0Icon"),
                _root.Q<Label>("InventorySlot0Label")
            );
            _slot1 = new HudSlot(
                _root.Q<VisualElement>("InventorySlot1"),
                _root.Q<VisualElement>("InventorySlot1Icon"),
                _root.Q<Label>("InventorySlot1Label")
            );
        }

        private void BuildMissionRows()
        {
            _missions.Clear();
            if (_routeChecklist == null) return;
            _routeChecklist.Clear();

            // 1. Objetivo: Herramientas
            AddMissionRow("find_cutters", "Find the pliers and save them to your inventory.");

            // 2. Objetivo: Pista
            AddMissionRow("find_clue", "Search the Guard's Office for the Fuse Box schematics.");

            // 3. Objetivo: Sabotaje
            AddMissionRow("disable_server", "Locate the Electrical Panel and cut the wires to disable the power.");

            // 4. Objetivo: Herramienta de acceso
            AddMissionRow("find_wrench", "Retrieve the wrench from the maintenance area.");

            // 5. Objetivo: Identificación y Apertura (Corregido)
            AddMissionRow("open_vent", "Find a Ventilation Shaft on the wall and use the wrench to open it.");

            // 6. Objetivo: Escape
            AddMissionRow("escape", "Crawl through the vents to reach the Roof Access and escape.");
        }

        private void AddMissionRow(string id, string title)
        {
            var rowRoot = new VisualElement { name = $"Mission_{id}" };
            rowRoot.style.flexDirection = FlexDirection.Column;
            rowRoot.style.marginBottom = 5;

            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;

            var marker = new Label("[ ]");
            marker.style.width = 28;
            marker.style.fontSize = 11;
            marker.style.unityFontStyleAndWeight = FontStyle.Bold;
            marker.style.color = new Color(0.76f, 0.84f, 0.92f);

            var titleLabel = new Label(title);
            titleLabel.style.flexGrow = 1;
            titleLabel.style.fontSize = 11;
            titleLabel.style.color = Color.white;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;

            var progressLabel = new Label(string.Empty);
            progressLabel.style.width = 42;
            progressLabel.style.fontSize = 10;
            progressLabel.style.color = new Color(0.76f, 0.84f, 0.92f);
            progressLabel.style.unityTextAlign = TextAnchor.MiddleRight;

            var track = new VisualElement();
            track.style.height = 3;
            track.style.marginLeft = 28;
            track.style.marginTop = 2;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);

            var fill = new VisualElement();
            fill.style.height = new Length(100f, LengthUnit.Percent);
            fill.style.width = 0;
            fill.style.backgroundColor = new Color(0.65f, 0.9f, 1f);
            track.Add(fill);

            line.Add(marker);
            line.Add(titleLabel);
            line.Add(progressLabel);
            rowRoot.Add(line);
            rowRoot.Add(track);
            _routeChecklist.Add(rowRoot);

            _missions[id] = new MissionRow(rowRoot, marker, titleLabel, progressLabel, track, fill, title);
        }

        private void TryBindSystems()
        {
            if (!_gsmBound && GameStateManager.Instance != null)
            {
                _gsm = GameStateManager.Instance;
                _gsm.onActiveRouteChanged?.AddListener(HandleActiveRouteChanged);
                _gsm.onRoute1StateChanged?.AddListener(HandleRoute1StateChanged);
                _gsm.onLocalInventoryChanged?.AddListener(HandleLocalInventoryChanged);
                _gsm.onGameStarted?.AddListener(HandleGameStarted);
                _gsm.onPhaseChanged?.AddListener(HandleLegacyPhaseChanged);
                _gsm.onGameEnd?.AddListener(HandleGameEnded);
                _gsmBound = true;

                if (_gsm.Route1State != null)
                    HandleRoute1StateChanged(_gsm.Route1State);
            }

            if (!_netBound && NetworkManager.Instance != null)
            {
                _net = NetworkManager.Instance;
                _net.OnPhaseJailStartEvent += HandleJailPhaseStarted;
                _net.OnPhaseWarningEvent += HandlePhaseWarning;
                _net.OnGameReconnectEvent += HandleGameReconnect;
                _net.OnGameEndEvent += HandleGameEndPayload;
                _net.OnGuardCatchResultEvent += HandleGuardCatchResult;
                _netBound = true;

                if (_net.CachedPhaseJailStart != null)
                    HandleJailPhaseStarted(_net.CachedPhaseJailStart);
                if (_net.CachedGameReconnect != null)
                    HandleGameReconnect(_net.CachedGameReconnect);
            }
        }

        private void TryBindLocalInventory()
        {
            if (_localInventoryBound && _localInventory != null) return;

            var root = FindLocalPlayerRoot();
            var inventory = root != null ? root.GetComponent<ItemInventory>() : null;
            if (inventory == null) return;

            _localInventory = inventory;
            _localInventory.onSlotSelected.AddListener(HandleLocalSlotSelected);
            _localInventoryBound = true;
            RefreshInventory();
        }

        private void UnbindLocalInventory()
        {
            if (_localInventoryBound && _localInventory != null)
                _localInventory.onSlotSelected.RemoveListener(HandleLocalSlotSelected);

            _localInventoryBound = false;
            _localInventory = null;
        }

        private static Transform FindLocalPlayerRoot()
        {
#if UNITY_2023_1_OR_NEWER
            var managers = UnityEngine.Object.FindObjectsByType<InteractionManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var managers = UnityEngine.Object.FindObjectsOfType<InteractionManager>();
#endif
            foreach (var manager in managers)
                if (manager != null && manager.enabled)
                    return manager.transform.root;

            return null;
        }

        private void UnbindSystems()
        {
            if (_gsmBound && _gsm != null)
            {
                _gsm.onActiveRouteChanged?.RemoveListener(HandleActiveRouteChanged);
                _gsm.onRoute1StateChanged?.RemoveListener(HandleRoute1StateChanged);
                _gsm.onLocalInventoryChanged?.RemoveListener(HandleLocalInventoryChanged);
                _gsm.onGameStarted?.RemoveListener(HandleGameStarted);
                _gsm.onPhaseChanged?.RemoveListener(HandleLegacyPhaseChanged);
                _gsm.onGameEnd?.RemoveListener(HandleGameEnded);
            }

            if (_netBound && _net != null)
            {
                _net.OnPhaseJailStartEvent -= HandleJailPhaseStarted;
                _net.OnPhaseWarningEvent -= HandlePhaseWarning;
                _net.OnGameReconnectEvent -= HandleGameReconnect;
                _net.OnGameEndEvent -= HandleGameEndPayload;
                _net.OnGuardCatchResultEvent -= HandleGuardCatchResult;
            }

            _gsmBound = false;
            _netBound = false;
            _gsm = null;
            _net = null;
        }

        private void DisableLegacyInventoryHud()
        {
            if (!disableLegacyTmpInventoryHud || _legacyHudDisabled) return;
            _legacyHudDisabled = true;

#if UNITY_2023_1_OR_NEWER
            var legacyHuds = UnityEngine.Object.FindObjectsByType<global::InventoryHUD>(FindObjectsSortMode.None);
#else
            var legacyHuds = UnityEngine.Object.FindObjectsOfType<global::InventoryHUD>(true);
#endif
            foreach (var hud in legacyHuds)
            {
                if (hud != null) hud.enabled = false;
            }
        }

        public void ShowToast(string message, float seconds = ToastDefaultSeconds)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (_toastPanel == null || _toastLabel == null) CacheElements();
            if (_toastPanel == null || _toastLabel == null) return;

            _toastLabel.text = message;
            _toastPanel.style.display = DisplayStyle.Flex;
            _toastPanel.style.opacity = 1f;
            _toastHideAtRealtime = Time.realtimeSinceStartup + Mathf.Max(0.5f, seconds);
        }

        private void RefreshToast()
        {
            if (_toastPanel == null) return;
            if (_toastHideAtRealtime <= 0f) return;
            if (Time.realtimeSinceStartup < _toastHideAtRealtime) return;

            _toastPanel.style.display = DisplayStyle.None;
            _toastHideAtRealtime = 0f;
        }

        private void RefreshAll()
        {
            RefreshVisibility();
            RefreshInventory();
            RefreshRoute1(_gsm != null ? _gsm.Route1State : null);
            RefreshPhaseLabel();
            RefreshTimer();
            RefreshGuardErrors();
        }

        private void RefreshVisibility()
        {
            var role = _gsm != null ? _gsm.LocalRole : null;
            _lastRenderedRole = role;

            var isGuard = IsGuard(role);
            var shouldShowInventory = !(isGuard && hideInventoryForGuard);
            var shouldShowRoute = ShouldShowRouteForRole(role);

            if (_inventoryPanel != null)
                _inventoryPanel.style.display = shouldShowInventory ? DisplayStyle.Flex : DisplayStyle.None;
            if (_route1Panel != null)
                _route1Panel.style.display = shouldShowRoute ? DisplayStyle.Flex : DisplayStyle.None;
            if (_guardErrorPanel != null)
                _guardErrorPanel.style.display = isGuard ? DisplayStyle.Flex : DisplayStyle.None;

            if (_roleLabel != null)
            {
                var roleText = string.IsNullOrEmpty(role) ? "--" : role.ToUpperInvariant();
                _roleLabel.text = $"ROLE: {roleText}";
            }
        }

        private bool ShouldShowRouteForRole(string role)
        {
            if (IsGuard(role)) return false;
            if (!string.IsNullOrEmpty(role) && !string.Equals(role, "prisoner", StringComparison.OrdinalIgnoreCase))
                return false;

            var activeRoute = _gsm != null ? _gsm.ActiveRouteId : null;
            return string.IsNullOrEmpty(activeRoute) || activeRoute == Route1Id;
        }

        private static bool IsGuard(string role)
        {
            return string.Equals(role, "guard", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshInventory()
        {
            if (_gsm == null)
            {
                SetHeldItem(null);
                SetSlot(_slot0, null);
                SetSlot(_slot1, null);
                return;
            }

            SetHeldItem(_gsm.LocalHeldItemId);
            var slots = _gsm.LocalInventorySlots;
            var selectedIndex = _localInventory != null ? _localInventory.SelectedIndex : 0;
            SetSlot(_slot0, slots != null && slots.Length > 0 ? slots[0] : null, selectedIndex == 0);
            SetSlot(_slot1, slots != null && slots.Length > 1 ? slots[1] : null, selectedIndex == 1);
        }

        private void SetHeldItem(string itemId)
        {
            var hasItem = !string.IsNullOrEmpty(itemId);
            if (_heldItemLabel != null)
                _heldItemLabel.text = hasItem ? DisplayNameForItem(itemId) : "Empty hand";
            SetIcon(_heldItemIcon, hasItem ? IconForItem(itemId) : null);
        }

        private void SetSlot(HudSlot slot, InventorySlotData data, bool selected = false)
        {
            if (slot == null) return;

            var itemId = data != null ? data.itemId : null;
            var hasItem = !string.IsNullOrEmpty(itemId);

            if (slot.label != null)
                slot.label.text = hasItem ? ShortNameForItem(itemId) : "Empty";
            SetIcon(slot.icon, hasItem ? IconForItem(itemId) : null);

            if (slot.root != null)
            {
                slot.root.style.opacity = hasItem ? 1f : 0.72f;
                slot.root.style.backgroundColor = selected
                    ? new Color(1f, 0.72f, 0.28f, 0.18f)
                    : new Color(1f, 1f, 1f, 0.08f);
                slot.root.style.borderTopWidth = selected ? 2 : 1;
                slot.root.style.borderRightWidth = selected ? 2 : 1;
                slot.root.style.borderBottomWidth = selected ? 2 : 1;
                slot.root.style.borderLeftWidth = selected ? 2 : 1;
                var borderColor = selected
                    ? new Color(1f, 0.82f, 0.38f, 1f)
                    : new Color(1f, 1f, 1f, 0.14f);
                slot.root.style.borderTopColor = borderColor;
                slot.root.style.borderRightColor = borderColor;
                slot.root.style.borderBottomColor = borderColor;
                slot.root.style.borderLeftColor = borderColor;
            }

            if (slot.label != null)
                slot.label.style.color = selected ? Color.white : new Color(0.77f, 0.85f, 0.92f);
        }

        private Sprite IconForItem(string itemId)
        {
            switch (itemId)
            {
                case "route1_cutters": return cuttersIcon != null ? cuttersIcon : unknownItemIcon;
                case "route1_wrench": return wrenchIcon != null ? wrenchIcon : unknownItemIcon;
                default: return unknownItemIcon;
            }
        }

        private static string DisplayNameForItem(string itemId)
        {
            switch (itemId)
            {
                case "route1_cutters": return "Cutters";
                case "route1_wrench": return "Wrench";
                default: return string.IsNullOrEmpty(itemId) ? "Empty hand" : itemId;
            }
        }

        private static string ShortNameForItem(string itemId)
        {
            switch (itemId)
            {
                case "route1_cutters": return "Cutters";
                case "route1_wrench": return "Wrench";
                default: return string.IsNullOrEmpty(itemId) ? "Empty" : itemId;
            }
        }

        private static void SetIcon(VisualElement icon, Sprite sprite)
        {
            if (icon == null) return;
            icon.style.backgroundImage = sprite != null
                ? new StyleBackground(sprite)
                : new StyleBackground();
            icon.style.opacity = sprite != null ? 1f : 0.28f;
        }

        private void RefreshRoute1(EscapeRoute1StatePayload state)
        {
            if (_missions.Count == 0) return;

            var missions = state != null ? state.missions : null;
            var transformerSuffix = state != null && state.clueFound && !string.IsNullOrEmpty(state.correctServerId)
                ? $"(Target: {ServerDisplayName(state.correctServerId)})"
                : null;

            ApplyMission("find_cutters", GetMissionStatus(missions, "find_cutters", "available"), null, 0f, false);
            ApplyMission("find_clue", GetMissionStatus(missions, "find_clue", "available"), transformerSuffix,
                ProgressForAction(state, "route1.search_clue"), true);
            ApplyMission("disable_server", GetMissionStatus(missions, "disable_server", "locked"), transformerSuffix,
                ProgressForAction(state, "route1.disable_server"), true);
            ApplyMission("find_wrench", GetMissionStatus(missions, "find_wrench", "available"), null, 0f, false);

            var ventProgress = Mathf.Max(ProgressForAction(state, "route1.open_vent"), MaxVentProgress(state));
            ApplyMission("open_vent", GetMissionStatus(missions, "open_vent", "locked"), null, ventProgress, true);
            ApplyMission("escape", GetMissionStatus(missions, "escape", "locked"), null,
                ProgressForAction(state, "route1.escape"), true);
        }

        private static string GetMissionStatus(Route1Missions missions, string missionId, string fallback)
        {
            if (missions == null) return fallback;
            switch (missionId)
            {
                case "find_cutters": return string.IsNullOrEmpty(missions.find_cutters) ? fallback : missions.find_cutters;
                case "find_clue": return string.IsNullOrEmpty(missions.find_clue) ? fallback : missions.find_clue;
                case "disable_server": return string.IsNullOrEmpty(missions.disable_server) ? fallback : missions.disable_server;
                case "find_wrench": return string.IsNullOrEmpty(missions.find_wrench) ? fallback : missions.find_wrench;
                case "open_vent": return string.IsNullOrEmpty(missions.open_vent) ? fallback : missions.open_vent;
                case "escape": return string.IsNullOrEmpty(missions.escape) ? fallback : missions.escape;
                default: return fallback;
            }
        }

        private static float ProgressForAction(EscapeRoute1StatePayload state, string action)
        {
            if (state == null || state.activeInteractions == null) return 0f;

            var best = 0f;
            foreach (var interaction in state.activeInteractions)
            {
                if (interaction == null || interaction.action != action) continue;
                best = Mathf.Max(best, Mathf.Clamp01(interaction.progress));
            }
            return best;
        }

        private static float MaxVentProgress(EscapeRoute1StatePayload state)
        {
            if (state == null) return 0f;
            if (state.openVentIds != null && state.openVentIds.Length > 0) return 1f;
            if (state.ventProgress == null) return 0f;

            var best = 0f;
            foreach (var vent in state.ventProgress)
            {
                if (vent == null) continue;
                best = Mathf.Max(best, Mathf.Clamp01(vent.progress));
            }
            return best;
        }

        private void ApplyMission(string id, string status, string suffix, float progress, bool canShowProgress)
        {
            if (!_missions.TryGetValue(id, out var row)) return;

            status = string.IsNullOrEmpty(status) ? "locked" : status;
            row.marker.text = MarkerForStatus(status);
            row.title.text = string.IsNullOrEmpty(suffix) ? row.baseTitle : $"{row.baseTitle} {suffix}";

            var color = ColorForStatus(status);
            row.marker.style.color = color;
            row.title.style.color = color;
            row.root.style.opacity = status == "locked" ? 0.55f : 1f;

            var showProgress = canShowProgress && progress > 0f && status != "complete";
            row.track.style.display = showProgress ? DisplayStyle.Flex : DisplayStyle.None;
            row.progress.text = showProgress ? $"{Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f)}%" : string.Empty;
            row.fill.style.width = new Length(Mathf.Clamp01(progress) * 100f, LengthUnit.Percent);
            row.fill.style.backgroundColor = color;
        }

        private static string MarkerForStatus(string status)
        {
            switch (status)
            {
                case "complete": return "[x]";
                case "in_progress": return "[~]";
                case "available": return "[ ]";
                default: return "[-]";
            }
        }

        private static Color ColorForStatus(string status)
        {
            switch (status)
            {
                case "complete": return new Color(0.46f, 0.92f, 0.62f);
                case "in_progress": return new Color(0.65f, 0.88f, 1f);
                case "available": return Color.white;
                default: return new Color(0.58f, 0.63f, 0.68f);
            }
        }

        private static string ServerDisplayName(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return "Transformer ?";
            const string prefix = "server_";
            return serverId.StartsWith(prefix, StringComparison.Ordinal)
                ? $"Transformer {serverId.Substring(prefix.Length).ToUpperInvariant()}"
                : serverId;
        }

        private void HandleActiveRouteChanged(string _)
        {
            RefreshVisibility();
        }

        private void HandleRoute1StateChanged(EscapeRoute1StatePayload data)
        {
            RefreshVisibility();
            RefreshRoute1(data);
        }

        private void HandleLocalInventoryChanged()
        {
            RefreshVisibility();
            RefreshInventory();
        }

        private void HandleLocalSlotSelected(int _)
        {
            RefreshInventory();
        }

        private void HandleGameStarted()
        {
            RefreshAll();
        }

        private void HandleLegacyPhaseChanged(string phaseName)
        {
            if (!string.IsNullOrEmpty(_phaseName)) return;
            _phaseName = phaseName;
            _phaseZone = null;
            _phaseNumber = 0;
            _phaseDuration = 0f;
            RefreshPhaseLabel();
        }

        private void HandleGameEnded(string winner)
        {
            _phaseName = string.IsNullOrEmpty(winner) ? "Game ended" : $"{winner} win";
            _phaseZone = null;
            _phaseNumber = 0;
            _phaseDuration = 0f;
            RefreshPhaseLabel();
            RefreshTimer();
        }

        private void HandleGuardCatchResult(GuardCatchPayload payload)
        {
            if (payload == null) return;
            if (payload.guardErrorThreshold > 0)
                _guardErrorThreshold = payload.guardErrorThreshold;
            _guardErrorCount = Mathf.Max(0, payload.guardErrorCount);
            RefreshGuardErrors();

            if (!IsGuard(_gsm != null ? _gsm.LocalRole : null)) return;
            if (!payload.success) return;

            if (!payload.isPlayer)
                ShowToast($"Wrong target! Mistakes {_guardErrorCount}/{_guardErrorThreshold}");
        }

        private void RefreshGuardErrors()
        {
            if (_guardErrorLabel == null) return;
            var threshold = _guardErrorThreshold > 0 ? _guardErrorThreshold : 3;
            _guardErrorLabel.text = $"{_guardErrorCount}/{threshold}";

            if (_guardErrorCount >= threshold)
                _guardErrorLabel.style.color = new Color(1f, 0.42f, 0.42f);
            else if (_guardErrorCount > 0)
                _guardErrorLabel.style.color = new Color(1f, 0.78f, 0.36f);
            else
                _guardErrorLabel.style.color = Color.white;
        }

        private void HandleGameEndPayload(GameEndPayload payload)
        {
            if (payload == null) return;
            _phaseName = $"{payload.winner} win";
            _phaseZone = payload.reason;
            _phaseNumber = 0;
            _phaseDuration = 0f;
            RefreshPhaseLabel();
            RefreshTimer();
        }

        private void HandleJailPhaseStarted(PhaseJailStartPayload data)
        {
            if (data == null) return;
            _phaseNumber = data.phase;
            _phaseName = data.phaseName;
            _phaseZone = data.zone;
            _phaseDuration = Mathf.Max(0f, data.duration);
            _phaseStartedAtRealtime = Time.realtimeSinceStartup;
            RefreshPhaseLabel();
            RefreshTimer();
        }

        private void HandlePhaseWarning(PhaseWarningPayload data)
        {
            if (data == null || _timeLabel == null) return;
            _timeLabel.text = $"NEXT PHASE IN {FormatSeconds(data.warningInSeconds)} ({data.nextPhaseName})";
        }

        private void HandleGameReconnect(GameReconnectPayload data)
        {
            if (data?.jailPhase != null)
            {
                _phaseNumber = data.jailPhase.phase;
                _phaseName = $"Phase {_phaseNumber}";
                _phaseZone = data.jailPhase.zone;
                _phaseDuration = 0f;
                _phaseStartedAtRealtime = 0f;
                RefreshPhaseLabel();
                RefreshTimer();
            }
        }

        private void RefreshPhaseLabel()
        {
            if (_phaseLabel == null) return;

            if (string.IsNullOrEmpty(_phaseName))
            {
                _phaseLabel.text = "PHASE: WAITING";
                return;
            }

            var prefix = _phaseNumber > 0 ? $"PHASE {_phaseNumber}" : "PHASE";
            var zone = string.IsNullOrEmpty(_phaseZone) ? string.Empty : $" | {_phaseZone}";
            _phaseLabel.text = $"{prefix}: {_phaseName}{zone}".ToUpperInvariant();
        }

        private void RefreshTimer()
        {
            if (_timeLabel == null) return;

            if (_phaseDuration <= 0f || _phaseStartedAtRealtime <= 0f)
            {
                _timeLabel.text = "--:--";
                return;
            }

            var elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - _phaseStartedAtRealtime);
            var remaining = Mathf.Max(0f, _phaseDuration - elapsed);
            _timeLabel.text = $"NEXT PHASE IN {FormatSeconds(remaining)}";
        }

        private static string FormatSeconds(float seconds)
        {
            var total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            var minutes = total / 60;
            var secs = total % 60;
            return $"{minutes:00}:{secs:00}";
        }

        private sealed class HudSlot
        {
            public readonly VisualElement root;
            public readonly VisualElement icon;
            public readonly Label label;

            public HudSlot(VisualElement root, VisualElement icon, Label label)
            {
                this.root = root;
                this.icon = icon;
                this.label = label;
            }
        }

        private sealed class MissionRow
        {
            public readonly VisualElement root;
            public readonly Label marker;
            public readonly Label title;
            public readonly Label progress;
            public readonly VisualElement track;
            public readonly VisualElement fill;
            public readonly string baseTitle;

            public MissionRow(
                VisualElement root,
                Label marker,
                Label title,
                Label progress,
                VisualElement track,
                VisualElement fill,
                string baseTitle
            )
            {
                this.root = root;
                this.marker = marker;
                this.title = title;
                this.progress = progress;
                this.track = track;
                this.fill = fill;
                this.baseTitle = baseTitle;
            }
        }
    }
}
