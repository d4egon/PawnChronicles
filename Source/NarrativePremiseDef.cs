using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    public class NarrativePremiseDef : Def
    {
        public List<NarrativeCondition> conditions = new List<NarrativeCondition>();
        public List<NarrativeTagRequirement> tagRequirements = new List<NarrativeTagRequirement>();
        public List<PersonalEpicDef> epicPool = new List<PersonalEpicDef>();
        public EpicSelectionStrategy epicSelectionStrategy = EpicSelectionStrategy.WeightedRandom;
        public float activationChance = 75f;
        public bool uniquePerPawn = true;

        /// <summary>
        /// Calculated specificity used to rank premises.
        /// More conditions + requirements = higher specificity.
        /// </summary>
        public float Specificity => (conditions.Count * 10f) + (tagRequirements.Count * 5f);

        /// <summary>
        /// Checks only hard conditions and uniqueness - NO probability roll here.
        /// The activation chance roll is handled exclusively by PremiseEvaluator.FindBestMatch
        /// so it isn't consumed before the evaluator sees the candidate.
        /// </summary>
        public bool ConditionsMet(Pawn pawn)
        {
            if (pawn == null) return false;

            // Hard conditions - all must pass
            if (!conditions.All(c => c.IsMet(pawn))) return false;

            // Uniqueness - skip if this pawn already completed an epic from this pool
            if (uniquePerPawn)
            {
                var comp = pawn.GetComp<CompPersonalChronicles>();
                if (comp != null && epicPool.Any(e => comp.HasCompletedEpic(e)))
                    return false;
            }

            return true;
        }

        public float MatchScore(PawnNarrativeProfile profile)
        {
            float score = profile.MatchScore(tagRequirements);
            if (score < 0f) return -1f;
            return score + Specificity;
        }

        public PersonalEpicDef? SelectEpic(PawnNarrativeProfile profile)
        {
            if (epicPool.NullOrEmpty()) return null;

            return epicSelectionStrategy switch
            {
                EpicSelectionStrategy.WeightedRandom => epicPool.RandomElementByWeight(e => e.generationWeight),
                EpicSelectionStrategy.BestTagMatch   => BestMatchingEpic(profile),
                EpicSelectionStrategy.AlwaysFirst    => epicPool[0],
                _                                    => epicPool.RandomElement()
            };
        }

        private PersonalEpicDef? BestMatchingEpic(PawnNarrativeProfile profile)
        {
            if (epicPool.NullOrEmpty()) return null;
            return epicPool
                .OrderByDescending(e => e.tagRequirements != null
                    ? profile.MatchScore(e.tagRequirements)
                    : 0f)
                .ThenByDescending(e => e.generationWeight)
                .FirstOrDefault();
        }
    }

    public enum EpicSelectionStrategy
    {
        WeightedRandom,
        BestTagMatch,
        AlwaysFirst
    }
}