using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Grammar;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Enhanced Lexicon - deeply integrated with RimWorld systems.
    /// Heavy focus on everyday life, small overlooked moments, and narrative evolution.
    /// </summary>
    public static class Lexicon
    {
        // ─────────────────────────────────────────────────────────────────────
        //  ENTRY POINT
        // ─────────────────────────────────────────────────────────────────────

        public static List<Rule> GetDerivedRules(Pawn pawn, PawnNarrativeProfile profile)
        {
            var rules = new List<Rule>(80);

            if (pawn == null) return rules;

            EmitWoundSummary(pawn, rules);
            EmitDemeanor(pawn, rules);
            EmitCombatPosture(pawn, rules);
            EmitSocialAnchor(pawn, rules);
            EmitWorldFeel(pawn, rules);
            EmitIdentityEcho(pawn, rules);
            EmitMoralState(pawn, rules);
            EmitTagVerb(pawn, profile, rules);
            EmitScene(pawn, rules);

            // ── EVERYDAY LIFE LAYER ─────────────────────────────────────
            EmitEverydayLife(pawn, rules);
            EmitSmallMercies(pawn, rules);
            EmitRoutineEcho(pawn, rules);
            EmitQuietMoments(pawn, rules);

            // ── NARRATIVE EVOLUTION LAYER ───────────────────────────────
            EmitRedemptionHint(pawn, profile, rules);
            EmitMemoryEcho(pawn, rules);
            EmitJobEcho(pawn, rules);

            return rules;
        }

        // ===================================================================
        //  LEGACY BRIDGE (kept for compatibility)
        // ===================================================================

        public static string GetVerbFor(NarrativeTagDef tag, Pawn pawn)
            => ComputeTagVerb(tag, pawn);

        public static string GetAdverbFor(NarrativeTagDef tag, Pawn pawn)
            => ComputeDemeanor(pawn);

        public static string GetInfinitiveFor(NarrativeTagDef tag)
            => tag.label.ToLower() switch
            {
                "trauma"     => "to endure",
                "noble"      => "to command",
                "underworld" => "to deceive",
                "devotion"   => "to serve",
                "war"        => "to conquer",
                "frontier"   => "to explore",
                _            => "to persevere"
            };

        public static string GetRandomCopular()
        {
            var options = new[] { "is", "remains", "seems", "appears", "stands" };
            return options[Rand.Range(0, options.Length)];
        }

        // ===================================================================
        //  EVERYDAY LIFE - Small, Overlooked Moments
        // ===================================================================

        private static void EmitEverydayLife(Pawn pawn, List<Rule> rules)
        {
            Emit(rules, "pc_lex_daily_rhythm", ComputeDailyRhythm(pawn));
            Emit(rules, "pc_lex_small_comfort", ComputeSmallComfort(pawn));
            Emit(rules, "pc_lex_quiet_frustration", ComputeQuietFrustration(pawn));
            Emit(rules, "pc_lex_tiny_joy", ComputeTinyJoy(pawn));
        }

        private static string ComputeDailyRhythm(Pawn pawn)
        {
            if (pawn.Map == null) return "adrift in the quiet hours";
            int hour = GenLocalDate.HourOfDay(pawn.Map);
            return hour switch
            {
                < 5  => "in the deep hush before dawn",
                < 9  => "with the first pale light on [pawn_possessive] hands",
                < 12 => "mid-morning, tools already warm from use",
                < 15 => "in the long steady hours of honest work",
                < 18 => "as the light begins its slow retreat",
                < 22 => "in the soft wind-down of evening",
                _    => "deep into the quiet hours"
            };
        }

        private static string ComputeSmallComfort(Pawn pawn)
        {
            var options = new[]
            {
                "a mug that fits [pawn_possessive] hand just right",
                "the smell of fresh bread drifting from the kitchen",
                "a patch of sunlight warming the workbench",
                "the familiar creak of [pawn_possessive] favorite chair",
                "clean socks after a long muddy day",
                "a tool that finally behaves on the first try",
                "the gentle rhythm of rain on the roof while inside"
            };
            return options.RandomElement();
        }

        private static string ComputeQuietFrustration(Pawn pawn)
        {
            var options = new[]
            {
                "the hinge that still squeaks no matter what",
                "that one sock that always disappears",
                "the same conversation that never quite resolves",
                "a door that never quite shuts properly",
                "the weather ruining another plan",
                "a joke [pawn_pronoun] has told three times this week"
            };
            return options.RandomElement();
        }

        private static string ComputeTinyJoy(Pawn pawn)
        {
            var options = new[]
            {
                "a perfectly ripe berry that survived the trip from field to mouth",
                "the fire burning clean and bright tonight",
                "someone remembering exactly how [pawn_pronoun] likes [pawn_possessive] tea",
                "a small laugh that slipped out unexpectedly",
                "finding something [pawn_pronoun] thought was lost for good"
            };
            return options.RandomElement();
        }

        // ===================================================================
        //  SMALL MERCIES & ROUTINE ECHOES
        // ===================================================================

        private static void EmitSmallMercies(Pawn pawn, List<Rule> rules)
        {
            Emit(rules, "pc_lex_small_mercy", ComputeSmallMercy(pawn));
        }

        private static string ComputeSmallMercy(Pawn pawn)
        {
            var options = new[]
            {
                "a moment where nothing demanded [pawn_possessive] attention",
                "a tool that worked on the first try",
                "someone remembering how [pawn_pronoun] takes [pawn_possessive] tea",
                "the fire burning clean tonight",
                "a quiet stretch of path with no emergencies",
                "finding something [pawn_pronoun] thought was lost"
            };
            return options.RandomElement();
        }

        private static void EmitRoutineEcho(Pawn pawn, List<Rule> rules)
        {
            Emit(rules, "pc_lex_routine_echo", ComputeRoutineEcho(pawn));
        }

        private static string ComputeRoutineEcho(Pawn pawn)
        {
            if (pawn.skills?.GetSkill(SkillDefOf.Construction)?.Level > 10)
                return "the quiet satisfaction of a job done the same way for years";
            return "the small comfort of muscle memory doing its work";
        }

        private static void EmitQuietMoments(Pawn pawn, List<Rule> rules)
        {
            Emit(rules, "pc_lex_quiet_moment", ComputeQuietMoment(pawn));
        }

        private static string ComputeQuietMoment(Pawn pawn)
        {
            var options = new[]
            {
                "watching steam rise from a fresh pot of stew",
                "the awkward silence after a joke that didn't land",
                "sorting socks that somehow never match",
                "pausing to watch a bug crawl across the table",
                "listening to the distant sound of someone else's laughter"
            };
            return options.RandomElement();
        }

        // ===================================================================
        //  NARRATIVE EVOLUTION LAYER
        // ===================================================================

        private static void EmitRedemptionHint(Pawn pawn, PawnNarrativeProfile profile, List<Rule> rules)
        {
            var dominant = profile.GetDominantTags(1);
            if (dominant.Count == 0) return;

            string hint = dominant[0].defName.ToLower() switch
            {
                "trauma" or "grief" => "trying not to let the old wound speak today",
                "violence" => "holding violence a little more gently than before",
                "noble" => "carrying a name [pawn_pronoun] is trying to deserve",
                "devotion" or "faith" => "believing a little more quietly",
                "isolation" => "testing the edges of solitude",
                _ => "becoming someone [pawn_pronoun] barely recognizes"
            };

            Emit(rules, "pc_lex_redemption_hint", hint);
        }

        private static void EmitMemoryEcho(Pawn pawn, List<Rule> rules)
        {
            string echo = "the person [pawn_nameDef] used to be still walks beside [pawn_objective]";
            Emit(rules, "pc_lex_memory_echo", echo);
        }

        private static void EmitJobEcho(Pawn pawn, List<Rule> rules)
        {
            // The Surgical Patch: Guard against null pawn or null job definition
            if (pawn == null) return;

            if (pawn.CurJob?.def == null)
            {
                Emit(rules, "pc_lex_job_echo", "resting for a moment");
                return;
            }

            // Capture label safely to avoid potential property-getter nulls
            string label = pawn.CurJob.def.label?.ToLower() ?? string.Empty;

            string jobEcho = label switch
            {
                "haul" => "hauling something heavy again",
                "mine" => "chipping away at the stone",
                "cook" => "stirring the pot with practiced hands",
                "construct" => "building something that will outlast [pawn_objective]",
                "clean" => "wiping away the day's grime",
                _ => "working through another ordinary task"
            };

            Emit(rules, "pc_lex_job_echo", jobEcho);
        }

        // ===================================================================
        //  CORE DERIVED EMITTERS (Improved)
        // ===================================================================

        private static void EmitWoundSummary(Pawn pawn, List<Rule> rules)
        {
            if (pawn.health?.hediffSet == null)
            {
                Emit(rules, "pc_lex_wound_summary", "unmarked");
                return;
            }

            var wounds = pawn.health.hediffSet.hediffs
                .Where(h => h.Visible && h.def.isBad)
                .OrderByDescending(h => h.Severity)
                .ToList();

            if (wounds.Count == 0)
            {
                Emit(rules, "pc_lex_wound_summary", "unblemished");
                return;
            }

            float pain = pawn.health.hediffSet.PainTotal;
            string painWord = pain switch
            {
                > 0.75f => "in constant pain",
                > 0.4f  => "aching",
                > 0.1f  => "bearing scars",
                _       => "marked by old wounds"
            };

            string part = wounds[0].Part?.Label ?? "body";
            Emit(rules, "pc_lex_wound_summary", $"{painWord} on the {part}");
        }

        private static void EmitDemeanor(Pawn pawn, List<Rule> rules)
        {
            Emit(rules, "pc_lex_demeanor", ComputeDemeanor(pawn));
        }

        private static string ComputeDemeanor(Pawn pawn)
        {
            if (pawn.story?.traits != null)
            {
                if (pawn.story.traits.HasTrait(TraitDefOf.Kind)) return "gentle";
                if (pawn.story.traits.HasTrait(TraitDefOf.Psychopath)) return "unreadable";
                if (pawn.story.traits.HasTrait(TraitDefOf.Bloodlust)) return "predatory";
            }

            float mood = pawn.needs?.mood?.CurLevelPercentage ?? 0.5f;
            return mood switch
            {
                > 0.85f => "light-hearted",
                > 0.6f  => "steady",
                > 0.35f => "weary",
                _       => "heavy"
            };
        }

        private static void EmitCombatPosture(Pawn pawn, List<Rule> rules)
        {
            var primary = pawn.equipment?.Primary;
            if (primary == null)
            {
                Emit(rules, "pc_lex_combat_posture", "hands empty and restless");
                return;
            }

            primary.TryGetQuality(out QualityCategory qc);
            string qualAdj = qc switch
            {
                QualityCategory.Legendary  => "legendary",
                QualityCategory.Masterwork => "masterwork",
                QualityCategory.Excellent  => "fine",
                _                          => ""
            };

            string verb = primary.def.IsMeleeWeapon ? "gripping" : "shouldering";
            string label = string.IsNullOrEmpty(qualAdj)
                ? primary.LabelShort
                : $"{qualAdj} {primary.LabelShort}";

            Emit(rules, "pc_lex_combat_posture", $"{verb} a {label}");
        }

        private static void EmitSocialAnchor(Pawn pawn, List<Rule> rules)
        {
            if (pawn.relations == null)
            {
                Emit(rules, "pc_lex_social_anchor", "alone in thought");
                return;
            }

            var lover = pawn.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Lover) ??
                        pawn.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Spouse);

            if (lover != null)
            {
                Emit(rules, "pc_lex_social_anchor", $"thinking of {lover.LabelShort}");
                return;
            }

            var friend = pawn.relations.PotentiallyRelatedPawns
                .Where(p => p.RaceProps.Humanlike && pawn.relations.OpinionOf(p) > 40)
                .OrderByDescending(p => pawn.relations.OpinionOf(p))
                .FirstOrDefault();

            if (friend != null)
            {
                Emit(rules, "pc_lex_social_anchor", $"standing near {friend.LabelShort}");
                return;
            }

            var rival = pawn.relations.PotentiallyRelatedPawns
                .Where(p => p.RaceProps.Humanlike && pawn.relations.OpinionOf(p) < -40)
                .OrderBy(p => pawn.relations.OpinionOf(p))
                .FirstOrDefault();

            if (rival != null)
            {
                Emit(rules, "pc_lex_social_anchor", $"avoiding the gaze of {rival.LabelShort}");
                return;
            }

            Emit(rules, "pc_lex_social_anchor", "alone in the crowd");
        }

        private static void EmitWorldFeel(Pawn pawn, List<Rule> rules)
        {
            if (pawn.Map == null)
            {
                Emit(rules, "pc_lex_world_feel", "adrift among the stars");
                return;
            }

            string weather = pawn.Map.weatherManager.curWeather.label;
            string season = GenLocalDate.Season(pawn.Map).LabelCap();

            Emit(rules, "pc_lex_world_feel", $"beneath the {weather} of {season}");
        }

        private static void EmitIdentityEcho(Pawn pawn, List<Rule> rules)
        {
            string? childhood = pawn.story?.Childhood?.title;
            string? adulthood = pawn.story?.Adulthood?.title;

            if (childhood != null && adulthood != null)
                Emit(rules, "pc_lex_identity_echo", $"once {childhood}, now {adulthood}");
            else if (childhood != null)
                Emit(rules, "pc_lex_identity_echo", $"shaped by a childhood as {childhood}");
            else
                Emit(rules, "pc_lex_identity_echo", "of unknown origins");
        }

        private static void EmitMoralState(Pawn pawn, List<Rule> rules)
        {
            if (pawn.needs?.mood == null)
            {
                Emit(rules, "pc_lex_moral_state", "beyond reading");
                return;
            }

            float mood = pawn.needs.mood.CurLevelPercentage;
            string state = mood switch
            {
                > 0.9f => "euphoric, almost dangerously so",
                > 0.7f => "content and steady",
                > 0.5f => "holding together",
                > 0.3f => "fraying at the edges",
                > 0.1f => "on the verge of breaking",
                _      => "shattered"
            };

            Emit(rules, "pc_lex_moral_state", state);
        }

        private static void EmitTagVerb(Pawn pawn, PawnNarrativeProfile profile, List<Rule> rules)
        {
            if (profile == null)
            {
                Emit(rules, "pc_lex_tag_verb", "waited");
                return;
            }

            var dominant = profile.GetDominantTags();
            if (dominant.Count == 0)
            {
                Emit(rules, "pc_lex_tag_verb", "waited");
                return;
            }

            Emit(rules, "pc_lex_tag_verb", ComputeTagVerb(dominant[0], pawn));
        }

        private static string ComputeTagVerb(NarrativeTagDef tag, Pawn pawn)
        {
            if (pawn.InMentalState && pawn.MentalStateDef != null)
            {
                string letter = pawn.MentalStateDef.beginLetterLabel;
                if (!string.IsNullOrEmpty(letter))
                    return letter.ToLower().Replace("pawn", "").Trim();
            }

            return tag.label.ToLower() switch
            {
                "trauma"     => "flinched",
                "noble"      => "decreed",
                "underworld" => "schemed",
                "devotion"   => "knelt",
                "war"        => "charged",
                "frontier"   => "pressed forward",
                "science"    => "calculated",
                "beast"      => "snarled",
                _            => "waited"
            };
        }

        private static void EmitScene(Pawn pawn, List<Rule> rules)
        {
            if (pawn.Map == null)
            {
                Emit(rules, "pc_lex_scene", "adrift somewhere beyond the horizon");
                return;
            }

            int hour = GenLocalDate.HourOfDay(pawn.Map);
            string timePhrase = hour switch
            {
                < 5  => "in the middle of the night",
                < 8  => "in the early morning",
                < 12 => "at midday",
                < 15 => "in the afternoon",
                < 18 => "at dinner-time",
                < 21 => "in the fading evening light",
                _    => "under a night sky"
            };

            string biome   = pawn.Map.Biome?.label ?? "the wilds";
            string weather = pawn.Map.weatherManager?.curWeather?.label ?? "open sky";

            bool isRaining = pawn.Map.weatherManager?.curWeather?.rainRate > 0.1f;
            string scene   = isRaining
                ? $"{timePhrase} as {weather} falls over the {biome}"
                : $"{timePhrase}, the {biome} spread out around {pawn.gender.GetPronoun()}";

            Emit(rules, "pc_lex_scene", scene);
        }

        /// <summary>
        /// Generates a player-visible mechanical reason for failure.
        /// Used in letters and Chronicles tab.
        /// </summary>
        public static void EmitMechanicalFailureReason(EntangledArcState arc, List<Rule> rules)
        {
            if (arc.initiator == null || arc.partner == null)
            {
                Emit(rules, "pc_lex_mechanical_failure_reason", "unknown factors");
                return;
            }

            var reasons = new List<string>();

            // Relationship strength
            int opinion = arc.initiator.relations?.OpinionOf(arc.partner) ?? 0;
            if (opinion < -20)
                reasons.Add($"relationship was {opinion} (very poor)");
            else if (opinion < 10)
                reasons.Add($"relationship was only {opinion}");

            // Skill gap (especially mentor/apprentice)
            if (arc.ArcDef?.arcType == EntangledArcType.MentorApprentice)
            {
                int mentorSkill = arc.initiator.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                int apprenticeSkill = arc.partner.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                if (mentorSkill - apprenticeSkill < 4)
                    reasons.Add($"skill gap too narrow for meaningful teaching");
                else if (mentorSkill < 6)
                    reasons.Add($"mentor skill level only {mentorSkill}");
            }

            // Time pressure
            int arcDays = (Find.TickManager.TicksGame - arc.startedAtTick) / 60000;
            if (arcDays < 8)
                reasons.Add($"arc resolved too quickly (only {arcDays} days)");

            string finalReason = reasons.Count > 0 
                ? string.Join("; ", reasons) 
                : "insufficient conditions met";

            Emit(rules, "pc_lex_mechanical_failure_reason", finalReason);
        }

        /// <summary>
        /// Generates a player-visible mechanical reason for SUCCESS.
        /// </summary>
        public static void EmitMechanicalSuccessReason(EntangledArcState arc, List<Rule> rules)
        {
            if (arc.initiator == null || arc.partner == null)
            {
                Emit(rules, "pc_lex_mechanical_success_reason", "conditions were met");
                return;
            }

            var reasons = new List<string>();

            // Strong relationship
            int opinion = arc.initiator.relations?.OpinionOf(arc.partner) ?? 0;
            if (opinion > 40)
                reasons.Add($"strong bond ({opinion})");
            else if (opinion > 15)
                reasons.Add($"positive relationship ({opinion})");

            // Skill contribution (mentor/apprentice)
            if (arc.ArcDef?.arcType == EntangledArcType.MentorApprentice)
            {
                int mentorSkill = arc.initiator.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                if (mentorSkill >= 10)
                    reasons.Add($"mentor expertise (Social {mentorSkill})");
            }

            // Time invested
            int arcDays = (Find.TickManager.TicksGame - arc.startedAtTick) / 60000;
            if (arcDays > 15)
                reasons.Add($"given enough time ({arcDays} days)");

            string finalReason = reasons.Count > 0 
                ? string.Join("; ", reasons) 
                : "conditions were sufficiently met";

            Emit(rules, "pc_lex_mechanical_success_reason", finalReason);
        }

        private static void Emit(List<Rule> rules, string keyword, string output)
        {
            if (string.IsNullOrEmpty(output)) return;
            rules.Add(new Rule_String(keyword, output));
        }
    }
}