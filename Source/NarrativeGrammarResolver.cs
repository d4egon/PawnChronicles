using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Grammar;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Resolves narrative letter text for a pawn's current epic stage
    /// by matching their dominant tag combination against a grammar rule bank.
    ///
    /// THREE-LAYER PIPELINE:
    ///   Layer 1 - PawnDataScraper (raw facts)
    ///   Layer 2 - Lexicon (derived prose hints)
    ///   Layer 3 - Grammar XML (PC_NarrativeGrammar.xml)
    /// </summary>
    public static class NarrativeGrammarResolver
    {
        private const string DefaultRulePackDefName  = "PC_NarrativeGrammar";
        private const string MedievalRulePackDefName = "PC_NarrativeGrammar_Medieval";

        // ── Performance telemetry ─────────────────────────────────────────────
        public static long LastResolveMs    = 0;
        public static long PeakResolveMs    = 0;
        public static int  ResolveCount     = 0;
        /// <summary>How many resolves fell back to the default string (no matching grammar key found).</summary>
        public static int  FallbackCount    = 0;

        public const string RoleOpening = "opening";
        public const string RoleMiddle  = "middle";
        public const string RoleClimax  = "climax";
        public const string RoleSuccess = "success";
        public const string RoleFailure = "failure";

        // ─────────────────────────────────────────────────────────────────────
        //  PUBLIC API
        // ─────────────────────────────────────────────────────────────────────

        public static string ResolveDynamic(Pawn pawn, PawnNarrativeProfile profile, string stageRole, string textType)
        {
            if (pawn == null || profile == null) return FallbackText(pawn!, stageRole, textType);

            // FIX: You must build the context (the 'request') for this method too
            GrammarRequest request = BuildGrammarContext(pawn, profile, stageRole);
            
            // FIX: Pull in the XML rules so the search actually has something to look at
            var rulePack = DefDatabase<RulePackDef>.GetNamed(DefaultRulePackDefName, false);
            AddGrammarRules(request, rulePack);

            var tags = profile.ActiveTags.OrderByDescending(t => profile.GetScore(t)).ToList();
            string tag1 = tags.Count > 0 ? StripTagPrefix(tags[0].defName) : "";
            string tag2 = tags.Count > 1 ? StripTagPrefix(tags[1].defName) : "";

            List<string> potentialKeys = new List<string> {
                $"{tag1}_{tag2}_{stageRole}_{textType}",
                $"{tag1}_{stageRole}_{textType}",
                $"{stageRole}_{textType}",
                $"default_{textType}"
            };

            foreach (var key in potentialKeys)
            {
                // FIX: Now 'request' is defined and passed correctly
                if (IsKeywordValid(key, request)) 
                {
                    string result = GrammarResolver.Resolve(key, request, $"PC_Dynamic_{pawn.LabelShort}", forceLog: false);
                    if (!string.IsNullOrEmpty(result) && !result.Contains(key)) 
                        return result;
                }
            }
            FallbackCount++;
            return FallbackText(pawn!, stageRole, textType);
        }

        public static string ResolveTitle(Pawn pawn, PawnNarrativeProfile profile, string stageRole, Dictionary<string, float>? snapshot = null)
            => Resolve(pawn, profile, stageRole, "title", snapshot);

        public static string ResolveBody(Pawn pawn, PawnNarrativeProfile profile, string stageRole, Dictionary<string, float>? snapshot = null)
            => Resolve(pawn, profile, stageRole, "body", snapshot);

        // One-shot diagnostic: logs full failure detail on the FIRST fallback, then goes silent.
        private static bool _diagLogged = false;

        public static string Resolve(Pawn pawn, PawnNarrativeProfile profile, string stageRole, string textType, Dictionary<string, float>? snapshot = null)
        {
            if (pawn == null || profile == null) return FallbackText(pawn!, stageRole, textType);
            var _sw = System.Diagnostics.Stopwatch.StartNew();

            // 1. Initialize and build the Three-Layer Context
            GrammarRequest request = BuildGrammarContext(pawn, profile, stageRole);

            // 2. Inject Layer 1.5: External Snapshot (The 'Snatch-motor' stats)
            if (snapshot != null)
            {
                foreach (var kvp in snapshot)
                {

                    request.Rules.Add(new Rule_String($"snapshot_{kvp.Key}", kvp.Value.ToString("F1")));

                }
            }

            // 3. Include the XML RulePack (Layer 3)
            var rulePack = DefDatabase<RulePackDef>.GetNamed(DefaultRulePackDefName, false);
            AddGrammarRules(request, rulePack);

            // 4. Determine Primary and Secondary tags for the Cascade
            var dominant = profile.Scores
                .OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

            string primary = dominant.Count > 0 ? StripTagPrefix(dominant[0].defName) : "none";
            string secondary = dominant.Count > 1 ? StripTagPrefix(dominant[1].defName) : "none";

            // 5. Resolution Cascade (Highest specificity to Lowest)
            string[] attempts = new string[]
            {
                $"{primary}_{secondary}_{stageRole}_{textType}",
                $"{primary}_{stageRole}_{textType}",
                $"{stageRole}_{textType}",
                $"default_{textType}"
            };

            var diagSb = (!_diagLogged) ? new System.Text.StringBuilder() : null;
            diagSb?.AppendLine($"[PC DIAG] Resolve fail for {pawn.LabelShort} role={stageRole} type={textType}");
            diagSb?.AppendLine($"  rulePack null: {rulePack == null}");
            diagSb?.AppendLine($"  medievalMode: {PawnChroniclesMod.Settings.medievalMode}");
            diagSb?.AppendLine($"  request.Rules: {request.Rules.Count}  request.Includes: {request.Includes.Count}");
            diagSb?.AppendLine($"  primary={primary}  secondary={secondary}");

            foreach (string rootKeyword in attempts)
            {
                // SURGICAL STRIKE: Check if the RulePack actually contains the keyword first.
                // This prevents GrammarResolver from dumping 1000+ lines of snapshot data on failure.
                bool valid = IsKeywordValid(rootKeyword, request);
                diagSb?.AppendLine($"  attempt [{rootKeyword}] valid={valid}");

                if (!valid) continue;

                string result = GrammarResolver.Resolve(rootKeyword, request, $"PC_Resolve_{pawn.LabelShort}", forceLog: false);
                diagSb?.AppendLine($"    resolve result: [{result?.Length ?? -1} chars] containsKey={result?.Contains(rootKeyword)}  first80=[{result?.Substring(0, System.Math.Min(80, result?.Length ?? 0))}]");

                if (!string.IsNullOrEmpty(result) && !result.Contains(rootKeyword))
                {
                    _sw.Stop();
                    LastResolveMs = _sw.ElapsedMilliseconds;
                    if (LastResolveMs > PeakResolveMs) PeakResolveMs = LastResolveMs;
                    ResolveCount++;
                    return result;
                }
            }

            if (diagSb != null && !_diagLogged)
            {
                _diagLogged = true;
                Log.Warning(diagSb.ToString());
            }

            _sw.Stop();
            LastResolveMs = _sw.ElapsedMilliseconds;
            if (LastResolveMs > PeakResolveMs) PeakResolveMs = LastResolveMs;
            ResolveCount++;
            FallbackCount++; // no grammar key matched — fell back to default text
            return FallbackText(pawn, stageRole, textType);

        }
        /// <summary>
        /// Adds the main grammar pack to the request.
        /// In medieval mode, rules whose keywords are overridden in PC_NarrativeGrammar_Medieval
        /// are filtered out from the main pack and replaced with the medieval versions.
        /// In normal mode the whole pack is added via Includes (cheaper, preserves rulesFiles).
        /// </summary>
        private static void AddGrammarRules(GrammarRequest request, RulePackDef mainPack)
        {
            if (mainPack == null) return;

            if (!PawnChroniclesMod.Settings.medievalMode)
            {
                // GrammarRequest.Includes is a computed property in 1.6 — .Add() does nothing.
                // Add rules directly instead.
                request.Rules.AddRange(mainPack.RulesPlusIncludes);
                return;
            }

            var medievalPack = DefDatabase<RulePackDef>.GetNamed(MedievalRulePackDefName, false);
            if (medievalPack == null)
            {
                // No medieval pack found — fall back to normal.
                request.Rules.AddRange(mainPack.RulesPlusIncludes);
                return;
            }

            // Collect the keyword set that the medieval pack overrides.
            var medievalRules  = medievalPack.RulesPlusIncludes;
            var overrideKeys   = new HashSet<string>(medievalRules.Select(r => r.keyword));

            // Add every main-pack rule whose keyword is NOT overridden.
            foreach (var rule in mainPack.RulesPlusIncludes)
                if (!overrideKeys.Contains(rule.keyword))
                    request.Rules.Add(rule);

            // Add the medieval overrides on top.
            request.Rules.AddRange(medievalRules);
        }

        private static bool IsKeywordValid(string keyword, GrammarRequest request)
        {
            // All rules (C#-injected + XML pack) are in request.Rules.
            // request.Includes is a computed property in RimWorld 1.6 and cannot be written to.
            for (int i = 0; i < request.Rules.Count; i++)
            {
                if (request.Rules[i].keyword == keyword) return true;
            }

            // Dead code kept for safety in case Includes ever works — remove in future cleanup.
            for (int i = 0; i < request.Includes.Count; i++)
            {
                var rules = request.Includes[i].RulesPlusIncludes;
                for (int j = 0; j < rules.Count; j++)
                {
                    if (rules[j].keyword == keyword) return true;
                }
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  INTERNAL LOGIC - THE THREE-LAYER PIPELINE
        // ─────────────────────────────────────────────────────────────────────

        private static GrammarRequest BuildGrammarContext(Pawn pawn, PawnNarrativeProfile profile, string stageRole)
        {
            var request = new GrammarRequest();

            // ── LAYER 0: RimWorld built-in pawn grammar ──
            request.Rules.AddRange(GrammarUtility.RulesForPawn("pawn", pawn));

            // ── LAYER 1: Exhaustive data scrape (Raw Facts) ──
            // Skip heavy scraping for dead, unspawned, or non-mapHeld pawns
            // (world pawns, caravans without a home map, etc.) - they have no
            // meaningful live context and this was the source of 23k rule blowout.
            if (!pawn.Dead && pawn.Spawned)
                request.Rules.AddRange(PawnDataScraper.ScrapeAll(pawn));

            // ── LAYER 2: Lexicon (Derived Phrases) ──
            request.Rules.AddRange(Lexicon.GetDerivedRules(pawn, profile));

            // ── LAYER 2.5: Tag Metadata & UI Symbols ──
            foreach (var kv in profile.Scores)
            {
                string tagKey = StripTagPrefix(kv.Key.defName);
                // [pc_tagscore_trauma]
                request.Rules.Add(new Rule_String($"pc_tagscore_{tagKey}", ((int)kv.Value).ToString()));
                // [pc_tagscore_trauma_tier] -> "dominant", "strong", etc.
                request.Rules.Add(new Rule_String($"pc_tagscore_{tagKey}_tier", ScoreTier(kv.Value)));
            }

            // Global Narrative Symbols
            request.Rules.Add(new Rule_String("epicStageRole", stageRole));

            var dominant = profile.Scores.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            if (dominant.Count > 0)
            {
                request.Rules.Add(new Rule_String("tag_primary_label", dominant[0].label));
                request.Rules.Add(new Rule_String("tag_primary_def", dominant[0].defName));
            }
            if (dominant.Count > 1)
            {
                request.Rules.Add(new Rule_String("tag_secondary_label", dominant[1].label));
                request.Rules.Add(new Rule_String("tag_secondary_def", dominant[1].defName));
            }

            // All active tag labels as a comma-separated list [epicActiveTags]
            var activeLabels = dominant.Where(t => profile.Scores[t] >= PawnNarrativeProfile.ActiveThreshold).Select(t => t.label);
            request.Rules.Add(new Rule_String("epicActiveTags", string.Join(", ", activeLabels)));

            var lexiconRules = Lexicon.GetDerivedRules(pawn, profile);
                if (lexiconRules != null)
                {
                    request.Rules.AddRange(lexiconRules);
                }

            return request;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ENTANGLED ARC GRAMMAR
        // ─────────────────────────────────────────────────────────────────────

        private const string EntangledRulePackDefName = "PC_EntangledGrammar";

        /// <summary>
        /// Resolves a title for a shared two-pawn arc stage.
        /// Grammar cascade (most specific first):
        ///   entangled_{arcType}_{role}_title
        ///   entangled_{role}_title
        ///   {role}_title          (falls back to solo grammar)
        ///   default_title
        /// </summary>
        public static string ResolveEntangledTitle(
            Pawn initiator, Pawn partner,
            PawnNarrativeProfile iProfile, PawnNarrativeProfile pProfile,
            string stageRole, EntangledArcType arcType)
            => ResolveEntangled(initiator, partner, iProfile, pProfile, stageRole, "title", arcType);

        /// <summary>Resolves body text for a shared two-pawn arc stage.</summary>
        public static string ResolveEntangledBody(
            Pawn initiator, Pawn partner,
            PawnNarrativeProfile iProfile, PawnNarrativeProfile pProfile,
            string stageRole, EntangledArcType arcType)
            => ResolveEntangled(initiator, partner, iProfile, pProfile, stageRole, "body", arcType);

        private static string ResolveEntangled(
            Pawn initiator, Pawn partner,
            PawnNarrativeProfile iProfile, PawnNarrativeProfile pProfile,
            string stageRole, string textType, EntangledArcType arcType)
        {
            if (initiator == null || partner == null)
                return FallbackText(initiator ?? partner!, stageRole, textType);

            var request = BuildEntangledGrammarContext(
                initiator, partner, iProfile, pProfile, stageRole, arcType);

            // Include both rule packs
            var mainPack = DefDatabase<RulePackDef>.GetNamed(DefaultRulePackDefName, false);
            AddGrammarRules(request, mainPack);

            var entangledPack = DefDatabase<RulePackDef>.GetNamed(EntangledRulePackDefName, false);
            if (entangledPack != null) request.Rules.AddRange(entangledPack.RulesPlusIncludes);

            string arcTypeLower = arcType.ToString().ToLowerInvariant();

            string[] attempts =
            {
                $"entangled_{arcTypeLower}_{stageRole}_{textType}",
                $"entangled_{stageRole}_{textType}",
                $"{stageRole}_{textType}",
                $"default_{textType}"
            };

            foreach (var key in attempts)
            {
                if (!IsKeywordValid(key, request)) continue;
                string result = GrammarResolver.Resolve(
                    key, request, $"PC_Entangled_{initiator.LabelShort}", forceLog: false);
                if (!string.IsNullOrEmpty(result) && !result.Contains(key))
                    return result;
            }

            return FallbackText(initiator, stageRole, textType);
        }

        /// <summary>
        /// Builds a GrammarRequest with BOTH pawns' symbols injected.
        ///
        /// Primary pawn (initiator) uses standard "pawn_*" prefix from RimWorld.
        /// Partner pawn uses "partner_*" prefix via GrammarUtility.RulesForPawn("partner", ...).
        /// Additional entangled symbols:
        ///   [entangled_arc_type]     - "rivalry", "romance", etc.
        ///   [partner_tag_primary]    - dominant tag label of the partner
        ///   [partner_tag_secondary]  - second tag label of the partner
        /// </summary>
        private static GrammarRequest BuildEntangledGrammarContext(
            Pawn initiator, Pawn partner,
            PawnNarrativeProfile iProfile, PawnNarrativeProfile pProfile,
            string stageRole, EntangledArcType arcType)
        {
            // Start from the initiator's full context
            var request = BuildGrammarContext(initiator, iProfile, stageRole);

            // ── Partner pawn symbols (partner_nameDef, partner_label, etc.) ──
            request.Rules.AddRange(GrammarUtility.RulesForPawn("partner", partner));

            // ── Partner data scrape ───────────────────────────────────────────
            // Prefix all partner scrape rules with "partner_pc_" to avoid collision
            foreach (var rule in PawnDataScraper.ScrapeAll(partner))
            {
                var prefixed = new Rule_String($"partner_{rule.keyword}", rule.Generate());
                request.Rules.Add(prefixed);
            }

            // ── Partner profile symbols ───────────────────────────────────────
            var pDominant = pProfile.Scores
                .OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();

            if (pDominant.Count > 0)
            {
                request.Rules.Add(new Rule_String("partner_tag_primary",   pDominant[0].label));
                request.Rules.Add(new Rule_String("partner_tag_def",       pDominant[0].defName));
            }
            if (pDominant.Count > 1)
            {
                request.Rules.Add(new Rule_String("partner_tag_secondary", pDominant[1].label));
            }

            foreach (var kv in pProfile.Scores)
            {
                string tagKey = StripTagPrefix(kv.Key.defName);
                request.Rules.Add(new Rule_String($"partner_tagscore_{tagKey}", ((int)kv.Value).ToString()));
                request.Rules.Add(new Rule_String($"partner_tagscore_{tagKey}_tier", ScoreTier(kv.Value)));
            }

            // ── Arc metadata ──────────────────────────────────────────────────
            request.Rules.Add(new Rule_String("entangled_arc_type", arcType.ToString().ToLowerInvariant()));
            request.Rules.Add(new Rule_String("entangled_arc_label", ArcTypeLabel(arcType)));

            return request;
        }

        private static string ArcTypeLabel(EntangledArcType t) => t switch
        {
            EntangledArcType.Rivalry           => "rivalry",
            EntangledArcType.Romance           => "love",
            EntangledArcType.MentorApprentice  => "mentorship",
            EntangledArcType.UnlikelyAllies    => "unlikely alliance",
            EntangledArcType.BoundByBlood      => "blood bond",
            _                                  => "shared story"
        };

        // ─────────────────────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static string StripTagPrefix(string defName)
            => defName?.ToLowerInvariant().Replace("pc_tag_", "") ?? "unknown";

        private static string ScoreTier(float score) => score switch
        {
            >= 70f => "dominant",
            >= 40f => "strong",
            >= 20f => "present",
            _      => "dormant"
        };

        private static string FallbackText(Pawn pawn, string stageRole, string textType)
        {
            if (textType == "title")
                return stageRole switch
                {
                    RoleOpening => $"A story begins for {pawn?.LabelShort ?? "a Pawn"}",
                    RoleSuccess => $"{pawn?.LabelShort ?? "a Pawn"}'s story reaches its end",
                    RoleFailure => $"The cost of {pawn?.LabelShort ?? "a Pawn"}'s journey",
                    _           => $"The next chapter for {pawn?.LabelShort ?? "a Pawn"}"
                };

            return $"The story of {pawn?.LabelShort ?? "a Pawn"} continues in the {stageRole} phase.";
        }
    }
}