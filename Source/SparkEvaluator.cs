using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.QuestGen;

namespace PawnChronicles
{
    /// <summary>
    /// Generates a Spark - the smallest possible narrative unit.
    ///
    /// A Spark is a single moment: one letter, one small consequence, done.
    /// No stages, no arc, no profile chaining. It fires and resolves the same
    /// in-game day. The pawn feels it happened but it leaves no lasting mark
    /// on their epic state.
    ///
    /// Sparks exist to keep the world feeling reactive even when a pawn isn't
    /// in a Kindle/Flame/Fire arc. They are generated opportunistically when:
    ///   - The pawn has no active arc
    ///   - A strong single signal exists in the scraper (extreme mood, sudden
    ///     injury, death of someone nearby, first kill, skill breakthrough)
    ///
    /// Sparks do NOT chain. They do NOT count toward epic completion.
    /// They DO apply a small mood thought and log to the pawn's chronicle.
    ///
    /// CompPersonalChronicles runs TickSparks() independently of TickArc().
    /// A pawn can receive a Spark even while an Ember or arc is active.
    /// </summary>
    public static class SparkEvaluator
    {
        public const int SparkCooldownTicks  = 15000;  // ~0.25 days - reactive, not spammy
        public const int MaxSparksPerDay     = 2;

        /// <summary>
        /// Attempts to generate a Spark quest for this pawn.
        /// Returns null if conditions aren't right or no trigger found.
        /// </summary>
        public static Quest? TryGenerateSpark(Pawn pawn, CompPersonalChronicles comp)
        {
            if (pawn == null || comp == null) return null;
            if (QuestGen.Working) return null;

            string? trigger = SelectTrigger(pawn);
            if (trigger == null) return null;

            var questScript = DefDatabase<QuestScriptDef>.GetNamedSilentFail("PC_Quest_Spark");
            if (questScript == null)
            {
                Log.Warning("[PawnChronicles] PC_Quest_Spark quest script not found.");
                return null;
            }

            Slate slate = new Slate();
            slate.Set("pawn", pawn);
            slate.Set("epicStageRole", "spark");
            slate.Set("sparkTrigger", trigger);

            var profile = comp.GetOrBuildProfile();
            var dominant = profile.GetDominantTags();
            if (dominant.Count > 0) slate.Set("epicPrimaryTag", dominant[0].label);

            try
            {
                Quest quest = QuestGen.Generate(questScript, slate);
                if (quest != null)
                {
                    Find.QuestManager.Add(quest);
                    return quest;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[PawnChronicles] Spark generation failed for {pawn.LabelShort}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Selects the triggering event type for this Spark.
        /// Only fires when something notable is actually present - Sparks
        /// should feel earned, not random noise.
        /// </summary>
        private static string? SelectTrigger(Pawn pawn)
        {
            var candidates = new List<(string trigger, float weight)>();

            // ── THRESHOLD EVENTS ─────────────────────────────────────────
            // These only fire once per threshold crossing - checked against
            // the pawn's record values which only ever increase.

            // First kill
            if (pawn.records.GetAsInt(RecordDefOf.Kills) == 1)
                candidates.Add(("first_kill", 4f));

            // Near-death recovery (low health but currently stable)
            if (pawn.health?.summaryHealth?.SummaryHealthPercent < 0.25f &&
                !pawn.Downed)
                candidates.Add(("near_death_standing", 4f));

            // ── ACUTE STATE EVENTS ───────────────────────────────────────
            // Fire based on strong current states.

            // Extreme mood crash
            if (pawn.needs?.mood?.CurLevelPercentage < 0.15f)
                candidates.Add(("mood_breaking", 3f));

            // Sudden joy spike (feast, recreation)
            if (pawn.needs?.mood?.CurLevelPercentage > 0.95f)
                candidates.Add(("moment_of_joy", 2f));

            // Acute pain spike
            if (pawn.health?.hediffSet?.PainTotal > 0.5f)
                candidates.Add(("pain_spike", 3f));

            // Witnessing a death nearby
            if (pawn.Map != null)
            {
                var recentCorpse = pawn.Map.listerThings
                    .ThingsInGroup(ThingRequestGroup.Corpse)
                    .OfType<Corpse>()
                    .Where(c => c.InnerPawn?.RaceProps?.Humanlike == true &&
                                c.timeOfDeath >= Find.TickManager.TicksGame - 2500 &&
                                c.Position.InHorDistOf(pawn.Position, 20f))
                    .FirstOrDefault();
                if (recentCorpse != null)
                    candidates.Add(("witnessed_death", 3.5f));
            }

            // Skill level-up (passion skill just crossed a round number)
            if (pawn.skills != null)
            {
                var levelUpSkill = pawn.skills.skills
                    .Where(s => s.passion != Passion.None &&
                                s.Level > 0 && s.Level % 5 == 0 &&
                                s.xpSinceLastLevel < 100f)
                    .FirstOrDefault();
                if (levelUpSkill != null)
                    candidates.Add(("skill_milestone", 2.5f));
            }

            // Relationship formation (new bond)
            if (pawn.relations?.DirectRelations != null)
            {
                var newBond = pawn.relations.DirectRelations
                    .Where(r => r.startTicks >= Find.TickManager.TicksGame - 5000 &&
                                (r.def == PawnRelationDefOf.Lover ||
                                 r.def == PawnRelationDefOf.Spouse))
                    .FirstOrDefault();
                if (newBond != null)
                    candidates.Add(("new_bond", 4f));
            }

            // Trait expression moment (random, low weight - ambient color)
            if (pawn.story?.traits?.allTraits.Count > 0)
                candidates.Add(("trait_expression", 0.5f));

            if (candidates.Count == 0) return null;

            return candidates.RandomElementByWeight(c => c.weight).trigger;
        }
    }
}
