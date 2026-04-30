using UnityEngine;
using Jailbreak.UI;

namespace Jailbreak.Player
{
    /// <summary>
    /// Draws a minimal, centered crosshair via OnGUI for aiming feedback.
    /// Consistent with the existing GuardCaptureSystem OnGUI pattern.
    ///
    /// Auto-hides when the cursor is unlocked (pause menu, emote panel,
    /// end-game overlay) by consulting <see cref="CursorLockManager"/>.
    /// </summary>
    public class CrosshairHUD : MonoBehaviour
    {
        // ──────────────────────────── Style ──────────────────────────────
        [Header("Crosshair Style")]
        [Tooltip("Length of each crosshair arm in pixels.")]
        public float lineLength = 8f;

        [Tooltip("Thickness of each arm in pixels.")]
        public float lineThickness = 2f;

        [Tooltip("Gap between the center and the start of each arm.")]
        public float centerGap = 3f;

        [Tooltip("Crosshair color (alpha controls opacity).")]
        public Color color = new Color(1f, 1f, 1f, 0.85f);

        [Header("Center Dot")]
        [Tooltip("Show a small dot at the exact center.")]
        public bool showCenterDot = true;

        [Tooltip("Radius of the center dot in pixels.")]
        public float dotRadius = 1.5f;

        [Header("Shadow / Outline")]
        [Tooltip("Draw a 1px dark shadow behind the crosshair for contrast on bright surfaces.")]
        public bool drawShadow = true;

        // ──────────────────────────── Private ────────────────────────────
        private Texture2D _pixel;
        private PlayerInputController _input;

        private void Awake()
        {
            _input = GetComponent<PlayerInputController>();

            // Procedural 1×1 white pixel — used for all rect drawing.
            _pixel = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        private void OnDestroy()
        {
            if (_pixel != null)
                Destroy(_pixel);
        }

        // ──────────────────────────── Render ─────────────────────────────
        private void OnGUI()
        {
            // Only render for the local player once input has been enabled.
            if (_input != null && !_input.InputEnabled) return;

            // Hide whenever the cursor is free (pause, emote panel, end-game).
            if (!CursorLockManager.ShouldBeLocked) return;

            float cx = Screen.width  * 0.5f;
            float cy = Screen.height * 0.5f;

            // Optional dark shadow pass for contrast on bright environments.
            if (drawShadow)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                DrawCrosshair(cx + 1f, cy + 1f);
            }

            // Foreground pass.
            GUI.color = color;
            DrawCrosshair(cx, cy);

            // Reset GUI color.
            GUI.color = Color.white;
        }

        private void DrawCrosshair(float cx, float cy)
        {
            float halfThick = lineThickness * 0.5f;

            // ─ Left arm
            DrawRect(cx - centerGap - lineLength, cy - halfThick, lineLength, lineThickness);
            // ─ Right arm
            DrawRect(cx + centerGap, cy - halfThick, lineLength, lineThickness);
            // │ Top arm
            DrawRect(cx - halfThick, cy - centerGap - lineLength, lineThickness, lineLength);
            // │ Bottom arm
            DrawRect(cx - halfThick, cy + centerGap, lineThickness, lineLength);

            // Center dot
            if (showCenterDot)
            {
                float d = dotRadius * 2f;
                DrawRect(cx - dotRadius, cy - dotRadius, d, d);
            }
        }

        private void DrawRect(float x, float y, float w, float h)
        {
            GUI.DrawTexture(new Rect(x, y, w, h), _pixel);
        }
    }
}
