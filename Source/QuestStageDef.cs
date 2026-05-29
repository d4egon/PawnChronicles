using System.Collections.Generic;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// A single stage template within a personal epic's stage pool.
    ///
    /// QuestStageDef declares its narrative role (opening/middle/climax) and tag
    /// requirements. At runtime the stage selector picks the best match and
    /// generates the appropriate universal quest script, passing the stage role
    /// into the Slate so QuestNode_EpicStage can resolve the right grammar.
    ///
    /// XML example:
    ///
    ///   <PawnChronicles.QuestStageDef>
    ///     <defName>PC_Stage_Ghost_Middle_UnderworldContact</defName>
    ///     <label>The Contact</label>
    ///     <isMiddle>true</isMiddle>
    ///     <tagRequirements>
    ///       <li>
    ///         <tag>PC_Tag_Underworld</tag>
    ///         <minScore>25</minScore>
    ///         <weight>2.0</weight>
    ///       </li>
    ///     </tagRequirements>
    ///   </PawnChronicles.QuestStageDef>
    /// </summary>
    public class QuestStageDef : Def
    {
        // ── Stage role flags ──────────────────────────────────────────────────

        public bool isOpening = false;
        public bool isMiddle  = true;

        /// <summary>
        /// Eligible as the climax (final) stage. Climax stages route to either
        /// PC_Quest_EpicSuccess or PC_Quest_EpicFailure based on outcomeSetsSuccess.
        /// </summary>
        public bool isClimax = false;

        /// <summary>
        /// For climax stages: true fires PawnEpic_Success (redeemed backstory).
        /// False fires PawnEpic_Failure (corrupted backstory).
        /// The grammar bank automatically picks "success" or "failure" text.
        /// </summary>
        public bool outcomeSetsSuccess = true;

        // ── Tag matching ──────────────────────────────────────────────────────

        public List<NarrativeTagRequirement> tagRequirements = new List<NarrativeTagRequirement>();

        // ── Stage role string ─────────────────────────────────────────────────

        /// <summary>
        /// When set, overrides the grammar key used for title/body resolution.
        /// The key cascade becomes: {primary}_{grammarRoleOverride}_{type},
        /// {grammarRoleOverride}_{type}, default_{type}.
        /// Used by addiction arc stages (e.g. "addiction_alcohol_opening").
        /// Leave null for all standard arc stages.
        /// </summary>
        public string grammarRoleOverride = null;

        /// <summary>
        /// The grammar role key for this stage.
        /// Passed into the quest Slate as "epicStageRole".
        /// </summary>
        public string StageRole
        {
            get
            {
                if (isClimax)
                    return outcomeSetsSuccess
                        ? NarrativeGrammarResolver.RoleSuccess
                        : NarrativeGrammarResolver.RoleFailure;
                if (isOpening) return NarrativeGrammarResolver.RoleOpening;
                return NarrativeGrammarResolver.RoleMiddle;
            }
        }

        /// <summary>
        /// Grammar role used for title/body resolution. Returns grammarRoleOverride
        /// when set, otherwise falls back to StageRole. Use this everywhere grammar
        /// is resolved; use StageRole/QuestScript for quest dispatch only.
        /// </summary>
        public string GrammarRole =>
            !string.IsNullOrEmpty(grammarRoleOverride) ? grammarRoleOverride : StageRole;

        /// <summary>
        /// Returns the universal quest script for this stage's role.
        /// Never null - always routes to one of the three PC_Quest_Epic* scripts.
        /// </summary>
        public QuestScriptDef QuestScript
        {
            get
            {
                if (isClimax)
                    return outcomeSetsSuccess
                        ? DefDatabase<QuestScriptDef>.GetNamed("PC_Quest_EpicSuccess")
                        : DefDatabase<QuestScriptDef>.GetNamed("PC_Quest_EpicFailure");

                return DefDatabase<QuestScriptDef>.GetNamed("PC_Quest_EpicStage");
            }
        }
        // Branch tag for Inferno path selection
        // XML: <branchTags><li>PC_Branch_Shadow</li></branchTags>
        public List<string> branchTags = new List<string>();
    
        public bool HasBranchTag(string tag)
        {
            return branchTags != null && branchTags.Contains(tag);
        }
        // ── Scoring ───────────────────────────────────────────────────────────

        /// <summary>
        /// Scores this stage against a pawn's narrative profile.
        /// Returns -1 if any hard requirement fails.
        /// Returns weighted sum of tag scores otherwise.
        /// </summary>
        // ── Mechanical outcomes ─────────────────────────────────────────────────────
        // Applied by EpicOutcomeApplicator when this climax stage resolves.
        // Only climax stages need these; opening/middle stages leave them null.
        public EpicOutcome successOutcome;
        public EpicOutcome failureOutcome;

        // ── Narrative incident ────────────────────────────────────────────────
        // Fires when this stage is triggered. Sends a combined letter:
        //   [narrative body] + separator + [bridge explanation of why this event]
        // Then fires the actual RimWorld incident (raid, wanderer, trader, etc.)
        // with sendLetter=false so only our letter appears.
        public NarrativeIncident onStartIncident;

        public float MatchScore(PawnNarrativeProfile profile)
        {
            if (profile == null) return -1f;

            float total = 0f;
            foreach (var req in tagRequirements)
            {
                if (req.tag == null) continue;
                float pawnScore = profile.GetScore(req.tag);
                if (req.minScore > 0 && pawnScore < req.minScore)
                    return -1f;
                total += pawnScore * req.weight;
            }
            return total;
        }
        
    }
    
}