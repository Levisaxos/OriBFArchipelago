using System;
using System.Collections.Generic;

namespace OriBFArchipelago.Core
{
    /**
     * Per-area slice of a run's statistics
     */
    internal class AreaStats
    {
        public float Time;
        public int Deaths;
    }

    /**
     * The moment a skill was granted, measured in run time
     */
    internal class SkillPickup
    {
        public InventoryItem Skill;
        public float Time;
    }

    /**
     * Statistics gathered over a single run. Persisted per save slot alongside the inventory.
     *
     * Deliberately holds no pickup counts: those are derived from the receiver's checked
     * locations so that death rollbacks (CheckedNotSaved -> LostOnDeath) are reflected. An
     * incrementing counter would over-report after every death.
     */
    internal class RunStats
    {
        public const int CURRENT_VERSION = 1;

        public int Version = CURRENT_VERSION;

        // Seconds of gameplay, excluding menus and time the character is suspended
        public float TotalTime;

        // Subset of TotalTime spent between dying and regaining control
        public float TimeLost;

        public int Deaths;
        public int Teleports;

        public bool Completed;
        public float CompletionTime;

        public Dictionary<WorldArea, AreaStats> Areas = new Dictionary<WorldArea, AreaStats>();
        public List<SkillPickup> SkillTimeline = new List<SkillPickup>();

        /**
         * Gets the stats bucket for an area, creating it on first use
         */
        public AreaStats GetArea(WorldArea area)
        {
            if (!Areas.TryGetValue(area, out AreaStats stats))
            {
                stats = new AreaStats();
                Areas.Add(area, stats);
            }

            return stats;
        }

        /**
         * Returns the run time at which the given skill was granted, or null if it isn't owned yet
         */
        public float? GetSkillTime(InventoryItem skill)
        {
            foreach (SkillPickup pickup in SkillTimeline)
            {
                if (pickup.Skill == skill)
                    return pickup.Time;
            }

            return null;
        }

        /**
         * Formats seconds the same way the vanilla inventory screen formats playtime,
         * so an AP page sitting in the same slot reads identically
         */
        public static string FormatTime(float seconds)
        {
            if (seconds < 0f)
                seconds = 0f;

            int total = (int)seconds;
            return $"{total / 3600:D2}:{total / 60 % 60:D2}:{total % 60:D2}";
        }
    }
}
