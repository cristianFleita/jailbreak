using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Calls the /health endpoint on the backend and displays the result.
/// Attach to any GameObject in the scene — it creates its own Canvas and Button
/// at runtime so no UI prefab wiring is required.
/// </summary>
public class HealthChecker : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern string GetBackendUrl();
#endif

    private string backendUrl;
    private Text _statusText;

    private void Start()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        backendUrl = GetBackendUrl();
#else
        backendUrl = "http://localhost:3001";
#endif
        EnsureEventSystem();
        BuildUI();
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a GET /health request to the backend.
    /// Wired to the Health Check button's onClick in BuildUI().
    /// </summary>
    public void CallHealth()
    {
        StartCoroutine(GetHealth());
    }

    // ─── UI construction ─────────────────────────────────────────────────────

    private void BuildUI()
    {
        // Canvas
        var canvasGO = new GameObject("HealthCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Button
        var buttonGO = new GameObject("HealthButton");
        buttonGO.transform.SetParent(canvasGO.transform, false);
        var buttonRect = buttonGO.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(200f, 60f);
        buttonRect.anchoredPosition = new Vector2(0f, 30f);
        buttonGO.AddComponent<Image>().color = new Color(0.2f, 0.6f, 1f);
        var btn = buttonGO.AddComponent<Button>();
        btn.onClick.AddListener(CallHealth);

        // Button label
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(buttonGO.transform, false);
        var labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        var label = labelGO.AddComponent<Text>();
        label.text = "Check /health";
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.fontSize = 18;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Status text
        var statusGO = new GameObject("StatusText");
        statusGO.transform.SetParent(canvasGO.transform, false);
        var statusRect = statusGO.AddComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0.5f, 0.5f);
        statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.pivot = new Vector2(0.5f, 0.5f);
        statusRect.sizeDelta = new Vector2(500f, 40f);
        statusRect.anchoredPosition = new Vector2(0f, -30f);
        _statusText = statusGO.AddComponent<Text>();
        _statusText.text = "Awaiting health check…";
        _statusText.alignment = TextAnchor.MiddleCenter;
        _statusText.color = Color.white;
        _statusText.fontSize = 16;
        _statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<EventSystem>();
        esGO.AddComponent<InputSystemUIInputModule>();
    }

    // ─── Network ─────────────────────────────────────────────────────────────

    private IEnumerator GetHealth()
    {
        var url = $"{backendUrl}/health";
        SetStatus("Checking…");

        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"[HealthChecker] {req.downloadHandler.text}");
            SetStatus($"OK — {req.downloadHandler.text}");
        }
        else
        {
            Debug.LogError($"[HealthChecker] {req.error}");
            SetStatus($"Error: {req.error}");
        }
    }

    private void SetStatus(string msg)
    {
        if (_statusText != null) _statusText.text = msg;
    }
}
