using System.Collections.Generic;
using Verse;
using Verse.Grammar;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;

namespace PawnChronicles
{
    // =========================================================================
    //  EXPEDITION QUEST NODES  (Stage 7 - AncientWarehouse contact site)
    // =========================================================================

    /// <summary>
    /// Sets quest name/description and sends the opening letter for the expedition.
    /// Uses direct factual text - the arc chronicle carries the narrative prose.
    /// </summary>
    public class QuestNode_LuciferiumExpeditionNarrative : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var slate = QuestGen.slate;
            var quest = QuestGen.quest;

            slate.TryGet("pawn", out Pawn pawn);
            string pawnName = pawn?.LabelShort ?? "Unknown";

            string title = "PC_LucQuest_Expedition_Label".Translate(pawnName);
            string body  = "PC_LucQuest_Expedition_Desc".Translate(pawnName);

            quest.name        = title;
            quest.description = body;
            QuestGen.AddQuestNameRules(new List<Rule> { new Rule_String("questName", title) });
            QuestGen.AddQuestDescriptionRules(new List<Rule> { new Rule_String("questDescription", body) });

            string inSignal = slate.Get<string>("inSignal");
            quest.Letter(LetterDefOf.NeutralEvent, inSignal, text: body, label: title);
        }
    }

    /// <summary>
    /// Runs after QuestNode_Root_Site for the expedition quest.
    /// Stores $site.Tile in comp.lucifExpeditionTile for the arc's expedition_cleared condition.
    /// </summary>
    public class QuestNode_LuciferiumExpeditionRegisterSite : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var slate = QuestGen.slate;
            if (!slate.TryGet("pawn", out Pawn pawn) || pawn == null) return;
            if (!slate.TryGet("site", out WorldObject site) || site == null) return;

            var comp = pawn.GetComp<CompPersonalChronicles>();
            if (comp == null) return;

            comp.lucifExpeditionTile = site.Tile;
            Log.Message($"[PawnChronicles] Expedition site registered at tile {site.Tile} for {pawn.LabelShort}.");
        }
    }

    /// <summary>
    /// Reads $broker and $site from the slate (set by SitePartWorker_LuciferiumMeeting
    /// during QuestNode_Root_Site generation) and wires the exchange mechanic:
    ///   - QuestPart_BegForItems: broker waits for 75 silver
    ///   - On payment: 1 luciferium drops at colony, broker leaves, quest succeeds
    ///   - On 2-day timeout: broker leaves with no reward
    /// Broker spawning itself is handled by GenStep_LuciferiumMeeting.
    /// </summary>
    public class QuestNode_LuciferiumExpeditionSetupBroker : QuestNode
    {
        private const int SilverCost     = 75;
        private const int BrokerWaitDays = 2;

        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var slate = QuestGen.slate;
            var quest  = QuestGen.quest;

            // $broker set by SitePartWorker_LuciferiumMeeting.Notify_GeneratedByQuestGen
            if (!slate.TryGet("broker", out Pawn broker) || broker == null)
            {
                Log.Warning("[PawnChronicles] LuciferiumExpeditionSetupBroker: no $broker in slate.");
                return;
            }
            if (!slate.TryGet("site", out WorldObject site) || !(site is MapParent siteMapParent))
            {
                Log.Warning("[PawnChronicles] LuciferiumExpeditionSetupBroker: no $site in slate.");
                return;
            }

            var colonyMap = QuestGen_Get.GetMap();

            // Quest tag so signals track this pawn
            string brokerQuestTag = QuestGenUtility.HardcodedTargetQuestTagWithQuestID("broker");
            QuestUtility.AddQuestTag(ref broker.questTags, brokerQuestTag);

            string mapGeneratedSignal  = QuestGenUtility.HardcodedSignalWithQuestID("site.MapGenerated");
            string itemsReceivedSignal = QuestGen.GenerateNewSignal("ItemsReceived");

            // Exchange mechanic: broker waits for 75 silver at the site
            var begPart = new QuestPart_BegForItems
            {
                inSignal               = mapGeneratedSignal,
                outSignalItemsReceived = itemsReceivedSignal,
                target                 = broker,
                faction                = broker.Faction,
                mapParent              = siteMapParent,
                thingDef               = ThingDefOf.Silver,
                amount                 = SilverCost
            };
            begPart.pawns.Add(broker);
            quest.AddPart(begPart);

            // On payment: deliver from PC_Luciferium_QuestLoot pool
            if (colonyMap != null)
            {
                var rewardMaker = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail("PC_Luciferium_QuestLoot");
                if (rewardMaker != null)
                {
                    var rewardParms = new ThingSetMakerParams
                    {
                        totalMarketValueRange = new FloatRange(1500f, 3000f)
                    };
                    var rewardThings = rewardMaker.root.Generate(rewardParms);
                    if (rewardThings != null && rewardThings.Count > 0)
                        quest.DropPods(colonyMap.Parent, rewardThings, inSignal: itemsReceivedSignal);
                }

                quest.Letter(LetterDefOf.PositiveEvent, itemsReceivedSignal,
                    text:  "PC_Luciferium_ContactPaid_Desc".Translate(),
                    label: "PC_Luciferium_ContactPaid_Label".Translate());
            }

            // Broker leaves immediately when paid
            QuestGenUtility.RunInner(() =>
            {
                quest.Leave((IEnumerable<Pawn>) new List<Pawn> { broker },
                    sendStandardLetter: false,
                    leaveOnCleanup: false);
            }, itemsReceivedSignal);

            // Quest ends on payment
            quest.End(QuestEndOutcome.Success,
                inSignal: itemsReceivedSignal,
                sendStandardLetter: false);

            // Timeout: broker leaves after 2 days if unpaid
            QuestGenUtility.RunInner(() =>
            {
                quest.Delay(GenDate.TicksPerDay * BrokerWaitDays, () =>
                {
                    quest.Leave((IEnumerable<Pawn>) new List<Pawn> { broker },
                        sendStandardLetter: false,
                        leaveOnCleanup: false);
                }, inSignalDisable: itemsReceivedSignal);
            }, mapGeneratedSignal);

            // Broker killed: end quest cleanly.
            // Arc still advances when the player returns home - expedition_cleared
            // only requires pawnDeparted + onHomeMap, not quest success.
            string brokerKilledSignal = QuestGenUtility.HardcodedSignalWithQuestID("broker.Killed");
            quest.Letter(LetterDefOf.NegativeEvent, brokerKilledSignal,
                label: "PC_Luciferium_BrokerDead_Label".Translate(),
                text:  "PC_Luciferium_BrokerDead_Desc".Translate());
            quest.End(QuestEndOutcome.Fail,
                inSignal: brokerKilledSignal,
                sendStandardLetter: false);

            // Broker arrested: end quest + cartel turns hostile.
            // Arresting their contact is a deliberate act. They will respond.
            string brokerArrestedSignal = QuestGenUtility.HardcodedSignalWithQuestID("broker.Arrested");
            quest.Letter(LetterDefOf.NegativeEvent, brokerArrestedSignal,
                label: "PC_Luciferium_BrokerArrested_Label".Translate(),
                text:  "PC_Luciferium_BrokerArrested_Desc".Translate());
            quest.AddPart(new QuestPart_FactionRelationChange
            {
                faction                = broker.Faction,
                relationKind           = FactionRelationKind.Hostile,
                canSendHostilityLetter = false,
                inSignal               = brokerArrestedSignal
            });
            quest.End(QuestEndOutcome.Fail,
                inSignal: brokerArrestedSignal,
                sendStandardLetter: false);
        }
    }

    // =========================================================================
    //  DELVING QUEST NODES  (Stage 8 - AncientGarrison garrison raid)
    // =========================================================================

    /// <summary>
    /// Sets quest name/description and sends the opening letter for the delving stage.
    /// Uses direct factual text - the arc chronicle carries the narrative prose.
    /// </summary>
    public class QuestNode_LuciferiumDelvingNarrative : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var slate = QuestGen.slate;
            var quest = QuestGen.quest;

            slate.TryGet("pawn", out Pawn pawn);
            string pawnName = pawn?.LabelShort ?? "Unknown";

            string title = "PC_LucQuest_Delving_Label".Translate(pawnName);
            string body  = "PC_LucQuest_Delving_Desc".Translate(pawnName);

            quest.name        = title;
            quest.description = body;
            QuestGen.AddQuestNameRules(new List<Rule> { new Rule_String("questName", title) });
            QuestGen.AddQuestDescriptionRules(new List<Rule> { new Rule_String("questDescription", body) });

            string inSignal = slate.Get<string>("inSignal");
            quest.Letter(LetterDefOf.NeutralEvent, inSignal, text: body, label: title);
        }
    }

    /// <summary>
    /// Runs after QuestNode_Root_Site for the delving quest.
    /// Stores $site.Tile in comp.lucifDelvingTile for the arc's delving_cleared condition.
    /// </summary>
    public class QuestNode_LuciferiumDelvingRegisterSite : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var slate = QuestGen.slate;
            if (!slate.TryGet("pawn", out Pawn pawn) || pawn == null) return;
            if (!slate.TryGet("site", out WorldObject site) || site == null) return;

            var comp = pawn.GetComp<CompPersonalChronicles>();
            if (comp == null) return;

            comp.lucifDelvingTile = site.Tile;
            Log.Message($"[PawnChronicles] Delving site registered at tile {site.Tile} for {pawn.LabelShort}.");
        }
    }

    /// <summary>
    /// Delivers a small supply reward when the delving site is cleared.
    /// The pawn raided a pre-collapse garrison - they come back with something useful.
    /// </summary>
    public class QuestNode_LuciferiumDelvingDeliver : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var slate = QuestGen.slate;
            slate.TryGet("pawn", out Pawn pawn);

            var map = pawn?.MapHeld ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            var rewardMaker = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail("PC_Reward_Delving");
            if (rewardMaker == null)
            {
                Log.Warning("[PawnChronicles] QuestNode_LuciferiumDelvingDeliver: PC_Reward_Delving not found.");
                return;
            }

            var rewardParms = new ThingSetMakerParams
            {
                totalMarketValueRange = new FloatRange(800f, 2000f)
            };
            var things = rewardMaker.root.Generate(rewardParms);
            if (things == null || things.Count == 0) return;

            var dropSpot = DropCellFinder.TradeDropSpot(map);
            DropPodUtility.DropThingsNear(dropSpot, map, things,
                openDelay: 110, leaveSlag: false,
                canInstaDropDuringInit: false);

            Find.LetterStack.ReceiveLetter(
                "PC_Luciferium_DelvingComplete_Label".Translate(),
                "PC_Luciferium_DelvingComplete_Desc".Translate(pawn?.LabelShort ?? "Unknown"),
                LetterDefOf.PositiveEvent,
                new LookTargets(map.Parent));
        }
    }
}
