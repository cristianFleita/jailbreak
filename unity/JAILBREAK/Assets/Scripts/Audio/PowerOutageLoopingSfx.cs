using UnityEngine;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Keeps a prop loop audible while ventilation power is on and fades it out
    /// when the network-driven PowerOutage state disables the prison power.
    /// </summary>
    [RequireComponent(typeof(LoopingSfx))]
    public class PowerOutageLoopingSfx : MonoBehaviour
    {
        [Header("References")]
        public LoopingSfx loop;
        public global::PowerOutage powerOutage;

        [Header("Behavior")]
        public bool playWhilePowered = true;

        private global::PowerOutage subscribedPowerOutage;
        private float nextBindAttemptAt;

        private void Awake()
        {
            if (loop == null) loop = GetComponent<LoopingSfx>();
        }

        private void OnEnable()
        {
            BindPowerOutage();
            ApplyPowerState(powerOutage == null || !powerOutage.IsBlackedOut);
        }

        private void Start()
        {
            BindPowerOutage();
            ApplyPowerState(powerOutage == null || !powerOutage.IsBlackedOut);
        }

        private void Update()
        {
            if (subscribedPowerOutage != null || Time.unscaledTime < nextBindAttemptAt) return;

            nextBindAttemptAt = Time.unscaledTime + 0.5f;
            BindPowerOutage();
            if (powerOutage != null)
                ApplyPowerState(!powerOutage.IsBlackedOut);
        }

        private void OnDisable()
        {
            UnbindPowerOutage();
        }

        public void HandlePowerOutage()
        {
            ApplyPowerState(false);
        }

        public void HandlePowerRestored()
        {
            ApplyPowerState(true);
        }

        private void BindPowerOutage()
        {
            if (subscribedPowerOutage != null) return;

            if (powerOutage == null)
                powerOutage = FindPowerOutage();

            if (powerOutage == null) return;

            powerOutage.onPowerOutage?.AddListener(HandlePowerOutage);
            powerOutage.onPowerRestored?.AddListener(HandlePowerRestored);
            subscribedPowerOutage = powerOutage;
        }

        private void UnbindPowerOutage()
        {
            if (subscribedPowerOutage == null) return;

            subscribedPowerOutage.onPowerOutage?.RemoveListener(HandlePowerOutage);
            subscribedPowerOutage.onPowerRestored?.RemoveListener(HandlePowerRestored);
            subscribedPowerOutage = null;
        }

        private void ApplyPowerState(bool powered)
        {
            if (loop == null) return;

            if (powered)
            {
                if (playWhilePowered)
                    loop.Play();
            }
            else
            {
                loop.Stop();
            }
        }

        private static global::PowerOutage FindPowerOutage()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<global::PowerOutage>(FindObjectsInactive.Include);
#else
            return Object.FindObjectOfType<global::PowerOutage>(true);
#endif
        }
    }
}
