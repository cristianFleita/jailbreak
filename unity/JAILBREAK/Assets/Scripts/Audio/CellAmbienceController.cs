using UnityEngine;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Owns the three cell-ambience layers and crossfades between them.
    ///   • Normal       — default jail ambience loop.
    ///   • LightsOff    — ambience while power is out.
    ///   • Riot         — ambience after the riot has been triggered.
    ///
    /// Wire as a single GameObject in GameScene with three child AudioSources
    /// (or three <see cref="LoopingSfx"/> components). The active layer fades
    /// to its full volume; the others fade to zero.
    ///
    /// Inspector wiring (no code needed):
    ///   PowerOutage.onPowerOutage     → CellAmbienceController.SetLightsOff(true)
    ///   PowerOutage.onPowerRestored   → CellAmbienceController.SetLightsOff(false)
    /// Riot is driven from <see cref="GameAudioBridge"/> via SetRiot(true/false).
    /// </summary>
    public class CellAmbienceController : MonoBehaviour
    {
        [Header("Layers")]
        public LoopingSfx normal;
        public LoopingSfx lightsOff;
        public LoopingSfx riot;

        [Header("Behavior")]
        [Tooltip("Crossfade time between layers in seconds.")]
        public float crossfadeSeconds = 0.6f;

        [Tooltip("Start the normal ambience automatically when this scene loads.")]
        public bool autoStartNormal = true;

        public bool LightsOff { get; private set; }
        public bool RiotActive { get; private set; }

        private void Start()
        {
            // Make sure all three are loaded but silent before we pick the active layer.
            PrimeSilent(normal);
            PrimeSilent(lightsOff);
            PrimeSilent(riot);

            if (autoStartNormal) Refresh();
        }

        // ── Public API (wire from inspector) ─────────────────────────────────

        public void SetLightsOff(bool value)
        {
            if (LightsOff == value) return;
            LightsOff = value;
            Refresh();
        }

        public void SetRiot(bool value)
        {
            if (RiotActive == value) return;
            RiotActive = value;
            Refresh();
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private static void PrimeSilent(LoopingSfx layer)
        {
            if (layer == null) return;
            layer.fadeInSeconds = Mathf.Max(layer.fadeInSeconds, 0.01f);
            layer.Stop();
        }

        private void Refresh()
        {
            // Priority: riot > lightsOff > normal.
            // Only one layer plays at full volume at a time.
            LoopingSfx active =
                RiotActive ? riot :
                LightsOff  ? lightsOff :
                              normal;

            CrossfadeOnly(active);
        }

        private void CrossfadeOnly(LoopingSfx target)
        {
            FadeLayer(normal,    target == normal);
            FadeLayer(lightsOff, target == lightsOff);
            FadeLayer(riot,      target == riot);
        }

        private void FadeLayer(LoopingSfx layer, bool shouldPlay)
        {
            if (layer == null) return;

            // Configure crossfade duration on the layer itself (LoopingSfx exposes both).
            layer.fadeInSeconds = crossfadeSeconds;
            layer.fadeOutSeconds = crossfadeSeconds;

            if (shouldPlay) layer.Play();
            else layer.Stop();
        }
    }
}
