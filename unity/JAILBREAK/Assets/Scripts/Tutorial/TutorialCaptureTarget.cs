using UnityEngine;

namespace Jailbreak.Tutorial
{
    public class TutorialCaptureTarget : MonoBehaviour
    {
        public string targetId = "tutorial_target";
        public string displayName = "Prisoner";
        public bool suspiciousTarget;
        public bool correctCaptureTarget;
        public bool routineNpc = true;

        private void Reset()
        {
            targetId = gameObject.name;
            displayName = gameObject.name;
        }
    }
}
