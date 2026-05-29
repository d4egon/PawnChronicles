using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    public static class FlameEvaluator
    {
        public const int   StageCount            = 4;
        public const int   MinStageCooldownTicks = 240000;
        public const int   MaxIgnoredTicks       = 900000;
        public const float MinProfileWeight      = 50f;

        private const int FactionRelationDeltaSuccess = 5;
        private const int FactionRelationDeltaFailure = -8;

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
            ApplyFactionTouch(pawn, success);

            string verb = success ? "emerged from the fire" : "was burned by it";
            Messages.Message(
                $"{pawn.LabelShort} {verb}.",
                pawn,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent);
        }

        private static void ApplyThought(Pawn pawn, bool success)
        {
            if (pawn.needs?.mood?.thoughts?.memories == null) return;
            string defName = success ? "PC_Thought_FlameBurned" : "PC_Thought_FlameScorched";
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            if (def != null)
                pawn.needs.mood.thoughts.memories.TryGainMemory(def);
        }

        private static void ApplyFactionTouch(Pawn pawn, bool success)
        {
            // 1.6: Faction no longer has HostilityResponseMode property
            // Use HostileTo() instead
            var target = pawn.Map?.ParentFaction
                ?? Find.FactionManager.RandomNonHostileFaction(
                    allowHidden: false,
                    minTechLevel: TechLevel.Neolithic);

            if (target == null || target == Faction.OfPlayer) return;

            int delta = success ? FactionRelationDeltaSuccess : FactionRelationDeltaFailure;
            target.TryAffectGoodwillWith(Faction.OfPlayer, delta, canSendMessage: false);
        }
    }
}
