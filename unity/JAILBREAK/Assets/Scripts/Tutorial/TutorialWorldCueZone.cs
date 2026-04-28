using UnityEngine;

namespace Jailbreak.Tutorial
{
    [RequireComponent(typeof(Collider))]
    public class TutorialWorldCueZone : MonoBehaviour
    {
        [SerializeField] private string cueLabel = "investigate this area";
        [SerializeField] private bool emitCueOnStart;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        private void Start()
        {
            if (emitCueOnStart)
                TutorialMissionEvents.EmitWorldCue(transform.position, cueLabel);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<TutorialGuardCaptureController>() == null) return;
            TutorialMissionEvents.Emit(TutorialMissionEvents.GuardWorldCueVisited);
        }
    }
}
