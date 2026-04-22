using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ProgressBar : MonoBehaviour
{
    [Header("References")]
    public GameObject container;
    public Image fillImage;
    public TextMeshProUGUI labelText;

    public void Show(string label, float normalizedValue)
    {
        container.SetActive(true);
        labelText.text   = label;
        fillImage.fillAmount = normalizedValue;
    }

    public void UpdateProgress(float normalizedValue)
    {
        fillImage.fillAmount = normalizedValue;
    }

    public void Hide()
    {
        container.SetActive(false);
    }
}