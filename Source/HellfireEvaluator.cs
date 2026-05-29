using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace PawnChronicles
{
    public static class HellfireEvaluator
    {
        public const float MinProfileWeight = 180f;
        public const int   MinChainLinks    = 2;
        public const int   MaxChainLinks    = 4;
        public const int   LinkCooldownTicks = 600000;

        public static bool IsEligible(Pawn pawn, CompPersonalChronicles comp)
        {
            if (pawn == null || comp == null) return false;
            if (comp.hasActiveEpic) return false;
            if (pawn.Dead || pawn.Downed || !pawn.Spawned) return false;
            if (pawn.MentalStateDef != null) return false;

            bool hasInferno = comp.CompletedEpics?.Any(e => 
                e.modus == EpicModus.Inferno || e.modus == EpicModus.Hellfire) ?? false;

            if (!hasInferno) return false;

            if (Find.TickManager.TicksGame < 730 * 60000) return false;

            var profile = comp.GetOrBuildProfile();
            return profile.TotalTagWeight() >= MinProfileWeight;
        }

        public static HellfireChainState BeginChain(Pawn pawn, PersonalEpicDef firstEpic)
        {
            int linkCount = Rand.RangeInclusive(MinChainLinks, MaxChainLinks);

            var state = new HellfireChainState
            {
                linkCount        = linkCount,
                currentLink      = 0,
                chainTitle       = GenerateChainTitle(pawn),
                startTick        = Find.TickManager.TicksGame,
                linkEpicDefNames = new List<string> { firstEpic.defName }
            };

            Messages.Message(
                $"A Hellfire has ignited around {pawn.LabelShort}. " +
                $"Nothing will be the same. ({linkCount} trials ahead)",
                pawn, MessageTypeDefOf.ThreatBig);

            return state;
        }

        public static void OnLinkCompleted(
            Pawn pawn,
            CompPersonalChronicles comp,
            HellfireChainState chain,
            bool success)
        {
            if (pawn == null || comp == null || chain == null) return;

            chain.currentLink++;

            if (!success)
            {
                TerminateChain(pawn, comp, chain, false);
                return;
            }

            if (chain.currentLink >= chain.linkCount)
            {
                TerminateChain(pawn, comp, chain, true);
                return;
            }

            comp.HellfireChain = chain;
            comp.HellfireLinkCooldownTicks = LinkCooldownTicks;

            Messages.Message(
                $"{pawn.LabelShort} endures. " +
                $"Link {chain.currentLink}/{chain.linkCount} of the Hellfire begins after a rest.",
                pawn, MessageTypeDefOf.NeutralEvent);
        }

        public static PersonalEpicDef? SelectNextLinkEpic(
            Pawn pawn,
            CompPersonalChronicles comp,
            HellfireChainState chain)
        {
            var pool = DefDatabase<PersonalEpicDef>.AllDefsListForReading
                .Where(e => (e.modus == EpicModus.Fire || e.modus == EpicModus.Inferno) &&
                            !chain.linkEpicDefNames.Contains(e.defName))
                .ToList();

            if (pool.Count == 0)
                pool = DefDatabase<PersonalEpicDef>.AllDefsListForReading
                    .Where(e => e.modus == EpicModus.Fire || e.modus == EpicModus.Inferno)
                    .ToList();

            return pool.RandomElementByWeight(e => e.generationWeight);
        }

        private static void TerminateChain(
            Pawn pawn,
            CompPersonalChronicles comp,
            HellfireChainState chain,
            bool success)
        {
            comp.HellfireChain = null;
            comp.HellfireLinkCooldownTicks = 0;

            ApplyPermanentWorldMark(pawn, chain, success);
            WriteChronicleEntry(pawn, comp, chain, success);

            string outcome = success
                ? $"{pawn.LabelShort}'s Hellfire is complete. The world has changed."
                : $"{pawn.LabelShort}'s Hellfire was broken. The scars run deep.";

            Messages.Message(outcome, pawn,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent);
        }

        private static void ApplyPermanentWorldMark(
            Pawn pawn, HellfireChainState chain, bool success)
        {
            if (success)
            {
                if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile)) return;

                var faction = Find.FactionManager.RandomNonHostileFaction(
                    allowHidden: false, minTechLevel: TechLevel.Industrial);

                if (faction != null)
                {
                    var sitePartDef = DefDatabase<SitePartDef>.GetNamedSilentFail("Outpost")
                                   ?? DefDatabase<SitePartDef>.AllDefsListForReading.FirstOrDefault();

                    if (sitePartDef == null) return;

                    Site site = SiteMaker.MakeSite(sitePartDef, tile, faction);
                    site.customLabel = $"{pawn.LabelShort}'s Legacy";
                    Find.WorldObjects.Add(site);
                    faction.TryAffectGoodwillWith(Faction.OfPlayer, 30, canSendMessage: true);
                }
            }
            else
            {
                if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile)) return;

                var enemyFaction = Find.FactionManager.RandomEnemyFaction();
                if (enemyFaction == null) return;

                var sitePartDef = DefDatabase<SitePartDef>.GetNamedSilentFail("Outpost")
                               ?? DefDatabase<SitePartDef>.AllDefsListForReading.FirstOrDefault();

                if (sitePartDef == null) return;

                Site threat = SiteMaker.MakeSite(sitePartDef, tile, enemyFaction);
                threat.customLabel = $"The Wound of {pawn.LabelShort}";
                Find.WorldObjects.Add(threat);
            }
        }

        private static void WriteChronicleEntry(
            Pawn pawn,
            CompPersonalChronicles comp,
            HellfireChainState chain,
            bool success)
        {
            string entry = success
                ? $"[{chain.chainTitle}] Survived {chain.linkCount} trials of the Hellfire."
                : $"[{chain.chainTitle}] Broken after {chain.currentLink} of {chain.linkCount} trials.";

            comp.AddChronicleEntry(entry);
        }

        private static string GenerateChainTitle(Pawn pawn)
        {
            var profile = pawn.GetNarrativeProfile();
            string? tag = profile.DominantTag();

            return tag switch
            {
                "PC_Tag_Trauma"     => $"The Trial of {pawn.LabelShort}",
                "PC_Tag_Violence"   => $"The War of {pawn.LabelShort}",
                "PC_Tag_Loss"       => $"The Grief of {pawn.LabelShort}",
                "PC_Tag_Underworld" => $"The Shadow of {pawn.LabelShort}",
                "PC_Tag_Devotion"   => $"The Faith of {pawn.LabelShort}",
                "PC_Tag_Nurture"    => $"The Heart of {pawn.LabelShort}",
                _                   => $"The Legend of {pawn.LabelShort}"
            };
        }
    }

    public class HellfireChainState : IExposable
    {
        public int    linkCount        = 0;
        public int    currentLink      = 0;
        public string chainTitle       = "";
        public int    startTick        = 0;
        public List<string> linkEpicDefNames = new List<string>();

        public HellfireChainState() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref linkCount,    "linkCount",    0);
            Scribe_Values.Look(ref currentLink,  "currentLink",  0);
            Scribe_Values.Look(ref chainTitle,   "chainTitle",   "");
            Scribe_Values.Look(ref startTick,    "startTick",    0);
            Scribe_Collections.Look(ref linkEpicDefNames, "linkEpicDefNames", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                linkEpicDefNames ??= new List<string>();
        }
    }
}