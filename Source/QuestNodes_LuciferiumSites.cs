using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Grammar;
using Verse.AI.Group;
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
        private const int SilverCost     = 300;
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

            // On payment: deliver 1-2 luciferium - the whole point of the deal.
            if (colonyMap != null)
            {
                var lucDef = DefDatabase<ThingDef>.GetNamedSilentFail("Luciferium");
                if (lucDef != null)
                {
                    var luc = ThingMaker.MakeThing(lucDef);
                    luc.stackCount = Rand.RangeInclusive(1, 2);
                    quest.DropPods(colonyMap.Parent, new List<Thing> { luc }, inSignal: itemsReceivedSignal);
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
    //  SHARED STASH ROOM BUILDER
    //  5x5 BlocksSlate room, MorbidSlab_Medium interior, south door, NE torch.
    //  Used by both CartelFacility (serum) and LuciferiumStash (pills).
    // =========================================================================

    internal static class StashRoomBuilder
    {
        private const string WallDefName   = "Wall";
        private const string WallStuffName = "BlocksSlate";
        private const string DoorDefName   = "Door";
        private const string TorchDefName  = "Darktorch";
        private const string FloorDefName  = "MorbidSlab_Medium";
        private const int    Extent        = 2;   // 5x5 total, 3x3 interior

        /// <summary>
        /// Builds the room centred on map.Center and places <paramref name="item"/>
        /// at the exact centre cell.
        /// </summary>
        public static void BuildAndPlace(Map map, Thing item, string logTag)
        {
            IntVec3 c = map.Center;

            var wallDef   = DefDatabase<ThingDef>.GetNamedSilentFail(WallDefName);
            var wallStuff = DefDatabase<ThingDef>.GetNamedSilentFail(WallStuffName);
            var doorDef   = DefDatabase<ThingDef>.GetNamedSilentFail(DoorDefName);
            var torchDef  = DefDatabase<ThingDef>.GetNamedSilentFail(TorchDefName);
            var floorTerr = DefDatabase<TerrainDef>.GetNamedSilentFail(FloorDefName);

            for (int dx = -Extent; dx <= Extent; dx++)
            {
                for (int dz = -Extent; dz <= Extent; dz++)
                {
                    IntVec3 cell = c + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map)) continue;

                    bool isWall = System.Math.Abs(dx) == Extent || System.Math.Abs(dz) == Extent;
                    bool isDoor = dz == -Extent && dx == 0;

                    if (!isWall && floorTerr != null)
                        map.terrainGrid.SetTerrain(cell, floorTerr);

                    if (isWall)
                    {
                        cell.GetThingList(map).ToList()
                            .ForEach(t => { if (!(t is Pawn)) t.Destroy(); });

                        if (isDoor)
                        {
                            if (doorDef != null)
                            {
                                var door = doorDef.MadeFromStuff && wallStuff != null
                                    ? ThingMaker.MakeThing(doorDef, wallStuff)
                                    : ThingMaker.MakeThing(doorDef);
                                GenSpawn.Spawn(door, cell, map, Rot4.East);
                            }
                        }
                        else
                        {
                            if (wallDef != null)
                            {
                                var wall = wallStuff != null
                                    ? ThingMaker.MakeThing(wallDef, wallStuff)
                                    : ThingMaker.MakeThing(wallDef);
                                GenSpawn.Spawn(wall, cell, map);
                            }
                        }
                    }

                    if (!isDoor)
                        map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                }
            }

            // Darktorch directly north of centre
            IntVec3 torchCell = c + new IntVec3(0, 0, 1);
            if (torchCell.InBounds(map) && torchDef != null)
                GenSpawn.Spawn(ThingMaker.MakeThing(torchDef), torchCell, map);

            // Item at dead centre
            GenSpawn.Spawn(item, c, map);
            Log.Message($"[PawnChronicles] {logTag} placed at map centre.");
        }
    }

    // =========================================================================
    //  CARTEL FACILITY SITE PART WORKER  (Stage 12)
    //  Builds a stash room at map centre containing the HealerMechSerum, then
    //  spawns 10-20 cartel combat pawns. The cartel turns hostile on arrival
    //  via a QuestPart_FactionRelationChange in the quest XML.
    // =========================================================================

    public class SitePartWorker_CartelFacility : SitePartWorker_AncientComplex
    {
        private const string CartelFactionDef  = "PC_Faction_LucifersCartelHostile";
        private const string SerumDefName      = "PC_Item_FakeHealerSerum";
        private const int    MinCartelPawns    = 20;
        private const int    MaxCartelPawns    = 30;

        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);

            PlaceSerum(map);
            SpawnCartelDefenders(map);
        }

        private static void PlaceSerum(Map map)
        {
            var serumDef = DefDatabase<ThingDef>.GetNamedSilentFail(SerumDefName);
            if (serumDef == null)
            {
                Log.Warning("[PawnChronicles] SitePartWorker_CartelFacility: MechSerumHealer not found.");
                return;
            }

            StashRoomBuilder.BuildAndPlace(map, ThingMaker.MakeThing(serumDef), "Cartel serum");
        }

        private static void SpawnCartelDefenders(Map map)
        {
            var cartelFaction = Find.FactionManager.AllFactions
                .FirstOrDefault(f => f.def.defName == CartelFactionDef);

            if (cartelFaction == null)
            {
                Log.Warning("[PawnChronicles] SitePartWorker_CartelFacility: cartel faction not found, skipping defender spawn.");
                return;
            }

            int targetCount = Rand.RangeInclusive(MinCartelPawns, MaxCartelPawns);

            // Floor at 3000 to guarantee 20+ pawns regardless of story progress.
            // Cartel pawn avg combat power ~105, so 3000 pts -> ~28 pawns.
            float points = StorytellerUtility.DefaultSiteThreatPointsNow() * Rand.Range(1.2f, 1.8f);
            points = Math.Max(points, 3000f);

            var groupParms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                tile      = map.Tile,
                faction   = cartelFaction,
                points    = points
            };

            var defenders = PawnGroupMakerUtility.GeneratePawns(groupParms, warnOnZeroResults: true).ToList();

            // Cap to our target range - GeneratePawns can return more than expected.
            if (defenders.Count > MaxCartelPawns)
                defenders = defenders.Take(MaxCartelPawns).ToList();

            // Give all defenders a defend-base lord so they hold position and engage intruders.
            var lord = LordMaker.MakeNewLord(
                cartelFaction,
                new LordJob_DefendBase(cartelFaction, map.Center, delayBeforeAssault: 0),
                map);

            foreach (var pawn in defenders)
            {
                IntVec3 spawnCell;
                if (!CellFinder.TryFindRandomCellNear(CellFinder.RandomNotEdgeCell(8, map), map, 15,
                        c => c.Standable(map) && !c.Fogged(map), out spawnCell))
                    spawnCell = CellFinder.RandomNotEdgeCell(8, map);

                GenSpawn.Spawn(pawn, spawnCell, map);
                lord.AddPawn(pawn);
            }

            Log.Message($"[PawnChronicles] Cartel facility: spawned {defenders.Count} defenders.");
        }
    }

    // =========================================================================
    //  LUCIFERIUM STASH SITE PART WORKER
    //  Places 3-5 luciferium pills on the ground when the warehouse map generates.
    // =========================================================================

    public class SitePartWorker_LuciferiumStash : SitePartWorker
    {
        private const int MinCount = 3;
        private const int MaxCount = 5;

        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);

            var lucDef = DefDatabase<ThingDef>.GetNamedSilentFail("Luciferium");
            if (lucDef == null)
            {
                Log.Warning("[PawnChronicles] SitePartWorker_LuciferiumStash: Luciferium ThingDef not found.");
                return;
            }

            var luc = ThingMaker.MakeThing(lucDef);
            luc.stackCount = Rand.RangeInclusive(MinCount, MaxCount);

            StashRoomBuilder.BuildAndPlace(map, luc, $"Luciferium stash ({luc.stackCount})");
        }
    }

    // =========================================================================
    //  DELVING QUEST NODES  (Stage 8 - AncientWarehouse luciferium stash)
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
