using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Gives an interactable a stable, network-unique id so remote clients
/// can resolve the same object when replaying broadcast actions
/// (sit/stand etc.). Attach alongside the IInteractable component.
///
/// If `networkId` is left empty, the hierarchy path is used. That's
/// stable enough for scene-placed interactables (chairs, beds, desks)
/// but dynamic spawns should set an explicit id.
/// </summary>
[DisallowMultipleComponent]
public class NetworkInteractable : MonoBehaviour
{
    [Tooltip("Unique id shared by every client. Leave empty to derive from hierarchy path.")]
    public string networkId;

    private static readonly Dictionary<string, NetworkInteractable> Registry = new();

    public string NetworkId => networkId;

    void OnEnable()
    {
        if (string.IsNullOrEmpty(networkId))
            networkId = BuildHierarchyPath(transform);

        Registry[networkId] = this;
    }

    void OnDisable()
    {
        if (string.IsNullOrEmpty(networkId)) return;
        if (Registry.TryGetValue(networkId, out var existing) && existing == this)
            Registry.Remove(networkId);
    }

    public static NetworkInteractable Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Registry.TryGetValue(id, out var n);
        return n;
    }

    static string BuildHierarchyPath(Transform t)
    {
        var sb = new StringBuilder();
        while (t != null)
        {
            if (sb.Length > 0) sb.Insert(0, '/');
            sb.Insert(0, t.name);
            t = t.parent;
        }
        return sb.ToString();
    }
}
