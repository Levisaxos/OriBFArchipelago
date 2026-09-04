using Game;
using HarmonyLib;
using OriBFArchipelago.Core;
using OriBFArchipelago.MapTracker.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OriBFArchipelago.ArchipelagoUI
{
    internal enum StatsPage
    {
        Vanilla,
        ApGlobal,
        ApAreas
    }

    /**
     * Adds archipelago statistics pages to the vanilla inventory screen.
     *
     * The screen already renders a Statistics block of six icon+value slots, a World Events
     * block and the Skills ring. Rather than build a foreign looking window, Y (Legend) cycles
     * that block through vanilla -> AP global -> AP per area.
     *
     * The global page reuses the six vanilla MessageBoxes. That has to happen in a postfix on
     * InventoryManager.UpdateItems, because vanilla rewrites all six every FixedUpdate with
     * SetMessage, which would clobber anything written earlier.
     *
     * The per area page hides those slots plus WorldEventsGroup - a public field no vanilla
     * code ever reads - and fills the space with cloned MessageBoxes, which UpdateItems does
     * not touch.
     */
    [HarmonyPatch]
    internal class ApStatsPages : MonoBehaviour
    {
        // Recomputing multiworld counters walks the whole received-item history, so it must
        // not happen at UpdateItems' ~50Hz cadence.
        private const float COUNTER_REFRESH_RATE = 1f;

        // First pass layout for the per area page, expressed as multiples of the vanilla slot
        // spacing so it scales with whatever the prefab actually uses. Expect to tune these
        // against a screenshot.
        private const int AREA_COLUMNS = 2;
        private const float AREA_COLUMN_SPACING = 1.5f;
        private const float AREA_ROW_SPACING = 0.55f;

        // Areas in the order they are shown, Void excluded since it holds no real checks
        private static readonly WorldArea[] DisplayAreas =
        {
            WorldArea.Glades, WorldArea.Grove, WorldArea.Grotto, WorldArea.Blackroot,
            WorldArea.Swamp, WorldArea.Ginso, WorldArea.Valley, WorldArea.Misty,
            WorldArea.Forlorn, WorldArea.Sorrow, WorldArea.Horu
        };

        private static readonly Dictionary<WorldArea, string> AreaNames = new Dictionary<WorldArea, string>
        {
            { WorldArea.Glades, "Sunken Glades" },
            { WorldArea.Grove, "Hollow Grove" },
            { WorldArea.Grotto, "Moon Grotto" },
            { WorldArea.Blackroot, "Black Root Burrows" },
            { WorldArea.Swamp, "Thornfelt Swamp" },
            { WorldArea.Ginso, "Ginso Tree" },
            { WorldArea.Valley, "Valley of the Wind" },
            { WorldArea.Misty, "Misty Woods" },
            { WorldArea.Forlorn, "Forlorn Ruins" },
            { WorldArea.Sorrow, "Sorrow Pass" },
            { WorldArea.Horu, "Mount Horu" }
        };

        public static StatsPage Page { get; private set; } = StatsPage.Vanilla;

        // Cached multiworld counters and per-area pickup counts
        private static int checksCollected, checksSent, itemsReceived;
        private static Dictionary<WorldArea, int> pickupsByArea = new Dictionary<WorldArea, int>();
        private static float counterTimer;
        private static bool countersDirty = true;

        // Cloned rows for the per area page, and the objects hidden to make room for them
        private readonly List<MessageBox> areaRows = new List<MessageBox>();
        private readonly List<GameObject> hiddenForAreaPage = new List<GameObject>();
        private readonly Dictionary<AbilityType, MessageBox> skillLabels = new Dictionary<AbilityType, MessageBox>();

        // Last string written to each cloned box. Vanilla never touches the clones, so
        // skipping an unchanged write avoids re-rendering their text every FixedUpdate.
        private readonly Dictionary<MessageBox, string> clonedText = new Dictionary<MessageBox, string>();

        private bool builtAreaRows;
        private bool loggedHierarchy;
        private bool skillLabelsShown;
        private bool areaPageActive;

        [HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.Awake))]
        [HarmonyPostfix]
        private static void Awake_Postfix(InventoryManager __instance)
        {
            if (__instance.GetComponent<ApStatsPages>() == null)
                __instance.gameObject.AddComponent<ApStatsPages>();
        }

        [HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.UpdateItems))]
        [HarmonyPostfix]
        private static void UpdateItems_Postfix(InventoryManager __instance)
        {
            // Read the component off the manager that actually called us rather than a static:
            // the cloned boxes are parented under a specific inventory screen, so a stale
            // instance would write into a dead hierarchy after a scene reload.
            ApStatsPages pages = __instance.GetComponent<ApStatsPages>();
            if (pages != null)
                pages.ApplyPage(__instance);
        }

        /**
         * Advances to the next page. Only meaningful in an archipelago run.
         */
        public static void Cycle()
        {
            if (RunStatsTracker.Current == null)
                return;

            switch (Page)
            {
                case StatsPage.Vanilla: Page = StatsPage.ApGlobal; break;
                case StatsPage.ApGlobal: Page = StatsPage.ApAreas; break;
                default: Page = StatsPage.Vanilla; break;
            }

            // Recompute immediately so the flip doesn't show a stale value for a second
            countersDirty = true;
        }

        /**
         * Called when the run completes. Selects the archipelago page and tries to open the
         * inventory screen on it, so the stats surface without a second screen to build.
         */
        public static void ShowOnCompletion()
        {
            Page = StatsPage.ApGlobal;
            countersDirty = true;

            try
            {
                if (UI.Menu != null && !UI.Menu.IsInventoryVisible())
                    UI.Menu.ShowInventoryOrPauseMenu();
            }
            catch (Exception e)
            {
                // The ending sequence may refuse to open a menu; the page selection still
                // stands, so the player sees the stats the next time they pause.
                ModLogger.Debug($"Could not open the inventory screen on completion: {e}");
            }
        }

        private void ApplyPage(InventoryManager inventory)
        {
            try
            {
                bool visible = UI.Menu != null && UI.Menu.IsInventoryVisible();

                // Never leave vanilla objects hidden once the screen is gone, or navigation
                // will land on an inactive CleverMenuItem the next time it opens.
                if (!visible || Page != StatsPage.ApAreas)
                    RestoreAreaPage();

                if (RunStatsTracker.Current == null)
                {
                    // Between runs there is nothing to show, and leaving the page selected
                    // would drop the next run straight onto an archipelago page
                    Page = StatsPage.Vanilla;
                }

                if (!visible || Page == StatsPage.Vanilla)
                {
                    ShowSkillLabels(false);
                    return;
                }

                RefreshCounters();
                BuildSkillLabels(inventory);
                LogHierarchyOnce(inventory);

                if (Page == StatsPage.ApGlobal)
                    ApplyGlobalPage(inventory);
                else
                    ApplyAreaPage(inventory);

                ShowSkillLabels(true);
            }
            catch (Exception e)
            {
                // This runs from FixedUpdate; a throw here would spam every tick, so fail
                // back to the vanilla screen instead.
                ModLogger.Error($"AP statistics page failed, reverting to vanilla: {e}");
                Page = StatsPage.Vanilla;
                RestoreAreaPage();
            }
        }

        /**
         * Walking the received item history and the location table is far too costly to do at
         * UpdateItems' cadence, and neither number moves quickly, so both refresh once a second.
         */
        private static void RefreshCounters()
        {
            counterTimer += Time.deltaTime;
            if (counterTimer < COUNTER_REFRESH_RATE && !countersDirty)
                return;

            counterTimer = 0f;
            countersDirty = false;

            RandomizerManager.Connection?.GetItemCounts(out checksCollected, out checksSent, out itemsReceived);
            pickupsByArea = RunStatsTracker.GetPickupsByArea();
        }

        /**
         * Page 2: overwrite the six vanilla stat slots.
         *
         * Time, completion and deaths keep their meaning, so their icons still read correctly.
         * The remaining three slots borrow icons that do not match their new values - the
         * layout and font are the game's own, which is the point.
         */
        private void ApplyGlobalPage(InventoryManager inventory)
        {
            RunStats stats = RunStatsTracker.Current;

            // These six have to be rewritten every tick rather than only on change: vanilla's
            // UpdateItems has just overwritten them, so skipping a write would leave the
            // vanilla value on screen.
            SetText(inventory.TimeText, RunStats.FormatTime(stats.TotalTime));
            SetText(inventory.CompletionText, $"{checksCollected} / {RunStatsTracker.GetTotalChecks()}");
            SetText(inventory.DeathText, stats.Deaths.ToString());
            SetText(inventory.HealthUpgradesText, stats.Teleports.ToString());
            SetText(inventory.EnergyUpgradesText, RunStats.FormatTime(stats.TimeLost));
            SetText(inventory.SkillPointUniquesText, $"{checksSent} / {itemsReceived}");
        }

        /**
         * Page 3: hide the vanilla slots and world events, then fill the space with one
         * cloned row per area. Clones are untouched by UpdateItems, so they only need
         * rewriting while this page is up.
         */
        private void ApplyAreaPage(InventoryManager inventory)
        {
            if (!BuildAreaRows(inventory))
                return;

            HideForAreaPage(inventory);

            RunStats stats = RunStatsTracker.Current;
            Dictionary<WorldArea, int> totals = RunStatsTracker.GetTotalsByArea();

            for (int i = 0; i < DisplayAreas.Length && i < areaRows.Count; i++)
            {
                WorldArea area = DisplayAreas[i];

                stats.Areas.TryGetValue(area, out AreaStats areaStats);
                pickupsByArea.TryGetValue(area, out int got);
                totals.TryGetValue(area, out int total);

                string time = RunStats.FormatTime(areaStats?.Time ?? 0f);
                int deaths = areaStats?.Deaths ?? 0;

                areaRows[i].gameObject.SetActive(true);
                SetClonedText(areaRows[i], $"{AreaNames[area]}   {time}   {deaths}   {got}/{total}");
            }
        }

        /**
         * Clones one MessageBox per area from the playtime slot. Done lazily and only while
         * the screen is visible, so the clone inherits a sane transparency state.
         */
        private bool BuildAreaRows(InventoryManager inventory)
        {
            if (builtAreaRows)
                return true;

            if (inventory.TimeText == null)
                return false;

            Transform donor = inventory.TimeText.transform;
            Transform parent = donor.parent;
            if (parent == null)
                return false;

            GetSlotSpacing(inventory, out Vector3 origin, out float columnSpacing, out float rowSpacing);

            for (int i = 0; i < DisplayAreas.Length; i++)
            {
                GameObject clone = Instantiate(donor.gameObject);
                clone.name = $"apAreaRow{i}";
                clone.transform.SetParent(parent, false);
                clone.transform.localRotation = donor.localRotation;
                clone.transform.localScale = donor.localScale;

                int column = i % AREA_COLUMNS;
                int row = i / AREA_COLUMNS;
                clone.transform.localPosition = origin
                    + new Vector3(column * columnSpacing * AREA_COLUMN_SPACING,
                                  -row * rowSpacing * AREA_ROW_SPACING,
                                  0f);

                StripInteractiveComponents(clone);
                CopyRenderState(donor.gameObject, clone);

                MessageBox box = clone.GetComponent<MessageBox>();
                if (box == null)
                {
                    Destroy(clone);
                    continue;
                }

                clone.SetActive(false);
                areaRows.Add(box);
            }

            builtAreaRows = areaRows.Count > 0;
            return builtAreaRows;
        }

        /**
         * Derives the grid origin and spacing from the vanilla slots, so the area rows follow
         * whatever the prefab actually uses rather than hardcoded coordinates.
         */
        private static void GetSlotSpacing(InventoryManager inventory, out Vector3 origin, out float columnSpacing, out float rowSpacing)
        {
            List<Vector3> positions = GetStatSlots(inventory)
                .Where(b => b != null)
                .Select(b => b.transform.localPosition)
                .ToList();

            if (positions.Count == 0)
            {
                origin = Vector3.zero;
                columnSpacing = 1f;
                rowSpacing = 1f;
                return;
            }

            float minX = positions.Min(p => p.x);
            float maxX = positions.Max(p => p.x);
            float minY = positions.Min(p => p.y);
            float maxY = positions.Max(p => p.y);

            origin = new Vector3(minX, maxY, positions[0].z);

            // The vanilla block is three columns by two rows
            columnSpacing = Mathf.Abs(maxX - minX) / 2f;
            rowSpacing = Mathf.Abs(maxY - minY);

            if (columnSpacing <= 0.001f) columnSpacing = 1f;
            if (rowSpacing <= 0.001f) rowSpacing = 1f;
        }

        private static IEnumerable<MessageBox> GetStatSlots(InventoryManager inventory)
        {
            yield return inventory.TimeText;
            yield return inventory.CompletionText;
            yield return inventory.DeathText;
            yield return inventory.HealthUpgradesText;
            yield return inventory.EnergyUpgradesText;
            yield return inventory.SkillPointUniquesText;
        }

        private void HideForAreaPage(InventoryManager inventory)
        {
            if (areaPageActive)
                return;

            areaPageActive = true;

            foreach (MessageBox slot in GetStatSlots(inventory))
            {
                if (slot == null) continue;

                // The icon lives alongside the value, so hide the whole slot rather than
                // just the text - these are CleverMenuItems, so deactivating also removes
                // them from menu navigation.
                GameObject target = slot.transform.parent != null
                    ? slot.transform.parent.gameObject
                    : slot.gameObject;

                if (target.activeSelf)
                {
                    target.SetActive(false);
                    hiddenForAreaPage.Add(target);
                }
            }

            if (inventory.WorldEventsGroup != null && inventory.WorldEventsGroup.activeSelf)
            {
                inventory.WorldEventsGroup.SetActive(false);
                hiddenForAreaPage.Add(inventory.WorldEventsGroup);
            }

            // Selection may have been sitting on a slot we just deactivated
            inventory.NavigationManager?.SetIndexToFirst();
        }

        private void RestoreAreaPage()
        {
            // Runs on every tick that isn't showing the area page, so bail out cheaply when
            // there is nothing to undo
            if (!areaPageActive)
                return;

            areaPageActive = false;

            foreach (MessageBox row in areaRows)
            {
                if (row != null)
                    row.gameObject.SetActive(false);
            }

            foreach (GameObject hidden in hiddenForAreaPage)
            {
                if (hidden != null)
                    hidden.SetActive(true);
            }

            hiddenForAreaPage.Clear();
        }

        /**
         * Labels each acquired skill on the ring with the run time at which it arrived.
         *
         * Labels are parented to the ring's parent rather than to the icon: a locked ability's
         * TransparencyAnimator drives its children to zero alpha, and in colour/dissolve mode
         * hard-disables their renderers.
         */
        private void ShowSkillLabels(bool show)
        {
            if (!show)
            {
                if (!skillLabelsShown)
                    return;

                foreach (MessageBox label in skillLabels.Values)
                {
                    if (label != null)
                        label.gameObject.SetActive(false);
                }

                skillLabelsShown = false;
                return;
            }

            skillLabelsShown = true;

            RunStats stats = RunStatsTracker.Current;
            if (stats == null)
                return;

            foreach (SkillPickup pickup in stats.SkillTimeline)
            {
                AbilityType ability;
                try
                {
                    ability = EnumParser.GetEnumValue<AbilityType>(pickup.Skill.ToString());
                }
                catch (Exception)
                {
                    continue;
                }

                if (!skillLabels.TryGetValue(ability, out MessageBox label) || label == null)
                    continue;

                label.gameObject.SetActive(true);
                SetClonedText(label, RunStats.FormatTime(pickup.Time));
            }
        }

        /**
         * Builds the ring labels once, keyed by ability
         */
        private void BuildSkillLabels(InventoryManager inventory)
        {
            if (skillLabels.Count > 0 || inventory.NavigationManager == null || inventory.TimeText == null)
                return;

            Transform donor = inventory.TimeText.transform;

            foreach (CleverMenuItem item in inventory.NavigationManager.MenuItems)
            {
                if (item == null) continue;

                InventoryAbilityItem ability = item.GetComponent<InventoryAbilityItem>();
                if (ability == null) continue;

                GameObject clone = Instantiate(donor.gameObject);
                clone.name = $"apSkillTime_{ability.Ability}";
                clone.transform.SetParent(item.transform.parent, false);
                clone.transform.localRotation = donor.localRotation;
                clone.transform.localScale = donor.localScale * 0.6f;
                clone.transform.position = item.transform.position + new Vector3(0f, -0.6f, 0f);

                StripInteractiveComponents(clone);
                CopyRenderState(donor.gameObject, clone);

                MessageBox box = clone.GetComponent<MessageBox>();
                if (box == null)
                {
                    Destroy(clone);
                    continue;
                }

                clone.SetActive(false);
                skillLabels[ability.Ability] = box;
            }
        }

        /**
         * Cloned stat slots must not join menu navigation or pop help boxes
         */
        private static void StripInteractiveComponents(GameObject clone)
        {
            foreach (CleverMenuItem item in clone.GetComponentsInChildren<CleverMenuItem>(true))
                Destroy(item);

            foreach (InventoryItemHelpText help in clone.GetComponentsInChildren<InventoryItemHelpText>(true))
                Destroy(help);

            foreach (InventoryAbilityItem ability in clone.GetComponentsInChildren<InventoryAbilityItem>(true))
                Destroy(ability);
        }

        /**
         * A fresh GameObject defaults to layer 0 and sorting order 0, which gets it culled by
         * the menu camera. Same fix as LogicIconRings.
         */
        private static void CopyRenderState(GameObject donor, GameObject clone)
        {
            Renderer source = donor.GetComponentInChildren<Renderer>(true);
            if (source == null)
                return;

            clone.layer = donor.layer;

            foreach (Renderer target in clone.GetComponentsInChildren<Renderer>(true))
            {
                target.gameObject.layer = donor.layer;
                target.sortingLayerID = source.sortingLayerID;
                target.sortingOrder = source.sortingOrder;
            }

            try
            {
                TransparencyAnimator.Register(clone.transform);
            }
            catch (Exception)
            {
                // Registration is a nicety for the fade; the label is still readable without it
            }
        }

        private static void SetText(MessageBox box, string text)
        {
            if (box == null)
                return;

            box.MessageProvider = null;
            box.SetMessage(new MessageDescriptor(text));
        }

        /**
         * Cloned boxes are ours alone, so an unchanged string can be skipped. That matters:
         * SetMessage re-renders the text mesh, and the clones outnumber the vanilla slots.
         */
        private void SetClonedText(MessageBox box, string text)
        {
            if (box == null)
                return;

            if (clonedText.TryGetValue(box, out string previous) && previous == text)
                return;

            clonedText[box] = text;
            SetText(box, text);
        }

        /**
         * One-off dump of the statistics block so the exact prefab layout can be tuned from
         * the log rather than guessed. Only runs at debug level.
         */
        private void LogHierarchyOnce(InventoryManager inventory)
        {
            if (loggedHierarchy)
                return;

            loggedHierarchy = true;

            try
            {
                foreach (MessageBox slot in GetStatSlots(inventory))
                {
                    if (slot == null) continue;
                    Transform t = slot.transform;
                    ModLogger.Debug($"[ApStats] slot {t.name} parent={t.parent?.name} local={t.localPosition} layer={t.gameObject.layer}");

                    if (t.parent != null)
                    {
                        foreach (Transform child in t.parent)
                            ModLogger.Debug($"[ApStats]   sibling {child.name} active={child.gameObject.activeSelf} local={child.localPosition}");
                    }
                }

                ModLogger.Debug($"[ApStats] worldEvents={inventory.WorldEventsGroup?.name} skillLabels={skillLabels.Count}");
            }
            catch (Exception e)
            {
                ModLogger.Debug($"[ApStats] hierarchy dump failed: {e}");
            }
        }
    }
}
