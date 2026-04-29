using System.Collections;
using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Player
{
    public class GuardStunFeedback : MonoBehaviour
    {
        [Header("Stun")]
        public float fallbackDuration = 3f;
        public string animatorBoolParam = "IsStunned";
        public string soapAnimatorBoolParam = "IsFalling";

        [Header("Local Camera Effect")]
        public ControlMareo mareoController;
        public GameObject mareoPrefab;

        private StunAction stunAction;
        private RemotePlayerSync remoteSync;
        private PlayerInputController localInput;
        private Coroutine mareoRoutine;

        private void Awake()
        {
            stunAction = GetComponent<StunAction>();
            if (stunAction == null)
                stunAction = gameObject.AddComponent<StunAction>();

            remoteSync = GetComponent<RemotePlayerSync>();
            localInput = GetComponent<PlayerInputController>();
        }

        private void OnEnable()
        {
            var net = NetworkManager.Instance;
            if (net != null) net.OnGuardStunEvent += HandleGuardStun;
        }

        private void OnDisable()
        {
            var net = NetworkManager.Instance;
            if (net != null) net.OnGuardStunEvent -= HandleGuardStun;
        }

        private void HandleGuardStun(GuardStunPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.guardId)) return;

            string representedId = ResolveRepresentedGuardId();
            if (string.IsNullOrEmpty(representedId)) return;
            if (payload.guardId != representedId) return;

            if (stunAction == null)
            {
                stunAction = GetComponent<StunAction>();
                if (stunAction == null)
                    stunAction = gameObject.AddComponent<StunAction>();
            }

            float duration = payload.duration > 0f ? payload.duration : fallbackDuration;
            string animatorParam = payload.itemKind == "soap" ? soapAnimatorBoolParam : animatorBoolParam;

            stunAction.ApplyStun(duration, animatorParam);

            if (IsLocalGuard())
                PlayLocalMareo(duration);
        }

        private void PlayLocalMareo(float duration)
        {
            var mareo = ResolveMareoController();
            if (mareo == null) return;

            if (mareoRoutine != null)
                StopCoroutine(mareoRoutine);

            mareoRoutine = StartCoroutine(MareoRoutine(mareo, duration));
        }

        private IEnumerator MareoRoutine(ControlMareo mareo, float duration)
        {
            mareo.EmpezarAMarear();
            yield return new WaitForSeconds(duration);
            mareo.DetenerMareo();
            mareoRoutine = null;
        }

        private ControlMareo ResolveMareoController()
        {
            if (mareoController != null)
                return mareoController;

            mareoController = GetComponentInChildren<ControlMareo>(true);
            if (mareoController != null)
                return mareoController;

            if (mareoPrefab != null)
            {
                var instance = Instantiate(mareoPrefab, transform);
                instance.name = mareoPrefab.name;
                mareoController = instance.GetComponentInChildren<ControlMareo>(true);
                if (mareoController != null)
                    return mareoController;
            }

#if UNITY_2023_1_OR_NEWER
            mareoController = Object.FindFirstObjectByType<ControlMareo>(FindObjectsInactive.Include);
#else
            mareoController = Object.FindObjectOfType<ControlMareo>(true);
#endif
            return mareoController;
        }

        private string ResolveRepresentedGuardId()
        {
            if (remoteSync == null)
                remoteSync = GetComponent<RemotePlayerSync>();

            if (remoteSync != null && remoteSync.Role == "guard")
                return remoteSync.PlayerId;

            if (IsLocalGuard())
                return NetworkManager.Instance?.LocalUserId ?? NetworkManager.Instance?.LocalPlayerId;

            return null;
        }

        private bool IsLocalGuard()
        {
            if (localInput == null)
                localInput = GetComponent<PlayerInputController>();

            if (localInput == null || remoteSync != null) return false;

            var gsm = GameStateManager.Instance;
            return gsm != null && gsm.LocalRole == "guard";
        }
    }
}
