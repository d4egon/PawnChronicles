using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Draws randomised tradeoff choices from the EffectEntryDef pools.
    ///
    /// Positive and negative pools are queried independently from the DefDatabase,
    /// filtered by the provided tags (an entry matches if it has at least one
    /// matching tag, OR if it has the "general" tag).  The results are shuffled
    /// and paired: positive[0]+negative[0], positive[1]+negative[1], etc.
    ///
    /// Each resulting StageChoice uses a time-based wait condition so the arc
    /// keeps pacing itself.
    /// </summary>
    public static class EffectPoolDrawer
    {
        /// <summary>
        /// Build <paramref name="count"/> randomised tradeoff choices from the
        /// effect pools matching <paramref name="tags"/>.
        /// Falls back to drawing from the "general" pool if a pool comes up short.
        /// </summary>
        public static List<StageChoice> DrawChoices(
            Pawn pawn,
            IEnumerable<string> tags,
            int count = 3,
            float waitDays = 5f)
        {
            var tagSet = new HashSet<string>(tags.Select(t => t.ToLowerInvariant()));

            var allDefs = DefDatabase<EffectEntryDef>.AllDefsListForReading;

            var positives = allDefs
                .Where(d => d.isPositive && Matches(d, tagSet))
                .ToList()
                .InRandomOrder()
                .ToList();

            var negatives = allDefs
                .Where(d => !d.isPositive && Matches(d, tagSet))
                .ToList()
                .InRandomOrder()
                .ToList();

            // If pools are too small, pad with "general" entries
            if (positives.Count < count)
            {
                var extras = allDefs
                    .Where(d => d.isPositive && d.tags.Contains("general") && !positives.Contains(d))
                    .ToList()
                    .InRandomOrder();
                positives.AddRange(extras);
            }
            if (negatives.Count < count)
            {
                var extras = allDefs
                    .Where(d => !d.isPositive && d.tags.Contains("general") && !negatives.Contains(d))
                    .ToList()
                    .InRandomOrder();
                negatives.AddRange(extras);
            }

            int pairs = System.Math.Min(count, System.Math.Min(positives.Count, negatives.Count));
            int waitTicks = (int)(waitDays * 60000f);

            var choices = new List<StageChoice>();
            for (int i = 0; i < pairs; i++)
            {
                var pos = positives[i];
                var neg = negatives[i];

                string hint = "PC_Effect_HintFormat".Translate(pos.DisplayLabel, neg.DisplayLabel);

                choices.Add(new StageChoice
                {
                    tagDefName           = "",
                    actionLabel          = pos.label,
                    mechanicalHint       = hint,
                    conditionKey         = "time",
                    conditionLabel       = "PC_Wait_ChooseProceed".Translate(),
                    baseline             = Find.TickManager.TicksGame,
                    targetDelta          = waitTicks,
                    effects              = new List<ChoiceEffect>(),
                    positiveEntryDefName = pos.defName,
                    negativeEntryDefName = neg.defName,
                    isHardRoad           = false,
                    isEasyOut            = false
                });
            }

            // If still empty (no entries at all), return a single time-only placeholder
            if (choices.Count == 0)
            {
                Log.Warning($"[PawnChronicles] EffectPoolDrawer: no entries matched tags [{string.Join(", ", tagSet)}]. Falling back to time wait.");
                choices.Add(new StageChoice
                {
                    tagDefName     = "",
                    actionLabel    = "PC_Choice_Default".Translate(),
                    mechanicalHint = "",
                    conditionKey   = "time",
                    conditionLabel = "PC_Wait_ChooseProceed".Translate(),
                    baseline       = Find.TickManager.TicksGame,
                    targetDelta    = waitTicks,
                    effects        = new List<ChoiceEffect>()
                });
            }

            return choices;
        }

        // ── Matching ──────────────────────────────────────────────────────────────

        private static bool Matches(EffectEntryDef def, HashSet<string> tags)
        {
            if (def.tags == null || def.tags.Count == 0) return false;
            foreach (var t in def.tags)
            {
                string tl = t.ToLowerInvariant();
                if (tl == "general" || tags.Contains(tl)) return true;
            }
            return false;
        }

        // ── Tag helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the stage-specific sub-tag for an addiction grammar role.
        /// E.g. "addiction_alcohol_withdrawal" -> "withdrawal".
        /// </summary>
        public static string AddictionStageTag(string grammarRole)
        {
            if (string.IsNullOrEmpty(grammarRole)) return "general";
            var parts = grammarRole.Split('_');
            return parts.Length > 0 ? parts[parts.Length - 1].ToLowerInvariant() : "general";
        }
    }
}
