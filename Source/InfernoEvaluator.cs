using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace PawnChronicles
{
    public static class InfernoEvaluator
    {
        public const int   StageCount            = 5;
        public const int   MinStageCooldownTicks = 360000;
        public const int   MaxIgnoredTicks       = 2400000;
        public const float MinProfileWeight      = 120f;

        private const int FactionRelationDeltaSuccess = 20;
        private const int FactionRelationDeltaFailure = -25;

        public static bool IsEligible(Pawn pawn, CompPersonalChronicles comp)
        {
            if (pawn == null || comp == null) return false;
            if (comp.hasActiveEpic) return false;
            if (pawn.Dead || !pawn.Spawned) return false;

            bool hasCompletedFire = comp.CompletedEpics
                .Any(e => e.modus == EpicModus.Fire || e.modus == EpicModus.Inferno);
            if (!hasCompletedFire) return false;

            var profile = comp.GetOrBuildProfile();
            return profile.TotalTagWeight() >= MinProfileWeight;
        }

        public static InfernoBranchPath SelectBranch(Pawn pawn)
        {
            var profile = pawn.GetNarrativeProfile();
            string? dominant = profile.DominantTag();

            bool shadowPath = dominant == "PC_Tag_Trauma"    ||
                              dominant == "PC_Tag_Violence"   ||
                              dominant == "PC_Tag_Grief"      ||
                              dominant == "PC_Tag_Underworld";

            return shadowPath ? InfernoBranchPath.Shadow : InfernoBranchPath.Ascent;
        }

        public static QuestStageDef? SelectStage(
            PersonalEpicDef epic,
            PawnNarrativeProfile profile,
            List<QuestStageDef> usedStages,
            bool isClimax,
            bool isOpening,
            int currentStage,
            InfernoBranchPath branch)
        {
            if (currentStage == 2)
            {
                string branchTag = branch == InfernoBranchPath.Shadow
                    ? "PC_Branch_Shadow"
                    : "PC_Branch_Ascent";

                var branchPool = epic.stagePool
                    .Where(s => s.isMiddle &&
                                !usedStages.Contains(s) &&
                                s.HasBranchTag(branchTag))
                    .ToList();

                if (branchPool.Count > 0)
                    return SelectFromPool(branchPool, profile);
            }

            return PremiseEvaluator.SelectNextStage(
                epic, profile, usedStages, isClimax, isOpening);
        }

        /// <summary>
        /// Selects the best-matching stage from a pool by tag score.
        /// Used for branch-filtered pools and anywhere a sub-pool is needed.
        /// </summary>
        public static QuestStageDef? SelectFromPool(
            List<QuestStageDef> pool,
            PawnNarrativeProfile profile)
        {
            if (pool == null || pool.Count == 0) return null;

            return pool
                .OrderByDescending(s =>
                    s.tagRequirements != null && s.tagRequirements.Count > 0
                        ? profile.MatchScore(s.tagRequirements)
                        : 0f)
                .FirstOrDefault();
        }

        public static void ApplyOutcome(Pawn pawn, PersonalEpicDef epic, bool success)
        {
            if (pawn == null) return;

            ApplyThought(pawn, success);
            ApplyWorldConsequence(pawn, success);

            if (!success)
            {
                var profile = pawn.GetNarrativeProfile();
                bool violent = profile.GetScore("PC_Tag_Violence") > 40f;
                var breakDef = violent ? MentalStateDefOf.Berserk : MentalStateDefOf.Wander_Psychotic;
                pawn.mindState.mentalStateHandler.TryStartMentalState(breakDef);
            }

            string verb = success ? "passed through the inferno" : "was devoured by it";
            Messages.Message(
                $"{pawn.LabelShort} {verb}.",
                pawn,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent);
        }

        private static void ApplyThought(Pawn pawn, bool success)
        {
            if (pawn.needs?.mood?.thoughts?.memories == null) return;
            string defName = success
                ? "PC_Thought_InfernoTranscended"
                : "PC_Thought_InfernoDevoured";
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            if (def != null)
                pawn.needs.mood.thoughts.memories.TryGainMemory(def);
        }

        private static void ApplyWorldConsequence(Pawn pawn, bool success)
        {
            var factions = Find.FactionManager.AllFactionsListForReading
                .Where(f => !f.IsPlayer && !f.defeated)
                .ToList();

            if (factions.Count > 0)
            {
                var target = factions.RandomElement();
                int delta = success ? FactionRelationDeltaSuccess : FactionRelationDeltaFailure;
                target.TryAffectGoodwillWith(Faction.OfPlayer, delta, canSendMessage: true);
            }

            if (success) TrySpawnRewardSite(pawn);
            else         TriggerPunishmentEvent(pawn);
        }

        private static void TrySpawnRewardSite(Pawn pawn)
        {
            // 1.6: TileFinder uses PlanetTile
            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile)) return;

            var faction = Find.FactionManager.RandomNonHostileFaction(
                allowHidden: false,
                minTechLevel: TechLevel.Industrial);
            if (faction == null) return;

            // 1.6: SitePartDefOf.Outpost removed - look up dynamically
            var sitePartDef = DefDatabase<SitePartDef>.GetNamedSilentFail("Outpost")
                           ?? DefDatabase<SitePartDef>.AllDefsListForReading.FirstOrDefault();
            if (sitePartDef == null) return;

            Site site = SiteMaker.MakeSite(sitePartDef, tile, faction);
            Find.WorldObjects.Add(site);

            Messages.Message(
                $"The resolution of {pawn.LabelShort}'s ordeal has drawn attention.",
                MessageTypeDefOf.PositiveEvent);
        }

        private static void TriggerPunishmentEvent(Pawn pawn)
        {
            Map? map = pawn.Map;
            if (map == null) return;

            var enemyFaction = Find.FactionManager.RandomEnemyFaction();
            if (enemyFaction == null) return;

            float points = StorytellerUtility.DefaultThreatPointsNow(map) * 0.8f;

            IncidentParms parms = new IncidentParms
            {
                target          = map,
                faction         = enemyFaction,
                points          = points,
                raidStrategy    = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn
            };

            IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
        }

        public static bool QualifiesForHellfire(PawnNarrativeProfile profile)
        {
            return profile != null &&
                   profile.TotalTagWeight() >= HellfireEvaluator.MinProfileWeight;
        }
    }

    public enum InfernoBranchPath
    {
        Ascent,
        Shadow
    }
}
