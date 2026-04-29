using UnityEngine;
using UnityEngine.Rendering; 

public class ControlMareo : MonoBehaviour
{
    [Header("Referencias")]
    public Volume volumenMareo; 

    [Header("Configuración")]
    public float smoothness = 2.0f; 
    
    private float targetWeight = 0f; 

    void Update()
    {
        if (volumenMareo == null) return;
        volumenMareo.weight = Mathf.Lerp(volumenMareo.weight, targetWeight, Time.deltaTime * smoothness);
    }

    public void EmpezarAMarear()
    {
        targetWeight = 1f;
    }

    public void DetenerMareo()
    {
        targetWeight = 0f;
    }
}
