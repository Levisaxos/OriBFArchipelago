using HarmonyLib;
using OriBFArchipelago.Core;
using OriBFArchipelago.MapTracker.Core;

namespace OriBFArchipelago.Patches
{
    [HarmonyPatch(typeof(MenuScreenManager), nameof(MenuScreenManager.ShowMenuScreen), [typeof(MenuScreenManager.Screens), typeof(bool)])]
    internal class ShowMenuScreenPatch
    {
        static bool Prefix()
        {
            try
            {
                RandomizerSettings.ShowSettings = true;
                RuntimeGameWorldAreaPatch.ToggleDiscoveredAreas(MaptrackerSettings.MapVisibility);
                MaptrackerSettings.ResetCheckCount();
            }
            catch
            {

            }
            return true;
        }
    }

    [HarmonyPatch(typeof(MenuScreenManager), nameof(MenuScreenManager.HideMenuScreen))]
    internal class HideMenuScreenPatch
    {
        static bool Prefix()
        {
            // Don't hide menu if feedback window is active
            if (ArchipelagoUI.Feedback.FeedbackWindow.IsActive)
            {
                return false; // Prevent hiding the menu
            }

            RandomizerSettings.ShowSettings = false;
            return true;
        }
    }
}
