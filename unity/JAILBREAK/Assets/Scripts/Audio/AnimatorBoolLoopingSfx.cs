using UnityEngine;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Watches an Animator bool and plays/stops a <see cref="LoopingSfx"/> in lockstep.
    /// The interaction layer already toggles bools like "isLoadingWasher",
    /// "isWorking", "isInspecting" via <see cref="ProgressAction"/> — we piggy-back
    /// on those instead of patching the interaction code.
    ///
    /// Drop on the same GameObject that has the LoopingSfx and assign the Animator.
    /// </summary>
    public class AnimatorBoolLoopingSfx : MonoBehaviour
    {
        [Header("Source")]
        public Animator animator;

        [Tooltip("Bool parameter name on the Animator. Loop plays while true, stops when false.")]
        public string boolName;

        [Header("Target Loop")]
        public LoopingSfx loop;

        [Header("Behavior")]
        [Tooltip("Invert the bool — useful if the animator drives 'isStopped' style flags.")]
        public bool invert = false;

        private int _hash;
        private bool _hasParam;
        private bool _wasActive;

        private void Awake()
        {
            if (loop == null) loop = GetComponent<LoopingSfx>();
            if (animator == null) animator = GetComponentInParent<Animator>();
            CacheParam();
        }

        private void OnValidate()
        {
            CacheParam();
        }

        private void CacheParam()
        {
            _hasParam = false;
            if (animator == null || string.IsNullOrEmpty(boolName)) return;
            _hash = Animator.StringToHash(boolName);
            // Validate the param actually exists to avoid spamming warnings every frame.
            if (animator.runtimeAnimatorController == null) return;
            foreach (var p in animator.parameters)
            {
                if (p.name == boolName && p.type == AnimatorControllerParameterType.Bool)
                {
                    _hasParam = true; break;
                }
            }
        }

        private void Update()
        {
            if (!_hasParam || loop == null) return;

            bool raw = animator.GetBool(_hash);
            bool active = invert ? !raw : raw;
            if (active == _wasActive) return;

            _wasActive = active;
            if (active) loop.Play(); else loop.Stop();
        }
    }
}
