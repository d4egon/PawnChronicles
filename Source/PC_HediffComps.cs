using System.Collections.Generic;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    // ═══════════════════════════════════════════════════════════════════════
    //  HediffComp_NarrativePulse
    //
    //  A flexible comp for narrative hediffs that have ongoing psychological
    //  weight. Periodically injects thoughts from a configurable pool and,
    //  at high severity, can trigger mental states.
    //
    //  Used by all PC_Hediff_* narrative hediffs.
    // ═══════════════════════════════════════════════════════════════════════

    public class HediffCompProperties_NarrativePulse : HediffCompProperties
    {
        /// <summary>Mean time in days between thought pulses.</summary>
        public float mtbDaysBetweenThoughts = 2f;

        /// <summary>Pool of ThoughtDef defNames to randomly draw from each pulse.</summary>
        public List<string> thoughtDefs = new List<string>();

        /// <summary>
        /// Optional MentalStateDef defName. When severity is at or above
        /// mentalStateMinSeverity, this mental state can fire.
        /// </summary>
        public string mentalStateDef;

        /// <summary>Severity threshold before mental state risk begins.</summary>
        public float mentalStateMinSeverity = 0.6f;

        /// <summary>Mean time in days for the mental state to fire (when above threshold).</summary>
        public float mentalStateMtbDays = 5f;

        /// <summary>If true, fires a thought on removal (resolution of the condition).</summary>
        public string recoveryThoughtDef;

        public HediffCompProperties_NarrativePulse()
            => compClass = typeof(HediffComp_NarrativePulse);
    }

    public class HediffComp_NarrativePulse : HediffComp
    {
        private HediffCompProperties_NarrativePulse Props
            => (HediffCompProperties_NarrativePulse)props;

        // Check every ~5 seconds (300 ticks) to keep overhead minimal
        private const int CheckInterval = 300;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            var pawn = Pawn;
            if (!pawn.IsHashIntervalTick(CheckInterval, delta)) return;
            if (!pawn.Spawned || pawn.Dead || !pawn.RaceProps.Humanlike) return;
            if (pawn.needs?.mood == null) return;

            // ── Periodic thought pulse ───────────────────────────────────────
            if (!Props.thoughtDefs.NullOrEmpty()
                && Rand.MTBEventOccurs(Props.mtbDaysBetweenThoughts, 60000f, CheckInterval))
            {
                string defName = Props.thoughtDefs.RandomElement();
                var thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
                if (thoughtDef != null)
                    pawn.needs.mood.thoughts.memories.TryGainMemory(thoughtDef);
            }

            // ── Mental state at high severity ────────────────────────────────
            if (!string.IsNullOrEmpty(Props.mentalStateDef)
                && parent.Severity >= Props.mentalStateMinSeverity
                && pawn.Awake()
                && Rand.MTBEventOccurs(Props.mentalStateMtbDays, 60000f, CheckInterval))
            {
                var msDef = DefDatabase<MentalStateDef>.GetNamedSilentFail(Props.mentalStateDef);
                if (msDef != null)
                    pawn.mindState?.mentalStateHandler?.TryStartMentalState(
                        msDef, parent.def.LabelCap, transitionSilently: true);
            }
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            if (string.IsNullOrEmpty(Props.recoveryThoughtDef)) return;
            if (Pawn.Dead || Pawn.needs?.mood == null) return;

            var thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(Props.recoveryThoughtDef);
            if (thoughtDef != null)
                Pawn.needs.mood.thoughts.memories.TryGainMemory(thoughtDef);
        }
    }
}
