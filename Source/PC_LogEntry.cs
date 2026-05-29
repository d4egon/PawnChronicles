using System.Collections.Generic;
using Verse;

namespace PawnChronicles
{
    /// <summary>
    /// A plain-text entry that appears in the combat log whenever an ember
    /// consequence fires during work. Clicking a pawn in the log filters to
    /// only show entries that concern them (via GetConcerns / Concerns).
    /// </summary>
    public class PC_LogEntry : LogEntry
    {
        private string _message;
        private Pawn   _pawn;

        // ── Parameterless ctor required by the save/load system ───────────────
        public PC_LogEntry() : base() { }

        public PC_LogEntry(string message, Pawn pawn)
            : base(DefDatabase<LogEntryDef>.GetNamedSilentFail("PC_WorkIncident"))
        {
            // base() sets ticksAbs = Find.TickManager.TicksAbs and assigns logID automatically.
            _message = message;
            _pawn    = pawn;
        }

        // ── Text ──────────────────────────────────────────────────────────────

        // ToGameStringFromPOV is NOT virtual - override the worker instead.
        protected override string ToGameStringFromPOV_Worker(Thing pov, bool forceLog = false)
            => _message ?? string.Empty;

        // ── Concerns ─────────────────────────────────────────────────────────

        public override bool Concerns(Thing t) => t == _pawn;

        public override IEnumerable<Thing> GetConcerns()
        {
            if (_pawn != null) yield return _pawn;
        }

        // ── Save / load ───────────────────────────────────────────────────────

        public override void ExposeData()
        {
            base.ExposeData(); // saves ticksAbs, logID, def
            Scribe_Values.Look(ref _message, "message");
            Scribe_References.Look(ref _pawn, "pawn");
        }
    }
}
