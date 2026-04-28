using UnityEngine;

namespace Jailbreak.Tutorial
{
    public class TutorialMissionSignal : MonoBehaviour
    {
        [SerializeField] private string signal;
        [SerializeField] private string toast;
        [SerializeField] private bool emitWorldCue;
        [SerializeField] private string worldCueLabel;

        public void Emit()
        {
            TutorialMissionEvents.Emit(signal);
            if (!string.IsNullOrEmpty(toast))
                TutorialMissionEvents.Toast(toast);
            if (emitWorldCue)
                TutorialMissionEvents.EmitWorldCue(transform.position, worldCueLabel);
        }
    }
}
