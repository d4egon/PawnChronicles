using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Static evaluator for entangled arcs.
    ///
    /// Responsibilities:
    ///   • Scan all free-colonist pairs and score them against all EntangledArcDefs
    ///   • Select the best-matching EntangledStageDef for a given arc position
    ///
    /// Called by EntangledArcManager during its periodic evaluation pass.
    /// </summary>
    public static class EntangledArcEvaluator
    {
        // ─────────────────────────────────────────────────────────────────────
        //  PAIR EVALUATION (called by manager)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scans the provided colonist list for pairs eligible for an entangled arc.
        /// Starts at most ONE new arc per call to avoid narrative spam.
        /// </summary>
        public static void EvaluatePairs(List<Pawn> colonists, EntangledArcManager manager)
        {
            if (colonists == null || colonists.Count < 2) return;

            var allDefs = DefDatabase<EntangledArcDef>.AllDefsListForReading;
            if (allDefs.Count == 0) return;

            // Prefer arc defs that have never fired before; fall back to all defs
            // (random pick among qualifiers) once every def has been used at least once.
            var unusedDefs = allDefs.Where(d => !manager.IsArcDefUsed(d.defName)).ToList();
            bool anyUnused = unusedDefs.Count > 0;

            // Shuffle so we don't always favour the same pawn order
            var shuffled = colonists.InRandomOrder().ToList();

            for (int i = 0; i < shuffled.Count; i++)
            {
                for (int j = i + 1; j < shuffled.Count; j++)
                {
                    var a = shuffled[i];
                    var b = shuffled[j];

                    if (manager.IsInEntangledArc(a) || manager.IsInEntangledArc(b)) continue;

                    EntangledArcDef? chosenDef;
                    bool swap;

                    if (anyUnused)
                    {
                        // Score-based selection among unused defs only
                        (chosenDef, swap) = FindBestArcDef(a, b, unusedDefs);
                    }
                    else
                    {
                        // All defs have been seen — pick randomly from whatever qualifies
                        (chosenDef, swap) = FindRandomQualifyingArcDef(a, b, allDefs);
                    }

                    if (chosenDef == null) continue;

                    // Activation chance roll (0–100)
                    if (Rand.Value * 100f > chosenDef.activationChance) continue;

                    // If requirements matched better with B as initiator, swap
                    var initiator = swap ? b : a;
                    var partner   = swap ? a : b;

                    if (manager.TryStartArc(initiator, partner, chosenDef))
                        return; // One arc per evaluation cycle
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  DEF SELECTION
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the highest-scoring EntangledArcDef for a pair plus whether
        /// the roles should be swapped (b becomes initiator).
        /// Returns (null, false) if no def qualifies.
        /// </summary>
        private static (EntangledArcDef? def, bool swap) FindBestArcDef(
            Pawn a, Pawn b, List<EntangledArcDef> allDefs)
        {
            var profA = GetProfile(a);
            var profB = GetProfile(b);

            float bestScore   = -1f;
            EntangledArcDef? bestDef = null;
            bool bestSwap     = false;

            foreach (var def in allDefs)
            {
                // Relation gate
                if (!string.IsNullOrEmpty(def.requiredRelationDefName) &&
                    !HasRelation(a, b, def.requiredRelationDefName))
                    continue;

                // Try A=initiator, B=partner
                float scoreAB = ScorePair(profA, profB, def);
                if (scoreAB >= 0f && scoreAB > bestScore)
                {
                    bestScore = scoreAB;
                    bestDef   = def;
                    bestSwap  = false;
                }

                // Try B=initiator, A=partner (requirements may be asymmetric)
                float scoreBA = ScorePair(profB, profA, def);
                if (scoreBA >= 0f && scoreBA > bestScore)
                {
                    bestScore = scoreBA;
                    bestDef   = def;
                    bestSwap  = true;
                }
            }

            return (bestDef, bestSwap);
        }

        /// <summary>
        /// When all defs have already been used, collect every def whose requirements
        /// are met by this pair and return one at random. Returns (null, false) if none qualify.
        /// </summary>
        private static (EntangledArcDef? def, bool swap) FindRandomQualifyingArcDef(
            Pawn a, Pawn b, List<EntangledArcDef> allDefs)
        {
            var profA = GetProfile(a);
            var profB = GetProfile(b);

            var candidates = new List<(EntangledArcDef def, bool swap)>();

            foreach (var def in allDefs)
            {
                if (!string.IsNullOrEmpty(def.requiredRelationDefName) &&
                    !HasRelation(a, b, def.requiredRelationDefName))
                    continue;

                if (ScorePair(profA, profB, def) >= 0f)
                    candidates.Add((def, false));
                else if (ScorePair(profB, profA, def) >= 0f)
                    candidates.Add((def, true));
            }

            if (candidates.Count == 0) return (null, false);
            var chosen = candidates.RandomElement();
            return (chosen.def, chosen.swap);
        }

        private static float ScorePair(
            PawnNarrativeProfile initiator,
            PawnNarrativeProfile partner,
            EntangledArcDef def)
        {
            float iScore = initiator.MatchScore(def.initiatorRequirements);
            if (iScore < 0f) return -1f;

            float pScore = partner.MatchScore(def.partnerRequirements);
            if (pScore < 0f) return -1f;

            return iScore + pScore;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  STAGE SELECTION
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Selects the best-matching unused stage from the arc's pool
        /// for the current position (opening / middle / climax).
        /// </summary>
        public static EntangledStageDef? SelectNextStage(
            EntangledArcDef def,
            PawnNarrativeProfile initiatorProfile,
            PawnNarrativeProfile partnerProfile,
            List<EntangledStageDef> usedStages,
            bool isOpening,
            bool isClimax,
            string? preferredTagDefName = null)
        {
            // Filter by usage and role
            var available = def.stagePool
                .Where(s => !usedStages.Contains(s))
                .Where(s => !isClimax  || s.isClimax)
                .Where(s => !isOpening || s.isOpening ||
                            !def.stagePool.Any(x => x.isOpening))
                .ToList();

            if (available.Count == 0)
            {
                Log.Warning($"[PawnChronicles] Entangled arc '{def.defName}' " +
                            $"has no available stages (opening={isOpening}, climax={isClimax}).");
                return null;
            }

            // Score and sort, with optional player-choice bias
            var preferred = string.IsNullOrEmpty(preferredTagDefName)
                ? null
                : DefDatabase<NarrativeTagDef>.GetNamedSilentFail(preferredTagDefName);

            var scored = available
                .Select(s =>
                {
                    float score = s.MatchScore(initiatorProfile, partnerProfile);
                    if (score >= 0f && preferred != null &&
                        s.initiatorTagRequirements?.Any(r => r.tag == preferred) == true)
                        score += 2f;
                    return (stage: s, score);
                })
                .Where(x => x.score >= 0f)
                .OrderByDescending(x => x.score)
                .ToList();

            if (scored.Count == 0)
            {
                Log.Warning($"[PawnChronicles] No tag-matching stages in entangled arc '{def.defName}'. Falling back.");
                return available.RandomElement();
            }

            return scored[0].stage;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static PawnNarrativeProfile GetProfile(Pawn pawn) =>
            pawn.GetComp<CompPersonalChronicles>()?.GetOrBuildProfile()
            ?? PawnNarrativeProfile.BuildFor(pawn);

        private static bool HasRelation(Pawn a, Pawn b, string relDefName)
        {
            var rel = DefDatabase<PawnRelationDef>.GetNamedSilentFail(relDefName);
            if (rel == null) return false;
            return a.relations?.DirectRelationExists(rel, b) == true ||
                   b.relations?.DirectRelationExists(rel, a) == true;
        }
    }
}
