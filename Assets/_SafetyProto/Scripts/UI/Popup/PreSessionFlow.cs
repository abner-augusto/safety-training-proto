using SafetyProto.Runtime.Simulation;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Silences the participant's pre-session flow (identification popup, mode picker, onboarding)
    /// so the session simulator can start the session itself.
    /// </summary>
    public static class PreSessionFlow
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Domain reload is disabled on Play Mode entry; static state persists between runs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterWithSimulator() => SessionSimulator.PreSessionSuppressor = Suppress;
#endif

        public static void Suppress()
        {
            var popupService = PopupService.Instance;
            if (popupService != null)
                popupService.Hide();

            foreach (var controller in Object.FindObjectsByType<NameEntryController>(FindObjectsInactive.Include))
            {
                controller.StopAllCoroutines();
                controller.enabled = false;
            }
            foreach (var controller in Object.FindObjectsByType<SessionModeSelectionController>(FindObjectsInactive.Include))
                controller.enabled = false;
            foreach (var controller in Object.FindObjectsByType<OnboardingController>(FindObjectsInactive.Include))
            {
                controller.StopAllCoroutines();
                controller.enabled = false;
            }
        }
    }
}
