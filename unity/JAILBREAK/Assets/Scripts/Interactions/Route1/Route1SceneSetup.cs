using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jailbreak.Interactions.Route1
{
    public enum Route1AnchorKind
    {
        GuardDesk,
        Server,
        Vent
    }

    [Serializable]
    public class Route1AnchorDefinition
    {
        public string id;
        public Route1AnchorKind kind;
        public Vector3 position;
        public Vector3 eulerAngles;
        public float radius = 1.25f;
    }

    [DefaultExecutionOrder(-100)]
    public class Route1SceneSetup : MonoBehaviour
    {
        [Header("Runtime setup")]
        public bool createMissingAnchors = true;
        public bool createDebugVisuals = false;
        public bool ensureWorldStateController = true;

        [Header("Anchors")]
        public Route1AnchorDefinition[] anchors;

        [Header("Debug")]
        public bool debugLogs = false;

        private static bool sceneHookInstalled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallSceneHook()
        {
            if (sceneHookInstalled) return;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            sceneHookInstalled = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInitialScene()
        {
            EnsureRuntimeInstance(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureRuntimeInstance(scene);
        }

        private static void EnsureRuntimeInstance(Scene scene)
        {
            if (scene.name != "GameScene") return;

#if UNITY_2023_1_OR_NEWER
            var existing = UnityEngine.Object.FindFirstObjectByType<Route1SceneSetup>(FindObjectsInactive.Include);
#else
            var existing = UnityEngine.Object.FindObjectOfType<Route1SceneSetup>(true);
#endif
            if (existing != null) return;

            var go = new GameObject("Route1SceneSetup");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<Route1SceneSetup>();
        }

        private void Awake()
        {
            if (anchors == null || anchors.Length == 0)
                anchors = BuildDefaultAnchors();

            if (ensureWorldStateController && GetComponent<Route1WorldStateController>() == null)
                gameObject.AddComponent<Route1WorldStateController>();

            if (createMissingAnchors)
                CreateMissingRouteAnchors();
        }

        private void CreateMissingRouteAnchors()
        {
            var parent = new GameObject("Route1_RuntimeAnchors");
            parent.transform.SetParent(transform, false);

            foreach (var anchor in anchors)
            {
                if (anchor == null || string.IsNullOrEmpty(anchor.id)) continue;
                if (NetworkInteractable.Find(anchor.id) != null)
                {
                    if (debugLogs) Debug.Log($"[Route1SceneSetup] Existing NetworkInteractable found for {anchor.id}; skipping runtime anchor");
                    continue;
                }

                CreateAnchor(parent.transform, anchor);
            }
        }

        private void CreateAnchor(Transform parent, Route1AnchorDefinition anchor)
        {
            var go = new GameObject(anchor.id);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.position = anchor.position;
            go.transform.rotation = Quaternion.Euler(anchor.eulerAngles);
            go.layer = ResolveInteractableLayer();

            var collider = go.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = Mathf.Max(0.1f, anchor.radius);

            var network = go.AddComponent<NetworkInteractable>();
            network.networkId = anchor.id;

            var progress = go.AddComponent<ProgressAction>();
            progress.animatorBoolName = "isWorkingTable";
            progress.duration = DurationFor(anchor.kind);
            progress.resetOnStop = false;

            var point = go.AddComponent<ProgressPointAction>();
            point.actionPoint = go.transform;

            switch (anchor.kind)
            {
                case Route1AnchorKind.GuardDesk:
                    go.AddComponent<GuardDeskClueInteractable>();
                    break;
                case Route1AnchorKind.Server:
                    go.AddComponent<ServerSabotageInteractable>();
                    break;
                case Route1AnchorKind.Vent:
                    go.AddComponent<VentUnscrewInteractable>();
                    go.AddComponent<VentEscapeInteractable>();
                    break;
            }

            if (createDebugVisuals)
                CreateDebugVisual(go.transform, anchor.kind);

            go.SetActive(true);

            if (debugLogs)
                Debug.Log($"[Route1SceneSetup] Created {anchor.kind} anchor {anchor.id} at {anchor.position}", go);
        }

        private static Route1AnchorDefinition[] BuildDefaultAnchors()
        {
            var guardOffice = ResolveObjectPosition("GuardOffice", new Vector3(-3.6f, 0f, 24.4f));
            var serverOrigin = ResolveObjectPosition("Control", new Vector3(-17.2f, 0f, 8.9f));
            var ventOrigin = ResolveObjectPosition("VentilationGrille", new Vector3(-4.8f, 0.8f, -9.6f));

            var result = new Route1AnchorDefinition[19];
            int index = 0;

            var deskOffsets = new[]
            {
                new Vector3(-3.6f, 1.1f, -2.2f),
                new Vector3(-1.2f, 1.1f, -2.2f),
                new Vector3(1.2f, 1.1f, -2.2f),
                new Vector3(3.6f, 1.1f, -2.2f),
            };
            for (int i = 0; i < deskOffsets.Length; i++)
            {
                result[index++] = new Route1AnchorDefinition
                {
                    id = $"guard_desk_{i + 1}",
                    kind = Route1AnchorKind.GuardDesk,
                    position = guardOffice + deskOffsets[i],
                    eulerAngles = new Vector3(0f, 180f, 0f),
                    radius = 1.2f,
                };
            }

            for (int i = 0; i < 12; i++)
            {
                int col = i % 4;
                int row = i / 4;
                result[index++] = new Route1AnchorDefinition
                {
                    id = $"server_{i + 1}",
                    kind = Route1AnchorKind.Server,
                    position = serverOrigin + new Vector3(col * 1.35f, 0.7f, row * 1.35f),
                    eulerAngles = new Vector3(0f, 180f, 0f),
                    radius = 1.0f,
                };
            }

            for (int i = 0; i < 3; i++)
            {
                result[index++] = new Route1AnchorDefinition
                {
                    id = $"vent_{i + 1}",
                    kind = Route1AnchorKind.Vent,
                    position = ventOrigin + new Vector3(i * 5.5f, 0.1f, 0f),
                    eulerAngles = new Vector3(0f, 180f, 0f),
                    radius = 1.35f,
                };
            }

            return result;
        }

        private static Vector3 ResolveObjectPosition(string objectName, Vector3 fallback)
        {
            var go = GameObject.Find(objectName);
            return go != null ? go.transform.position : fallback;
        }

        private static int ResolveInteractableLayer()
        {
            int layer = LayerMask.NameToLayer("Interactable");
            return layer >= 0 ? layer : 6;
        }

        private static float DurationFor(Route1AnchorKind kind)
        {
            switch (kind)
            {
                case Route1AnchorKind.GuardDesk: return 3f;
                case Route1AnchorKind.Server: return 15f;
                case Route1AnchorKind.Vent: return 25f;
                default: return 1f;
            }
        }

        private static void CreateDebugVisual(Transform parent, Route1AnchorKind kind)
        {
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            primitive.name = $"{parent.name}_visual";
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = Vector3.zero;
            primitive.transform.localRotation = Quaternion.identity;

            switch (kind)
            {
                case Route1AnchorKind.GuardDesk:
                    primitive.transform.localScale = new Vector3(1.2f, 0.55f, 0.8f);
                    SetColor(primitive, new Color(0.42f, 0.25f, 0.12f));
                    break;
                case Route1AnchorKind.Server:
                    primitive.transform.localScale = new Vector3(0.75f, 1.8f, 0.55f);
                    SetColor(primitive, new Color(0.08f, 0.11f, 0.14f));
                    break;
                case Route1AnchorKind.Vent:
                    primitive.transform.localScale = new Vector3(1.1f, 0.2f, 1.1f);
                    SetColor(primitive, new Color(0.45f, 0.48f, 0.5f));
                    break;
            }

            var collider = primitive.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        private static void SetColor(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var material = shader != null ? new Material(shader) : new Material(renderer.sharedMaterial);
            material.color = color;
            renderer.sharedMaterial = material;
        }
    }
}
