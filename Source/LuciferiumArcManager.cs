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
        private const string SitePartDefName = "OpportunitySite_AncientGarrison";

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
    }
}
