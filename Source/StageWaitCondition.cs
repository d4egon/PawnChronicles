using System.Linq;
using Verse;
using Verse.Grammar;
using RimWorld;
using System.Collections.Generic;

namespace PawnChronicles
{
    public static class StageWaitCondition
    {
        // ─────────────────────────────────────────────────────────────────────
        //  BUILD - select condition for a pawn based on dominant tag
        // ─────────────────────────────────────────────────────────────────────

        public static (string key, string label, int baseline, int targetDelta)
            BuildFor(Pawn pawn, PawnNarrativeProfile profile, string stageRole)
        {
            string dominant = profile.DominantTag() ?? "default";
            string tag = dominant.ToLowerInvariant().Replace("pc_tag_", "");

            if (stageRole == NarrativeGrammarResolver.RoleSuccess ||
                stageRole == NarrativeGrammarResolver.RoleFailure)
            {
                return BuildTimeCondition(pawn, 15);
            }

            return tag switch
            {
                "violence" or "duty" or "betrayal" or "survival" => BuildKillCondition(pawn),
                "trauma" or "decay" or "resilience" => BuildInjuredCondition(pawn),
                "healer" or "nurture" => BuildHealedCondition(pawn),
                "craft" or "curiosity" or "scholar" or "artist" => BuildCraftedCondition(pawn),
                "kinship" or "loss" or "grief" or "refugee" => BuildSocialCondition(pawn),
                "devotion" or "faith" => BuildRitualOrPrayerCondition(pawn),
                "noble" or "leadership" or "power" => BuildSocialCondition(pawn),
                "animalfriend" => BuildTamedCondition(pawn),
                "wandering" or "isolation" or "pacifism" => BuildTimeCondition(pawn, 15),
                "augmentation" or "underworld" => BuildTimeCondition(pawn, 15),
                _ => BuildTimeCondition(pawn, 15)
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CONDITION CHECKERS - The Dynamic Engine
        // ─────────────────────────────────────────────────────────────────────

        public static bool CheckConditionMet(Pawn pawn, ArcStageEntry entry)
        {
            if (pawn == null || pawn.Dead) return false;

            string key = entry.waitConditionKey;
            int target = entry.waitBaselineValue + entry.waitTargetDelta;

            // 1. Check Hardcoded Specialized Logic first
            switch (key)
            {
                case "kills": return GetRecord(pawn, RecordDefOf.Kills) >= target;
                case "downed": return GetRecord(pawn, RecordDefOf.TimesInMentalState) >= target;
                case "crafted": return (GetRecord(pawn, RecordDefOf.ThingsConstructed) + GetRecord(pawn, RecordDefOf.ThingsCrafted)) >= target;
                case "social":
                {
                    var comp = pawn.GetComp<CompPersonalChronicles>();
                    return (comp?.socialInteractionCount ?? 0) >= target;
                }
                case "injured": return CountBadHediffs(pawn) > entry.waitBaselineValue;
                case "healed": return CountTempBadHediffs(pawn) < entry.waitBaselineValue;
                case "inspired": return pawn.Inspiration != null;
                case "mood_low": return (pawn.needs?.mood?.CurLevelPercentage ?? 1f) < 0.35f;
                case "mood_high": return (pawn.needs?.mood?.CurLevelPercentage ?? 0f) > 0.75f;
                case "tamed": return GetRecord(pawn, RecordDefOf.AnimalsTamed) >= target;
                case "ritual":
                case "prayer":
                {
                    var comp = pawn.GetComp<CompPersonalChronicles>();
                    return (comp?.ritualParticipationCount ?? 0) >= target;
                }
                case "time": return Find.TickManager.TicksGame >= target;
                case "withdrawal":
                {
                    var comp = pawn.GetComp<CompPersonalChronicles>();
                    string addictionDef = comp?.currentEpic?.addictionHediffDef;
                    if (string.IsNullOrEmpty(addictionDef)) return false;
                    var hediff = pawn.health.hediffSet.hediffs
                        .OfType<Hediff_Addiction>()
                        .FirstOrDefault(h => h.def.defName == addictionDef);
                    return hediff?.CurStageIndex == 1; // WithdrawalStageIndex = 1
                }
                case "sobriety":
                {
                    var comp = pawn.GetComp<CompPersonalChronicles>();
                    string addictionDef = comp?.currentEpic?.addictionHediffDef;
                    if (string.IsNullOrEmpty(addictionDef)) return true;
                    return !pawn.health.hediffSet.hediffs.Any(h => h.def.defName == addictionDef);
                }
            }

            // 2. DYNAMIC FALLBACK: If key isn't hardcoded, try to resolve it as a Def
            float dynamicValue = ResolveDynamicValue(pawn, key);
            if (dynamicValue != -1f)
            {
                return dynamicValue >= target;
            }

            // 3. ABSOLUTE FALLBACK: 4 hour timeout
            return Find.TickManager.TicksGame >= entry.waitBaselineValue + (4 * 60000);
        }

        private static float ResolveDynamicValue(Pawn pawn, string key)
        {
            // Try Records (XML name)
            RecordDef record = DefDatabase<RecordDef>.GetNamed(key, false);
            if (record != null) return pawn.records?.GetValue(record) ?? 0f;

            // Try Skills
            SkillDef skill = DefDatabase<SkillDef>.GetNamed(key, false);
            if (skill != null) return pawn.skills?.GetSkill(skill)?.Level ?? 0f;

            // Try Needs
            NeedDef need = DefDatabase<NeedDef>.GetNamed(key, false);
            if (need != null) return pawn.needs?.TryGetNeed(need)?.CurLevelPercentage ?? 0f;

            return -1f;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CONDITION BUILDERS (Unchanged - Safely preserved)
        // ─────────────────────────────────────────────────────────────────────

        private static (string key, string label, int baseline, int targetDelta) BuildKillCondition(Pawn pawn) => 
            ("kills", ResolveConditionLabel(pawn, "wait_condition_kills"), GetRecord(pawn, RecordDefOf.Kills), 1);

        private static (string key, string label, int baseline, int targetDelta) BuildInjuredCondition(Pawn pawn) => 
            ("injured", ResolveConditionLabel(pawn, "wait_condition_injured"), CountBadHediffs(pawn), 0);

        private static (string key, string label, int baseline, int targetDelta) BuildHealedCondition(Pawn pawn) =>
            ("healed", ResolveConditionLabel(pawn, "wait_condition_healed"), CountTempBadHediffs(pawn), 0);

        private static (string key, string label, int baseline, int targetDelta) BuildCraftedCondition(Pawn pawn) =>
            ("crafted", ResolveConditionLabel(pawn, "wait_condition_crafted"), GetRecord(pawn, RecordDefOf.ThingsConstructed) + GetRecord(pawn, RecordDefOf.ThingsCrafted), 20);

        private static (string key, string label, int baseline, int targetDelta) BuildSocialCondition(Pawn pawn)
        {
            var comp = pawn.GetComp<CompPersonalChronicles>();
            int baseline = comp?.socialInteractionCount ?? 0;
            return ("social", ResolveConditionLabel(pawn, "wait_condition_social"), baseline, 10);
        }

        private static (string key, string label, int baseline, int targetDelta) BuildRitualOrPrayerCondition(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive)
                return BuildTimeCondition(pawn, 15);
            var comp = pawn.GetComp<CompPersonalChronicles>();
            int baseline = comp?.ritualParticipationCount ?? 0;
            return ("ritual", ResolveConditionLabel(pawn, "wait_condition_ritual"), baseline, 1);
        }

        private static (string key, string label, int baseline, int targetDelta) BuildTamedCondition(Pawn pawn) => 
            ("tamed", ResolveConditionLabel(pawn, "wait_condition_tamed"), GetRecord(pawn, RecordDefOf.AnimalsTamed), 1);

        private static (string key, string label, int baseline, int targetDelta) BuildTimeCondition(Pawn pawn, int days) =>
            ("time", ResolveConditionLabel(pawn, "wait_condition_time"), Find.TickManager.TicksGame, days * 60000);

        private static (string key, string label, int baseline, int targetDelta) BuildWithdrawalCondition(Pawn pawn) =>
            ("withdrawal", ResolveConditionLabel(pawn, "wait_condition_withdrawal"), 0, 1);

        private static (string key, string label, int baseline, int targetDelta) BuildSobrietyCondition(Pawn pawn) =>
            ("sobriety", ResolveConditionLabel(pawn, "wait_condition_sobriety"), 0, 0);

        /// <summary>
        /// Selects the appropriate wait condition for a fixed addiction arc stage.
        /// grammarRole is the stage's full grammar role, e.g. "addiction_alcohol_withdrawal".
        /// The stage type is derived from the final segment.
        /// </summary>
        public static (string key, string label, int baseline, int targetDelta)
            BuildForAddiction(Pawn pawn, string grammarRole)
        {
            string[] parts = grammarRole.Split('_');
            string stage = parts.Length > 0 ? parts[parts.Length - 1] : "";
            return stage switch
            {
                "opening"    => BuildTimeCondition(pawn, 5),
                "dependency" => BuildTimeCondition(pawn, 5),
                "social"     => BuildSocialCondition(pawn),
                "withdrawal" => BuildWithdrawalCondition(pawn),
                "crisis"     => ("mood_low",
                                 ResolveConditionLabel(pawn, "wait_condition_mood_low"),
                                 0, 0),
                _            => BuildTimeCondition(pawn, 3)
            };
        }

        /// <summary>
        /// Builds the two climax choices for an addiction arc:
        /// hard road waits for sobriety (addiction hediff gone),
        /// easy out resolves immediately.
        /// </summary>
        public static List<StageChoice> BuildAddictionClimaxDoors(Pawn pawn)
        {
            return new List<StageChoice>
            {
                new StageChoice
                {
                    tagDefName     = "",
                    actionLabel    = "PC_Addiction_HardRoad_Label".Translate(),
                    mechanicalHint = "PC_Addiction_HardRoad_Hint".Translate(),
                    conditionKey   = "sobriety",
                    conditionLabel = ResolveConditionLabel(pawn, "wait_condition_sobriety"),
                    baseline       = 0,
                    targetDelta    = 0,
                    effects        = new List<ChoiceEffect>(),
                    isHardRoad     = true,
                    isEasyOut      = false
                },
                new StageChoice
                {
                    tagDefName     = "",
                    actionLabel    = "PC_Addiction_EasyOut_Label".Translate(),
                    mechanicalHint = "PC_Addiction_EasyOut_Hint".Translate(),
                    conditionKey   = "time",
                    conditionLabel = "PC_Wait_TakeItNow".Translate(),
                    baseline       = Find.TickManager.TicksGame,
                    targetDelta    = 0,
                    effects        = new List<ChoiceEffect>(),
                    isHardRoad     = false,
                    isEasyOut      = true
                }
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CLIMAX SKILL CHECK - maps dominant tag to a skill + threshold
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a skill-check wait condition for the arc climax.
        /// Maps the pawn's dominant narrative tag to the most thematically appropriate
        /// RimWorld skill and selects a threshold (level 6–9) the pawn must reach.
        /// Returns (key=SkillDef.defName, label, baseline=0, targetDelta=threshold).
        /// CheckConditionMet resolves this correctly via ResolveDynamicValue -> SkillDef.
        /// </summary>
        public static (string key, string label, int baseline, int targetDelta)
            BuildClimaxSkillCheck(Pawn pawn, PawnNarrativeProfile profile, string? preferredTag = null)
        {
            string tag = !string.IsNullOrEmpty(preferredTag)
                ? preferredTag.ToLowerInvariant().Replace("pc_tag_", "")
                : profile.DominantTag()?.ToLowerInvariant().Replace("pc_tag_", "") ?? "default";

            // Map tag -> (skillDefName, threshold level)
            var (skillName, threshold) = tag switch
            {
                "violence" or "betrayal" or "survival"
                    => (HasShootingSkill(pawn) ? "Shooting" : "Melee", 8),
                "duty"
                    => ("Melee", 8),
                "trauma" or "decay" or "resilience"
                    => ("Melee", 7),
                "healer" or "nurture"
                    => ("Medicine", 8),
                "craft" or "artist"
                    => ("Crafting", 8),
                "curiosity" or "scholar"
                    => ("Intellectual", 8),
                "animalfriend"
                    => ("Animals", 8),
                "kinship" or "loss" or "grief" or "refugee"
                    => ("Social", 7),
                "noble" or "leadership" or "power"
                    => ("Social", 8),
                "devotion" or "faith"
                    => ("Social", 7),
                "wandering" or "isolation" or "pacifism"
                    => ("Intellectual", 6),
                "augmentation" or "underworld"
                    => ("Intellectual", 7),
                _ => ("Intellectual", 6)
            };

            // If the pawn cannot use this skill at all, fall back gracefully
            var skillDef = DefDatabase<SkillDef>.GetNamed(skillName, errorOnFail: false);
            if (skillDef != null && (pawn.skills?.GetSkill(skillDef)?.TotallyDisabled ?? false))
            {
                skillName  = "Intellectual";
                threshold  = 5;
                skillDef   = DefDatabase<SkillDef>.GetNamed(skillName, errorOnFail: false);
            }

            string skillLabel   = skillDef?.label ?? skillName;
            int    currentLevel = (int)(pawn.skills?.GetSkill(skillDef)?.Level ?? 0f);
            string label        = "PC_Climax_SkillCheck".Translate(skillLabel, threshold.ToString(), currentLevel.ToString());

            return (skillName, label, 0, threshold);
        }

        private static bool HasShootingSkill(Pawn pawn)
        {
            var def = DefDatabase<SkillDef>.GetNamed("Shooting", errorOnFail: false);
            if (def == null) return false;
            var skill = pawn.skills?.GetSkill(def);
            return skill != null && !skill.TotallyDisabled;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  GRAMMAR & HELPERS (Unchanged - Safely preserved)
        // ─────────────────────────────────────────────────────────────────────

        private static string ResolveConditionLabel(Pawn pawn, string grammarKey)
        {
            var grammarPack = DefDatabase<RulePackDef>.GetNamedSilentFail("PC_NarrativeGrammar");
            if (grammarPack == null) return FallbackLabel(grammarKey, pawn);
            var rule = grammarPack.RulesPlusIncludes.OfType<Rule_String>().FirstOrDefault(r => r.keyword.Equals(grammarKey, System.StringComparison.OrdinalIgnoreCase));
            if (rule == null) return FallbackLabel(grammarKey, pawn);

            try
            {
                var profile = pawn.GetNarrativeProfile();
                var request = new GrammarRequest();
                request.Rules.AddRange(GrammarUtility.RulesForPawn("pawn", pawn));
                request.Rules.AddRange(PawnDataScraper.ScrapeAll(pawn));
                request.Rules.AddRange(Lexicon.GetDerivedRules(pawn, profile));
                request.Rules.Add(new Rule_String("root", rule.Generate()));
                return GrammarResolver.Resolve("root", request, $"PC_WaitCondition:{grammarKey}", forceLog: false);
            }
            catch { return FallbackLabel(grammarKey, pawn); }
        }

        private static string FallbackLabel(string key, Pawn pawn)
        {
            string name = pawn?.LabelShort ?? "them";
            return key switch
            {
                "wait_condition_kills"   => "PC_WaitFallback_Kills".Translate(name),
                "wait_condition_injured" => "PC_WaitFallback_Injured".Translate(name),
                "wait_condition_healed"  => "PC_WaitFallback_Healed".Translate(name),
                "wait_condition_crafted" => "PC_WaitFallback_Crafted".Translate(name),
                "wait_condition_social"  => "PC_WaitFallback_Social".Translate(name),
                "wait_condition_ritual"  => "PC_WaitFallback_Ritual".Translate(name),
                "wait_condition_tamed"      => "PC_WaitFallback_Tamed".Translate(name),
                "wait_condition_time"       => "PC_WaitFallback_Time".Translate(name),
                "wait_condition_withdrawal" => "PC_WaitFallback_Withdrawal".Translate(name),
                "wait_condition_sobriety"   => "PC_WaitFallback_Sobriety".Translate(name),
                "wait_condition_mood_low"   => "PC_WaitFallback_MoodLow".Translate(name),
                _                           => "PC_WaitFallback_Default".Translate(name)
            };
        }

        private static int GetRecord(Pawn pawn, RecordDef def) => (int)(pawn.records?.GetValue(def) ?? 0);
        
        // Dynamic string-based record lookup
        private static int GetRecord(Pawn pawn, string defName)
        {
            RecordDef def = DefDatabase<RecordDef>.GetNamed(defName, false);
            return def == null ? 0 : (int)(pawn.records?.GetValue(def) ?? 0);
        }

        // All visible bad hediffs - used for the injured condition (scars and wounds both count).
        private static int CountBadHediffs(Pawn pawn) =>
            pawn.health?.hediffSet?.hediffs.Count(h => h.Visible && h.def.isBad) ?? 0;

        // Only temporary bad hediffs - used for the healed condition (permanent damage never clears).
        private static int CountTempBadHediffs(Pawn pawn) =>
            pawn.health?.hediffSet?.hediffs.Count(h => h.Visible && h.def.isBad && !h.IsPermanent()) ?? 0;

        // CheckRitualOrPrayer removed - ritual/prayer conditions now use
        // comp.ritualParticipationCount tracked by Patch_LordJobRitual_TrackParticipation.
    
        // ─────────────────────────────────────────────────────────────────────
        //  CHOICE BUILDER - top-3 tags -> List<StageChoice>
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates up to 3 tag-anchored choices from the pawn's dominant narrative
        /// tags, plus a walk-away option. Each choice carries its own wait condition
        /// and mechanical hint text.
        /// </summary>
        public static List<StageChoice> BuildChoicesFor(
            Pawn pawn, PawnNarrativeProfile profile, string stageRole, bool isClimax = false)
        {
            var choices = new List<StageChoice>();

            var topTags = profile.GetDominantTags(maxCount: 3);

            foreach (var tagDef in topTags)
            {
                string tag = tagDef.defName.ToLowerInvariant().Replace("pc_tag_", "");
                var (condKey, condLabel, baseline, delta) = BuildConditionForTag(pawn, tag, stageRole);
                var (actionLabel, hint) = GetTagFlavor(tag, condKey);

                choices.Add(new StageChoice
                {
                    tagDefName     = tagDef.defName,
                    actionLabel    = actionLabel,
                    mechanicalHint = hint,
                    conditionKey   = condKey,
                    conditionLabel = condLabel,
                    baseline       = baseline,
                    targetDelta    = delta,
                });
            }


            return choices;
        }

        private static (string key, string label, int baseline, int delta)
            BuildConditionForTag(Pawn pawn, string tag, string stageRole)
        {
            if (stageRole == NarrativeGrammarResolver.RoleSuccess ||
                stageRole == NarrativeGrammarResolver.RoleFailure)
                return BuildTimeCondition(pawn, 15);

            return tag switch
            {
                "violence" or "duty" or "betrayal" or "survival"
                    => BuildKillCondition(pawn),
                "trauma" or "decay"
                    => BuildInjuredCondition(pawn),
                "resilience" or "healer" or "nurture"
                    => BuildHealedCondition(pawn),
                "craft" or "curiosity" or "scholar" or "artist"
                    => BuildCraftedCondition(pawn),
                "kinship" or "loss" or "grief" or "refugee" or "noble" or "leadership" or "power"
                    => BuildSocialCondition(pawn),
                "devotion" or "faith"
                    => BuildRitualOrPrayerCondition(pawn),
                "animalfriend"
                    => BuildTamedCondition(pawn),
                "wandering" or "isolation" or "pacifism"
                    => BuildTimeCondition(pawn, 15),
                "augmentation" or "underworld"
                    => BuildTimeCondition(pawn, 15),
                _ => BuildTimeCondition(pawn, 15)
            };
        }

        private static (string actionLabel, string mechanicalHint) GetTagFlavor(string tag, string condKey)
        {
            string condHint = condKey switch
            {
                "kills"              => "PC_CondHint_Kills".Translate(),
                "injured"            => "PC_CondHint_Injured".Translate(),
                "healed"             => "PC_CondHint_Healed".Translate(),
                "crafted"            => "PC_CondHint_Crafted".Translate(),
                "social"             => "PC_CondHint_Social".Translate(),
                "ritual" or "prayer" => "PC_CondHint_Ritual".Translate(),
                "tamed"              => "PC_CondHint_Tamed".Translate(),
                "time"               => "PC_CondHint_Time".Translate(),
                _                    => "PC_CondHint_Default".Translate()
            };

            string action = tag switch
            {
                "violence"     => "PC_Choice_Violence".Translate(),
                "duty"         => "PC_Choice_Duty".Translate(),
                "betrayal"     => "PC_Choice_Betrayal".Translate(),
                "survival"     => "PC_Choice_Survival".Translate(),
                "trauma"       => "PC_Choice_Trauma".Translate(),
                "decay"        => "PC_Choice_Decay".Translate(),
                "resilience"   => "PC_Choice_Resilience".Translate(),
                "healer"       => "PC_Choice_Healer".Translate(),
                "nurture"      => "PC_Choice_Nurture".Translate(),
                "craft"        => "PC_Choice_Craft".Translate(),
                "curiosity"    => "PC_Choice_Curiosity".Translate(),
                "scholar"      => "PC_Choice_Scholar".Translate(),
                "artist"       => "PC_Choice_Artist".Translate(),
                "kinship"      => "PC_Choice_Kinship".Translate(),
                "loss"         => "PC_Choice_Loss".Translate(),
                "grief"        => "PC_Choice_Grief".Translate(),
                "refugee"      => "PC_Choice_Refugee".Translate(),
                "devotion"     => "PC_Choice_Devotion".Translate(),
                "faith"        => "PC_Choice_Faith".Translate(),
                "noble"        => "PC_Choice_Noble".Translate(),
                "leadership"   => "PC_Choice_Leadership".Translate(),
                "power"        => "PC_Choice_Power".Translate(),
                "isolation"    => "PC_Choice_Isolation".Translate(),
                "wandering"    => "PC_Choice_Wandering".Translate(),
                "pacifism"     => "PC_Choice_Pacifism".Translate(),
                "augmentation" => "PC_Choice_Augmentation".Translate(),
                "underworld"   => "PC_Choice_Underworld".Translate(),
                "animalfriend" => "PC_Choice_Animalfriend".Translate(),
                _              => "PC_Choice_Default".Translate()
            };

            string tagDisplay = tag.Length > 0
                ? char.ToUpper(tag[0]) + tag.Substring(1)
                : tag;
            return (action, "PC_CondHint_TagPath".Translate(condHint, tagDisplay));
        }

        public static Dictionary<string, float> GetPawnSnapshot(Pawn pawn)
        {
            var dict = new Dictionary<string, float>();
            if (pawn == null) return dict;

            // Fang alle Needs (Mood, Hunger, etc.)
            foreach (var need in pawn.needs.AllNeeds)
                dict[$"need_{need.def.defName}"] = need.CurLevelPercentage;

            // Fang alle Capacities (Manipulation = John Does arm!)
            foreach (var cap in DefDatabase<PawnCapacityDef>.AllDefs)
                dict[$"cap_{cap.defName}"] = pawn.health.capacities.GetLevel(cap);

            // Fang alle Skills
            foreach (var skill in pawn.skills.skills)
                dict[$"skill_{skill.def.defName}"] = skill.Level;

            // Fang overordnet smerte
            dict["stat_Pain"] = pawn.health.hediffSet.PainTotal;
            
            return dict;
        }

        // ------------------------------------------------------------------------
        //  SEED CHOICES -- opening stage, pick the arc path
        // ------------------------------------------------------------------------

        /// <summary>
        /// Builds 3 seed choices from the pawn's top narrative tags.
        /// No ChoiceEffects -- the seed just selects the arc path.
        /// Each choice carries a 2-day time condition so the letter has room to breathe.
        /// CompPersonalChronicles records the picked tagDefName as chosenPathTag.
        /// </summary>
        public static List<StageChoice> BuildSeedChoices(Pawn pawn, PawnNarrativeProfile profile)
        {
            var choices = new List<StageChoice>();
            var topTags = profile.GetDominantTags(maxCount: 3);

            foreach (var tagDef in topTags)
            {
                string tag = tagDef.defName.ToLowerInvariant().Replace("pc_tag_", "");
                string skillHint = GetPathSkillHint(tag);
                string actionLabel = GetSeedActionLabel(tag);

                choices.Add(new StageChoice
                {
                    tagDefName     = tagDef.defName,
                    actionLabel    = actionLabel,
                    mechanicalHint = "PC_Seed_MechanicalHint".Translate(FirstUpper(tag), skillHint),
                    conditionKey   = "time",
                    conditionLabel = "PC_Wait_SeedSettles".Translate(),
                    baseline       = Find.TickManager.TicksGame,
                    targetDelta    = (int)(PawnChroniclesMod.Settings.seedWaitDays * 60000f),
                    effects        = new List<ChoiceEffect>(),
                    isHardRoad     = false,
                    isEasyOut      = false
                });
            }

            return choices;
        }

        private static string GetSeedActionLabel(string tag) => tag switch
        {
            "violence"     => "PC_Seed_Violence".Translate(),
            "duty"         => "PC_Seed_Duty".Translate(),
            "betrayal"     => "PC_Seed_Betrayal".Translate(),
            "survival"     => "PC_Seed_Survival".Translate(),
            "trauma"       => "PC_Seed_Trauma".Translate(),
            "decay"        => "PC_Seed_Decay".Translate(),
            "resilience"   => "PC_Seed_Resilience".Translate(),
            "healer"       => "PC_Seed_Healer".Translate(),
            "nurture"      => "PC_Seed_Nurture".Translate(),
            "craft"        => "PC_Seed_Craft".Translate(),
            "curiosity"    => "PC_Seed_Curiosity".Translate(),
            "scholar"      => "PC_Seed_Scholar".Translate(),
            "artist"       => "PC_Seed_Artist".Translate(),
            "kinship"      => "PC_Seed_Kinship".Translate(),
            "loss"         => "PC_Seed_Loss".Translate(),
            "grief"        => "PC_Seed_Grief".Translate(),
            "refugee"      => "PC_Seed_Refugee".Translate(),
            "devotion"     => "PC_Seed_Devotion".Translate(),
            "faith"        => "PC_Seed_Faith".Translate(),
            "noble"        => "PC_Seed_Noble".Translate(),
            "leadership"   => "PC_Seed_Leadership".Translate(),
            "power"        => "PC_Seed_Power".Translate(),
            "isolation"    => "PC_Seed_Isolation".Translate(),
            "wandering"    => "PC_Seed_Wandering".Translate(),
            "pacifism"     => "PC_Seed_Pacifism".Translate(),
            "augmentation" => "PC_Seed_Augmentation".Translate(),
            "underworld"   => "PC_Seed_Underworld".Translate(),
            "animalfriend" => "PC_Seed_Animalfriend".Translate(),
            _              => "PC_Seed_Default".Translate()
        };

        private static string GetPathSkillHint(string tag) => tag switch
        {
            "violence" or "duty" or "betrayal" or "survival"
                => "PC_SkillHint_Combat".Translate(),
            "healer" or "nurture"
                => "PC_SkillHint_Medicine".Translate(),
            "craft" or "artist"
                => "PC_SkillHint_Crafting".Translate(),
            "scholar" or "curiosity" or "augmentation"
                => "PC_SkillHint_Intellectual".Translate(),
            "animalfriend"
                => "PC_SkillHint_Animals".Translate(),
            "noble" or "leadership" or "power" or "devotion" or "faith" or "kinship"
                => "PC_SkillHint_Social".Translate(),
            "trauma" or "grief" or "loss" or "wandering" or "isolation" or "refugee" or "decay"
                => "PC_SkillHint_Difficulty".Translate(),
            "underworld"
                => "PC_SkillHint_Underworld".Translate(),
            _ => "PC_SkillHint_Default".Translate()
        };

        // ------------------------------------------------------------------------
        //  TRADEOFF CHOICES -- middle stages, immediate stat consequences
        // ------------------------------------------------------------------------

        /// <summary>
        /// Builds 2-3 tradeoff choices for a middle arc stage.
        /// Each choice applies immediate ChoiceEffects (skill level changes)
        /// and a 2-day wait condition to pace the narrative.
        /// No walk-away -- middle stages are a committed path.
        /// </summary>
        public static List<StageChoice> BuildTradeoffChoices(Pawn pawn, string pathTag)
        {
            string tag = pathTag.ToLowerInvariant().Replace("pc_tag_", "");
            var pool = GetTradeoffPool(tag);
            var choices = new List<StageChoice>();

            foreach (var (label, hint, fx) in pool)
            {
                var condition = BuildConditionForTag(pawn, tag, "middle");
                // For time-based conditions, use the configurable middle wait; activity conditions stay as-is
                int condDelta = condition.key == "time"
                    ? (int)(PawnChroniclesMod.Settings.middleWaitDays * 60000f)
                    : condition.delta;
                choices.Add(new StageChoice
                {
                    tagDefName     = pathTag,
                    actionLabel    = label,
                    mechanicalHint = hint,
                    conditionKey   = condition.key,
                    conditionLabel = condition.label,
                    baseline       = condition.baseline,
                    targetDelta    = condDelta,
                    effects        = fx,
                    isHardRoad     = false,
                    isEasyOut      = false
                });
            }

            return choices;
        }

        /// <summary>
        /// Returns (actionLabel, mechanicalHint, List<ChoiceEffect>) tuples for a path tag.
        /// Each tuple is one selectable option in a middle stage.
        /// </summary>
        private static List<(string label, string hint, List<ChoiceEffect> effects)>
            GetTradeoffPool(string tag)
        {
            switch (tag)
            {
                case "violence": case "survival": case "betrayal":
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Combat_1".Translate(),
                         "PC_Tradeoff_Combat_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Shooting", 2), new ChoiceEffect("Medicine", -1) }),
                        ("PC_Tradeoff_Combat_2".Translate(),
                         "PC_Tradeoff_Combat_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Melee", 3), new ChoiceEffect("Social", -1) }),
                        ("PC_Tradeoff_Combat_3".Translate(),
                         "PC_Tradeoff_Combat_3_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Melee", 2), new ChoiceEffect("Medicine", 1) }),
                    };

                case "duty":
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Duty_1".Translate(),
                         "PC_Tradeoff_Duty_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Melee", 2), new ChoiceEffect("Intellectual", 1) }),
                        ("PC_Tradeoff_Duty_2".Translate(),
                         "PC_Tradeoff_Duty_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Social", 2), new ChoiceEffect("Melee", -1) }),
                        ("PC_Tradeoff_Duty_3".Translate(),
                         "PC_Tradeoff_Duty_3_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Melee", 3), new ChoiceEffect("Crafting", -1) }),
                    };

                case "leadership": case "noble": case "power":
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Leader_1".Translate(),
                         "PC_Tradeoff_Leader_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Social", 2), new ChoiceEffect("Intellectual", 1) }),
                        ("PC_Tradeoff_Leader_2".Translate(),
                         "PC_Tradeoff_Leader_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Social", 3), new ChoiceEffect("Melee", -1) }),
                        ("PC_Tradeoff_Leader_3".Translate(),
                         "PC_Tradeoff_Leader_3_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Intellectual", 2), new ChoiceEffect("Crafting", 1) }),
                    };

                case "craft": case "artist":
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Craft_1".Translate(),
                         "PC_Tradeoff_Craft_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Crafting", 2), new ChoiceEffect("Artistic", 1) }),
                        ("PC_Tradeoff_Craft_2".Translate(),
                         "PC_Tradeoff_Craft_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Artistic", 3), new ChoiceEffect("Social", -1) }),
                        ("PC_Tradeoff_Craft_3".Translate(),
                         "PC_Tradeoff_Craft_3_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Crafting", 2), new ChoiceEffect("Construction", 1) }),
                    };

                case "healer": case "nurture":
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Healer_1".Translate(),
                         "PC_Tradeoff_Healer_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Medicine", 2), new ChoiceEffect("Melee", -1) }),
                        ("PC_Tradeoff_Healer_2".Translate(),
                         "PC_Tradeoff_Healer_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Medicine", 3), new ChoiceEffect("Social", -1) }),
                        ("PC_Tradeoff_Healer_3".Translate(),
                         "PC_Tradeoff_Healer_3_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Medicine", 2), new ChoiceEffect("Intellectual", 1) }),
                    };

                case "scholar": case "curiosity":
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Scholar_1".Translate(),
                         "PC_Tradeoff_Scholar_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Intellectual", 2), new ChoiceEffect("Social", -1) }),
                        ("PC_Tradeoff_Scholar_2".Translate(),
                         "PC_Tradeoff_Scholar_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Intellectual", 3), new ChoiceEffect("Melee", -1) }),
                        ("PC_Tradeoff_Scholar_3".Translate(),
                         "PC_Tradeoff_Scholar_3_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Intellectual", 2), new ChoiceEffect("Crafting", 1) }),
                    };

                case "trauma": case "grief": case "loss":
                case "wandering": case "isolation": case "refugee": case "decay":
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Hardship_1".Translate(),
                         "PC_Tradeoff_Hardship_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Intellectual", 2), new ChoiceEffect("Social", -1) }),
                        ("PC_Tradeoff_Hardship_2".Translate(),
                         "PC_Tradeoff_Hardship_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Social", 2), new ChoiceEffect("Melee", -1) }),
                        ("PC_Tradeoff_Hardship_3".Translate(),
                         "PC_Tradeoff_Hardship_3_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Medicine", 1), new ChoiceEffect("Intellectual", 1) }),
                    };

                case "animalfriend":
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Animal_1".Translate(),
                         "PC_Tradeoff_Animal_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Animals", 2), new ChoiceEffect("Social", -1) }),
                        ("PC_Tradeoff_Animal_2".Translate(),
                         "PC_Tradeoff_Animal_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Animals", 3), new ChoiceEffect("Melee", -1) }),
                        ("PC_Tradeoff_Animal_3".Translate(),
                         "PC_Tradeoff_Animal_3_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Animals", 2), new ChoiceEffect("Medicine", 1) }),
                    };

                case "devotion": case "faith":
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Faith_1".Translate(),
                         "PC_Tradeoff_Faith_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Social", 2), new ChoiceEffect("Intellectual", -1) }),
                        ("PC_Tradeoff_Faith_2".Translate(),
                         "PC_Tradeoff_Faith_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Social", 3), new ChoiceEffect("Melee", -1) }),
                        ("PC_Tradeoff_Faith_3".Translate(),
                         "PC_Tradeoff_Faith_3_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Intellectual", 2), new ChoiceEffect("Social", 1) }),
                    };

                case "augmentation":
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Augmentation_1".Translate(),
                         "PC_Tradeoff_Augmentation_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Intellectual", 2), new ChoiceEffect("Medicine", -1) }),
                        ("PC_Tradeoff_Augmentation_2".Translate(),
                         "PC_Tradeoff_Augmentation_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Intellectual", 3), new ChoiceEffect("Social", -1) }),
                        ("PC_Tradeoff_Augmentation_3".Translate(),
                         "PC_Tradeoff_Augmentation_3_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Crafting", 2), new ChoiceEffect("Intellectual", 1) }),
                    };

                case "underworld":
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Underworld_1".Translate(),
                         "PC_Tradeoff_Underworld_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Social", 2), new ChoiceEffect("Melee", -1) }),
                        ("PC_Tradeoff_Underworld_2".Translate(),
                         "PC_Tradeoff_Underworld_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Melee", 2), new ChoiceEffect("Social", -1) }),
                        ("PC_Tradeoff_Underworld_3".Translate(),
                         "PC_Tradeoff_Underworld_3_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Intellectual", 2), new ChoiceEffect("Social", -1) }),
                    };

                default:
                    return new List<(string, string, List<ChoiceEffect>)>
                    {
                        ("PC_Tradeoff_Default_1".Translate(),
                         "PC_Tradeoff_Default_1_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Intellectual", 2), new ChoiceEffect("Social", -1) }),
                        ("PC_Tradeoff_Default_2".Translate(),
                         "PC_Tradeoff_Default_2_Hint".Translate(),
                         new List<ChoiceEffect> { new ChoiceEffect("Melee", 1), new ChoiceEffect("Intellectual", 1) }),
                    };
            }
        }

        // ------------------------------------------------------------------------
        //  CLIMAX DOORS -- Hard Road vs Easy Out
        // ------------------------------------------------------------------------

        /// <summary>
        /// Builds the two climax doors.
        ///
        /// Hard Road (isHardRoad=true):
        ///   2-day wait. CompPersonalChronicles fires the path's narrative incident on advance,
        ///   then calls CompleteEpic(true). Grants the redeemed backstory.
        ///
        /// Easy Out (isEasyOut=true):
        ///   Immediate condition (0-day wait). CompPersonalChronicles skips the incident
        ///   and calls CompleteEpic(false). Grants the corrupted backstory right now.
        /// </summary>
        public static List<StageChoice> BuildClimaxDoors(Pawn pawn, PersonalEpicDef epic, string pathTag)
        {
            string tag = pathTag.ToLowerInvariant().Replace("pc_tag_", "");

            string hardRoadLabel = GetHardRoadLabel(tag);
            string hardRoadHint  = GetHardRoadHint(tag);
            string easyOutLabel  = "PC_Climax_EasyOutLabel".Translate();
            string easyOutHint   = "PC_Climax_EasyOutHint".Translate();

            return new List<StageChoice>
            {
                new StageChoice
                {
                    tagDefName     = pathTag,
                    actionLabel    = hardRoadLabel,
                    mechanicalHint = hardRoadHint,
                    conditionKey   = "time",
                    conditionLabel = "PC_Wait_AwaitReckoning".Translate(),
                    baseline       = Find.TickManager.TicksGame,
                    targetDelta    = (int)(PawnChroniclesMod.Settings.hardRoadWaitDays * 60000f),
                    effects        = new List<ChoiceEffect>(),
                    isHardRoad     = true,
                    isEasyOut      = false
                },
                new StageChoice
                {
                    tagDefName     = pathTag,
                    actionLabel    = easyOutLabel,
                    mechanicalHint = easyOutHint,
                    conditionKey   = "time",
                    conditionLabel = "PC_Wait_TakeItNow".Translate(),
                    baseline       = Find.TickManager.TicksGame,
                    targetDelta    = 0, // immediately met
                    effects        = new List<ChoiceEffect>(),
                    isHardRoad     = false,
                    isEasyOut      = true
                }
            };
        }

        private static string GetHardRoadLabel(string tag) => tag switch
        {
            "violence" or "betrayal" or "survival" or "duty"
                => "PC_Climax_HardRoad_Violence".Translate(),
            "trauma" or "grief" or "loss" or "wandering" or "isolation" or "refugee" or "decay"
                => "PC_Climax_HardRoad_Hardship".Translate(),
            "healer" or "nurture"
                => "PC_Climax_HardRoad_Healer".Translate(),
            "animalfriend"
                => "PC_Climax_HardRoad_Animal".Translate(),
            "scholar" or "curiosity" or "augmentation"
                => "PC_Climax_HardRoad_Scholar".Translate(),
            "underworld"
                => "PC_Climax_HardRoad_Underworld".Translate(),
            "noble" or "leadership" or "power"
                => "PC_Climax_HardRoad_Leader".Translate(),
            "devotion" or "faith"
                => "PC_Climax_HardRoad_Faith".Translate(),
            "craft" or "artist"
                => "PC_Climax_HardRoad_Craft".Translate(),
            _ => "PC_Climax_HardRoad_Default".Translate()
        };

        private static string GetHardRoadHint(string tag)
        {
            string incidentType = GetClimaxIncidentType(tag);
            string consequence = incidentType switch
            {
                "SmallRaid"       => "PC_Climax_Consequence_SmallRaid".Translate(),
                "MentalBreak"     => "PC_Climax_Consequence_MentalBreak".Translate(),
                "FriendlyArrives" => "PC_Climax_Consequence_FriendlyArrives".Translate(),
                "HostileArrives"  => "PC_Climax_Consequence_HostileArrives".Translate(),
                _                 => "PC_Climax_Consequence_Default".Translate()
            };
            return "PC_Climax_HintFormat".Translate(FirstUpper(tag), consequence);
        }

        /// <summary>Maps a path tag to the climax incident type string for CompPersonalChronicles.</summary>
        public static string GetClimaxIncidentType(string tag) => tag switch
        {
            "violence" or "duty" or "survival" or "betrayal" or "leadership" or "power" or "noble"
                => "SmallRaid",
            "trauma" or "grief" or "loss" or "wandering" or "isolation" or "refugee" or "decay"
                => "MentalBreak",
            "animalfriend" or "healer" or "nurture" or "devotion" or "faith" or "kinship"
                => "FriendlyArrives",
            "scholar" or "curiosity" or "augmentation" or "underworld"
                => "HostileArrives",
            _ => "SmallRaid"
        };

        private static string FirstUpper(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

    }
}