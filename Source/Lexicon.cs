using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Grammar;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Lexicon - translates raw pawn data into grammar-ready words.
    ///
    /// Takes numbers and states from the scraper and converts them to
    /// single or short words the XML grammar can use directly.
    /// No prose, no sentences, no random pools - those belong in grammar XML.
    ///
    /// Keys emitted:
    ///   pc_lex_demeanor        single adjective from mood/traits
    ///   pc_lex_moral_state     short phrase from mood float
    ///   pc_lex_mood_phrase     short phrase from mood float (alt register)
    ///   pc_lex_wound_summary   short phrase from hediff set
    ///   pc_lex_combat_posture  short phrase from equipped weapon
    ///   pc_lex_social_anchor   short phrase from relation data
    ///   pc_lex_world_feel      short phrase from weather/season/biome
    ///   pc_lex_identity_echo   short phrase from backstory titles
    ///   pc_lex_tag_verb        past-tense verb from dominant narrative tag
    ///   pc_lex_scene           time+place phrase from map state
    ///   pc_lex_redemption_hint short phrase from dominant tag direction
    /// </summary>
    public static class Lexicon
    {
        // ─────────────────────────────────────────────────────────────────────
        //  ENTRY POINT
        // ─────────────────────────────────────────────────────────────────────

        public static List<Rule> GetDerivedRules(Pawn pawn, PawnNarrativeProfile profile)
        {
            var rules = new List<Rule>(32);
            if (pawn == null) return rules;

            EmitDemeanor(pawn, rules);
            EmitMoralState(pawn, rules);
            EmitMoodPhrase(pawn, rules);
            EmitWoundSummary(pawn, rules);
            EmitCombatPosture(pawn, rules);
            EmitSocialAnchor(pawn, rules);
            EmitWorldFeel(pawn, rules);
            EmitIdentityEcho(pawn, rules);
            EmitTagVerb(pawn, profile, rules);
            EmitScene(pawn, rules);
            EmitRedemptionHint(pawn, profile, rules);

            return rules;
        }

        // ===================================================================
        //  DEMEANOR - single adjective from mood/traits
        // ===================================================================

        private static void EmitDemeanor(Pawn pawn, List<Rule> rules)
        {
            Emit(rules, "pc_lex_demeanor", ComputeDemeanor(pawn));
        }

        private static string ComputeDemeanor(Pawn pawn)
        {
            if (pawn.story?.traits != null)
            {
                if (pawn.story.traits.HasTrait(TraitDefOf.Kind))       return "gentle";
                if (pawn.story.traits.HasTrait(TraitDefOf.Psychopath)) return "unreadable";
                if (pawn.story.traits.HasTrait(TraitDefOf.Bloodlust))  return "predatory";
                if (pawn.story.traits.HasTrait(TraitDefOf.Industriousness,  2)) return "focused";
                if (pawn.story.traits.HasTrait(TraitDefOf.Industriousness, -1)) return "unhurried";
                var nerves = DefDatabase<TraitDef>.GetNamedSilentFail("Nerves");
                if (nerves != null && pawn.story.traits.HasTrait(nerves, -1)) return "tense";
            }

            float mood = pawn.needs?.mood?.CurLevelPercentage ?? 0.5f;
            return mood switch
            {
                > 0.85f => "light-hearted",
                > 0.7f  => "steady",
                > 0.5f  => "carrying on",
                > 0.35f => "weary",
                > 0.2f  => "strained",
                _       => "heavy"
            };
        }

        // ===================================================================
        //  MORAL STATE - short phrase from mood float
        // ===================================================================

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
                > 0.9f  => "euphoric, almost dangerously so",
                > 0.75f => "content and steady",
                > 0.55f => "holding together",
                > 0.4f  => "fraying at the edges",
                > 0.25f => "on the verge of something",
                > 0.1f  => "close to the edge",
                _       => "past caring about most of it"
            };
            Emit(rules, "pc_lex_moral_state", state);
        }

        // ===================================================================
        //  MOOD PHRASE - alternate short phrase from mood float
        // ===================================================================

        private static void EmitMoodPhrase(Pawn pawn, List<Rule> rules)
        {
            if (pawn.needs?.mood == null)
            {
                Emit(rules, "pc_lex_mood_phrase", "unreadable");
                return;
            }

            float mood = pawn.needs.mood.CurLevelPercentage;
            string phrase = mood switch
            {
                > 0.85f => "mostly at peace with it",
                > 0.65f => "carrying on without complaint",
                > 0.45f => "quieter than usual",
                > 0.25f => "worn down",
                _       => "not doing well"
            };
            Emit(rules, "pc_lex_mood_phrase", phrase);
        }

        // ===================================================================
        //  WOUND SUMMARY - short phrase from hediff set
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
                Emit(rules, "pc_lex_wound_summary", "unmarked");
                return;
            }

            float pain = pawn.health.hediffSet.PainTotal;
            string partLabel = wounds[0].Part?.Label ?? "body";

            string summary = pain switch
            {
                > 0.75f => $"in constant pain from {partLabel}",
                > 0.4f  => $"aching where the {partLabel} damage sits",
                > 0.1f  => $"marked by old wounds on the {partLabel}",
                _       => "carrying scars"
            };

            Emit(rules, "pc_lex_wound_summary", summary);
        }

        // ===================================================================
        //  COMBAT POSTURE - short phrase from equipped weapon
        // ===================================================================

        private static void EmitCombatPosture(Pawn pawn, List<Rule> rules)
        {
            var primary = pawn.equipment?.Primary;
            if (primary == null)
            {
                Emit(rules, "pc_lex_combat_posture", "hands empty");
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
            string weaponLabel = string.IsNullOrEmpty(qualAdj)
                ? primary.LabelShort
                : $"{qualAdj} {primary.LabelShort}";

            Emit(rules, "pc_lex_combat_posture", $"{verb} a {weaponLabel}");
        }

        // ===================================================================
        //  SOCIAL ANCHOR - short phrase from relation data
        // ===================================================================

        private static void EmitSocialAnchor(Pawn pawn, List<Rule> rules)
        {
            if (pawn.relations == null)
            {
                Emit(rules, "pc_lex_social_anchor", "alone");
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
                Emit(rules, "pc_lex_social_anchor", $"near {friend.LabelShort}");
                return;
            }

            var rival = pawn.relations.PotentiallyRelatedPawns
                .Where(p => p.RaceProps.Humanlike && pawn.relations.OpinionOf(p) < -40)
                .OrderBy(p => pawn.relations.OpinionOf(p))
                .FirstOrDefault();

            if (rival != null)
            {
                Emit(rules, "pc_lex_social_anchor", $"avoiding {rival.LabelShort}");
                return;
            }

            Emit(rules, "pc_lex_social_anchor", "apart from the others");
        }

        // ===================================================================
        //  WORLD FEEL - short phrase from weather/season/biome
        // ===================================================================

        private static void EmitWorldFeel(Pawn pawn, List<Rule> rules)
        {
            if (pawn.Map == null)
            {
                Emit(rules, "pc_lex_world_feel", "adrift");
                return;
            }

            string weather = pawn.Map.weatherManager.curWeather.label;
            string season  = GenLocalDate.Season(pawn.Map).LabelCap();
            string biome   = pawn.Map.Biome?.label ?? "the wilds";

            // Pick the most specific descriptor available
            string feel = $"{weather}, {season}, {biome}";
            Emit(rules, "pc_lex_world_feel", feel);
        }

        // ===================================================================
        //  IDENTITY ECHO - short phrase from backstory titles
        // ===================================================================

        private static void EmitIdentityEcho(Pawn pawn, List<Rule> rules)
        {
            string? childhood = pawn.story?.Childhood?.title;
            string? adulthood = pawn.story?.Adulthood?.title;

            string echo;
            if (childhood != null && adulthood != null)
                echo = $"once {childhood}, now {adulthood}";
            else if (childhood != null)
                echo = $"former {childhood}";
            else if (adulthood != null)
                echo = adulthood;
            else
                echo = "unknown origins";

            Emit(rules, "pc_lex_identity_echo", echo);
        }

        // ===================================================================
        //  TAG VERB - past-tense verb from dominant narrative tag
        // ===================================================================

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
                "trauma"       => "flinched",
                "grief"        => "sat with it",
                "loss"         => "remembered",
                "violence"     => "held the line",
                "duty"         => "showed up",
                "betrayal"     => "kept count",
                "survival"     => "kept moving",
                "wandering"    => "moved on",
                "isolation"    => "stayed quiet",
                "noble"        => "decreed",
                "leadership"   => "decided",
                "power"        => "pressed forward",
                "underworld"   => "said nothing",
                "devotion"     => "knelt",
                "faith"        => "observed",
                "kinship"      => "stayed close",
                "craft"        => "kept working",
                "artist"       => "made something",
                "scholar"      => "took notes",
                "curiosity"    => "kept looking",
                "healer"       => "tended to it",
                "nurture"      => "looked after someone",
                "animalfriend" => "listened",
                "resilience"   => "continued",
                "decay"        => "endured",
                "pacifism"     => "stepped back",
                "refugee"      => "kept going",
                "augmentation" => "calculated",
                _              => "waited"
            };
        }

        // ===================================================================
        //  SCENE - time+place phrase from map state
        // ===================================================================

        private static void EmitScene(Pawn pawn, List<Rule> rules)
        {
            if (pawn.Map == null)
            {
                Emit(rules, "pc_lex_scene", "adrift");
                return;
            }

            int hour = GenLocalDate.HourOfDay(pawn.Map);
            string timePhrase = hour switch
            {
                < 5  => "in the middle of the night",
                < 7  => "in the early morning",
                < 10 => "mid-morning",
                < 13 => "at midday",
                < 16 => "in the afternoon",
                < 19 => "in the fading light",
                < 22 => "as evening settled",
                _    => "late at night"
            };

            string biome   = pawn.Map.Biome?.label ?? "the wilds";
            string weather = pawn.Map.weatherManager?.curWeather?.label ?? "open sky";
            bool   raining = (pawn.Map.weatherManager?.curWeather?.rainRate ?? 0f) > 0.1f;

            string scene = raining
                ? $"{timePhrase}, {weather} falling over the {biome}"
                : $"{timePhrase}, {biome}";

            Emit(rules, "pc_lex_scene", scene);
        }

        // ===================================================================
        //  REDEMPTION HINT - short phrase from dominant tag direction
        // ===================================================================

        private static void EmitRedemptionHint(Pawn pawn, PawnNarrativeProfile profile, List<Rule> rules)
        {
            var dominant = profile?.GetDominantTags(1);
            if (dominant == null || dominant.Count == 0)
            {
                Emit(rules, "pc_lex_redemption_hint", "becoming someone different");
                return;
            }

            string hint = dominant[0].defName.ToLower().Replace("pc_tag_", "") switch
            {
                "trauma" or "grief" or "loss"  => "carrying it differently",
                "violence" or "duty"            => "figuring out what comes after the fighting",
                "noble" or "leadership"         => "working out what the title actually means",
                "devotion" or "faith"           => "testing whether it holds",
                "isolation" or "wandering"      => "finding out if staying is possible",
                "craft" or "artist"             => "making something that will outlast [pawn_objective]",
                "scholar" or "curiosity"        => "following the question further than is comfortable",
                "kinship"                       => "finding out who is still there",
                "healer" or "nurture"           => "learning the difference between fixing and caring",
                "betrayal"                      => "working out whether [pawn_pronoun] can be trusted now",
                "resilience"                    => "still here",
                "underworld"                    => "getting clear of it",
                "survival"                      => "somewhere safer than before",
                "animalfriend"                  => "understanding something without words",
                "decay"                         => "outlasting what is trying to end [pawn_objective]",
                _                               => "becoming someone different"
            };

            Emit(rules, "pc_lex_redemption_hint", hint);
        }

        // ===================================================================
        //  MECHANICAL REASONS (entangled arc feedback)
        // ===================================================================

        public static void EmitMechanicalFailureReason(EntangledArcState arc, List<Rule> rules)
        {
            if (arc.initiator == null || arc.partner == null)
            {
                Emit(rules, "pc_lex_mechanical_failure_reason", "unknown factors");
                return;
            }

            var reasons = new List<string>();

            int opinion = arc.initiator.relations?.OpinionOf(arc.partner) ?? 0;
            if (opinion < -20)
                reasons.Add($"relationship was {opinion} (very poor)");
            else if (opinion < 10)
                reasons.Add($"relationship was only {opinion}");

            if (arc.ArcDef?.arcType == EntangledArcType.MentorApprentice)
            {
                int mentorSkill = arc.initiator.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                if (mentorSkill - (arc.partner.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0) < 4)
                    reasons.Add("skill gap too narrow for meaningful teaching");
                else if (mentorSkill < 6)
                    reasons.Add($"mentor skill level only {mentorSkill}");
            }

            int arcDays = (Find.TickManager.TicksGame - arc.startedAtTick) / 60000;
            if (arcDays < 8)
                reasons.Add($"arc resolved too quickly (only {arcDays} days)");

            Emit(rules, "pc_lex_mechanical_failure_reason",
                reasons.Count > 0 ? string.Join("; ", reasons) : "insufficient conditions met");
        }

        public static void EmitMechanicalSuccessReason(EntangledArcState arc, List<Rule> rules)
        {
            if (arc.initiator == null || arc.partner == null)
            {
                Emit(rules, "pc_lex_mechanical_success_reason", "conditions were met");
                return;
            }

            var reasons = new List<string>();

            int opinion = arc.initiator.relations?.OpinionOf(arc.partner) ?? 0;
            if (opinion > 40)
                reasons.Add($"strong bond ({opinion})");
            else if (opinion > 15)
                reasons.Add($"positive relationship ({opinion})");

            if (arc.ArcDef?.arcType == EntangledArcType.MentorApprentice)
            {
                int mentorSkill = arc.initiator.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                if (mentorSkill >= 10)
                    reasons.Add($"mentor expertise (Social {mentorSkill})");
            }

            int arcDays = (Find.TickManager.TicksGame - arc.startedAtTick) / 60000;
            if (arcDays > 15)
                reasons.Add($"given enough time ({arcDays} days)");

            Emit(rules, "pc_lex_mechanical_success_reason",
                reasons.Count > 0 ? string.Join("; ", reasons) : "conditions were sufficiently met");
        }

        // ===================================================================
        //  HELPERS
        // ===================================================================

        private static void Emit(List<Rule> rules, string keyword, string output)
        {
            if (string.IsNullOrEmpty(output)) return;
            rules.Add(new Rule_String(keyword, output));
        }
    }
}
