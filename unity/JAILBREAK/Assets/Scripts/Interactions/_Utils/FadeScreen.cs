using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class FadeScreen : MonoBehaviour
{
    [Header("Settings")]
    public float fadeInDuration  = 0.4f;
    public float fadeOutDuration = 0.6f;
    public Color fadeColor       = Color.black;

    private Image fadeImage;

    void Awake()
    {
        fadeImage       = GetComponentInChildren<Image>();
        fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, 0f);
    }

    public void FadeToBlackAndBack(float holdDuration, Action onBlack)
    {
        StartCoroutine(FadeRoutine(holdDuration, onBlack));
    }

    public float TotalDuration(float holdDuration) => fadeInDuration + holdDuration + fadeOutDuration;

    IEnumerator FadeRoutine(float holdDuration, Action onBlack)
    {
        yield return Fade(0f, 1f, fadeInDuration);
        onBlack?.Invoke();
        yield return new WaitForSeconds(holdDuration);
        yield return Fade(1f, 0f, fadeOutDuration);
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(from, to, elapsed / duration);
            fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, alpha);
            yield return null;
        }
        fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, to);
    }
}