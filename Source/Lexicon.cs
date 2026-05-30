using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Grammar;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Lexicon - derives pawn-specific narrative rules from live game state.
    /// Output strings may contain RimWorld grammar tags ([Animal], [TerrainFeature], etc.)
    /// which are resolved downstream by the grammar resolver.
    /// </summary>
    public static class Lexicon
    {
        // ─────────────────────────────────────────────────────────────────────
        //  ENTRY POINT
        // ─────────────────────────────────────────────────────────────────────

        public static List<Rule> GetDerivedRules(Pawn pawn, PawnNarrativeProfile profile)
        {
            var rules = new List<Rule>(100);
            if (pawn == null) return rules;

            // Core identity
            EmitWoundSummary(pawn, rules);
            EmitDemeanor(pawn, rules);
            EmitCombatPosture(pawn, rules);
            EmitSocialAnchor(pawn, rules);
            EmitWorldFeel(pawn, rules);
            EmitIdentityEcho(pawn, rules);
            EmitMoralState(pawn, rules);
            EmitMoodPhrase(pawn, rules);
            EmitTagVerb(pawn, profile, rules);
            EmitScene(pawn, rules);

            // Everyday life layer
            EmitEverydayLife(pawn, rules);
            EmitSmallMercies(pawn, rules);
            EmitRoutineEcho(pawn, rules);
            EmitQuietMoments(pawn, rules);

            // Narrative evolution layer
            EmitRedemptionHint(pawn, profile, rules);
            EmitMemoryEcho(pawn, rules);
            EmitJobEcho(pawn, rules);

            return rules;
        }

        // ===================================================================
        //  LEGACY BRIDGE
        // ===================================================================

        public static string GetVerbFor(NarrativeTagDef tag, Pawn pawn) => ComputeTagVerb(tag, pawn);
        public static string GetAdverbFor(NarrativeTagDef tag, Pawn pawn) => ComputeDemeanor(pawn);

        public static string GetInfinitiveFor(NarrativeTagDef tag)
            => tag.label.ToLower() switch
            {
                "trauma"       => "to endure",
                "noble"        => "to command",
                "underworld"   => "to deceive",
                "devotion"     => "to serve",
                "violence"     => "to fight",
                "kinship"      => "to belong",
                "grief"        => "to grieve",
                "loss"         => "to remember",
                "craft"        => "to build",
                "scholar"      => "to understand",
                "curiosity"    => "to question",
                "survival"     => "to endure",
                "wandering"    => "to move",
                "isolation"    => "to remain alone",
                "animalfriend" => "to listen",
                "healer"       => "to mend",
                "betrayal"     => "to reckon",
                "resilience"   => "to continue",
                _              => "to persevere"
            };

        public static string GetRandomCopular()
        {
            var options = new[] { "is", "remains", "seems", "appears", "stands" };
            return options[Rand.Range(0, options.Length)];
        }

        // ===================================================================
        //  EVERYDAY LIFE
        // ===================================================================

        private static void EmitEverydayLife(Pawn pawn, List<Rule> rules)
        {
            Emit(rules, "pc_lex_daily_rhythm",       ComputeDailyRhythm(pawn));
            Emit(rules, "pc_lex_small_comfort",      ComputeSmallComfort(pawn));
            Emit(rules, "pc_lex_quiet_frustration",  ComputeQuietFrustration(pawn));
            Emit(rules, "pc_lex_tiny_joy",           ComputeTinyJoy(pawn));
        }

        private static string ComputeDailyRhythm(Pawn pawn)
        {
            if (pawn.Map == null) return "adrift in the quiet hours";
            int hour = GenLocalDate.HourOfDay(pawn.Map);
            return hour switch
            {
                < 5  => "in the deep hush before dawn",
                < 7  => "with the first pale light falling over [NaturalObject]s",
                < 10 => "mid-morning, [pawn_possessive] hands already warm from work",
                < 13 => "in the long steady hours, [TerrainFeature] visible from the workbench",
                < 16 => "through the heavy [AdjectiveNatural] [pc_world_time_of_day] hours",
                < 19 => "as the light begins its slow retreat over the [TerrainFeature]",
                < 22 => "in the soft wind-down, [Animal]s settling somewhere nearby",
                _    => "deep into the quiet hours, the [TerrainFeature] dark outside"
            };
        }

        private static string ComputeSmallComfort(Pawn pawn)
        {
            var options = new[]
            {
                "a mug that fits [pawn_possessive] hand just right",
                "the smell of something warm drifting from across [pc_colony_name]",
                "a patch of [Color] light warming [pawn_possessive] workbench",
                "the familiar creak of [pawn_possessive] favourite chair",
                "clean socks after a long muddy day",
                "a tool that behaves on the first try",
                "the gentle rhythm of rain on the roof while inside",
                "a [AdjectiveNatural] [Animal] that decided not to run",
                "the fire burning clean and quiet",
                "a corner of the [TerrainFeature] that no one else uses",
                "the sound of [pawn_possessive] own breathing after a long shift",
                "a perfectly ripe [Color] fruit that survived the trip from field to mouth",
                "a [pc_lex_bug] that sat still long enough to properly observe",
                "the [pc_lex_sound] of [pc_colony_name] settling at night",
            };
            return options.RandomElement();
        }

        private static string ComputeQuietFrustration(Pawn pawn)
        {
            var options = new[]
            {
                "the hinge that still squeaks no matter what",
                "a door that never quite shuts properly",
                "the weather ruining another plan",
                "a joke [pawn_pronoun] has told three times this week",
                "the [AdjectiveNatural] [Animal] that keeps getting into [pc_colony_name]",
                "a perfectly good [TerrainFeature] that someone keeps walking through",
                "the [Color] stain on [pawn_possessive] favourite thing that won't come out",
                "that one task that keeps appearing at the bottom of the list",
                "the same conversation that never quite resolves",
                "a [pc_lex_bug] in the food storage that no one else seems concerned about",
                "the [pc_lex_sound] in the walls that only [pawn_pronoun] seems to hear",
            };
            return options.RandomElement();
        }

        private static string ComputeTinyJoy(Pawn pawn)
        {
            var options = new[]
            {
                "a perfectly ripe berry that survived the trip from field to mouth",
                "the fire burning [AdjectiveFriendly] and bright tonight",
                "someone remembering exactly how [pawn_pronoun] likes [pawn_possessive] tea",
                "a small laugh that slipped out unexpectedly",
                "finding something [pawn_pronoun] thought was lost for good",
                "a [Color] [Animal] passing close enough to notice",
                "the [TerrainFeature] looking [AdjectiveNatural] at exactly the right moment",
                "a [AdjectiveFriendly] word from someone who didn't have to say it",
                "waking up before the alarm and feeling good about it",
                "a [NaturalObject] that caught the light just right",
                "a [pc_lex_bug] doing something [pawn_pronoun] watched for longer than intended",
                "the [pc_lex_sound] of [pc_colony_name] at its most ordinary",
            };
            return options.RandomElement();
        }

        // ===================================================================
        //  SMALL MERCIES AND ROUTINE
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
                "finding something [pawn_pronoun] thought was lost",
                "a [AdjectiveNatural] [Animal] that moved aside without trouble",
                "the [TerrainFeature] clear when [pawn_pronoun] needed it to be",
                "no one asking anything of [pawn_objective] for a full hour",
            };
            return options.RandomElement();
        }

        private static void EmitRoutineEcho(Pawn pawn, List<Rule> rules)
        {
            string echo = "the small comfort of muscle memory doing its work";

            if (pawn.skills != null)
            {
                var best = pawn.skills.skills
                    .Where(s => !s.TotallyDisabled)
                    .OrderByDescending(s => s.Level)
                    .FirstOrDefault();

                if (best != null && best.Level > 8)
                    echo = $"the quiet certainty of someone who has done this [AdjectiveFriendly] thing for years";
                else if (best != null && best.Level > 4)
                    echo = "the small comfort of muscle memory doing its work";
            }

            Emit(rules, "pc_lex_routine_echo", echo);
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
                "listening to the [AdjectiveNatural] [pc_lex_sound] of [Animal]s settling for the night",
                "pausing to watch a [Color] [Animal] cross the [TerrainFeature]",
                "listening to the distant [pc_lex_sound] of someone else's laughter",
                "sitting with [pc_friend_name] without needing to say anything",
                "watching [NaturalObject]s bend in the wind near the [TerrainFeature]",
                "standing at the edge of [pc_colony_name] looking out at the [pc_world_biome]",
                "counting [Color] [NaturalObject]s that no one else seems to notice",
                "a [AnimalBadass] moving through the [TerrainFeature] as if [pc_colony_name] wasn't there",
                "tracking a [pc_lex_bug] across the table until it found somewhere better to be",
                "listening to the [pc_lex_sound_angsty] from the next room and deciding not to ask",
                "the [pc_lex_sound_badass] outside that turned out to be nothing",
            };
            return options.RandomElement();
        }

        // ===================================================================
        //  NARRATIVE EVOLUTION
        // ===================================================================

        private static void EmitRedemptionHint(Pawn pawn, PawnNarrativeProfile profile, List<Rule> rules)
        {
            var dominant = profile?.GetDominantTags(1);
            if (dominant == null || dominant.Count == 0)
            {
                Emit(rules, "pc_lex_redemption_hint", "becoming someone [pawn_pronoun] barely recognizes");
                return;
            }

            string hint = dominant[0].defName.ToLower().Replace("pc_tag_", "") switch
            {
                "trauma" or "grief" or "loss" => "learning to carry it differently",
                "violence" or "duty"           => "finding out what [pawn_pronoun] is when the fighting stops",
                "noble" or "leadership"        => "working out what the name actually means",
                "devotion" or "faith"          => "testing whether the belief holds",
                "isolation" or "wandering"     => "discovering if staying is possible",
                "craft" or "artist"            => "making something that will last past [pawn_objective]",
                "scholar" or "curiosity"       => "following the question to the part that frightens [pawn_objective]",
                "kinship"                      => "finding out who is still there",
                "healer" or "nurture"          => "learning the difference between fixing and caring",
                "betrayal"                     => "working out whether [pawn_pronoun] can be trusted now",
                "resilience"                   => "still being here",
                "underworld"                   => "getting clear of it, maybe",
                "survival"                     => "being somewhere safer than before",
                "animalfriend"                 => "understanding something without words",
                "decay"                        => "outlasting what is trying to end [pawn_objective]",
                _                              => "becoming someone [pawn_pronoun] barely recognizes"
            };

            Emit(rules, "pc_lex_redemption_hint", hint);
        }

        private static void EmitMemoryEcho(Pawn pawn, List<Rule> rules)
        {
            var options = new[]
            {
                "the person [pawn_nameDef] used to be still walks beside [pawn_objective]",
                "once [pc_backstory_child_title], now [pc_backstory_adult_title], still working out what that means",
                "carrying [pc_backstory_child_title] years into a life [pawn_pronoun] did not plan",
                "[pawn_possessive] [pc_backstory_child_title] past shows up in the small things",
            };
            Emit(rules, "pc_lex_memory_echo", options.RandomElement());
        }

        private static void EmitJobEcho(Pawn pawn, List<Rule> rules)
        {
            if (pawn?.CurJob?.def == null)
            {
                Emit(rules, "pc_lex_job_echo", "resting for a moment");
                return;
            }

            string label = pawn.CurJob.def.label?.ToLower() ?? string.Empty;

            string echo = label switch
            {
                var l when l.Contains("haul")      => "hauling something [AdjectiveNatural] across [pc_colony_name]",
                var l when l.Contains("mine")      => "chipping away at [Color] stone near the [TerrainFeature]",
                var l when l.Contains("cook")      => "stirring the pot with practiced hands",
                var l when l.Contains("construct") => "building something that will outlast [pawn_objective]",
                var l when l.Contains("clean")     => "wiping away the day's grime",
                var l when l.Contains("tend")      => "tending to something that needed attention",
                var l when l.Contains("hunt")      => "tracking something [AdjectiveBadass] near the [TerrainFeature]",
                var l when l.Contains("research")  => "staring at data until it starts making sense",
                var l when l.Contains("sow")
                    || l.Contains("plant")
                    || l.Contains("harvest")       => "working the [Color] [NaturalObject]s in the growing field",
                var l when l.Contains("treat")
                    || l.Contains("tend") && l.Contains("patient") => "doing what the medic does",
                var l when l.Contains("train")     => "working with a [AnimalBadass] that is nearly cooperating",
                var l when l.Contains("tame")      => "approaching a [Animal] that has not decided yet",
                var l when l.Contains("draft")
                    || l.Contains("goto")          => "moving toward something [pawn_pronoun] has not named yet",
                var l when l.Contains("sleep")     => "finally sleeping",
                var l when l.Contains("eat")       => "eating without looking up",
                var l when l.Contains("joy")
                    || l.Contains("relax")         => "taking the hour that was offered",
                var l when l.Contains("talk")
                    || l.Contains("social")        => "saying the thing [pawn_pronoun] needed to say",
                var l when l.Contains("pray")      => "keeping [pawn_possessive] own counsel with the sky",
                _                                  => "working through another [AdjectiveFriendly] ordinary task"
            };

            Emit(rules, "pc_lex_job_echo", echo);
        }

        // ===================================================================
        //  CORE EMITTERS
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
                var options = new[] { "unmarked", "unblemished", "carrying no visible damage" };
                Emit(rules, "pc_lex_wound_summary", options.RandomElement());
                return;
            }

            float pain = pawn.health.hediffSet.PainTotal;
            string partLabel = wounds[0].Part?.Label ?? "body";

            string summary = pain switch
            {
                > 0.75f => $"in constant pain, the [AdjectiveAngsty] {partLabel} not letting [pawn_objective] forget",
                > 0.4f  => $"aching where the old {partLabel} damage sits",
                > 0.1f  => $"marked by old wounds on the {partLabel}",
                _       => $"carrying scars that have learned to be quiet"
            };

            Emit(rules, "pc_lex_wound_summary", summary);
        }

        private static void EmitDemeanor(Pawn pawn, List<Rule> rules)
        {
            Emit(rules, "pc_lex_demeanor", ComputeDemeanor(pawn));
        }

        private static string ComputeDemeanor(Pawn pawn)
        {
            if (pawn.story?.traits != null)
            {
                if (pawn.story.traits.HasTrait(TraitDefOf.Kind))        return "gentle";
                if (pawn.story.traits.HasTrait(TraitDefOf.Psychopath))  return "unreadable";
                if (pawn.story.traits.HasTrait(TraitDefOf.Bloodlust))   return "predatory";
                if (pawn.story.traits.HasTrait(TraitDefOf.Industriousness, 2)) return "focused";
                if (pawn.story.traits.HasTrait(TraitDefOf.Industriousness, -1)) return "unhurried";
                if (pawn.story.traits.HasTrait(DefDatabase<TraitDef>.GetNamedSilentFail("Nerves"), -1))     return "tense";
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

        private static void EmitCombatPosture(Pawn pawn, List<Rule> rules)
        {
            var primary = pawn.equipment?.Primary;
            if (primary == null)
            {
                var unarmed = new[]
                {
                    "hands empty and restless",
                    "hands free, watching the [TerrainFeature]",
                    "unarmed but not unaware",
                };
                Emit(rules, "pc_lex_combat_posture", unarmed.RandomElement());
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
                var withLover = new[]
                {
                    $"thinking of {lover.LabelShort}",
                    $"keeping {lover.LabelShort} somewhere in mind",
                    $"aware that {lover.LabelShort} is nearby",
                };
                Emit(rules, "pc_lex_social_anchor", withLover.RandomElement());
                return;
            }

            var friend = pawn.relations.PotentiallyRelatedPawns
                .Where(p => p.RaceProps.Humanlike && pawn.relations.OpinionOf(p) > 40)
                .OrderByDescending(p => pawn.relations.OpinionOf(p))
                .FirstOrDefault();

            if (friend != null)
            {
                var withFriend = new[]
                {
                    $"standing near {friend.LabelShort}",
                    $"aware of {friend.LabelShort} somewhere in [pc_colony_name]",
                    $"with {friend.LabelShort} close enough to hear",
                };
                Emit(rules, "pc_lex_social_anchor", withFriend.RandomElement());
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

            var lonelyOptions = new[]
            {
                "alone in the crowd",
                "not quite part of what is happening in [pc_colony_name]",
                "on the outside of a conversation [pawn_pronoun] did not join",
            };
            Emit(rules, "pc_lex_social_anchor", lonelyOptions.RandomElement());
        }

        private static void EmitWorldFeel(Pawn pawn, List<Rule> rules)
        {
            if (pawn.Map == null)
            {
                var voidOptions = new[]
                {
                    "adrift among the stars",
                    "somewhere between [AdjectiveAngsty] places",
                    "in transit, as always",
                };
                Emit(rules, "pc_lex_world_feel", voidOptions.RandomElement());
                return;
            }

            string weather = pawn.Map.weatherManager.curWeather.label;
            string season  = GenLocalDate.Season(pawn.Map).LabelCap();
            string biome   = pawn.Map.Biome?.label ?? "the wilds";

            var options = new[]
            {
                $"beneath the {weather} of {season}",
                $"somewhere in the {biome}, {weather} overhead",
                $"under a [Color] {season} sky with {weather} coming in",
                $"with {season} settling over the {biome}",
            };
            Emit(rules, "pc_lex_world_feel", options.RandomElement());
        }

        private static void EmitIdentityEcho(Pawn pawn, List<Rule> rules)
        {
            string? childhood = pawn.story?.Childhood?.title;
            string? adulthood = pawn.story?.Adulthood?.title;

            string echo;
            if (childhood != null && adulthood != null)
            {
                var options = new[]
                {
                    $"once {childhood}, now {adulthood}",
                    $"a former {childhood}, working as {adulthood}",
                    $"{childhood} by upbringing, {adulthood} by necessity",
                };
                echo = options.RandomElement();
            }
            else if (childhood != null)
                echo = $"shaped by a childhood as {childhood}";
            else if (adulthood != null)
                echo = $"known around [pc_colony_name] as {adulthood}";
            else
                echo = "of unknown origins";

            Emit(rules, "pc_lex_identity_echo", echo);
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
                > 0.75f => "content and steady",
                > 0.55f => "holding together",
                > 0.4f  => "fraying at the edges",
                > 0.25f => "on the verge of something",
                > 0.1f  => "close to the edge",
                _       => "past caring about most of it"
            };
            Emit(rules, "pc_lex_moral_state", state);
        }

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
                > 0.85f => "[AdjectiveFriendly] and mostly at peace with it",
                > 0.65f => "carrying on without complaint",
                > 0.45f => "[AdjectiveAngsty] in a way [pawn_pronoun] cannot fully name",
                > 0.25f => "tired of the particular weight of this",
                _       => "running on something that is not quite hope"
            };
            Emit(rules, "pc_lex_mood_phrase", phrase);
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

        private static void EmitScene(Pawn pawn, List<Rule> rules)
        {
            if (pawn.Map == null)
            {
                var voidScenes = new[]
                {
                    "adrift somewhere beyond the horizon",
                    "in the dark between [AdjectiveAngsty] places",
                    "somewhere the stars do not have names yet",
                };
                Emit(rules, "pc_lex_scene", voidScenes.RandomElement());
                return;
            }

            int hour = GenLocalDate.HourOfDay(pawn.Map);
            string timePhrase = hour switch
            {
                < 5  => "in the middle of the night",
                < 7  => "in the early morning",
                < 10 => "mid-morning",
                < 13 => "at midday",
                < 16 => "in the [AdjectiveNatural] afternoon",
                < 19 => "in the fading [AdjectiveNatural] light",
                < 22 => "as evening settled",
                _    => "under a [Color] night sky"
            };

            string biome   = pawn.Map.Biome?.label ?? "the wilds";
            string weather = pawn.Map.weatherManager?.curWeather?.label ?? "open sky";
            bool   raining = (pawn.Map.weatherManager?.curWeather?.rainRate ?? 0f) > 0.1f;

            var scenes = raining
                ? new[]
                {
                    $"{timePhrase}, {weather} falling over the {biome}",
                    $"{timePhrase} as {weather} moved across [NaturalObject]s near the [TerrainFeature]",
                    $"in {weather}, the [TerrainFeature] [AdjectiveNatural] and quiet, the [pc_lex_sound] of it steady",
                    $"{timePhrase}, a [pc_lex_sound] somewhere in the {weather}",
                }
                : new[]
                {
                    $"{timePhrase}, the {biome} spread out beyond [pc_colony_name]",
                    $"{timePhrase} near the [TerrainFeature], [NaturalObject]s visible from here",
                    $"at the edge of [pc_colony_name], {timePhrase}, a [Color] [Animal] somewhere nearby",
                    $"{timePhrase} with the {biome} [AdjectiveNatural] and the [TerrainFeature] close",
                    $"{timePhrase}, a [pc_lex_sound] carrying across from the [TerrainFeature]",
                    $"{timePhrase} - a [pc_lex_bug] on the windowsill, the [pc_world_biome] beyond it",
                };

            Emit(rules, "pc_lex_scene", scenes.RandomElement());
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
