using UnityEngine;
using UnityEngine.Events;

public class PowerOutage : MonoBehaviour
{
    [Header("Main Lighting")]
    public Light mainLight; 
    public Light[] artificialLights;

    [Header("Outage Configuration")]
    [Tooltip("Ambient color during the outage.")]
    public Color outageAmbientColor = new Color(0.05f, 0.06f, 0.08f);
    
    [Tooltip("Fog color during the outage.")]
    public Color outageFogColor = new Color(0.02f, 0.02f, 0.03f);

    [Header("Events")]
    public UnityEvent onPowerOutage;
    public UnityEvent onPowerRestored;

    [Header("Debug")]
    [Tooltip("If enabled, Space/R keys trigger/restore the outage. Leave off in production builds.")]
    public bool enableDebugKeys = false;

    public bool IsBlackedOut { get; private set; }

    private Color originalAmbientColor;
    private UnityEngine.Rendering.AmbientMode originalAmbientMode;
    private float originalMainLightIntensity;
    private Color originalFogColor;

    void Awake()
    {
        // Capture originals in Awake so PowerOutageNetworkBridge.Start can safely
        // hydrate an outage from the cached world state without losing the
        // restore baseline.
        originalAmbientColor = RenderSettings.ambientLight;
        originalAmbientMode = RenderSettings.ambientMode;
        originalFogColor = RenderSettings.fogColor;

        if (mainLight != null)
            originalMainLightIntensity = mainLight.intensity;
    }

    void Update()
    {
        if (!enableDebugKeys) return;
        if (Input.GetKeyDown(KeyCode.Space)) TriggerPowerOutage();
        if (Input.GetKeyDown(KeyCode.R)) RestorePower();
    }

    public void TriggerPowerOutage()
    {
        if (IsBlackedOut) return;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = outageAmbientColor;
        RenderSettings.fogColor = outageFogColor;
        RenderSettings.reflectionIntensity = 0f;

        if (mainLight != null) mainLight.intensity = 0f;

        foreach (Light light in artificialLights)
        {
            if (light != null) light.enabled = false;
        }

        IsBlackedOut = true;
        onPowerOutage?.Invoke();
    }

    public void RestorePower()
    {
        if (!IsBlackedOut) return;

        RenderSettings.ambientMode = originalAmbientMode;
        RenderSettings.ambientLight = originalAmbientColor;
        RenderSettings.fogColor = originalFogColor;
        RenderSettings.reflectionIntensity = 1f;

        if (mainLight != null) mainLight.intensity = originalMainLightIntensity;

        foreach (Light light in artificialLights)
        {
            if (light != null) light.enabled = true;
        }

        IsBlackedOut = false;
        onPowerRestored?.Invoke();
    }
}
