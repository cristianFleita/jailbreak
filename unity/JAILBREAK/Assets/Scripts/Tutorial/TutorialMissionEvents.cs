using System;
using UnityEngine;

namespace Jailbreak.Tutorial
{
    public static class TutorialMissionEvents
    {
        public const string ReviewRoute = "tutorial.reviewRoute";
        public const string FoodPicked = "tutorial.foodPicked";
        public const string Seated = "tutorial.seated";
        public const string TrayDeposited = "tutorial.trayDeposited";
        public const string Sprinted = "tutorial.sprinted";
        public const string ToolSearched = "tutorial.toolSearched";
        public const string ItemPicked = "tutorial.itemPicked";
        public const string SlotChanged = "tutorial.slotChanged";
        public const string ItemStored = "tutorial.itemStored";
        public const string ItemDropped = "tutorial.itemDropped";
        public const string DeskSearched = "tutorial.deskSearched";
        public const string PowerDisabled = "tutorial.powerDisabled";
        public const string HiddenInCart = "tutorial.hiddenInCart";

        public const string GuardObservedRoutine = "tutorial.guardObservedRoutine";
        public const string GuardNearAnomaly = "tutorial.guardNearAnomaly";
        public const string GuardCaptureComplete = "tutorial.guardCaptureComplete";
        public const string GuardMistake = "tutorial.guardMistake";
        public const string GuardWorldCueVisited = "tutorial.guardWorldCueVisited";

        public static event Action<string> OnSignal;
        public static event Action<float> OnGuardFocusProgress;
        public static event Action<int, int> OnGuardErrorsChanged;
        public static event Action<Vector3, string> OnWorldCue;
        public static event Action<string, float> OnToast;

        public static void Emit(string signal)
        {
            if (!string.IsNullOrEmpty(signal))
                OnSignal?.Invoke(signal);
        }

        public static void SetGuardFocus(float normalized)
        {
            OnGuardFocusProgress?.Invoke(Mathf.Clamp01(normalized));
        }

        public static void SetGuardErrors(int count, int threshold)
        {
            OnGuardErrorsChanged?.Invoke(Mathf.Max(0, count), Mathf.Max(1, threshold));
        }

        public static void EmitWorldCue(Vector3 position, string label)
        {
            OnWorldCue?.Invoke(position, label);
        }

        public static void Toast(string message, float seconds = 2.4f)
        {
            if (!string.IsNullOrWhiteSpace(message))
                OnToast?.Invoke(message, seconds);
        }
    }
}
