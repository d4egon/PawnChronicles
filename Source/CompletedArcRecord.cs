using System.Collections.Generic;
using Verse;

namespace PawnChronicles
{
    /// <summary>
    /// Archived record of a completed arc. Saved to disk.
    /// Stored in CompPersonalChronicles.completedArcHistory.
    /// </summary>
    public class CompletedArcRecord : IExposable
    {
        public string arcLabel      = "";
        public string arcDefName    = "";
        public bool   wasSuccess    = false;
        public int    completedTick = 0;

        /// <summary>
        /// Pre-built one-line skill summary, e.g. "Social +20000 xp, Melee +20000 xp"
        /// Built at completion time so it doesn't require the def to still be loaded.
        /// </summary>
        public string skillSummary = "";

        public List<ArcStageEntry> entries = new();

        public CompletedArcRecord() { }

        public CompletedArcRecord(
            string label, string defName, bool success,
            int tick, string summary, List<ArcStageEntry> arcEntries)
        {
            arcLabel      = label;
            arcDefName    = defName;
            wasSuccess    = success;
            completedTick = tick;
            skillSummary  = summary;
            entries       = new List<ArcStageEntry>(arcEntries);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref arcLabel,      "arcLabel",      "");
            Scribe_Values.Look(ref arcDefName,    "arcDefName",    "");
            Scribe_Values.Look(ref wasSuccess,    "wasSuccess",    false);
            Scribe_Values.Look(ref completedTick, "completedTick", 0);
            Scribe_Values.Look(ref skillSummary,  "skillSummary",  "");
            Scribe_Collections.Look(ref entries,  "entries",       LookMode.Deep);
            entries ??= new List<ArcStageEntry>();
        }
    }
}
