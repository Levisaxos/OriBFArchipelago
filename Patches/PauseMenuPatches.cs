using HarmonyLib;
using OriBFArchipelago.ArchipelagoUI;
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
                IconHoverUI.InvalidateCache();
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
            RandomizerSettings.ShowSettings = false;
            return true;
        }
    }
}
