using HarmonyLib;
using OriBFArchipelago.MapTracker.Core;
using System;
using System.Text;
using UnityEngine;

namespace OriBFArchipelago.ArchipelagoUI
{
    /**
     * One-off dump of the inventory screen's scene graph.
     *
     * The layout of this screen is prefab data: none of it is visible in Assembly-CSharp, and
     * nothing in the game looks its pieces up by path, so positions, icon names and animator
     * settings can only be discovered at runtime. This writes them to the BepInEx log the
     * first time the screen is opened so the archipelago pages can be laid out against real
     * numbers instead of derived guesses.
     *
     * Purely diagnostic - it reads and never modifies. Safe to delete once the layout settles.
     */
    [HarmonyPatch]
    internal class ApStatsDiagnostics : MonoBehaviour
    {
        private const int MAX_DEPTH = 4;
        private const int MAX_LINES = 500;

        private static bool dumped;

        private InventoryManager inventory;
        private int lines;

        [HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.Awake))]
        [HarmonyPostfix]
        private static void Awake_Postfix(InventoryManager __instance)
        {
            if (__instance.GetComponent<ApStatsDiagnostics>() == null)
                __instance.gameObject.AddComponent<ApStatsDiagnostics>();
        }

        private void Awake()
        {
            inventory = GetComponent<InventoryManager>();
        }

        private void Update()
        {
            if (dumped || inventory == null || inventory.NavigationManager == null)
                return;

            // Wait until the screen is actually up, so transforms hold their shown positions
            if (!inventory.NavigationManager.IsVisible)
                return;

            dumped = true;
            Dump();
        }

        private void Dump()
        {
            try
            {
                Log("========== AP STATS SCREEN DUMP ==========");

                DumpNamedFields();
                DumpStatSlots();
                DumpMenuItems();

                Log("---------- full hierarchy ----------");
                DumpTransform(inventory.transform, 0);

                Log("========== END DUMP ==========");
            }
            catch (Exception e)
            {
                ModLogger.Error($"[ApStatsDump] failed: {e}");
            }
        }

        /**
         * The public handles on InventoryManager, so we know what each one actually points at
         */
        private void DumpNamedFields()
        {
            Log("---------- named fields ----------");
            LogObject("AbilityNameText", inventory.AbilityNameText?.gameObject);
            LogObject("AbilityItemHighlight", inventory.AbilityItemHighlight);
            LogObject("WorldEventsGroup", inventory.WorldEventsGroup);
            LogObject("HelpMessageBox", inventory.HelpMessageBox);
            LogObject("Difficulty", inventory.Difficulty?.gameObject);
            LogObject("GinsoTreeKey", inventory.GinsoTreeKey);
            LogObject("ForlornRuinsKey", inventory.ForlornRuinsKey);
            LogObject("MountHoruKey", inventory.MountHoruKey);
        }

        /**
         * Each stat value, plus its siblings - the icon is expected to be one of them, and
         * which one decides whether icons can be hidden or swapped per page.
         */
        private void DumpStatSlots()
        {
            Log("---------- stat slots ----------");

            DumpSlot("TimeText", inventory.TimeText);
            DumpSlot("CompletionText", inventory.CompletionText);
            DumpSlot("DeathText", inventory.DeathText);
            DumpSlot("HealthUpgradesText", inventory.HealthUpgradesText);
            DumpSlot("EnergyUpgradesText", inventory.EnergyUpgradesText);
            DumpSlot("SkillPointUniquesText", inventory.SkillPointUniquesText);
        }

        private void DumpSlot(string label, MessageBox box)
        {
            if (box == null)
            {
                Log($"  {label}: NULL");
                return;
            }

            Transform t = box.transform;
            Log($"  {label}: {Describe(t)}");
            Log($"    parent: {(t.parent != null ? Describe(t.parent) : "none")}");

            if (t.parent != null)
            {
                foreach (Transform sibling in t.parent)
                    Log($"      sibling: {Describe(sibling)} components=[{Components(sibling)}]");
            }
        }

        /**
         * The navigation list decides what "hover" can mean. We need to know what is in it,
         * which entries are ability icons, and whether entries carry help text.
         */
        private void DumpMenuItems()
        {
            Log("---------- navigation ----------");

            CleverMenuItemSelectionManager nav = inventory.NavigationManager;
            Log($"  manager: {Describe(nav.transform)} visible={nav.IsVisible}");

            LogReflected(nav, "ItemDirection");
            LogReflected(nav, "AngleTolerance");
            LogReflected(nav, "CopyFromCage");

            int index = 0;
            foreach (CleverMenuItem item in nav.MenuItems)
            {
                if (item == null)
                {
                    Log($"  [{index++}] NULL");
                    continue;
                }

                InventoryAbilityItem ability = item.GetComponent<InventoryAbilityItem>();
                InventoryItemHelpText help = item.GetComponent<InventoryItemHelpText>();
                TransparencyAnimator animator = item.GetComponent<TransparencyAnimator>();

                string abilityInfo = ability != null ? $" ability={ability.Ability} has={ability.HasAbility}" : "";
                string helpInfo = help != null ? " hasHelpText" : "";
                string animInfo = animator != null ? $" animator(animateChildren={ReadField(animator, "AnimateChildren")})" : "";

                Log($"  [{index++}] {Describe(item.transform)}{abilityInfo}{helpInfo}{animInfo}");
            }
        }

        private void DumpTransform(Transform t, int depth)
        {
            if (depth > MAX_DEPTH || lines > MAX_LINES)
                return;

            Log($"{new string(' ', depth * 2)}{Describe(t)} components=[{Components(t)}]");

            foreach (Transform child in t)
                DumpTransform(child, depth + 1);
        }

        private static string Describe(Transform t)
        {
            Renderer renderer = t.GetComponent<Renderer>();
            string sorting = renderer != null
                ? $" sortLayer={renderer.sortingLayerID} order={renderer.sortingOrder}"
                : "";

            return $"'{t.name}' active={t.gameObject.activeSelf} layer={t.gameObject.layer} " +
                   $"local={Fmt(t.localPosition)} world={Fmt(t.position)} scale={Fmt(t.localScale)}{sorting}";
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:F2},{v.y:F2},{v.z:F2})";
        }

        private static string Components(Transform t)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                if (sb.Length > 0) sb.Append(",");
                sb.Append(c.GetType().Name);
            }
            return sb.ToString();
        }

        private void LogObject(string label, GameObject go)
        {
            Log(go == null ? $"  {label}: NULL" : $"  {label}: {Describe(go.transform)} components=[{Components(go.transform)}]");
        }

        private void LogReflected(object target, string member)
        {
            Log($"  {member} = {ReadField(target, member)}");
        }

        /**
         * Several of the interesting settings are serialised fields whose accessibility we
         * cannot rely on, so read them reflectively rather than failing to compile.
         */
        private static string ReadField(object target, string member)
        {
            if (target == null) return "<null target>";

            try
            {
                Type type = target.GetType();

                System.Reflection.FieldInfo field = AccessTools.Field(type, member);
                if (field != null)
                    return $"{field.GetValue(target)}";

                System.Reflection.PropertyInfo property = AccessTools.Property(type, member);
                if (property != null)
                    return $"{property.GetValue(target, null)}";

                return "<not found>";
            }
            catch (Exception e)
            {
                return $"<error: {e.GetType().Name}>";
            }
        }

        private void Log(string text)
        {
            lines++;
            ModLogger.Info($"[ApStatsDump] {text}");
        }
    }
}
