using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class RimGlowController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Material rimGlowMaterial;

    [Header("Loop Timing")]
    [SerializeField, Range(0.05f, 3f)] private float transitionDuration = 0.3f;
    [SerializeField, Range(0f, 5f)]    private float holdOnDuration     = 0.5f;
    [SerializeField, Range(0f, 5f)]    private float holdOffDuration    = 0.2f;

    [Header("Behavior")]
    [Tooltip("If false, the glow stays idle until StartLoop()/ActivateGlow() is called externally.")]
    [SerializeField] private bool autoStart = false;

    [Header("Rim")]
    [SerializeField] private Color             rimColor     = new Color(0f, 0.6f, 1f, 1f);
    [SerializeField, Range(0.1f, 16f)] private float rimPower     = 4f;
    [SerializeField, Range(0f, 10f)]   private float rimIntensity = 3f;
    [SerializeField, Range(-1f, 1f)]   private float rimOffset    = 0f;

    [Header("Flicker")]
    [SerializeField] private bool              flickerEnabled   = false;
    [SerializeField, Range(0.1f, 20f)] private float flickerSpeed     = 6f;
    [SerializeField, Range(0f, 1f)]    private float flickerMin       = 0.2f;
    [SerializeField, Range(0f, 2f)]    private float flickerMax       = 1f;
    [SerializeField, Range(1f, 16f)]   private float flickerSharpness = 4f;

    private static readonly int PropRimColor         = Shader.PropertyToID("_RimColor");
    private static readonly int PropRimPower         = Shader.PropertyToID("_RimPower");
    private static readonly int PropRimIntensity     = Shader.PropertyToID("_RimIntensity");
    private static readonly int PropRimOffset        = Shader.PropertyToID("_RimOffset");
    private static readonly int PropIntensityScale   = Shader.PropertyToID("_IntensityScale");
    private static readonly int PropFlickerSpeed     = Shader.PropertyToID("_FlickerSpeed");
    private static readonly int PropFlickerMin       = Shader.PropertyToID("_FlickerMin");
    private static readonly int PropFlickerMax       = Shader.PropertyToID("_FlickerMax");
    private static readonly int PropFlickerSharpness = Shader.PropertyToID("_FlickerSharpness");

    private const string KeywordFlicker = "_FLICKER_ON";

    private Material   instancedGlowMaterial;
    private Material[] originalMaterials;
    private Material[] materialsWithGlow;
    private Coroutine  loopCoroutine;
    private bool       isLooping;
    private bool       glowAttached;

    void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        instancedGlowMaterial = new Material(rimGlowMaterial);
        originalMaterials     = targetRenderer.materials;
        materialsWithGlow     = BuildMaterialsWithGlow();

        // ApplyAllProperties();
        if (autoStart) StartLoop();
    }

    void OnDestroy()
    {
        if (instancedGlowMaterial != null)
            Destroy(instancedGlowMaterial);
    }

    public void StartLoop()
    {
        if (isLooping) return;
        isLooping    = true;
        loopCoroutine = StartCoroutine(GlowLoop());
    }

    public void StopLoop()
    {
        if (!isLooping) return;
        isLooping = false;

        if (loopCoroutine != null)
        {
            StopCoroutine(loopCoroutine);
            loopCoroutine = null;
        }

        StartCoroutine(FadeOutAndDetach());
    }

    public void ActivateGlow()
    {
        AttachGlow();
        StopAllCoroutines();
        StartCoroutine(FadeScale(0f, 1f));
    }

    public void DeactivateGlow()
    {
        StopAllCoroutines();
        StartCoroutine(FadeOutAndDetach());
    }

    public void SetFlickerEnabled(bool enabled)
    {
        flickerEnabled = enabled;
        ApplyFlickerKeyword();
    }

    public void SetRimColor(Color color)
    {
        rimColor = color;
        instancedGlowMaterial?.SetColor(PropRimColor, rimColor);
    }

    public void SetRimIntensity(float intensity)
    {
        rimIntensity = intensity;
        instancedGlowMaterial?.SetFloat(PropRimIntensity, rimIntensity);
    }

    IEnumerator GlowLoop()
    {
        while (isLooping)
        {
            AttachGlow();
            yield return FadeScale(0f, 1f);
            yield return new WaitForSeconds(holdOnDuration);
            yield return FadeScale(1f, 0f);
            DetachGlow();
            yield return new WaitForSeconds(holdOffDuration);
        }
    }

    IEnumerator FadeOutAndDetach()
    {
        yield return FadeScale(instancedGlowMaterial.GetFloat(PropIntensityScale), 0f);
        DetachGlow();
    }

    IEnumerator FadeScale(float from, float to)
    {
        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionDuration));
            instancedGlowMaterial.SetFloat(PropIntensityScale, Mathf.Lerp(from, to, t));
            yield return null;
        }

        instancedGlowMaterial.SetFloat(PropIntensityScale, to);
    }

    void AttachGlow()
    {
        if (glowAttached) return;
        glowAttached              = true;
        targetRenderer.materials  = materialsWithGlow;
    }

    void DetachGlow()
    {
        if (!glowAttached) return;
        glowAttached              = false;
        targetRenderer.materials  = originalMaterials;
    }

    Material[] BuildMaterialsWithGlow()
    {
        var mats = new Material[originalMaterials.Length + 1];
        for (int i = 0; i < originalMaterials.Length; i++)
            mats[i] = originalMaterials[i];
        mats[originalMaterials.Length] = instancedGlowMaterial;
        return mats;
    }

    void ApplyAllProperties()
    {
        if (instancedGlowMaterial == null) return;

        instancedGlowMaterial.SetColor(PropRimColor,         rimColor);
        instancedGlowMaterial.SetFloat(PropRimPower,         rimPower);
        instancedGlowMaterial.SetFloat(PropRimIntensity,     rimIntensity);
        instancedGlowMaterial.SetFloat(PropRimOffset,        rimOffset);
        instancedGlowMaterial.SetFloat(PropIntensityScale,   0f);
        instancedGlowMaterial.SetFloat(PropFlickerSpeed,     flickerSpeed);
        instancedGlowMaterial.SetFloat(PropFlickerMin,       flickerMin);
        instancedGlowMaterial.SetFloat(PropFlickerMax,       flickerMax);
        instancedGlowMaterial.SetFloat(PropFlickerSharpness, flickerSharpness);

        ApplyFlickerKeyword();
    }

    void ApplyFlickerKeyword()
    {
        if (instancedGlowMaterial == null) return;
        if (flickerEnabled) instancedGlowMaterial.EnableKeyword(KeywordFlicker);
        else                instancedGlowMaterial.DisableKeyword(KeywordFlicker);
    }

    void OnValidate()
    {
        if (instancedGlowMaterial != null)
            ApplyAllProperties();
    }
}