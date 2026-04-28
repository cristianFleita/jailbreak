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

    private PowerOutage subscribedSource;

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
    }

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

    private void HandlePowerOutage() => TurnOnFlashlight();
    private void HandlePowerRestored() => StowFlashlight();

    private static PowerOutage FindPowerOutage()
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<PowerOutage>(FindObjectsInactive.Include);
#else
        return Object.FindObjectOfType<PowerOutage>(true);
#endif
    }

    public void StowFlashlight()
    {
        if (armRig != null) armRig.weight = 0f;

        if (flashlightModel != null) flashlightModel.SetActive(false);
        if (flashlightLight != null) flashlightLight.enabled = false;
    }

    public void TurnOnFlashlight()
    {
        if (flashlightModel != null) flashlightModel.SetActive(true);
        if (flashlightLight != null) flashlightLight.enabled = true;

        if (armRig != null) armRig.weight = 1f;
    }

    public void TurnOffFlashlight()
    {
        if (flashlightModel != null) flashlightModel.SetActive(true);
        if (flashlightLight != null) flashlightLight.enabled = false;

        if (armRig != null) armRig.weight = 1f;
    }
}
