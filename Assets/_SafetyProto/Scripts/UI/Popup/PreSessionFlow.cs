using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Silences the participant's pre-session flow (identification popup, mode picker, onboarding)
    /// so the session simulator can start the session itself.
    /// </summary>
    public static class PreSessionFlow
    {
        public static void Suppress()
        {
            var popupService = PopupService.Instance;
            if (popupService != null)
                popupService.Hide();

            foreach (var controller in Object.FindObjectsByType<NameEntryController>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                controller.StopAllCoroutines();
                controller.enabled = false;
            }
            foreach (var controller in Object.FindObjectsByType<SessionModeSelectionController>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                controller.enabled = false;
            foreach (var controller in Object.FindObjectsByType<OnboardingController>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                controller.StopAllCoroutines();
                controller.enabled = false;
            }
        }
    }
}
