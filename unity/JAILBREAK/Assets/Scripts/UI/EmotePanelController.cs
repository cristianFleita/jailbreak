using Jailbreak.Player;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Jailbreak.UI
{
    /// <summary>
    /// UI Toolkit controller for the radial emote panel.
    /// Creates the emote wheel dynamically from <see cref="EmoteSystem.Catalogue"/>
    /// and binds each button to the local player's <see cref="EmoteSystem"/>.
    ///
    /// Lifecycle:
    ///   - Hold B → panel shows + cursor unlocks
    ///   - Click emote → emote plays, panel closes, cursor locks
    ///   - Release B without clicking → panel closes, cursor locks
    ///   - While emote plays, a small cancel hint appears
    ///
    /// The panel is built procedurally in code so there's zero UXML/USS dependency
    /// beyond the existing GameGUI root container.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class EmotePanelController : MonoBehaviour
    {
        private UIDocument _uiDoc;
        private VisualElement _root;
        private VisualElement _emoteContainer;
        private VisualElement _emoteWheel;
        private Label _emoteHint;
        private VisualElement _cancelHint;

        private EmoteSystem _emoteSystem;
        private bool _panelBuilt;
        private bool _bHeld;

        private void OnEnable()
        {
            _uiDoc = GetComponent<UIDocument>();
            if (_uiDoc == null || _uiDoc.rootVisualElement == null) return;
            _root = _uiDoc.rootVisualElement;
        }

        private void Update()
        {
            TryBindEmoteSystem();
            HandleInput();
            UpdateCancelHint();
        }

        // ── Lazy bind to local player's EmoteSystem ─────────────────────────
        private void TryBindEmoteSystem()
        {
            if (_emoteSystem != null) return;

            // Find the local player's EmoteSystem
#if UNITY_2023_1_OR_NEWER
            var systems = Object.FindObjectsByType<EmoteSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var systems = Object.FindObjectsOfType<EmoteSystem>();
#endif
            foreach (var sys in systems)
            {
                // EmoteSystem lives on the local player (has PlayerInputController)
                if (sys.GetComponent<PlayerInputController>() != null)
                {
                    _emoteSystem = sys;
                    _emoteSystem.OnPanelToggled += HandlePanelToggled;
                    _emoteSystem.OnEmoteChanged += HandleEmoteChanged;
                    break;
                }
            }
        }

        // ── Input: B key to open/close ──────────────────────────────────────
        private void HandleInput()
        {
            if (_emoteSystem == null) return;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;

            bool bDown = kb.bKey.isPressed;

            if (bDown && !_bHeld)
            {
                _bHeld = true;
                if (!_emoteSystem.IsEmoting)
                {
                    EnsurePanelBuilt();
                    _emoteSystem.OpenPanel();
                }
            }
            else if (!bDown && _bHeld)
            {
                _bHeld = false;
                if (_emoteSystem.IsPanelOpen)
                    _emoteSystem.ClosePanel();
            }
#endif
        }

        // ── Panel visibility ────────────────────────────────────────────────
        private void HandlePanelToggled(bool isOpen)
        {
            if (_emoteContainer != null)
                _emoteContainer.style.display = isOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HandleEmoteChanged(string emoteId)
        {
            UpdateCancelHint();
        }

        private void UpdateCancelHint()
        {
            if (_cancelHint == null) return;

            bool show = _emoteSystem != null && _emoteSystem.IsEmoting;
            _cancelHint.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Build the emote panel (procedural UI Toolkit) ───────────────────
        private void EnsurePanelBuilt()
        {
            if (_panelBuilt || _root == null) return;
            _panelBuilt = true;

            // Main container — centered overlay
            _emoteContainer = new VisualElement { name = "EmotePanel" };
            _emoteContainer.pickingMode = PickingMode.Ignore;
            _emoteContainer.style.position = Position.Absolute;
            _emoteContainer.style.left = 0;
            _emoteContainer.style.top = 0;
            _emoteContainer.style.right = 0;
            _emoteContainer.style.bottom = 0;
            _emoteContainer.style.alignItems = Align.Center;
            _emoteContainer.style.justifyContent = Justify.Center;
            _emoteContainer.style.display = DisplayStyle.None;

            // Semi-transparent backdrop
            var backdrop = new VisualElement { name = "EmoteBackdrop" };
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = 0;
            backdrop.style.top = 0;
            backdrop.style.right = 0;
            backdrop.style.bottom = 0;
            backdrop.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            backdrop.pickingMode = PickingMode.Ignore;
            _emoteContainer.Add(backdrop);

            // Emote wheel card
            _emoteWheel = new VisualElement { name = "EmoteWheel" };
            _emoteWheel.style.width = 380;
            _emoteWheel.style.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 0.94f);
            _emoteWheel.style.borderTopLeftRadius = 12;
            _emoteWheel.style.borderTopRightRadius = 12;
            _emoteWheel.style.borderBottomLeftRadius = 12;
            _emoteWheel.style.borderBottomRightRadius = 12;
            _emoteWheel.style.paddingTop = 14;
            _emoteWheel.style.paddingBottom = 14;
            _emoteWheel.style.paddingLeft = 14;
            _emoteWheel.style.paddingRight = 14;
            _emoteWheel.style.borderTopWidth = 1;
            _emoteWheel.style.borderRightWidth = 1;
            _emoteWheel.style.borderBottomWidth = 1;
            _emoteWheel.style.borderLeftWidth = 1;
            _emoteWheel.style.borderTopColor = new Color(1f, 0.8f, 0.36f, 0.4f);
            _emoteWheel.style.borderRightColor = new Color(1f, 0.8f, 0.36f, 0.4f);
            _emoteWheel.style.borderBottomColor = new Color(1f, 0.8f, 0.36f, 0.4f);
            _emoteWheel.style.borderLeftColor = new Color(1f, 0.8f, 0.36f, 0.4f);
            _emoteContainer.Add(_emoteWheel);

            // Title
            var title = new Label("EMOTES");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14;
            title.style.color = new Color(1f, 0.8f, 0.36f);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.letterSpacing = 2;
            title.style.marginBottom = 8;
            _emoteWheel.Add(title);

            // Subtitle
            var subtitle = new Label("Click to perform an animation — blend in with NPCs");
            subtitle.style.fontSize = 10;
            subtitle.style.color = new Color(0.76f, 0.84f, 0.92f);
            subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            subtitle.style.marginBottom = 12;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            _emoteWheel.Add(subtitle);

            // ── Section: Social ──────────────────────────────────────────
            AddSectionHeader("SOCIAL");
            var socialGrid = CreateGrid();
            AddEmoteButton(socialGrid, "talking");
            AddEmoteButton(socialGrid, "telling_secret");
            AddEmoteButton(socialGrid, "salute");
            AddEmoteButton(socialGrid, "dismissing");
            AddEmoteButton(socialGrid, "surprised");
            AddEmoteButton(socialGrid, "angry");
            _emoteWheel.Add(socialGrid);

            // ── Section: Exercise ────────────────────────────────────────
            AddSectionHeader("EXERCISE");
            var exerciseGrid = CreateGrid();
            AddEmoteButton(exerciseGrid, "pushup");
            AddEmoteButton(exerciseGrid, "situps");
            AddEmoteButton(exerciseGrid, "crunch");
            AddEmoteButton(exerciseGrid, "dance");
            _emoteWheel.Add(exerciseGrid);

            // ── Key hint ─────────────────────────────────────────────────
            _emoteHint = new Label("[B] Hold to keep open · [WASD] Cancel emote");
            _emoteHint.style.fontSize = 9;
            _emoteHint.style.color = new Color(0.55f, 0.6f, 0.66f);
            _emoteHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            _emoteHint.style.marginTop = 10;
            _emoteWheel.Add(_emoteHint);

            // ── Cancel hint (shown during emote) ─────────────────────────
            _cancelHint = new VisualElement { name = "EmoteCancelHint" };
            _cancelHint.style.position = Position.Absolute;
            _cancelHint.style.bottom = 120;
            _cancelHint.style.left = new Length(50f, LengthUnit.Percent);
            _cancelHint.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0);
            _cancelHint.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            _cancelHint.style.paddingTop = 6;
            _cancelHint.style.paddingBottom = 6;
            _cancelHint.style.paddingLeft = 14;
            _cancelHint.style.paddingRight = 14;
            _cancelHint.style.borderTopLeftRadius = 4;
            _cancelHint.style.borderTopRightRadius = 4;
            _cancelHint.style.borderBottomLeftRadius = 4;
            _cancelHint.style.borderBottomRightRadius = 4;
            _cancelHint.style.display = DisplayStyle.None;

            var cancelLabel = new Label("Press WASD to cancel emote");
            cancelLabel.style.fontSize = 11;
            cancelLabel.style.color = new Color(1f, 0.8f, 0.36f);
            cancelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _cancelHint.Add(cancelLabel);
            _emoteContainer.Add(_cancelHint);

            // Add to root
            _root.Add(_emoteContainer);
        }

        private void AddSectionHeader(string text)
        {
            var header = new Label(text);
            header.style.fontSize = 9;
            header.style.color = new Color(0.55f, 0.6f, 0.66f);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.letterSpacing = 1;
            header.style.marginTop = 6;
            header.style.marginBottom = 4;
            header.style.marginLeft = 2;
            _emoteWheel.Add(header);
        }

        private VisualElement CreateGrid()
        {
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            return grid;
        }

        private void AddEmoteButton(VisualElement parent, string emoteId)
        {
            EmoteSystem.EmoteEntry entry = null;
            foreach (var e in EmoteSystem.Catalogue)
                if (e.id == emoteId) { entry = e; break; }
            if (entry == null) return;

            var btn = new Button(() => OnEmoteClicked(emoteId));
            btn.name = $"EmoteBtn_{emoteId}";
            btn.style.width = 82;
            btn.style.height = 58;
            btn.style.marginRight = 6;
            btn.style.marginBottom = 6;
            btn.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            btn.style.borderTopWidth = 1;
            btn.style.borderRightWidth = 1;
            btn.style.borderBottomWidth = 1;
            btn.style.borderLeftWidth = 1;
            btn.style.borderTopColor = new Color(1f, 1f, 1f, 0.1f);
            btn.style.borderRightColor = new Color(1f, 1f, 1f, 0.1f);
            btn.style.borderBottomColor = new Color(1f, 1f, 1f, 0.1f);
            btn.style.borderLeftColor = new Color(1f, 1f, 1f, 0.1f);
            btn.style.borderTopLeftRadius = 6;
            btn.style.borderTopRightRadius = 6;
            btn.style.borderBottomLeftRadius = 6;
            btn.style.borderBottomRightRadius = 6;
            btn.style.flexDirection = FlexDirection.Column;
            btn.style.alignItems = Align.Center;
            btn.style.justifyContent = Justify.Center;
            btn.style.paddingTop = 4;
            btn.style.paddingBottom = 4;

            var icon = new Label(entry.icon);
            icon.style.fontSize = 20;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.pickingMode = PickingMode.Ignore;
            btn.Add(icon);

            var label = new Label(entry.displayName);
            label.style.fontSize = 9;
            label.style.color = Color.white;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.marginTop = 2;
            label.pickingMode = PickingMode.Ignore;
            btn.Add(label);

            // Hover effect via callbacks
            btn.RegisterCallback<MouseEnterEvent>(evt =>
            {
                btn.style.backgroundColor = new Color(1f, 0.8f, 0.36f, 0.18f);
                btn.style.borderTopColor = new Color(1f, 0.8f, 0.36f, 0.5f);
                btn.style.borderRightColor = new Color(1f, 0.8f, 0.36f, 0.5f);
                btn.style.borderBottomColor = new Color(1f, 0.8f, 0.36f, 0.5f);
                btn.style.borderLeftColor = new Color(1f, 0.8f, 0.36f, 0.5f);
            });
            btn.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                btn.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
                btn.style.borderTopColor = new Color(1f, 1f, 1f, 0.1f);
                btn.style.borderRightColor = new Color(1f, 1f, 1f, 0.1f);
                btn.style.borderBottomColor = new Color(1f, 1f, 1f, 0.1f);
                btn.style.borderLeftColor = new Color(1f, 1f, 1f, 0.1f);
            });

            parent.Add(btn);
        }

        private void OnEmoteClicked(string emoteId)
        {
            if (_emoteSystem == null) return;
            _emoteSystem.PlayEmote(emoteId);
        }

        private void OnDisable()
        {
            if (_emoteSystem != null)
            {
                _emoteSystem.OnPanelToggled -= HandlePanelToggled;
                _emoteSystem.OnEmoteChanged -= HandleEmoteChanged;
            }
        }
    }
}
