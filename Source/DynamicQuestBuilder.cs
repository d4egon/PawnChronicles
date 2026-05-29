using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Grammar;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;

namespace PawnChronicles
{
    public static class DynamicQuestBuilder
    {
        private enum QuestArchetype
        {
            MedicalRescue,
            UnderworldDeal,
            LostRelative,
            PersonalVendetta,
            SkillMasteryTrial,
            WorldEcho,
            IdeologicalCrusade,
            NurtureMission
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Resolved narrative text - set once per BuildStage call,
        //  consumed by every archetype builder and NarrativeLetterOnly.
        // ─────────────────────────────────────────────────────────────────────

        private static string _resolvedName        = "";
        private static string _resolvedDescription = "";
        private static string _resolvedLetterLabel = "";
        private static string _resolvedLetterBody  = "";

        public static void BuildStage(
            Quest quest,
            Pawn pawn,
            Map map,
            string inSignal,
            string outSignal,
            QuestStageDef? stage,
            PawnNarrativeProfile profile)
        {
            Log.Message($"[PawnChronicles] BuildStage called - pawn={pawn?.LabelShort ?? "NULL"}, role={stage?.StageRole ?? "NULL"}, profile={profile != null}");

            string role = stage?.StageRole ?? NarrativeGrammarResolver.RoleMiddle;

            if (pawn == null || profile == null)
            {
                ResolveNarrativeText(null, null, NarrativeGrammarResolver.RoleMiddle);
                NarrativeLetterOnly(quest, inSignal, outSignal, null);
                return;
            }

            ResolveNarrativeText(pawn, profile, role);

            // ── Set quest name and description on the quest object ────────────
            // RimWorld reads quest.name and quest.description for the quest log.
            // These must be set before any quest parts are added.
            quest.name        = _resolvedName;
            quest.description = _resolvedDescription;

            // Also inject into QuestGen's grammar rules so RimWorld's own
            // questName/questDescription resolver finds them.
            QuestGen.AddQuestNameRules(new List<Rule>
            {
                new Rule_String("questName", _resolvedName)
            });
            QuestGen.AddQuestDescriptionRules(new List<Rule>
            {
                new Rule_String("questDescription", _resolvedDescription)
            });

            // ── Select and build the archetype ────────────────────────────────
            var scraper   = PawnDataScraper.ScrapeAll(pawn);
            var dominant  = profile.GetDominantTags(3);
            var archetype = SelectArchetype(dominant, scraper, role, profile);

            switch (archetype)
            {
                case QuestArchetype.MedicalRescue:
                    BuildMedicalRescue(quest, pawn, map, inSignal, outSignal, profile, scraper);
                    break;
                case QuestArchetype.UnderworldDeal:
                    BuildUnderworldDeal(quest, pawn, map, inSignal, outSignal, profile);
                    break;
                case QuestArchetype.LostRelative:
                    BuildLostRelative(quest, pawn, inSignal, outSignal, profile);
                    break;
                case QuestArchetype.PersonalVendetta:
                    BuildPersonalVendetta(quest, pawn, map, inSignal, outSignal, profile);
                    break;
                case QuestArchetype.SkillMasteryTrial:
                    BuildSkillMasteryTrial(quest, pawn, inSignal, outSignal, profile);
                    break;
                case QuestArchetype.IdeologicalCrusade:
                    BuildIdeologicalCrusade(quest, pawn, inSignal, outSignal, profile);
                    break;
                case QuestArchetype.NurtureMission:
                    BuildNurtureMission(quest, pawn, inSignal, outSignal, profile);
                    break;
                default:
                    BuildWorldEcho(quest, pawn, inSignal, outSignal, profile, scraper);
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  NARRATIVE TEXT RESOLUTION
        //  All text comes from the grammar resolver - nothing is hardcoded here.
        // ─────────────────────────────────────────────────────────────────────

        private static void ResolveNarrativeText(
            Pawn? pawn,
            PawnNarrativeProfile? profile,
            string role)
        {
            if (pawn == null || profile == null)
            {
                _resolvedName        = "An untold story";
                _resolvedDescription = "Something stirs.";
                _resolvedLetterLabel = "An untold story";
                _resolvedLetterBody  = "Something stirs.";
                return;
            }

            // Title -> quest name and letter label
            _resolvedLetterLabel = NarrativeGrammarResolver.ResolveTitle(pawn, profile, role);
            _resolvedName        = _resolvedLetterLabel;

            // Body -> quest description and letter body
            _resolvedLetterBody  = NarrativeGrammarResolver.ResolveBody(pawn, profile, role);
            _resolvedDescription = _resolvedLetterBody;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ARCHETYPE SELECTION
        // ─────────────────────────────────────────────────────────────────────

        private static QuestArchetype SelectArchetype(
            List<NarrativeTagDef> dominant,
            List<Rule> scraper,
            string stageRole,
            PawnNarrativeProfile profile)
        {
            float trauma     = profile.GetScore("PC_Tag_Trauma")
                               + GetScraperFloat(scraper, "pc_pain_level") * 20f;
            float underworld = profile.GetScore("PC_Tag_Underworld");
            float loss       = profile.GetScore("PC_Tag_Loss")
                               + profile.GetScore("PC_Tag_Grief");
            float violence   = profile.GetScore("PC_Tag_Violence");
            float devotion   = profile.GetScore("PC_Tag_Devotion")
                               + profile.GetScore("PC_Tag_Faith");
            float nurture    = profile.GetScore("PC_Tag_Nurture")
                               + profile.GetScore("PC_Tag_Healer");

            float skillHigh = scraper
                .OfType<Rule_String>()
                .Any(r => r.keyword.StartsWith("pc_skill_") &&
                          int.TryParse(r.Generate() ?? "0", out int lvl) && lvl >= 12)
                ? 40f : 0f;

            var candidates = new List<(QuestArchetype arch, float weight)>();

            if (trauma > 50f && stageRole != NarrativeGrammarResolver.RoleFailure)
                candidates.Add((QuestArchetype.MedicalRescue, trauma));
            if (underworld > 40f)
                candidates.Add((QuestArchetype.UnderworldDeal, underworld + 20f));
            if (loss > 45f)
                candidates.Add((QuestArchetype.LostRelative, loss));
            if (violence > 50f)
                candidates.Add((QuestArchetype.PersonalVendetta, violence));
            if (skillHigh > 0f)
                candidates.Add((QuestArchetype.SkillMasteryTrial, skillHigh));
            if (devotion > 35f)
                candidates.Add((QuestArchetype.IdeologicalCrusade, devotion + 10f));
            if (nurture > 40f)
                candidates.Add((QuestArchetype.NurtureMission, nurture + 10f));

            candidates.Add((QuestArchetype.WorldEcho, 30f));

            return candidates.RandomElementByWeight(c => c.weight).arch;
        }

        private static float GetScraperFloat(List<Rule> rules, string key)
        {
            var rule = rules?.OfType<Rule_String>()
                             .FirstOrDefault(r => r.keyword == key);
            return rule != null &&
                   float.TryParse(rule.Generate() ?? "0", out float val)
                ? val : 0f;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ARCHETYPE BUILDERS
        //  All letter text comes from _resolvedLetterLabel / _resolvedLetterBody.
        //  No strings are hardcoded anywhere below this line.
        // ─────────────────────────────────────────────────────────────────────

        private static void BuildMedicalRescue(
            Quest quest, Pawn pawn, Map map,
            string inSignal, string outSignal,
            PawnNarrativeProfile profile, List<Rule> scraper)
        {
            if (map == null)
            {
                NarrativeLetterOnly(quest, inSignal, outSignal, LetterDefOf.NeutralEvent);
                return;
            }

            var worstBad = pawn.health?.hediffSet?.hediffs
                .Where(h => h.Visible && h.def.isBad)
                .OrderByDescending(h => h.Severity)
                .FirstOrDefault();

            Thing reward;
            if (worstBad is Hediff_MissingPart)
            {
                reward = ThingMaker.MakeThing(
                    DefDatabase<ThingDef>.GetNamedSilentFail("BionicArm")
                    ?? ThingDefOf.MedicineUltratech);
            }
            else if (worstBad != null && worstBad.Severity > 0.6f)
            {
                reward = ThingMaker.MakeThing(ThingDefOf.MedicineUltratech);
                reward.stackCount = 12;
            }
            else
            {
                reward = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
                reward.stackCount = Rand.RangeInclusive(8, 25);
            }

            quest.DropPods(map.Parent, new List<Thing> { reward },
                inSignal: inSignal, joinPlayer: false);

            quest.Letter(LetterDefOf.PositiveEvent, inSignal,
                text:  _resolvedLetterBody,
                label: _resolvedLetterLabel);

            quest.SendSignals(new List<string> { outSignal }, inSignal);
            quest.End(QuestEndOutcome.Success, inSignal: outSignal, sendStandardLetter: false);
        }

        private static void BuildUnderworldDeal(
            Quest quest, Pawn pawn, Map map,
            string inSignal, string outSignal,
            PawnNarrativeProfile profile)
        {
            if (map == null || !TileFinder.TryFindNewSiteTile(out PlanetTile tile))
            {
                NarrativeLetterOnly(quest, inSignal, outSignal, LetterDefOf.NeutralEvent);
                return;
            }

            var faction = Find.FactionManager
                .RandomNonHostileFaction(allowHidden: false,
                    minTechLevel: TechLevel.Industrial)
                ?? Find.FactionManager.RandomEnemyFaction();

            var sitePartDef = DefDatabase<SitePartDef>.GetNamedSilentFail("Outpost")
                           ?? DefDatabase<SitePartDef>.AllDefsListForReading.FirstOrDefault();

            if (sitePartDef == null)
            {
                NarrativeLetterOnly(quest, inSignal, outSignal, LetterDefOf.NeutralEvent);
                return;
            }

            Site site = SiteMaker.MakeSite(sitePartDef, tile, faction);

            quest.Letter(LetterDefOf.ThreatBig, inSignal,
                text:  _resolvedLetterBody,
                label: _resolvedLetterLabel);

            quest.SpawnWorldObject(site, inSignal: inSignal);
            quest.SendSignals(new List<string> { outSignal }, inSignal);
            quest.End(QuestEndOutcome.Success, inSignal: outSignal, sendStandardLetter: false);
        }

        private static void BuildLostRelative(
            Quest quest, Pawn pawn,
            string inSignal, string outSignal,
            PawnNarrativeProfile profile)
        {
            NarrativeLetterOnly(quest, inSignal, outSignal, LetterDefOf.PositiveEvent);
        }

        private static void BuildPersonalVendetta(
            Quest quest, Pawn pawn, Map map,
            string inSignal, string outSignal,
            PawnNarrativeProfile profile)
        {
            if (map == null)
            {
                NarrativeLetterOnly(quest, inSignal, outSignal, LetterDefOf.NegativeEvent);
                return;
            }

            var enemyFaction = Find.FactionManager.RandomEnemyFaction();
            float points     = StorytellerUtility.DefaultThreatPointsNow(map) * 0.6f;

            var raidSignal = QuestGen.GenerateNewSignal("VendettaRaid");
            var raidPart   = new QuestPart_Incident
            {
                incident = IncidentDefOf.RaidEnemy,
                inSignal = raidSignal
            };
            raidPart.SetIncidentParmsAndRemoveTarget(new IncidentParms
            {
                target          = map,
                faction         = enemyFaction,
                points          = points,
                raidStrategy    = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn
            });

            quest.SendSignals(new List<string> { raidSignal }, inSignal);
            quest.AddPart(raidPart);

            quest.Letter(LetterDefOf.NegativeEvent, inSignal,
                text:  _resolvedLetterBody,
                label: _resolvedLetterLabel);

            quest.SendSignals(new List<string> { outSignal }, inSignal);
            quest.End(QuestEndOutcome.Success, inSignal: outSignal, sendStandardLetter: false);
        }

        private static void BuildSkillMasteryTrial(
            Quest quest, Pawn pawn,
            string inSignal, string outSignal,
            PawnNarrativeProfile profile)
        {
            NarrativeLetterOnly(quest, inSignal, outSignal, LetterDefOf.PositiveEvent);
        }

        private static void BuildWorldEcho(
            Quest quest, Pawn pawn,
            string inSignal, string outSignal,
            PawnNarrativeProfile profile, List<Rule> scraper)
        {
            NarrativeLetterOnly(quest, inSignal, outSignal, LetterDefOf.NeutralEvent);
        }

        private static void BuildIdeologicalCrusade(
            Quest quest, Pawn pawn,
            string inSignal, string outSignal,
            PawnNarrativeProfile profile)
        {
            NarrativeLetterOnly(quest, inSignal, outSignal, LetterDefOf.NeutralEvent);
        }

        private static void BuildNurtureMission(
            Quest quest, Pawn pawn,
            string inSignal, string outSignal,
            PawnNarrativeProfile profile)
        {
            NarrativeLetterOnly(quest, inSignal, outSignal, LetterDefOf.PositiveEvent);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SHARED LETTER-ONLY PATH
        //  Used by archetypes that are letter-only (no world objects, no raids).
        //  Text always comes from _resolvedLetterLabel / _resolvedLetterBody.
        // ─────────────────────────────────────────────────────────────────────

        private static void NarrativeLetterOnly(
            Quest quest,
            string inSignal,
            string outSignal,
            LetterDef? letterDef)
        {
            quest.Letter(
                letterDef ?? LetterDefOf.NeutralEvent,
                inSignal,
                text:  _resolvedLetterBody,
                label: _resolvedLetterLabel);

            quest.SendSignals(new List<string> { outSignal }, inSignal);
            quest.End(QuestEndOutcome.Success, inSignal: outSignal, sendStandardLetter: false);
        }
    }
}
