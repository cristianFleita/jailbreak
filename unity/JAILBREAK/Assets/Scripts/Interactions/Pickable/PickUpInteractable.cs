using UnityEngine;

[RequireComponent(typeof(PickableItem))]
public class PickUpInteractable : MonoBehaviour, IInteractable
{
    [Header("Interactable")]
    public string   actionLabel     = "Pick Up";
    public int      priority        = 5;
    public KeyCode  interactKey     = KeyCode.E;
    public string[] allowedInStates = null;

    [Header("Hold Point")]
    [Tooltip("Name of the transform in the player hierarchy used as hold point (e.g. 'Hand_R')")]
    public string holdPointName = "mixamorig:RightHandIndex1";

    public KeyCode   InteractKey     => interactKey;
    public string    ActionLabel     => actionLabel;
    public int       Priority        => priority;
    public Transform Transform       => transform;
    public bool      CanInteract     => routePickable == null && !pickable.IsHeld;
    public string[]  AllowedInStates => allowedInStates;

    private PickableItem pickable;
    private NetworkRoutePickable routePickable;

    void Awake()
    {
        pickable = GetComponent<PickableItem>();
        routePickable = GetComponent<NetworkRoutePickable>();
    }

    public void OnInteract(Collider source)
    {
        if (routePickable != null) return;
        if (pickable.IsHeld) return;

        var root = source.transform.root;
        var heldInput = root.GetComponent<HeldItemInput>();
        var inventory = root.GetComponent<ItemInventory>(); 

        if (heldInput == null) return;

        if (heldInput.HasItem)
        {
            if (inventory != null && !inventory.IsFull())
            {
                heldInput.ForceStore();
            }
            else
            {
                heldInput.Throw();
            }
        }

        var holdPoint = ResolveHoldPoint(root);
        if (holdPoint == null) return;

        pickable.OnPickedUp(holdPoint);
        heldInput.Attach(pickable);
    }

    private Transform ResolveHoldPoint(Transform root)
    {
        if (string.IsNullOrEmpty(holdPointName)) return root;

        foreach (Transform t in root.GetComponentsInChildren<Transform>())
            if (t.name == holdPointName) return t;

        return root;
    }
}
