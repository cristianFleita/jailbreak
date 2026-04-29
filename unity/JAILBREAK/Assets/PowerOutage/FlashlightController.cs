using Jailbreak.Network;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class FlashlightController : MonoBehaviour
{
    [Header("Flashlight Components")]
    [Tooltip("The physical 3D model of the flashlight in the character's hand.")]
    public GameObject flashlightModel;

    [Tooltip("The Light component that projects the beam.")]
    public Light flashlightLight;

    [Tooltip("The Rig component from the Animation Rigging package that controls the arm.")]
    public Rig armRig;

    [Header("Power Outage Integration")]
    [Tooltip("Optional explicit PowerOutage reference. Leave empty to auto-discover the scene's instance at runtime.")]
    public PowerOutage powerOutage;

    [Header("Toggle Settings")]
    [Tooltip("Key used by the local player to toggle the flashlight on/off during a power outage.")]
    public KeyCode toggleKey = KeyCode.F;

    [Tooltip("If true, this controller listens for local input. Set false for remote/NPC characters.")]
    public bool isLocalPlayer = false;

    /// <summary>True when the flashlight beam is active (model may still show — arm raised).</summary>
    public bool IsFlashlightOn { get; private set; }

    private PowerOutage subscribedSource;

    /// <summary>
    /// Tracks whether the player has manually turned the light off.
    /// Reset when a new outage starts or power is restored.
    /// </summary>
    private bool isManuallyOff;

    private void OnEnable()
    {
        SubscribeIfNeeded();
        SyncInitialState();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        // Late-bind: prefab-instanced characters can spawn before the scene's PowerOutage exists,
        // and PowerOutage is a scene object so it can't be wired through the prefab inspector.
        if (subscribedSource == null)
        {
            SubscribeIfNeeded();
            if (subscribedSource != null) SyncInitialState();
        }

        // ── Local player toggle input ────────────────────────────────────────
        if (isLocalPlayer && subscribedSource != null && subscribedSource.IsBlackedOut)
        {
            if (InputSystemKey.WasPressedThisFrame(toggleKey))
            {
                ToggleFlashlight();
            }
        }
    }

    // ─── Toggle API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Toggles the flashlight on/off. Only meaningful during a power outage.
    /// For local players this also broadcasts the state to the network.
    /// </summary>
    public void ToggleFlashlight()
    {
        if (subscribedSource == null || !subscribedSource.IsBlackedOut) return;

        if (isManuallyOff)
        {
            isManuallyOff = false;
            TurnOnFlashlight();
        }
        else
        {
            isManuallyOff = true;
            TurnOffFlashlight();
        }

        // Broadcast to other players
        if (isLocalPlayer)
        {
            BroadcastFlashlightState(!isManuallyOff);
        }
    }

    /// <summary>
    /// Called by RemoteInteractionHandler to apply another player's
    /// flashlight state on this avatar.
    /// </summary>
    public void SetFlashlightOn(bool on)
    {
        if (subscribedSource == null || !subscribedSource.IsBlackedOut) return;

        isManuallyOff = !on;
        if (on) TurnOnFlashlight();
        else    TurnOffFlashlight();
    }

    // ─── Power outage event handlers ─────────────────────────────────────────

    private void SubscribeIfNeeded()
    {
        if (subscribedSource != null) return;

        var source = powerOutage != null ? powerOutage : FindPowerOutage();
        if (source == null) return;

        if (source.onPowerOutage != null) source.onPowerOutage.AddListener(HandlePowerOutage);
        if (source.onPowerRestored != null) source.onPowerRestored.AddListener(HandlePowerRestored);
        subscribedSource = source;
    }

    private void Unsubscribe()
    {
        if (subscribedSource == null) return;
        if (subscribedSource.onPowerOutage != null) subscribedSource.onPowerOutage.RemoveListener(HandlePowerOutage);
        if (subscribedSource.onPowerRestored != null) subscribedSource.onPowerRestored.RemoveListener(HandlePowerRestored);
        subscribedSource = null;
    }

    private void SyncInitialState()
    {
        if (subscribedSource == null) { StowFlashlight(); return; }
        if (subscribedSource.IsBlackedOut) TurnOnFlashlight();
        else StowFlashlight();
    }

    private void HandlePowerOutage()
    {
        isManuallyOff = false;   // reset on every new outage
        TurnOnFlashlight();
    }

    private void HandlePowerRestored()
    {
        isManuallyOff = false;
        StowFlashlight();
    }

    private static PowerOutage FindPowerOutage()
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<PowerOutage>(FindObjectsInactive.Include);
#else
        return Object.FindObjectOfType<PowerOutage>(true);
#endif
    }

    // ─── Visual state helpers ────────────────────────────────────────────────

    public void StowFlashlight()
    {
        IsFlashlightOn = false;
        if (armRig != null) armRig.weight = 0f;

        if (flashlightModel != null) flashlightModel.SetActive(false);
        if (flashlightLight != null) flashlightLight.enabled = false;
    }

    public void TurnOnFlashlight()
    {
        IsFlashlightOn = true;
        if (flashlightModel != null) flashlightModel.SetActive(true);
        if (flashlightLight != null) flashlightLight.enabled = true;

        if (armRig != null) armRig.weight = 1f;
    }

    public void TurnOffFlashlight()
    {
        IsFlashlightOn = false;
        if (flashlightModel != null) flashlightModel.SetActive(true);
        if (flashlightLight != null) flashlightLight.enabled = false;

        if (armRig != null) armRig.weight = 1f;
    }

    // ─── Network broadcast ──────────────────────────────────────────────────

    private void BroadcastFlashlightState(bool on)
    {
        var net = NetworkManager.Instance;
        if (net == null) return;

        net.SendPlayerAction("flashlight_system", on ? "flashlight_on" : "flashlight_off");
        Debug.Log($"[Flashlight] Broadcast → {(on ? "ON" : "OFF")}");
    }
}
