using Game;
using OriBFArchipelago.Extensions;
using OriBFArchipelago.MapTracker.Core;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OriBFArchipelago.Core
{
    /**
     * Owns the statistics for the run in progress. Pumped every frame from
     * RandomizerManager.Update while in game.
     */
    internal static class RunStatsTracker
    {
        // Resolving the world area walks the world's area list, which is too costly to do
        // every frame. Same throttle idiom as ArchipelagoConnection's position tracking.
        private const float AREA_UPDATE_RATE = 0.25f;

        public static RunStats Current { get; private set; }

        private static int saveSlot = -1;
        private static float areaTimer;
        private static WorldArea currentArea = WorldArea.Void;

        // True between dying and regaining control, used to accumulate TimeLost
        private static bool losingTime;

        /**
         * Called once the save slot has been loaded and the connection established
         */
        public static void Begin(int slot, bool isNew)
        {
            saveSlot = slot;
            areaTimer = 0f;
            currentArea = WorldArea.Void;
            losingTime = false;

            if (!isNew && RandomizerIO.ReadStats(slot, out RunStats loaded))
            {
                Current = loaded;
                ModLogger.Debug($"Loaded run stats for slot {slot}");
            }
            else
            {
                Current = new RunStats();
                ModLogger.Debug($"Started new run stats for slot {slot}");
            }
        }

        /**
         * Called when leaving the save slot, so nothing writes to a stale slot
         */
        public static void End()
        {
            Save();
            Current = null;
            saveSlot = -1;
        }

        public static void Save()
        {
            if (Current != null && saveSlot >= 0)
                RandomizerIO.WriteStats(saveSlot, Current);
        }

        /**
         * Time only counts while the character exists and isn't suspended by a menu or
         * cutscene. Death and respawn are deliberately included, so TimeLost is a subset
         * of TotalTime rather than a separate clock.
         */
        private static bool GameplayRunning =>
            Characters.Sein != null && Characters.Sein.Active && !Characters.Sein.IsSuspended;

        public static void Update()
        {
            if (Current == null || !GameplayRunning)
                return;

            float delta = Time.deltaTime;

            areaTimer += delta;
            if (areaTimer >= AREA_UPDATE_RATE)
            {
                areaTimer = 0f;
                currentArea = Characters.Sein.CurrentWorldArea();
            }

            Current.TotalTime += delta;
            Current.GetArea(currentArea).Time += delta;

            if (losingTime)
            {
                if (RandomizerController.PlayerHasControl)
                    losingTime = false;
                else
                    Current.TimeLost += delta;
            }
        }

        public static void RecordDeath()
        {
            if (Current == null)
                return;

            Current.Deaths++;
            Current.GetArea(currentArea).Deaths++;
            losingTime = true;
        }

        public static void RecordTeleport()
        {
            if (Current != null)
                Current.Teleports++;
        }

        /**
         * Skills arrive as archipelago items, which are never revoked, so a timeline entry
         * does not need the rollback handling that local checks do.
         */
        public static void RecordSkill(InventoryItem skill)
        {
            if (Current == null || Current.GetSkillTime(skill).HasValue)
                return;

            Current.SkillTimeline.Add(new SkillPickup { Skill = skill, Time = Current.TotalTime });
        }

        public static void RecordCompletion()
        {
            if (Current == null || Current.Completed)
                return;

            Current.Completed = true;
            Current.CompletionTime = Current.TotalTime;
            Save();
        }

        /**
         * Pickups collected per area, derived from the receiver's checked locations so that
         * locations lost to a death rollback stop counting.
         */
        public static Dictionary<WorldArea, int> GetPickupsByArea()
        {
            Dictionary<WorldArea, int> counts = new Dictionary<WorldArea, int>();

            RandomizerReceiver receiver = RandomizerManager.Receiver;
            if (receiver == null)
                return counts;

            foreach (Location location in LocationLookup.GetLocations())
            {
                if (location.Area == WorldArea.Void)
                    continue;

                if (receiver.GetLocationStatus(location.Name) >= LocationStatus.CheckedNotSaved)
                {
                    counts.TryGetValue(location.Area, out int current);
                    counts[location.Area] = current + 1;
                }
            }

            return counts;
        }

        private static Dictionary<WorldArea, int> areaTotals;
        private static int totalChecks = -1;

        /**
         * Total in-world checks per area, excluding the ProgressiveMap pseudo locations.
         * The location table is fixed, so this is computed once and cached.
         */
        public static Dictionary<WorldArea, int> GetTotalsByArea()
        {
            if (areaTotals == null)
            {
                areaTotals = LocationLookup.GetLocations()
                    .Where(l => l.Area != WorldArea.Void)
                    .GroupBy(l => l.Area)
                    .ToDictionary(g => g.Key, g => g.Count());
            }

            return areaTotals;
        }

        /**
         * Total number of real in-world checks
         */
        public static int GetTotalChecks()
        {
            if (totalChecks < 0)
                totalChecks = GetTotalsByArea().Values.Sum();

            return totalChecks;
        }
    }
}
