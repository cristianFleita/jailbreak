using System.Collections;
using Jailbreak.Network;
using Jailbreak.Player;
using UnityEngine;

public class CarriedThrowableController : MonoBehaviour
{
    private const string FoodPlateId = "food_plate";
    private const string ClothesBundleId = "clothes_bundle";
    private const string FoldedClothesId = "folded_clothes";
    private const string ContainerId = "container";

    [Header("Fallback Input")]
    public KeyCode throwKey = KeyCode.Mouse0;

    [Header("Pickable Prefabs")]
    public GameObject containerPrefab;

    [Header("Throw")]
    public Transform aimTransform;
    public float throwForce = 12f;
    public float spawnForwardOffset = 0.7f;
    public float spawnUpOffset = 0.15f;
    public float stunDuration = 3f;
    public float projectileLifetime = 8f;
    public float fallbackColliderRadius = 0.28f;

    private HeldItemInput heldItemInput;
    private CarryFoodInteraction carryFood;
    private CarryClothesInteraction carryClothes;
    private CarryFoldedClothesInteraction carryFolded;
    private PlayerInputController localInput;
    private RemotePlayerSync remoteSync;

    private void Awake()
    {
        heldItemInput = GetComponent<HeldItemInput>();
        carryFood = GetComponent<CarryFoodInteraction>();
        carryClothes = GetComponent<CarryClothesInteraction>();
        carryFolded = GetComponent<CarryFoldedClothesInteraction>();
        localInput = GetComponent<PlayerInputController>();
        remoteSync = GetComponent<RemotePlayerSync>();
    }

    private void OnEnable()
    {
        var net = NetworkManager.Instance;
        if (net != null) net.OnThrowableThrowEvent += HandleNetworkThrow;
    }

    private void OnDisable()
    {
        var net = NetworkManager.Instance;
        if (net != null) net.OnThrowableThrowEvent -= HandleNetworkThrow;
    }

    private void Update()
    {
        if (!IsLocalPrisoner()) return;
        if (heldItemInput != null && heldItemInput.HasItem) return;

        var key = heldItemInput != null ? heldItemInput.ThrowKey : throwKey;
        if (InputSystemKey.WasPressedThisFrame(key))
            TryThrowLocal();
    }

    private void TryThrowLocal()
    {
        if (!TryResolveCarried(out var carried)) return;

        Vector3 direction = ResolveThrowDirection();
        Vector3 origin = ResolveOrigin(carried.attachPoint, direction);
        float force = heldItemInput != null ? heldItemInput.throwForce : throwForce;
        string throwerId = NetworkManager.Instance?.LocalUserId ?? NetworkManager.Instance?.LocalPlayerId;

        ClearCarriedVisual(carried.kind, suppressSync: true);
        LaunchProjectile(carried.kind, carried.prefab, origin, direction, force, carried.worldScale, throwerId, transform, true);

        var net = NetworkManager.Instance;
        if (net != null)
        {
            net.SendThrowableThrow(new ThrowableThrowPayload
            {
                itemKind = carried.kind,
                origin = SVector3.FromUnity(origin),
                direction = SVector3.FromUnity(direction.normalized),
                force = force
            });
        }
    }

    private void HandleNetworkThrow(ThrowableThrowBroadcast payload)
    {
        if (payload == null || string.IsNullOrEmpty(payload.throwerId)) return;

        string playerId = ResolveRepresentedPlayerId();
        if (string.IsNullOrEmpty(playerId) || payload.throwerId != playerId) return;

        if (!TryResolvePrefab(payload.itemKind, out var prefab, out var scale)) return;

        ClearCarriedVisual(payload.itemKind, suppressSync: false);
        LaunchProjectile(
            payload.itemKind,
            prefab,
            payload.origin.ToUnity(),
            payload.direction.ToUnity(),
            payload.force,
            scale,
            payload.throwerId,
            transform,
            false);
    }

    private void ClearCarriedVisual(string kind, bool suppressSync)
    {
        if (kind == FoodPlateId && carryFood != null)
        {
            if (suppressSync) carryFood.SuppressSync = true;
            carryFood.ForceReset();
            if (suppressSync) StartCoroutine(UnsuppressFood());
            return;
        }

        if (kind == ClothesBundleId && carryClothes != null)
        {
            if (suppressSync) carryClothes.SuppressSync = true;
            carryClothes.ForceReset();
            if (suppressSync) StartCoroutine(UnsuppressClothes());
            return;
        }

        if (kind == FoldedClothesId && carryFolded != null)
        {
            if (suppressSync) carryFolded.SuppressSync = true;
            carryFolded.ForceReset();
            if (suppressSync) StartCoroutine(UnsuppressFolded());
        }
    }

    private IEnumerator UnsuppressFood()
    {
        yield return new WaitForSeconds(0.5f);
        if (carryFood != null) carryFood.SuppressSync = false;
    }

    private IEnumerator UnsuppressClothes()
    {
        yield return new WaitForSeconds(0.5f);
        if (carryClothes != null) carryClothes.SuppressSync = false;
    }

    private IEnumerator UnsuppressFolded()
    {
        yield return new WaitForSeconds(0.5f);
        if (carryFolded != null) carryFolded.SuppressSync = false;
    }

    private void LaunchProjectile(
        string kind,
        GameObject prefab,
        Vector3 origin,
        Vector3 direction,
        float force,
        Vector3 scale,
        string throwerId,
        Transform ownerRoot,
        bool reportHits)
    {
        if (prefab == null) return;

        var rotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction.normalized, Vector3.up)
            : Quaternion.identity;

        var projectile = new GameObject($"Throwable_{kind}");
        projectile.transform.SetPositionAndRotation(origin, rotation);
        projectile.layer = prefab.layer;

        var collider = projectile.AddComponent<SphereCollider>();
        collider.radius = fallbackColliderRadius;

        var rb = projectile.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var visual = Instantiate(prefab, projectile.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = scale;
        SanitizeProjectileVisual(visual);

        var networkProjectile = projectile.GetComponent<NetworkThrowableProjectile>();
        if (networkProjectile == null)
            networkProjectile = projectile.AddComponent<NetworkThrowableProjectile>();

        networkProjectile.Launch(
            kind,
            throwerId,
            ownerRoot,
            direction,
            force,
            stunDuration,
            projectileLifetime,
            reportHits);
    }

    private void SanitizeProjectileVisual(GameObject visual)
    {
        foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;

        foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true))
            Destroy(body);

        foreach (var pickable in visual.GetComponentsInChildren<PickableItem>(true))
            Destroy(pickable);

        foreach (var pickup in visual.GetComponentsInChildren<PickUpInteractable>(true))
            Destroy(pickup);

        foreach (var hitHandler in visual.GetComponentsInChildren<ThrowHitHandler>(true))
            Destroy(hitHandler);

        foreach (var projectile in visual.GetComponentsInChildren<NetworkThrowableProjectile>(true))
            Destroy(projectile);
    }

    private bool TryResolveCarried(out CarriedThrowable carried)
    {
        if (carryFood != null && carryFood.IsCarrying && carryFood.platePrefab != null)
        {
            carried = new CarriedThrowable(FoodPlateId, carryFood.platePrefab, carryFood.handAttachPoint, carryFood.plateLocalScale);
            return true;
        }

        if (carryClothes != null && carryClothes.IsCarrying && carryClothes.clothesPrefab != null)
        {
            carried = new CarriedThrowable(ClothesBundleId, carryClothes.clothesPrefab, carryClothes.handAttachPoint, carryClothes.clothesLocalScale);
            return true;
        }

        if (carryFolded != null && carryFolded.IsCarrying && carryFolded.foldedClothesPrefab != null)
        {
            carried = new CarriedThrowable(FoldedClothesId, carryFolded.foldedClothesPrefab, carryFolded.handAttachPoint, carryFolded.foldedLocalScale);
            return true;
        }

        carried = default;
        return false;
    }

    private bool TryResolvePrefab(string kind, out GameObject prefab, out Vector3 scale)
    {
        if (kind == FoodPlateId && carryFood != null)
        {
            prefab = carryFood.platePrefab;
            scale = carryFood.plateLocalScale;
            return prefab != null;
        }

        if (kind == ClothesBundleId && carryClothes != null)
        {
            prefab = carryClothes.clothesPrefab;
            scale = carryClothes.clothesLocalScale;
            return prefab != null;
        }

        if (kind == FoldedClothesId && carryFolded != null)
        {
            prefab = carryFolded.foldedClothesPrefab;
            scale = carryFolded.foldedLocalScale;
            return prefab != null;
        }

        if (kind == ContainerId && containerPrefab != null)
        {
            prefab = containerPrefab;
            scale = containerPrefab.transform.localScale;
            return true;
        }

        prefab = null;
        scale = Vector3.one;
        return false;
    }

    private Vector3 ResolveThrowDirection()
    {
        if (aimTransform != null)
            return aimTransform.forward.normalized;

        if (heldItemInput != null && heldItemInput.aimTransform != null)
            return heldItemInput.aimTransform.forward.normalized;

        if (Camera.main != null)
            return Camera.main.transform.forward.normalized;

        return transform.forward.normalized;
    }

    private Vector3 ResolveOrigin(Transform attachPoint, Vector3 direction)
    {
        Vector3 basePosition = attachPoint != null
            ? attachPoint.position
            : transform.position + Vector3.up;

        return basePosition + Vector3.up * spawnUpOffset + direction.normalized * spawnForwardOffset;
    }

    private bool IsLocalPrisoner()
    {
        if (localInput == null || remoteSync != null) return false;

        var gsm = GameStateManager.Instance;
        return gsm == null || string.IsNullOrEmpty(gsm.LocalRole) || gsm.LocalRole == "prisoner";
    }

    private string ResolveRepresentedPlayerId()
    {
        if (remoteSync == null)
            remoteSync = GetComponent<RemotePlayerSync>();

        if (remoteSync != null)
            return remoteSync.PlayerId;

        if (IsLocalPrisoner())
            return NetworkManager.Instance?.LocalUserId ?? NetworkManager.Instance?.LocalPlayerId;

        return null;
    }

    private readonly struct CarriedThrowable
    {
        public readonly string kind;
        public readonly GameObject prefab;
        public readonly Transform attachPoint;
        public readonly Vector3 worldScale;

        public CarriedThrowable(string kind, GameObject prefab, Transform attachPoint, Vector3 worldScale)
        {
            this.kind = kind;
            this.prefab = prefab;
            this.attachPoint = attachPoint;
            this.worldScale = worldScale;
        }
    }
}
