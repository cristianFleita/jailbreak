using UnityEngine;
using TMPro;

public class InteractionPrompt : MonoBehaviour
{
    public GameObject promptUI;
    public TextMeshProUGUI promptText;

    public void Show(KeyCode key, string action)
    {
        if (promptUI == null) return;
        promptUI.SetActive(true);
        if (promptText != null)
            promptText.text = $"[ {KeyName(key)} ]  {action}";
    }

    public void Hide()
    {
        promptUI?.SetActive(false);
    }

    static string KeyName(KeyCode key) => key switch
    {
        KeyCode.E      => "E",
        KeyCode.F      => "F",
        KeyCode.R      => "R",
        KeyCode.Return => "Enter",
        _              => key.ToString()
    };
}