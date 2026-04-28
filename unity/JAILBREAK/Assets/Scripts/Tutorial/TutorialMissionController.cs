using System;
using System.Collections.Generic;
using Jailbreak.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace Jailbreak.Tutorial
{
    [RequireComponent(typeof(UIDocument))]
    public class TutorialMissionController : MonoBehaviour
    {
        public static TutorialMissionController Active { get; private set; }

        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private float fallbackDurationSeconds = 60f;
        [SerializeField] private bool prisonerStartsCollapsed;

        private readonly Dictionary<string, MissionDef> _missionDefs = new();
        private readonly Dictionary<string, MissionRow> _rows = new();
        private readonly HashSet<string> _completed = new();

        private VisualElement _root;
        private VisualElement _missionList;
        private VisualElement _missionPanel;
        private VisualElement _errorsPanel;
        private VisualElement _focusPanel;
        private VisualElement _focusFill;
        private VisualElement _toast;
        private Label _roleLabel;
        private Label _summaryLabel;
        private Label _timerLabel;
        private Label _missionTitle;
        private Label _collapseHint;
        private Label _errorsLabel;
        private Label _cueLabel;
        private Label _toastLabel;

        private NetworkManager _net;
        private bool _boundNetwork;
        private bool _collapsed;
        private string _role;
        private float _remainingSeconds;
        private float _fallbackEndsAt;
        private float _toastHideAt;
        private int _foodStep;
        private bool _changedSlotBeforeStore;

        private bool IsGuard => string.Equals(_role, "guard", StringComparison.OrdinalIgnoreCase);
        private bool IsPrisoner => !IsGuard;

        private void OnEnable()
        {
            Active = this;
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            CacheElements();
            BindEvents();
            BindNetwork();

            _collapsed = prisonerStartsCollapsed;
            _remainingSeconds = fallbackDurationSeconds;
            _fallbackEndsAt = Time.realtimeSinceStartup + fallbackDurationSeconds;

            if (_net != null && _net.CachedTutorialStart != null)
                ApplyStart(_net.CachedTutorialStart);
            else
                ApplyRole(string.IsNullOrEmpty(_role) ? "prisoner" : _role, null);

            if (_net != null && _net.CachedTutorialState != null)
                ApplyState(_net.CachedTutorialState);
        }

        private void OnDisable()
        {
            UnbindNetwork();
            UnbindEvents();
            if (Active == this) Active = null;
        }

        private void Update()
        {
            BindNetwork();
            HandleTab();
            RefreshTimer();
            RefreshToast();
        }

        public void ApplyStart(TutorialStartPayload payload)
        {
            if (payload == null) return;
            _remainingSeconds = payload.duration > 0f ? payload.duration : fallbackDurationSeconds;
            _fallbackEndsAt = Time.realtimeSinceStartup + _remainingSeconds;
            ApplyRole(payload.role, payload.missions);
        }

        public void ApplyRole(string role, string[] missionIds)
        {
            _role = string.IsNullOrEmpty(role) ? "prisoner" : role;
            BuildMissionDefs(_role, missionIds);
            BuildRows();
            RefreshStaticText();
            RefreshMissions();
            RefreshGuardErrors(0, 3);
        }

        public void ApplyState(TutorialStatePayload payload)
        {
            if (payload == null) return;
            _remainingSeconds = Mathf.Max(0f, payload.remainingSeconds);
            _fallbackEndsAt = Time.realtimeSinceStartup + _remainingSeconds;

            if (payload.completedMissionIds != null)
            {
                foreach (var id in payload.completedMissionIds)
                    if (!string.IsNullOrEmpty(id)) _completed.Add(id);
            }

            RefreshMissions();
            RefreshTimer();
        }

        private void CacheElements()
        {
            _root = uiDocument != null ? uiDocument.rootVisualElement : null;
            if (_root == null) return;

            _roleLabel = _root.Q<Label>("HeaderRoleLabel") ?? _root.Q<Label>("RoleLabel");
            _summaryLabel = _root.Q<Label>("RoleSummaryLabel");
            _timerLabel = _root.Q<Label>("TimerLabel") ?? _root.Q<Label>("Timer");
            _missionTitle = _root.Q<Label>("MissionTitle");
            _collapseHint = _root.Q<Label>("CollapseHint");
            _errorsLabel = _root.Q<Label>("ErrorsLabel");
            _cueLabel = _root.Q<Label>("CueLabel");
            _toastLabel = _root.Q<Label>("ToastLabel");
            _missionList = _root.Q<VisualElement>("MissionList");
            _missionPanel = _root.Q<VisualElement>("MissionPanel");
            _errorsPanel = _root.Q<VisualElement>("ErrorsPanel");
            _focusPanel = _root.Q<VisualElement>("FocusPanel");
            _focusFill = _root.Q<VisualElement>("FocusFill");
            _toast = _root.Q<VisualElement>("Toast");
        }

        private void BindNetwork()
        {
            if (_boundNetwork) return;
            _net = NetworkManager.Instance;
            if (_net == null) return;

            _net.OnTutorialStartEvent += ApplyStart;
            _net.OnTutorialStateEvent += ApplyState;
            _net.OnTutorialEndEvent += HandleTutorialEnd;
            _boundNetwork = true;
        }

        private void UnbindNetwork()
        {
            if (!_boundNetwork || _net == null) return;
            _net.OnTutorialStartEvent -= ApplyStart;
            _net.OnTutorialStateEvent -= ApplyState;
            _net.OnTutorialEndEvent -= HandleTutorialEnd;
            _boundNetwork = false;
            _net = null;
        }

        private void BindEvents()
        {
            TutorialMissionEvents.OnSignal += HandleSignal;
            TutorialMissionEvents.OnGuardFocusProgress += HandleGuardFocusProgress;
            TutorialMissionEvents.OnGuardErrorsChanged += RefreshGuardErrors;
            TutorialMissionEvents.OnWorldCue += HandleWorldCue;
            TutorialMissionEvents.OnToast += ShowToast;
        }

        private void UnbindEvents()
        {
            TutorialMissionEvents.OnSignal -= HandleSignal;
            TutorialMissionEvents.OnGuardFocusProgress -= HandleGuardFocusProgress;
            TutorialMissionEvents.OnGuardErrorsChanged -= RefreshGuardErrors;
            TutorialMissionEvents.OnWorldCue -= HandleWorldCue;
            TutorialMissionEvents.OnToast -= ShowToast;
        }

        private void BuildMissionDefs(string role, string[] missionIds)
        {
            _missionDefs.Clear();

            if (string.Equals(role, "guard", StringComparison.OrdinalIgnoreCase))
            {
                Add("g2_anomaly", "Find an anomaly", "Move within 4m of someone sprinting, hiding, or acting suspicious.");
                Add("g3_capture", "Hold focus", "Stand within 2m and hold Left Click for 0.5s on a valid suspect.");
                Add("g4_error", "Feel the cost", "Accuse an innocent NPC once and watch the practice mistake counter.");
                Add("g5_world_cue", "Follow world cues", "Enter the office or technical room after a sound or warning cue.");
            }
            else
            {
                Add("p1_review_route", "Review the plan", "Press TAB once to expand or hide your training checklist.");
                Add("p2_food_flow", "Blend in", "Take food, sit at the table, then leave the tray at the sink.");
                Add("p3_sprint", "Sprint with intent", "Hold Shift while moving for at least 1 second.");
                Add("p4_store_item", "Pick up contraband", "Walk up to the tool and press E to grab it.");
                Add("p5_drop_item", "Drop from a slot", "Select the stored tool and press G to drop it under control.");
                Add("p7_hide_cart", "Break line of sight", "Hide in a laundry cart for a few seconds.");
            }

            if (missionIds == null || missionIds.Length == 0) return;

            var allowed = new HashSet<string>(missionIds);
            var toRemove = new List<string>();
            foreach (var id in _missionDefs.Keys)
                if (!allowed.Contains(id)) toRemove.Add(id);
            foreach (var id in toRemove) _missionDefs.Remove(id);
        }

        private void Add(string id, string title, string detail)
        {
            _missionDefs[id] = new MissionDef(id, title, detail);
        }

        private void BuildRows()
        {
            _rows.Clear();
            if (_missionList == null) return;

            _missionList.Clear();
            foreach (var def in _missionDefs.Values)
            {
                var row = CreateMissionRow(def);
                _missionList.Add(row.root);
                _rows[def.id] = row;
            }
        }

        private MissionRow CreateMissionRow(MissionDef def)
        {
            var root = new VisualElement { name = $"Mission_{def.id}" };
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.FlexStart;
            root.style.marginBottom = 7;
            root.style.paddingTop = 7;
            root.style.paddingRight = 8;
            root.style.paddingBottom = 7;
            root.style.paddingLeft = 8;
            root.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            root.style.borderTopWidth = 1;
            root.style.borderRightWidth = 1;
            root.style.borderBottomWidth = 1;
            root.style.borderLeftWidth = 1;
            root.style.borderTopColor = new Color(1f, 1f, 1f, 0.12f);
            root.style.borderRightColor = new Color(1f, 1f, 1f, 0.12f);
            root.style.borderBottomColor = new Color(1f, 1f, 1f, 0.12f);
            root.style.borderLeftColor = new Color(1f, 1f, 1f, 0.12f);

            var marker = new Label("[ ]");
            marker.style.width = 32;
            marker.style.fontSize = 12;
            marker.style.unityFontStyleAndWeight = FontStyle.Bold;

            var textBox = new VisualElement();
            textBox.style.flexGrow = 1;
            textBox.style.flexShrink = 1;

            var title = new Label(def.title);
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.whiteSpace = WhiteSpace.Normal;

            var detail = new Label(def.detail);
            detail.style.fontSize = 10;
            detail.style.whiteSpace = WhiteSpace.Normal;
            detail.style.marginTop = 2;

            textBox.Add(title);
            textBox.Add(detail);
            root.Add(marker);
            root.Add(textBox);
            return new MissionRow(root, marker, title, detail);
        }

        private void RefreshStaticText()
        {
            if (_roleLabel != null)
                _roleLabel.text = IsGuard ? "TRAINING - GUARD" : "TRAINING - PRISONER";
            if (_summaryLabel != null)
                _summaryLabel.text = IsGuard
                    ? "Observe patterns, close distance, and hold focus only when you are sure."
                    : "Blend into routine, hide evidence, take risks only when the timing is right.";
            if (_missionTitle != null)
                _missionTitle.text = IsGuard ? "Guard Missions" : "Prisoner Missions";

            if (_errorsPanel != null)
                _errorsPanel.style.display = IsGuard ? DisplayStyle.Flex : DisplayStyle.None;
            if (_focusPanel != null)
                _focusPanel.style.display = IsGuard ? DisplayStyle.Flex : DisplayStyle.None;
            if (_collapseHint != null)
                _collapseHint.style.display = IsPrisoner ? DisplayStyle.Flex : DisplayStyle.None;

            ApplyCollapsedState();
        }

        private void RefreshMissions()
        {
            var firstOpenFound = false;
            foreach (var def in _missionDefs.Values)
            {
                if (!_rows.TryGetValue(def.id, out var row)) continue;

                var complete = _completed.Contains(def.id);
                var active = complete || !firstOpenFound;
                if (!complete && !firstOpenFound) firstOpenFound = true;

                row.marker.text = complete ? "[x]" : active ? "[ ]" : "[-]";
                var color = complete
                    ? new Color(0.42f, 0.95f, 0.62f)
                    : active
                        ? Color.white
                        : new Color(0.55f, 0.62f, 0.68f);
                row.marker.style.color = color;
                row.title.style.color = color;
                row.detail.style.color = complete
                    ? new Color(0.72f, 0.95f, 0.78f)
                    : active
                        ? new Color(0.76f, 0.86f, 0.92f)
                        : new Color(0.50f, 0.56f, 0.62f);
                row.root.style.opacity = active ? 1f : 0.58f;
                row.root.style.backgroundColor = complete
                    ? new Color(0.20f, 0.55f, 0.34f, 0.24f)
                    : active
                        ? new Color(1f, 1f, 1f, 0.08f)
                        : new Color(1f, 1f, 1f, 0.035f);
            }
        }

        private void HandleSignal(string signal)
        {
            switch (signal)
            {
                case TutorialMissionEvents.FoodPicked:
                    _foodStep = Mathf.Max(_foodStep, 1);
                    break;
                case TutorialMissionEvents.Seated:
                    if (_foodStep >= 1) _foodStep = Mathf.Max(_foodStep, 2);
                    break;
                case TutorialMissionEvents.TrayDeposited:
                    if (_foodStep >= 2) Complete("p2_food_flow");
                    break;
                case TutorialMissionEvents.Sprinted:
                    Complete("p3_sprint");
                    break;
                case TutorialMissionEvents.ToolSearched:
                    ShowToast("Tool cache searched. Walk up and press E to grab it.");
                    break;
                case TutorialMissionEvents.ItemPicked:
                    Complete("p4_store_item");
                    break;
                case TutorialMissionEvents.SlotChanged:
                    _changedSlotBeforeStore = true;
                    break;
                case TutorialMissionEvents.ItemStored:
                    break;
                case TutorialMissionEvents.ItemDropped:
                    Complete("p5_drop_item");
                    break;
                case TutorialMissionEvents.DeskSearched:
                case TutorialMissionEvents.PowerDisabled:
                    Complete("p6_risky_action");
                    break;
                case TutorialMissionEvents.HiddenInCart:
                    Complete("p7_hide_cart");
                    break;
                case TutorialMissionEvents.GuardObservedRoutine:
                    Complete("g1_observe");
                    break;
                case TutorialMissionEvents.GuardNearAnomaly:
                    Complete("g2_anomaly");
                    break;
                case TutorialMissionEvents.GuardCaptureComplete:
                    Complete("g3_capture");
                    break;
                case TutorialMissionEvents.GuardMistake:
                    Complete("g4_error");
                    break;
                case TutorialMissionEvents.GuardWorldCueVisited:
                    Complete("g5_world_cue");
                    break;
            }
        }

        private void Complete(string missionId)
        {
            if (string.IsNullOrEmpty(missionId)) return;
            if (!_missionDefs.ContainsKey(missionId)) return;
            if (!_completed.Add(missionId)) return;

            _net?.SendTutorialMissionComplete(missionId);
            RefreshMissions();
            ShowToast("Training step complete");
        }

        private void HandleTab()
        {
            if (!InputSystemKey.WasPressedThisFrame(KeyCode.Tab)) return;
            if (IsPrisoner)
            {
                _collapsed = !_collapsed;
                TutorialMissionEvents.Emit(TutorialMissionEvents.ReviewRoute);
                Complete("p1_review_route");
                ApplyCollapsedState();
            }
        }

        private void ApplyCollapsedState()
        {
            if (_missionList != null)
                _missionList.style.display = IsPrisoner && _collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (_collapseHint != null)
                _collapseHint.text = _collapsed ? "[TAB] SHOW" : "[TAB] HIDE";
        }

        private void RefreshTimer()
        {
            if (_timerLabel == null) return;

            var clockRemaining = _fallbackEndsAt > 0f
                ? Mathf.Max(0f, _fallbackEndsAt - Time.realtimeSinceStartup)
                : _remainingSeconds;
            var displaySeconds = Mathf.Min(Mathf.Max(0f, _remainingSeconds), clockRemaining);
            var total = Mathf.CeilToInt(displaySeconds);
            _timerLabel.text = $"GAME STARTS IN {total / 60:00}:{total % 60:00}";
        }

        private void HandleGuardFocusProgress(float progress)
        {
            if (_focusFill != null)
                _focusFill.style.width = new Length(Mathf.Clamp01(progress) * 100f, LengthUnit.Percent);
        }

        private void RefreshGuardErrors(int count, int threshold)
        {
            if (_errorsLabel != null)
            {
                _errorsLabel.text = $"{count}/{threshold}";
                _errorsLabel.style.color = count <= 0
                    ? Color.white
                    : count >= threshold
                        ? new Color(1f, 0.36f, 0.36f)
                        : new Color(1f, 0.78f, 0.34f);
            }
        }

        private void HandleWorldCue(Vector3 _, string label)
        {
            if (_cueLabel != null)
                _cueLabel.text = string.IsNullOrEmpty(label) ? "Cue: investigate the noise" : $"Cue: {label}";
        }

        private void ShowToast(string message, float seconds = 2.4f)
        {
            if (_toast == null || _toastLabel == null) return;
            _toastLabel.text = message;
            _toast.style.display = DisplayStyle.Flex;
            _toastHideAt = Time.realtimeSinceStartup + Mathf.Max(0.5f, seconds);
        }

        private void RefreshToast()
        {
            if (_toast == null || _toastHideAt <= 0f) return;
            if (Time.realtimeSinceStartup < _toastHideAt) return;
            _toast.style.display = DisplayStyle.None;
            _toastHideAt = 0f;
        }

        private void HandleTutorialEnd(TutorialEndPayload _)
        {
            _remainingSeconds = 0f;
            RefreshTimer();
        }

        private readonly struct MissionDef
        {
            public readonly string id;
            public readonly string title;
            public readonly string detail;

            public MissionDef(string id, string title, string detail)
            {
                this.id = id;
                this.title = title;
                this.detail = detail;
            }
        }

        private sealed class MissionRow
        {
            public readonly VisualElement root;
            public readonly Label marker;
            public readonly Label title;
            public readonly Label detail;

            public MissionRow(VisualElement root, Label marker, Label title, Label detail)
            {
                this.root = root;
                this.marker = marker;
                this.title = title;
                this.detail = detail;
            }
        }
    }
}
