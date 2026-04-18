using UnityEngine;

public static class InteractionIndicator
{
    public static GameObject CreateArrow(GameObject prefab = null)
    {
        if (prefab != null)
            return Object.Instantiate(prefab);

        GameObject arrow = new GameObject("InteractionArrow");

        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.transform.SetParent(arrow.transform);
        shaft.transform.localScale = new Vector3(0.06f, 0.25f, 0.06f);
        shaft.transform.localPosition = Vector3.zero;
        Object.Destroy(shaft.GetComponent<Collider>());

        GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tip.transform.SetParent(arrow.transform);
        tip.transform.localScale = Vector3.one * 0.15f;
        tip.transform.localPosition = new Vector3(0f, -0.3f, 0f);
        Object.Destroy(tip.GetComponent<Collider>());

        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = Color.yellow;
        shaft.GetComponent<Renderer>().material = mat;
        tip.GetComponent<Renderer>().material = mat;

        return arrow;
    }

    public static void UpdateArrow(GameObject arrow, Vector3 targetPosition, float height = 1.2f)
    {
        if (arrow == null) return;
        arrow.transform.position = targetPosition + Vector3.up * height;
        arrow.transform.Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);
    }

    public static void DestroyArrow(ref GameObject arrow)
    {
        if (arrow == null) return;
        Object.Destroy(arrow);
        arrow = null;
    }
}