using System.Collections.Generic;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace PawnChronicles
{
    /// <summary>
    /// Handles the luciferium arc's world-site mechanics.
    ///
    /// SpawnSite generates an OpportunitySite_AncientGarrison on the world map
    /// near the colony when the quest stage fires (PC_LucStage_Quest).
    /// The pawn must clear it to advance. site_cleared wait condition watches
    /// for the world object at that tile to disappear.
    ///
    /// MechSerumHealer (tagged RewardStandardCore) appears naturally in ancient
    /// garrison loot. The narrative guarantees it; the loot system delivers it.
    ///
    /// Auto-fail: if LuciferiumAddiction reaches stage index 1 (berserk/death)
    /// before the site is cleared, CompPersonalChronicles.TickWaitCondition
    /// sets PendingSignal = "PawnEpic_Failure".
    /// </summary>
    public static class LuciferiumArcManager
    {
        private const string SitePartDefName            = "OpportunitySite_AncientGarrison";
        private const string ExpeditionSitePartDefName  = "OpportunitySite_AncientWarehouse";

        public static void SpawnSite(Pawn pawn, CompPersonalChronicles comp)
        {
            var partDef = DefDatabase<SitePartDef>.GetNamedSilentFail(SitePartDefName);
            if (partDef == null)
            {
                Log.Error($"[PawnChronicles] LuciferiumArcManager: SitePartDef '{SitePartDefName}' not found.");
                return;
            }

            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile))
            {
                Log.Warning($"[PawnChronicles] LuciferiumArcManager: no valid tile found for luciferium site near {pawn.LabelShort}.");
                return;
            }

            // MakeSite with null faction - AncientGarrison is factionless ancient ruins.
            // GenerateDefaultParams handles null faction fine.
            var site = SiteMaker.MakeSite(
                new SitePartDef[] { partDef },
                tile,
                faction: null,
                ifHostileThenMustRemainHostile: false);

            if (site == null)
            {
                Log.Warning($"[PawnChronicles] LuciferiumArcManager: SiteMaker returned null for {pawn.LabelShort}.");
                return;
            }

            Find.WorldObjects.Add(site);
            comp.lucifSiteTile = tile;

            Log.Message($"[PawnChronicles] Luciferium quest site spawned at tile {tile} for {pawn.LabelShort}.");

            Find.LetterStack.ReceiveLetter(
                "PC_Luciferium_SiteFound_Label".Translate(),
                "PC_Luciferium_SiteFound_Desc".Translate(pawn.LabelShort),
                LetterDefOf.NeutralEvent,
                new GlobalTargetInfo(tile));
        }

        /// <summary>
        /// Called when site_cleared fires and the arc advances past the quest stage.
        /// Delivers a HealerMechSerum to the pawn's home map and closes the facility quest.
        /// </summary>
        public static void OnFacilityCleared(Pawn pawn, CompPersonalChronicles comp)
        {
            DeliverSerum(pawn);

            // Close the facility quest if it is still open
            if (comp.activeQuestId >= 0)
            {
                var q = Find.QuestManager.QuestsListForReading
                    .Find(x => x.id == comp.activeQuestId);
                if (q != null && !q.Historical)
                    q.End(QuestEndOutcome.Success, sendLetter: false);
            }
        }

        private static void DeliverSerum(Pawn pawn)
        {
            var map = pawn?.MapHeld ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            var serumDef = DefDatabase<ThingDef>.GetNamedSilentFail("HealerMechSerum");
            if (serumDef == null)
            {
                Log.Warning("[PawnChronicles] LuciferiumArcManager.DeliverSerum: HealerMechSerum not found.");
                return;
            }

            var serum    = ThingMaker.MakeThing(serumDef);
            var dropSpot = DropCellFinder.TradeDropSpot(map);

            DropPodUtility.DropThingsNear(dropSpot, map,
                new List<Thing> { serum },
                openDelay: 110,
                leaveSlag: false,
                canInstaDropDuringInit: false);

            Find.LetterStack.ReceiveLetter(
                "PC_Luciferium_SerumDelivered_Label".Translate(),
                "PC_Luciferium_SerumDelivered_Desc".Translate(pawn?.LabelShort ?? "Unknown"),
                LetterDefOf.PositiveEvent,
                new LookTargets(map.Parent));
        }

        /// <summary>
        /// Spawns the expedition site - an ancient warehouse used by the luciferium contact,
        /// roughly 15 tiles from the colony. The pawn must travel there and return home before
        /// the arc advances.
        /// </summary>
        public static void SpawnExpeditionSite(Pawn pawn, CompPersonalChronicles comp)
        {
            var partDef = DefDatabase<SitePartDef>.GetNamedSilentFail(ExpeditionSitePartDefName);
            if (partDef == null)
            {
                Log.Error($"[PawnChronicles] LuciferiumArcManager: SitePartDef '{ExpeditionSitePartDefName}' not found.");
                return;
            }

            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile))
            {
                Log.Warning($"[PawnChronicles] LuciferiumArcManager: no valid expedition tile found for {pawn.LabelShort}.");
                return;
            }

            // Use a hostile faction (pirates/outlanders) to populate the warehouse.
            // The contact is inside - clearing the site IS the meeting.
            var hostileFaction = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.defeated && f.HostileTo(Faction.OfPlayer)
                         && f.def.techLevel >= TechLevel.Industrial)
                .RandomElementWithFallback();

            var site = SiteMaker.MakeSite(
                new SitePartDef[] { partDef },
                tile,
                faction: hostileFaction,
                ifHostileThenMustRemainHostile: false);

            if (site == null)
            {
                Log.Warning($"[PawnChronicles] LuciferiumArcManager: SiteMaker returned null for expedition site ({pawn.LabelShort}).");
                return;
            }

            Find.WorldObjects.Add(site);
            comp.lucifExpeditionTile = tile;

            Log.Message($"[PawnChronicles] Luciferium expedition site spawned at tile {tile} for {pawn.LabelShort}.");

            Find.LetterStack.ReceiveLetter(
                "PC_Luciferium_ExpeditionSite_Label".Translate(),
                "PC_Luciferium_ExpeditionSite_Desc".Translate(pawn.LabelShort),
                LetterDefOf.NeutralEvent,
                new GlobalTargetInfo(tile));
        }
    }
}
