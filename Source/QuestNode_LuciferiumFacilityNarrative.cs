using System.Collections.Generic;
using Verse;
using Verse.Grammar;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;

namespace PawnChronicles
{
    /// <summary>
    /// Runs before QuestNode_Root_Site in PC_Quest_LuciferiumFacility.
    /// Sets quest name/description from the narrative grammar and sends the opening letter.
    /// </summary>
    public class QuestNode_LuciferiumFacilityNarrative : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var slate = QuestGen.slate;
            var quest = QuestGen.quest;

            slate.TryGet("pawn", out Pawn pawn);
            string pawnName = pawn?.LabelShort ?? "Unknown";

            string title = "PC_LucQuest_Facility_Label".Translate(pawnName);
            string body  = "PC_LucQuest_Facility_Desc".Translate(pawnName);

            quest.name        = title;
            quest.description = body;
            QuestGen.AddQuestNameRules(new List<Rule> { new Rule_String("questName", title) });
            QuestGen.AddQuestDescriptionRules(new List<Rule> { new Rule_String("questDescription", body) });

            string inSignal = slate.Get<string>("inSignal");
            quest.Letter(LetterDefOf.NeutralEvent, inSignal, text: body, label: title);
        }
    }

    /// <summary>
    /// Runs AFTER QuestNode_Root_Site in PC_Quest_LuciferiumFacility.
    /// Reads $site from the slate and stores its tile in comp.lucifSiteTile so the
    /// arc's site_cleared wait condition can track it.
    /// </summary>
    public class QuestNode_LuciferiumFacilityRegisterSite : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var slate = QuestGen.slate;

            if (!slate.TryGet("pawn", out Pawn pawn) || pawn == null) return;
            if (!slate.TryGet("site", out WorldObject site) || site == null) return;

            var comp = pawn.GetComp<CompPersonalChronicles>();
            if (comp == null) return;

            comp.lucifSiteTile = site.Tile;
            Log.Message($"[PawnChronicles] LuciferiumFacility: site registered at tile {site.Tile} for {pawn.LabelShort}.");
        }
    }

    /// <summary>
    /// Delivers a HealerMechSerum drop pod to the colony when the facility is cleared.
    /// Runs inside QuestNode_NoWorldObject's success node.
    /// </summary>
    public class QuestNode_LuciferiumFacilityDeliver : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var slate = QuestGen.slate;
            slate.TryGet("pawn", out Pawn pawn);

            var map = pawn?.MapHeld ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            var rewardMaker = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail("PC_Luciferium_QuestLoot");
            if (rewardMaker == null)
            {
                Log.Warning("[PawnChronicles] QuestNode_LuciferiumFacilityDeliver: PC_Luciferium_QuestLoot not found.");
                return;
            }

            var rewardParms = new ThingSetMakerParams
            {
                totalMarketValueRange = new FloatRange(1500f, 3000f)
            };
            var things   = rewardMaker.root.Generate(rewardParms);
            if (things == null || things.Count == 0) return;

            var dropSpot = DropCellFinder.TradeDropSpot(map);
            DropPodUtility.DropThingsNear(dropSpot, map, things,
                openDelay: 110,
                leaveSlag: false,
                canInstaDropDuringInit: false);

            Find.LetterStack.ReceiveLetter(
                "PC_Luciferium_SerumDelivered_Label".Translate(),
                "PC_Luciferium_SerumDelivered_Desc".Translate(pawn?.LabelShort ?? "Unknown"),
                LetterDefOf.PositiveEvent,
                new LookTargets(map.Parent));
        }
    }
}
