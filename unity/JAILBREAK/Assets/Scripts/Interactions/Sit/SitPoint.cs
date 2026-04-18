using UnityEngine;

public class SitPoint : MonoBehaviour
{
    [Tooltip("Adjust until the character sits flush on the seat. Negative = lower.")]
    public float sitHeightOffset;
    public bool isOccupied;

    public Vector3 GetSitPosition() => transform.position + Vector3.up * sitHeightOffset;

    void OnDrawGizmos()
    {
        Gizmos.color = isOccupied ? Color.red : Color.yellow;
        Gizmos.DrawSphere(GetSitPosition(), 0.08f);
        Gizmos.DrawRay(GetSitPosition(), transform.forward * 0.4f);

        Gizmos.color = Color.gray;
        Gizmos.DrawLine(transform.position, GetSitPosition());
    }
}