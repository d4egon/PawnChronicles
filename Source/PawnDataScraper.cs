using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Grammar;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Exhaustive, mod-agnostic pawn data scraper.
    ///
    /// Walks every data surface on a pawn and yields Rule_String entries
    /// with predictable, machine-generated keys. No prose, no opinion -
    /// just raw facts emitted as grammar symbols for the XML to consume.
    ///
    /// Key naming convention:
    ///   pc_{category}_{index}_{field}
    ///
    /// Example outputs:
    ///   pc_hediff_0_label        = "gunshot (torso)"
    ///   pc_hediff_0_severity     = "0.72"
    ///   pc_hediff_0_part         = "torso"
    ///   pc_trait_1_label         = "psychopath"
    ///   pc_trait_1_degree        = "1"
    ///   pc_skill_shooting_level  = "14"
    ///   pc_skill_shooting_passion = "major"
    ///   pc_record_kills          = "37"
    ///   pc_weapon_label          = "charge rifle"
    ///   pc_weapon_quality        = "masterwork"
    ///   pc_apparel_0_label       = "flak vest"
    ///   pc_world_weather         = "rain"
    ///   pc_world_season          = "fall"
    ///   pc_royal_title           = "count"
    ///   pc_ideo_role             = "moral guide"
    ///   ...and hundreds more, depending on active mods.
    ///
    /// The grammar XML can reference any of these with [pc_hediff_0_label],
    /// [pc_skill_shooting_level], etc. If a key doesn't exist for a given
    /// pawn, the GrammarResolver will simply skip rules that reference it
    /// (or fall through to a less-specific rule in the cascade).
    /// </summary>
    public static class PawnDataScraper
    {
        // ── Performance telemetry ─────────────────────────────────────────────
        public static long LastScrapeMs    = 0;
        public static long PeakScrapeMs    = 0;
        public static int  ScrapeCount     = 0;

        // ─────────────────────────────────────────────────────────────────────
        //  MASTER ENTRY POINT
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scrapes all available data from a pawn and returns grammar rules.
        /// Call this once per resolve pass - it is stateless and idempotent.
        /// </summary>
        public static List<Rule> ScrapeAll(Pawn pawn)
        {
            var _sw = System.Diagnostics.Stopwatch.StartNew();
            var rules = new List<Rule>(128); // pre-size for typical pawn

            if (pawn == null)
            {
                Log.Warning("[PawnChronicles] PawnDataScraper.ScrapeAll called with null pawn.");
                return rules;
            }

            // Each scraper method is isolated and null-safe.
            // Order doesn't matter - they all emit into the same flat list.
            ScrapeIdentity(pawn, rules);
            ScrapeBackstory(pawn, rules);
            ScrapeTraits(pawn, rules);
            ScrapeSkills(pawn, rules);
            ScrapeHealth(pawn, rules);
            ScrapeEquipment(pawn, rules);
            ScrapeRecords(pawn, rules);
            ScrapeRelationships(pawn, rules);
            ScrapeSocial(pawn, rules);
            ScrapeWorld(pawn, rules);
            ScrapeRoyalty(pawn, rules);
            ScrapeIdeology(pawn, rules);
            ScrapeBiotech(pawn, rules);
            ScrapeAnomaly(pawn, rules);
            ScrapeHediffSummary(pawn, rules);
            ScrapeTraitSummary(pawn, rules);
            ScrapeSkillSummary(pawn, rules);

            // ── Depth-gated narrative symbols ──────────────────────────────────
            var settings = PawnChroniclesSettings.Current;
            if (settings.scraperDepth >= 1)
            {
                ScrapeJob(pawn, rules);
                ScrapeLocation(pawn, rules);
                ScrapeMentalState(pawn, rules);  // rarely used, not worth always-on
                ScrapeNeeds(pawn, rules);        // ~35 rules, only useful for future grammar
                ScrapeApparel(pawn, rules);      // unused in grammar currently
            }
            if (settings.scraperDepth >= 2)
            {
                ScrapeNarrativePhrases(pawn, rules);
                ScrapeInventory(pawn, rules);    // unused in grammar
                ScrapeAbilities(pawn, rules);    // unused in grammar
            }

            // ── Hard cap ───────────────────────────────────────────────────────
            int cap = settings.scraperMaxRulesPerPawn;
            if (cap > 0 && rules.Count > cap)
                rules.RemoveRange(cap, rules.Count - cap);

            // ── Logging ────────────────────────────────────────────────────────
            if (settings.scraperLoggingEnabled)
                Log.Message($"[PawnChronicles] Scraper: {rules.Count} rules for {pawn.LabelShort} (depth {settings.scraperDepth})");

            _sw.Stop();
            LastScrapeMs = _sw.ElapsedMilliseconds;
            if (LastScrapeMs > PeakScrapeMs) PeakScrapeMs = LastScrapeMs;
            ScrapeCount++;
            return rules;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  IDENTITY - name, age, gender, race
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeIdentity(Pawn pawn, List<Rule> rules)
        {
            // pc_age_bio is used in grammar. Pronoun/possessive/objective/name
            // variants are already provided by RimWorld's own grammar symbols
            // (pawn_pronoun, pawn_possessive, pawn_objective, pawn_nameDef etc.)
            // so we don't duplicate them here.
            Emit(rules, "pc_age_bio", pawn.ageTracker?.AgeBiologicalYears.ToString() ?? "??");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  BACKSTORY - childhood + adulthood titles and descriptions
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeBackstory(Pawn pawn, List<Rule> rules)
        {
            if (pawn.story == null) return;

            // Titles are used in grammar. Descs are long tooltip strings - not referenced.
            if (pawn.story.Childhood != null)
                Emit(rules, "pc_backstory_child_title", pawn.story.Childhood.title);
            if (pawn.story.Adulthood != null)
                Emit(rules, "pc_backstory_adult_title", pawn.story.Adulthood.title);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  TRAITS - every trait, indexed + keyed by defName
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeTraits(Pawn pawn, List<Rule> rules)
        {
            var traits = pawn.story?.traits?.allTraits;
            if (traits == null) return;

            Emit(rules, "pc_trait_count", traits.Count.ToString());

            // Emit label + def per trait for grammar reference.
            // _desc (tooltip), _degree, and defName-keyed duplicates are unused - dropped.
            for (int i = 0; i < traits.Count; i++)
            {
                var t = traits[i];
                Emit(rules, $"pc_trait_{i}_label", t.Label);
                Emit(rules, $"pc_trait_{i}_def",   t.def.defName);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SKILLS - every skill by defName, level, passion, xp
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeSkills(Pawn pawn, List<Rule> rules)
        {
            if (pawn.skills == null) return;

            SkillRecord? best = null;
            SkillRecord? worst = null;

            foreach (var sk in pawn.skills.skills)
            {
                if (sk.TotallyDisabled) continue;

                string key = sk.def.defName.ToLower();
                Emit(rules, $"pc_skill_{key}_level",   sk.Level.ToString());
                Emit(rules, $"pc_skill_{key}_passion",  sk.passion.ToString().ToLower());
                Emit(rules, $"pc_skill_{key}_label",    sk.def.label);
                // _xp and _disabled are unused in grammar - dropped.

                if (best == null || sk.Level > best.Level) best = sk;
                if (worst == null || sk.Level < worst.Level) worst = sk;
            }

            if (best != null)
            {
                Emit(rules, "pc_skill_best_label", best.def.label);
                Emit(rules, "pc_skill_best_level", best.Level.ToString());
            }
            if (worst != null)
            {
                Emit(rules, "pc_skill_worst_label", worst.def.label);
                Emit(rules, "pc_skill_worst_level", worst.Level.ToString());
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  HEALTH - every hediff, indexed + sorted by severity
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeHealth(Pawn pawn, List<Rule> rules)
        {
            if (pawn.health?.hediffSet == null) return;

            var hediffs = pawn.health.hediffSet.hediffs
                .Where(h => h.Visible)
                .OrderByDescending(h => h.Severity)
                .ToList();

            Emit(rules, "pc_hediff_count", hediffs.Count.ToString());

            // Separate bad vs good for narrative convenience
            int badCount = 0, goodCount = 0;

            for (int i = 0; i < hediffs.Count; i++)
            {
                var h = hediffs[i];

                // Keep label, part, is_bad - enough for grammar reference.
                // _def, _severity, _is_chronic, _tends, _desc are unused - dropped.
                Emit(rules, $"pc_hediff_{i}_label",  h.Label);
                Emit(rules, $"pc_hediff_{i}_part",   h.Part?.Label ?? "whole body");
                Emit(rules, $"pc_hediff_{i}_is_bad", h.def.isBad.ToString().ToLower());

                if (h.def.isBad) badCount++; else goodCount++;
            }

            Emit(rules, "pc_hediff_bad_count",  badCount.ToString());
            Emit(rules, "pc_hediff_good_count", goodCount.ToString());
            // pc_pain_level and pc_consciousness are unused in grammar - dropped.
        }

        // ─────────────────────────────────────────────────────────────────────
        //  MENTAL STATE - what's going on right now
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeMentalState(Pawn pawn, List<Rule> rules)
        {
            Emit(rules, "pc_in_mental_state",
                pawn.InMentalState.ToString().ToLower());

            if (pawn.InMentalState && pawn.MentalStateDef != null)
            {
                Emit(rules, "pc_mental_state_label", pawn.MentalStateDef.label);
                Emit(rules, "pc_mental_state_def", pawn.MentalStateDef.defName);
                if (!string.IsNullOrEmpty(pawn.MentalStateDef.beginLetterLabel))
                    Emit(rules, "pc_mental_state_letter",
                        pawn.MentalStateDef.beginLetterLabel);
            }

            // Inspiration (positive mental state)
            if (pawn.Inspired && pawn.InspirationDef != null)
            {
                Emit(rules, "pc_inspired", "true");
                Emit(rules, "pc_inspiration_label", pawn.InspirationDef.label);
                Emit(rules, "pc_inspiration_def", pawn.InspirationDef.defName);
            }
            else
            {
                Emit(rules, "pc_inspired", "false");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  NEEDS - mood, food, rest, etc.
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeNeeds(Pawn pawn, List<Rule> rules)
        {
            if (pawn.needs == null) return;

            foreach (var need in pawn.needs.AllNeeds)
            {
                if (need?.def == null) continue;
                string key = need.def.defName.ToLower();
                Emit(rules, $"pc_need_{key}_level", need.CurLevel.ToString("F2"));
                Emit(rules, $"pc_need_{key}_pct", need.CurLevelPercentage.ToString("F2"));
                Emit(rules, $"pc_need_{key}_label", need.def.label);
            }

            // Quick-access mood
            if (pawn.needs.mood != null)
            {
                Emit(rules, "pc_mood", pawn.needs.mood.CurLevel.ToString("F2"));
                Emit(rules, "pc_mood_pct",
                    pawn.needs.mood.CurLevelPercentage.ToString("F2"));

                // Current dominant thought (highest impact)
                var thoughts = pawn.needs.mood.thoughts?.memories?.Memories;
                if (thoughts != null && thoughts.Count > 0)
                {
                    var strongest = thoughts
                        .Where(t => t != null && t.def != null)
                        .OrderByDescending(t => Math.Abs(t.MoodOffset()))
                        .FirstOrDefault();
                    if (strongest != null)
                    {
                        Emit(rules, "pc_thought_strongest_label",
                            strongest.LabelCap);
                        Emit(rules, "pc_thought_strongest_mood",
                            strongest.MoodOffset().ToString("F1"));
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  EQUIPMENT - primary weapon
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeEquipment(Pawn pawn, List<Rule> rules)
        {
            var primary = pawn.equipment?.Primary;
            if (primary != null)
            {
                Emit(rules, "pc_weapon_label", primary.LabelShort);
                // _def, _desc, _quality, _is_melee, _is_ranged, _stuff unused - dropped.
            }
            else
            {
                Emit(rules, "pc_weapon_label", "bare hands");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  APPAREL - every worn item, indexed
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeApparel(Pawn pawn, List<Rule> rules)
        {
            var worn = pawn.apparel?.WornApparel;
            if (worn == null || worn.Count == 0)
            {
                Emit(rules, "pc_apparel_count", "0");
                return;
            }

            Emit(rules, "pc_apparel_count", worn.Count.ToString());

            for (int i = 0; i < worn.Count; i++)
            {
                var a = worn[i];
                string idx = i.ToString();
                Emit(rules, $"pc_apparel_{idx}_label", a.LabelShort);
                Emit(rules, $"pc_apparel_{idx}_def", a.def.defName);

                if (a.TryGetQuality(out QualityCategory qc))
                    Emit(rules, $"pc_apparel_{idx}_quality", qc.GetLabel());

                if (a.Stuff != null)
                    Emit(rules, $"pc_apparel_{idx}_stuff", a.Stuff.label);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  RECORDS - every tracked stat (kills, meals, miles walked, etc.)
        // ─────────────────────────────────────────────────────────────────────

        // Records actually referenced in grammar or scorers - targeted whitelist.
        // Walking DefDatabase<RecordDef>.AllDefs was emitting 100+ rules per pawn
        // for records that were never consumed. Add to this list as grammar grows.
        private static readonly RecordDef?[] _trackedRecords = new RecordDef?[3];
        private static bool _recordsResolved = false;

        private static void ResolveTrackedRecords()
        {
            if (_recordsResolved) return;
            _trackedRecords[0] = DefDatabase<RecordDef>.GetNamedSilentFail("Kills");
            _trackedRecords[1] = DefDatabase<RecordDef>.GetNamedSilentFail("TimeAsColonistOrColonyAnimal");
            _trackedRecords[2] = DefDatabase<RecordDef>.GetNamedSilentFail("TimesInMentalState");
            _recordsResolved = true;
        }

        private static void ScrapeRecords(Pawn pawn, List<Rule> rules)
        {
            if (pawn.records == null) return;
            ResolveTrackedRecords();

            foreach (var def in _trackedRecords)
            {
                if (def == null) continue;
                try
                {
                    float val = pawn.records.GetValue(def);
                    if (Math.Abs(val) > 0.001f)
                        Emit(rules, $"pc_record_{def.defName.ToLower()}", ((int)val).ToString());
                }
                catch { }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  RELATIONSHIPS - direct relations (family, lovers, rivals)
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeRelationships(Pawn pawn, List<Rule> rules)
        {
            if (pawn.relations == null) return;

            var directRelations = pawn.relations.DirectRelations;
            if (directRelations == null || directRelations.Count == 0)
            {
                Emit(rules, "pc_relation_count", "0");
                return;
            }

            Emit(rules, "pc_relation_count", directRelations.Count.ToString());

            // Emit all direct relations - every mod-added PawnRelationDef shows up
            int idx = 0;
            foreach (var rel in directRelations)
            {
                if (rel.otherPawn == null) continue;
                Emit(rules, $"pc_relation_{idx}_type", rel.def.label);
                Emit(rules, $"pc_relation_{idx}_name", rel.otherPawn.LabelShort);
                // _def unused in grammar - dropped.
                idx++;
            }

            // Convenience: specific named slots for grammar shorthand
            EmitRelationSlot(pawn, rules, PawnRelationDefOf.Lover, "pc_lover");
            EmitRelationSlot(pawn, rules, PawnRelationDefOf.Spouse, "pc_spouse");
            EmitRelationSlot(pawn, rules, PawnRelationDefOf.Parent, "pc_parent");
            EmitRelationSlot(pawn, rules, PawnRelationDefOf.Child, "pc_child");
        }

        private static void EmitRelationSlot(
            Pawn pawn, List<Rule> rules, PawnRelationDef relDef, string key)
        {
            var related = pawn.relations.DirectRelations
                .Where(r => r.def == relDef && r.otherPawn != null)
                .Select(r => r.otherPawn)
                .FirstOrDefault();

            if (related != null)
                Emit(rules, $"{key}_name", related.LabelShort);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SOCIAL - opinion-based scraping (best friend, worst enemy)
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeSocial(Pawn pawn, List<Rule> rules)
        {
            if (pawn.relations == null) return;

            Pawn? bestFriend = null;
            Pawn? worstEnemy = null;
            int bestOp = int.MinValue;
            int worstOp = int.MaxValue;

            foreach (var other in pawn.relations.PotentiallyRelatedPawns)
            {
                if (other == null || !other.RaceProps.Humanlike) continue;
                int op = pawn.relations.OpinionOf(other);
                if (op > bestOp) { bestOp = op; bestFriend = other; }
                if (op < worstOp) { worstOp = op; worstEnemy = other; }
            }

            if (bestFriend != null && bestOp > 0)
            {
                Emit(rules, "pc_friend_name", bestFriend.LabelShort);
                Emit(rules, "pc_friend_opinion", bestOp.ToString());
            }
            if (worstEnemy != null && worstOp < 0)
            {
                Emit(rules, "pc_rival_name", worstEnemy.LabelShort);
                Emit(rules, "pc_rival_opinion", worstOp.ToString());
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  WORLD - map, weather, season, biome, time
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeWorld(Pawn pawn, List<Rule> rules)
        {
            if (pawn.Map == null)
            {
                Emit(rules, "pc_world_location", "the void between worlds");
                return;
            }

            var map = pawn.Map;

            // Weather
            Emit(rules, "pc_world_weather", map.weatherManager.curWeather.label);
            // _weather_def unused - dropped.

            // Season - emit both forms so grammar can use either.
            var season = GenLocalDate.Season(map);
            string seasonLabel = season.LabelCap();
            Emit(rules, "pc_world_season",       seasonLabel);
            Emit(rules, "pc_world_season_label",  seasonLabel); // alias for grammar convenience

            // Biome
            Emit(rules, "pc_world_biome", map.Biome.label);
            // _biome_def, _temp, _elevation, _rainfall unused - dropped.

            // Time of day
            int hour = GenLocalDate.HourOfDay(map);
            string timeOfDay = hour switch
            {
                < 6  => "deep night",
                < 10 => "morning",
                < 14 => "midday",
                < 18 => "afternoon",
                < 22 => "evening",
                _    => "night"
            };
            Emit(rules, "pc_world_time_of_day", timeOfDay);
            // pc_world_hour unused - dropped.

            // Colony name
            var faction = Faction.OfPlayer;
            if (faction != null)
                Emit(rules, "pc_colony_name", faction.Name);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ROYALTY (DLC-safe - only emits if the DLC is loaded)
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeRoyalty(Pawn pawn, List<Rule> rules)
        {
            if (!ModLister.RoyaltyInstalled) return;
            if (pawn.royalty == null) return;

            var titles = pawn.royalty.AllTitlesForReading;
            if (titles == null || titles.Count == 0) return;

            // Only the title label is used in grammar. Count, def, faction, permits - dropped.
            var senior = pawn.royalty.MostSeniorTitle;
            if (senior != null)
                Emit(rules, "pc_royal_title", senior.def.GetLabelFor(pawn));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  IDEOLOGY (DLC-safe)
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeIdeology(Pawn pawn, List<Rule> rules)
        {
            if (!ModLister.IdeologyInstalled) return;
            if (pawn.ideo?.Ideo == null) return;

            var ideo = pawn.ideo.Ideo;
            Emit(rules, "pc_ideo_name", ideo.name);

            // Precept labels only - _def unused, dropped.
            int preceptIdx = 0;
            foreach (var precept in ideo.PreceptsListForReading)
            {
                Emit(rules, $"pc_ideo_precept_{preceptIdx}_label", precept.Label);
                preceptIdx++;
            }

            // Role label only - _def and _certainty unused, dropped.
            var role = ideo.GetRole(pawn);
            if (role != null)
                Emit(rules, "pc_ideo_role", role.LabelCap);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  BIOTECH (DLC-safe) - genes, xenotype
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeBiotech(Pawn pawn, List<Rule> rules)
        {
            if (!ModLister.BiotechInstalled) return;
            if (pawn.genes == null) return;

            // Xenotype label only - _def, _desc, _custom_name unused, dropped.
            var xenotype = pawn.genes.Xenotype;
            if (xenotype != null)
                Emit(rules, "pc_xenotype_label", xenotype.label);

            // Gene labels only - _def and _active unused, dropped.
            // Modded xenotypes can have 20+ genes, so we keep this lean.
            var activeGenes = pawn.genes.GenesListForReading;
            if (activeGenes != null)
            {
                Emit(rules, "pc_gene_count", activeGenes.Count.ToString());
                for (int i = 0; i < activeGenes.Count; i++)
                    Emit(rules, $"pc_gene_{i}_label", activeGenes[i].Label);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ANOMALY (DLC-safe) - dark study, entities, etc.
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeAnomaly(Pawn pawn, List<Rule> rules)
        {
            if (!ModLister.AnomalyInstalled) return;

            // Anomaly-specific hediffs (e.g. metalhorror, creepjoiners, etc.)
            // are already captured by ScrapeHealth - they're just HediffDefs.
            // Here we emit anomaly-specific systems if the pawn has them.

            // Dark study level / knowledge - accessed via pawn's hediffs or
            // through the anomaly knowledge tracker if available.
            // This is future-proofed: if Anomaly adds more systems,
            // they'll show up as hediffs, traits, or records automatically.

            // Check for mutant status
            if (pawn.mutant != null)
            {
                Emit(rules, "pc_is_mutant", "true");
                var mutantDef = pawn.mutant.Def;
                if (mutantDef != null)
                {
                    Emit(rules, "pc_mutant_label", mutantDef.label);
                    Emit(rules, "pc_mutant_def", mutantDef.defName);
                }
            }
            else
            {
                Emit(rules, "pc_is_mutant", "false");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  INVENTORY - things carried in pockets / pack
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeInventory(Pawn pawn, List<Rule> rules)
        {
            var inv = pawn.inventory?.innerContainer;
            if (inv == null || inv.Count == 0)
            {
                Emit(rules, "pc_inventory_count", "0");
                return;
            }

            Emit(rules, "pc_inventory_count", inv.Count.ToString());
            int idx = 0;
            foreach (var thing in inv)
            {
                Emit(rules, $"pc_inventory_{idx}_label", thing.LabelShort);
                Emit(rules, $"pc_inventory_{idx}_def", thing.def.defName);
                Emit(rules, $"pc_inventory_{idx}_count", thing.stackCount.ToString());
                idx++;
                if (idx >= 10) break; // cap to prevent symbol explosion on pack mules
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ABILITIES - psycasts, royal abilities, modded abilities
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeAbilities(Pawn pawn, List<Rule> rules)
        {
            if (pawn.abilities == null) return;

            var abilities = pawn.abilities.AllAbilitiesForReading;
            if (abilities == null || abilities.Count == 0)
            {
                Emit(rules, "pc_ability_count", "0");
                return;
            }

            Emit(rules, "pc_ability_count", abilities.Count.ToString());
            for (int i = 0; i < abilities.Count; i++)
            {
                var ab = abilities[i];
                Emit(rules, $"pc_ability_{i}_label", ab.def.label);
                Emit(rules, $"pc_ability_{i}_def", ab.def.defName);
                if (!string.IsNullOrEmpty(ab.def.description))
                    Emit(rules, $"pc_ability_{i}_desc", ab.def.description);
            }

            // Psylink level (Royalty) - already DLC-gated by ability existence
            var psylink = pawn.psychicEntropy;
            if (psylink != null)
            {
                Emit(rules, "pc_psylink_level",
                    pawn.GetPsylinkLevel().ToString());
                Emit(rules, "pc_neural_heat",
                    psylink.EntropyValue.ToString("F1"));
                Emit(rules, "pc_neural_heat_max",
                    psylink.MaxEntropy.ToString("F1"));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SUMMARY EMITTERS - comma-joined lists for grammar shorthand
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeHediffSummary(Pawn pawn, List<Rule> rules)
        {
            if (pawn.health?.hediffSet == null) return;

            var badLabels = pawn.health.hediffSet.hediffs
                .Where(h => h.Visible && h.def.isBad)
                .Select(h => h.Label);
            var goodLabels = pawn.health.hediffSet.hediffs
                .Where(h => h.Visible && !h.def.isBad)
                .Select(h => h.Label);

            Emit(rules, "pc_hediff_bad_list",
                JoinOrDefault(badLabels, "no afflictions"));
            Emit(rules, "pc_hediff_good_list",
                JoinOrDefault(goodLabels, "no enhancements"));
        }

        private static void ScrapeTraitSummary(Pawn pawn, List<Rule> rules)
        {
            var traits = pawn.story?.traits?.allTraits;
            if (traits == null || traits.Count == 0)
            {
                Emit(rules, "pc_trait_list", "unremarkable");
                return;
            }
            Emit(rules, "pc_trait_list",
                string.Join(", ", traits.Select(t => t.Label)));
        }

        private static void ScrapeSkillSummary(Pawn pawn, List<Rule> rules)
        {
            if (pawn.skills == null) return;

            var passionSkills = pawn.skills.skills
                .Where(s => !s.TotallyDisabled && s.passion != Passion.None)
                .Select(s => $"{s.def.label} ({s.passion.ToString().ToLower()})");

            Emit(rules, "pc_skill_passions_list",
                JoinOrDefault(passionSkills, "no passions"));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  JOB (depth >= 1) - current task as a narrative phrase
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeJob(Pawn pawn, List<Rule> rules)
        {
            var job = pawn.CurJob;
            if (job?.def == null) return;

            string jobLabel = job.def.label ?? job.def.defName;
            Emit(rules, "pc_job_label", jobLabel);
            Emit(rules, "pc_job_def",   job.def.defName);

            // Try to get a meaningful target string
            string target = "";
            try
            {
                if (job.targetA.HasThing && job.targetA.Thing != null)
                    target = job.targetA.Thing.LabelShort;
                else if (job.targetA.Cell.IsValid)
                    target = "a location";
            }
            catch { /* target access can throw on some job states */ }

            string activity = BuildActivityPhrase(jobLabel, target);
            Emit(rules, "pc_lex_current_activity", activity);
            if (!string.IsNullOrEmpty(target))
                Emit(rules, "pc_lex_job_target", target);
        }

        private static string BuildActivityPhrase(string rawLabel, string target)
        {
            bool  hasTarget = !string.IsNullOrEmpty(target);
            string lbl      = rawLabel.ToLower();

            if (lbl.Contains("haul"))
                return hasTarget ? $"hauling {target}" : "hauling supplies";
            if (lbl.Contains("construct") || lbl.Contains("build"))
                return hasTarget ? $"building {target}" : "working on construction";
            if (lbl.Contains("mine"))
                return hasTarget ? $"mining {target}" : "working the stone";
            if (lbl.Contains("hunt"))
                return hasTarget ? $"hunting {target}" : "out on a hunt";
            if (lbl.Contains("cook") || lbl.Contains("meal"))
                return "preparing a meal";
            if (lbl.Contains("research"))
                return "bent over research notes";
            if (lbl.Contains("tend"))
                return hasTarget ? $"tending to {target}" : "tending the wounded";
            if (lbl.Contains("rescue"))
                return hasTarget ? $"rescuing {target}" : "carrying someone to safety";
            if (lbl.Contains("clean"))
                return "cleaning the colony";
            if (lbl.Contains("sow"))
                return "working the fields";
            if (lbl.Contains("harvest") || lbl.Contains("cut plant"))
                return "bringing in the harvest";
            if (lbl.Contains("craft") || lbl.Contains("smith"))
                return hasTarget ? $"crafting {target}" : "working at the bench";
            if (lbl.Contains("patrol"))
                return "walking the perimeter";
            if (lbl.Contains("sleep") || lbl.Contains("rest"))
                return "sleeping";
            if (lbl.Contains("eat") || lbl.Contains("ingest"))
                return "eating";
            if (lbl.Contains("wander") || lbl.Contains("meander"))
                return "wandering idly";
            if (lbl.Contains("joy") || lbl.Contains("recreation"))
                return "taking some time to rest";
            if (lbl.Contains("social") || lbl.Contains("chat") || lbl.Contains("talk"))
                return hasTarget ? $"talking with {target}" : "socializing";
            if (lbl.Contains("train"))
                return hasTarget ? $"training {target}" : "working on skills";
            if (lbl.Contains("tame"))
                return hasTarget ? $"trying to tame {target}" : "working with animals";
            if (lbl.Contains("attack") || lbl.Contains("shoot") || lbl.Contains("melee"))
                return hasTarget ? $"fighting {target}" : "in combat";
            if (lbl.Contains("goto") || lbl.Contains("go to"))
                return "moving into position";
            if (lbl.Contains("doctor") || lbl.Contains("surgery"))
                return hasTarget ? $"operating on {target}" : "performing surgery";
            if (lbl.Contains("repair") || lbl.Contains("fix"))
                return hasTarget ? $"repairing {target}" : "making repairs";
            if (lbl.Contains("load") || lbl.Contains("unload"))
                return hasTarget ? $"loading {target}" : "loading cargo";
            if (lbl.Contains("draft"))
                return "standing ready";

            // fallback - use raw label
            return hasTarget ? $"{rawLabel} - {target}" : rawLabel;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  LOCATION (depth >= 1) - room role + zone as narrative phrases
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeLocation(Pawn pawn, List<Rule> rules)
        {
            if (pawn.Map == null) return;

            try
            {
                var room = pawn.Position.GetRoom(pawn.Map);
                if (room != null && !room.PsychologicallyOutdoors)
                {
                    string roleLabel = room.Role?.label ?? "a room";
                    Emit(rules, "pc_room_role", roleLabel);

                    string defName = room.Role?.defName ?? "";
                    string locationPhrase = defName switch
                    {
                        "Bedroom"        => "their bedroom",
                        "Barracks"       => "the barracks",
                        "Kitchen"        => "the kitchen",
                        "DiningRoom"     => "the dining room",
                        "RecRoom"        => "the rec room",
                        "Hospital"       => "the hospital",
                        "Laboratory"     => "the lab",
                        "Workshop"       => "the workshop",
                        "Armory"         => "the armory",
                        "PrisonCell"     => "the prison cell",
                        "PrisonBarracks" => "the prison barracks",
                        "Throne"         => "the throne room",
                        "Temple"         => "the temple",
                        "Tomb"           => "the tomb",
                        "Barn"           => "the animal barn",
                        "Greenhouse"     => "the greenhouse",
                        _                => $"the {roleLabel}"
                    };
                    Emit(rules, "pc_lex_location_name", locationPhrase);
                }
                else
                {
                    Emit(rules, "pc_lex_location_name", "outside");
                    Emit(rules, "pc_room_role",          "outdoors");
                }
            }
            catch { /* room lookup can fail on unspawned or transient pawns */ }

            try
            {
                var zone = pawn.Map.zoneManager.ZoneAt(pawn.Position);
                if (zone != null)
                    Emit(rules, "pc_lex_zone_name", zone.label);
            }
            catch { /* zone lookup is non-critical */ }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  NARRATIVE PHRASES (depth >= 2) - season, time, atmosphere, mood, pain
        //
        //  These emit complete prose fragments - drop them straight into story
        //  text with [pc_lex_season_phrase], [pc_lex_atmosphere], etc.
        // ─────────────────────────────────────────────────────────────────────

        private static void ScrapeNarrativePhrases(Pawn pawn, List<Rule> rules)
        {
            if (pawn.Map == null) return;
            var map = pawn.Map;

            // ── Season ────────────────────────────────────────────────────────
            var season = GenLocalDate.Season(map);
            string seasonPhrase = season switch
            {
                Season.Spring         => "in the first breath of spring",
                Season.Summer         => "under the full weight of summer",
                Season.Fall           => "in the dying light of autumn",
                Season.Winter         => "in the grip of winter",
                Season.PermanentSummer=> "in the endless heat",
                Season.PermanentWinter=> "in the endless cold",
                _                     => "in an unnamed season"
            };
            Emit(rules, "pc_lex_season_phrase", seasonPhrase);

            // ── Time of day ───────────────────────────────────────────────────
            int hour = GenLocalDate.HourOfDay(map);
            string timePhrase = hour switch
            {
                < 4  => "in the dead of night",
                < 7  => "as the colony stirred awake",
                < 10 => "in the early morning",
                < 12 => "as the morning stretched on",
                < 14 => "at midday",
                < 17 => "in the afternoon",
                < 20 => "as evening settled in",
                < 22 => "in the quiet of evening",
                _    => "late at night"
            };
            Emit(rules, "pc_lex_time_phrase", timePhrase);

            // ── Atmosphere - weather rendered as prose ─────────────────────────
            try
            {
                string weatherLabel = map.weatherManager.curWeather.label.ToLower();
                string atmosphere;
                if      (weatherLabel.Contains("blizzard"))
                    atmosphere = "a blizzard had buried the colony in white";
                else if (weatherLabel.Contains("snow"))
                    atmosphere = "snow had settled quietly over everything";
                else if (weatherLabel.Contains("thunder") || weatherLabel.Contains("storm"))
                    atmosphere = "a storm rolled in from the horizon";
                else if (weatherLabel.Contains("fog"))
                    atmosphere = "a thick fog had swallowed the colony";
                else if (weatherLabel.Contains("rain"))
                    atmosphere = "the rain fell soft against the walls";
                else if (weatherLabel.Contains("heat wave") || weatherLabel.Contains("dry thunderstorm"))
                    atmosphere = "the heat pressed down without mercy";
                else if (weatherLabel.Contains("clear") || weatherLabel.Contains("sunny") || weatherLabel.Contains("fair"))
                    atmosphere = "the sky was open and clear";
                else if (weatherLabel.Contains("cold") || weatherLabel.Contains("freez"))
                    atmosphere = "bitter cold crept in at the edges";
                else if (weatherLabel.Contains("dry"))
                    atmosphere = "the air was dry and very still";
                else
                    atmosphere = $"the {map.weatherManager.curWeather.label} held over the colony";

                Emit(rules, "pc_lex_atmosphere", atmosphere);
            }
            catch { /* weather access non-critical */ }

            // ── Mood ──────────────────────────────────────────────────────────
            float mood = pawn.needs?.mood?.CurLevelPercentage ?? 0.5f;
            string moodPhrase = mood switch
            {
                < 0.15f => "close to breaking",
                < 0.30f => "struggling to hold it together",
                < 0.45f => "worn down and restless",
                < 0.55f => "neither happy nor miserable",
                < 0.70f => "holding steady",
                < 0.85f => "in good spirits",
                _       => "at peace with the world"
            };
            Emit(rules, "pc_lex_mood_phrase", moodPhrase);

            // ── Pain ──────────────────────────────────────────────────────────
            float pain = pawn.health?.hediffSet?.PainTotal ?? 0f;
            string painPhrase = pain switch
            {
                < 0.05f => "uninjured",
                < 0.20f => "carrying a dull ache",
                < 0.40f => "working through real pain",
                < 0.60f => "suffering badly",
                < 0.80f => "fighting through serious injury",
                _       => "barely able to function through the pain"
            };
            Emit(rules, "pc_lex_pain_phrase", painPhrase);

            // ── Backstory echo - first sentence of adulthood backstory ─────────
            try
            {
                string? desc = pawn.story?.Adulthood?.baseDesc;
                if (!string.IsNullOrEmpty(desc))
                {
                    int dotIdx = desc!.IndexOf('.');
                    string echo = dotIdx > 10 ? desc.Substring(0, dotIdx + 1) : desc;
                    Emit(rules, "pc_lex_backstory_echo", echo);
                }
            }
            catch { /* backstory access non-critical */ }

            // ── Dominant skill label ───────────────────────────────────────────
            if (pawn.skills != null)
            {
                var best = pawn.skills.skills
                    .Where(s => !s.TotallyDisabled)
                    .OrderByDescending(s => s.Level)
                    .FirstOrDefault();
                if (best != null)
                    Emit(rules, "pc_lex_dominant_skill_label", best.def.label);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static void Emit(List<Rule> rules, string keyword, string output)
        {
            if (string.IsNullOrEmpty(output)) return;
            rules.Add(new Rule_String(keyword, output));
        }

        private static string JoinOrDefault(IEnumerable<string> items, string fallback)
        {
            var list = items.ToList();
            return list.Count > 0 ? string.Join(", ", list) : fallback;
        }
    }
}