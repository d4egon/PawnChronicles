using System.Collections.Generic;
using Verse;
using Verse.Grammar;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;

namespace PawnChronicles
{
    public class QuestNode_EpicStage : QuestNode
    {
        public SlateRef<string> signal;
        public SlateRef<string> stageRole;
        public SlateRef<LetterDef> letterDef;

        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var slate = QuestGen.slate;
            var quest = QuestGen.quest;

            if (!slate.TryGet("pawn", out Pawn pawn) || pawn == null)
            {
                Log.Warning("[PawnChronicles] QuestNode_EpicStage: no pawn in slate.");
                SetFallbackQuestText("An Untold Story", "Something stirred.");
                return;
            }

            var profile = pawn.GetNarrativeProfile();

            string inSignal = slate.Get<string>("inSignal");
            string outSignal = signal.GetValue(slate) ?? "PawnEpic_StageComplete";

            slate.TryGet("currentQuestStageDef", out QuestStageDef currentStageDef);

            Map map = QuestGen_Get.GetMap();

            DynamicQuestBuilder.BuildStage(
                quest, pawn, map, inSignal, outSignal, currentStageDef, profile);
        }

        private static void SetFallbackQuestText(string name, string description)
        {
            QuestGen.slate.Set("resolvedQuestName",        name);
            QuestGen.slate.Set("resolvedQuestDescription", description);
            QuestGen.AddQuestNameRules(new List<Rule>
                { new Rule_String("questName", name) });
            QuestGen.AddQuestDescriptionRules(new List<Rule>
                { new Rule_String("questDescription", description) });
        }
    }
}