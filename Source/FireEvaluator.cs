using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    public static class FireEvaluator
    {
        public const int   StageCount            = 5;
        public const int   MinStageCooldownTicks = 300000;
        public const int   MaxIgnoredTicks       = 1800000;
        public const float MinProfileWeight      = 80f;

        private const int FactionRelationDeltaSuccess = 12;
        private const int FactionRelationDeltaFailure = -15;

        public static bool IsEligible(Pawn pawn, CompPersonalChronicles comp)
        {
            if (pawn == null || comp == null) return false;
            if (comp.hasActiveEpic) return false;
            if (pawn.Dead || !pawn.Spawned) return false;

            var profile = comp.GetOrBuildProfile();
            return profile.TotalTagWeight() >= MinProfileWeight;
        }

        public static QuestStageDef? SelectStage(
            PersonalEpicDef epic,
            PawnNarrativeProfile profile,
            List<QuestStageDef> usedStages,
            bool isClimax,
            bool isOpening)
        {
            return PremiseEvaluator.SelectNextStage(
                epic, profile, usedStages, isClimax, isOpening);
        }

        public static void ApplyOutcome(Pawn pawn, PersonalEpicDef epic, bool success)
        {
            if (pawn == null) return;

            ApplyThought(pawn, success);
            ApplyFactionConsequence(pawn, success);

            if (!success && Rand.Value < 0.55f)
                pawn.mindState.mentalStateHandler
                    .TryStartMentalState(MentalStateDefOf.Wander_Sad);

            string verb = success
                ? "walked through the fire and survived"
                : "was consumed by it";

            Messages.Message(
                $"{pawn.LabelShort} {verb}.",
                pawn,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent);
        }

        private static void ApplyThought(Pawn pawn, bool success)
        {
            if (pawn.needs?.mood?.thoughts?.memories == null) return;
            string defName = success ? "PC_Thought_FireRedeemed" : "PC_Thought_FireBroken";
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            if (def != null)
                pawn.needs.mood.thoughts.memories.TryGainMemory(def);
        }

        private static void ApplyFactionConsequence(Pawn pawn, bool success)
        {
            // 1.6: Faction.HostilityResponseMode removed - filter by HostileTo()
            var factions = Find.FactionManager.AllFactionsListForReading
                .Where(f => !f.IsPlayer && !f.defeated && !f.HostileTo(Faction.OfPlayer))
                .ToList();

            if (factions.Count == 0) return;

            var target = factions.RandomElement();
            int delta = success ? FactionRelationDeltaSuccess : FactionRelationDeltaFailure;
            target.TryAffectGoodwillWith(Faction.OfPlayer, delta, canSendMessage: true);
        }

        public static bool QualifiesForInferno(PawnNarrativeProfile profile)
        {
            return profile != null &&
                   profile.TotalTagWeight() >= InfernoEvaluator.MinProfileWeight;
        }
    }
}
