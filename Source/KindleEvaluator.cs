using System.Collections.Generic;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    public static class KindleEvaluator
    {
        public const int StageCount            = 3;
        public const int MinStageCooldownTicks = 180000;
        public const int MaxIgnoredTicks       = 600000;

        public static bool IsEligible(Pawn pawn, CompPersonalChronicles comp)
        {
            if (pawn == null || comp == null) return false;
            if (comp.hasActiveEpic) return false;
            if (pawn.Dead || !pawn.Spawned) return false;

            var profile = comp.GetOrBuildProfile();
            return profile.TotalTagWeight() >= 20f;
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

        public static void ApplyOutcome(Pawn pawn, bool success)
        {
            if (pawn?.needs?.mood?.thoughts?.memories == null) return;

            string defName = success ? "PC_Thought_KindleMoment" : "PC_Thought_KindleSetback";
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            if (def != null)
                pawn.needs.mood.thoughts.memories.TryGainMemory(def);

            string verb = success ? "found clarity" : "stumbled";
            Messages.Message(
                $"{pawn.LabelShort} has {verb}.",
                pawn,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent);
        }
    }
}
