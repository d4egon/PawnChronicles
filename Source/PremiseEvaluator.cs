using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnChronicles
{
    public static class PremiseEvaluator
    {
        public static QuestStageDef? SelectFromPool(
        List<QuestStageDef> pool,
        PawnNarrativeProfile profile)
    {
        if (pool == null || pool.Count == 0) return null;

        return pool
            .OrderByDescending(s =>
                s.tagRequirements != null && s.tagRequirements.Count > 0
                    ? profile.MatchScore(s.tagRequirements)
                    : 0f)
            .FirstOrDefault();
    }
        public static (NarrativePremiseDef? premise, PersonalEpicDef? epic) FindBestMatch(Pawn pawn)
        {
            var comp = pawn.GetComp<CompPersonalChronicles>();
            if (comp == null) return (null, null);

            var profile = comp.GetOrBuildProfile();

            var candidates = new List<(NarrativePremiseDef premise, float score)>();

            foreach (var premise in DefDatabase<NarrativePremiseDef>.AllDefsListForReading)
            {
                // Hard conditions + uniqueness - no probability roll yet
                if (!premise.ConditionsMet(pawn)) continue;

                float score = premise.MatchScore(profile);
                if (score < 0f) continue;

                candidates.Add((premise, score));
            }

            if (candidates.Count == 0) return (null, null);

            // Most specific/best scoring first
            candidates.Sort((a, b) => b.score.CompareTo(a.score));

            // Walk in order - activation chance roll happens here (once, per candidate)
            foreach (var (premise, _) in candidates)
            {
                if (Rand.Value * 100f > premise.activationChance) continue;

                var epic = premise.SelectEpic(profile);
                if (epic == null) continue;

                if (Rand.Value > epic.generationWeight / 100f) continue;

                return (premise, epic);
            }

            return (null, null);
        }

        /// <summary>
        /// Select the best matching unused stage for the current position
        /// in the epic chain. This is the single authority for stage selection -
        /// PersonalEpicDef no longer has its own SelectStage method.
        /// </summary>
        public static QuestStageDef? SelectNextStage(
            PersonalEpicDef epic,
            PawnNarrativeProfile profile,
            List<QuestStageDef> usedStages,
            bool isClimax,
            bool isOpening,
            string? preferredTagDefName = null)
        {
            var available = epic.stagePool
                .Where(s => !usedStages.Contains(s))
                .Where(s => !isClimax  || s.isClimax)
                .Where(s => !isOpening || s.isOpening || !epic.stagePool.Any(x => x.isOpening))
                .ToList();

            if (available.Count == 0)
            {
                Log.Warning($"[PawnChronicles] Epic '{epic.defName}' has no available stages " +
                            $"(climax={isClimax}, opening={isOpening}, used={usedStages.Count})");
                return null;
            }

            var scored = available
                .Select(s =>
                {
                    float score = s.MatchScore(profile);
                    // Boost stages whose requirements include the player's chosen tag
                    if (score >= 0f && !string.IsNullOrEmpty(preferredTagDefName))
                    {
                        var preferred = DefDatabase<NarrativeTagDef>.GetNamedSilentFail(preferredTagDefName);
                        if (preferred != null && s.tagRequirements?.Any(r => r.tag == preferred) == true)
                            score += 2f;
                    }
                    return (stage: s, score);
                })
                .Where(x => x.score >= 0f)
                .OrderByDescending(x => x.score)
                .ToList();

            if (scored.Count == 0)
            {
                Log.Warning($"[PawnChronicles] No tag-matching stages in '{epic.defName}'. Falling back.");
                return available.FirstOrDefault();
            }

            return scored[0].stage;
        }
    }
}