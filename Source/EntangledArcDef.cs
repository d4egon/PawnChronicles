using System.Collections.Generic;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// The type of relationship an entangled arc depicts.
    /// Drives grammar selection, stage pool shape, and outcome flavour.
    /// </summary>
    public enum EntangledArcType
    {
        Rivalry,            // Conflict, competition, mutual enmity turning into something else
        Romance,            // Kinship-dominant, desire, intimacy, loss of it
        MentorApprentice,   // Skill or experience gap - one pawn forging the other
        UnlikelyAllies,     // Radically different profiles forced into shared purpose
        BoundByBlood        // Family members facing a shared ordeal
    }

    /// <summary>
    /// Defines an arc that is shared between exactly two pawns.
    ///
    /// The EntangledArcManager evaluates all free-colonist pairs periodically.
    /// When both pawns satisfy the initiator and partner requirements, and the
    /// activation roll succeeds, a new EntangledArcState is created and the arc
    /// begins its first stage.
    ///
    /// XML example:
    ///   &lt;PawnChronicles.EntangledArcDef&gt;
    ///     &lt;defName&gt;PC_Entangled_Rivalry&lt;/defName&gt;
    ///     &lt;arcType&gt;Rivalry&lt;/arcType&gt;
    ///     &lt;stageCount&gt;3&lt;/stageCount&gt;
    ///     &lt;activationChance&gt;35&lt;/activationChance&gt;
    ///     &lt;initiatorRequirements&gt;...&lt;/initiatorRequirements&gt;
    ///     &lt;partnerRequirements&gt;...&lt;/partnerRequirements&gt;
    ///     &lt;stagePool&gt;&lt;li&gt;PC_EStage_Rivalry_Opening&lt;/li&gt;...&lt;/stagePool&gt;
    ///   &lt;/PawnChronicles.EntangledArcDef&gt;
    /// </summary>
    public class EntangledArcDef : Def
    {
        // ── Arc identity ──────────────────────────────────────────────────────
        public EntangledArcType arcType   = EntangledArcType.UnlikelyAllies;
        public int              stageCount = 3;

        /// <summary>0–100. Chance this def is selected once a pair qualifies.</summary>
        public float activationChance  = 35f;
        public float generationWeight  = 10f;

        // ── Pawn requirements ─────────────────────────────────────────────────
        /// <summary>Profile requirements for the "primary" pawn (higher-scoring match).</summary>
        public List<NarrativeTagRequirement> initiatorRequirements = new List<NarrativeTagRequirement>();

        /// <summary>Profile requirements for the "secondary" pawn.</summary>
        public List<NarrativeTagRequirement> partnerRequirements   = new List<NarrativeTagRequirement>();

        /// <summary>
        /// Optional. If set, at least one of the two pawns must have this
        /// relation with the other before the arc can start.
        /// Use the PawnRelationDef defName (e.g. "Lover", "Spouse", "Sibling").
        /// </summary>
        public string? requiredRelationDefName;

        // ── Stage pool ────────────────────────────────────────────────────────
        public List<EntangledStageDef> stagePool = new List<EntangledStageDef>();

        // ── Outcomes ──────────────────────────────────────────────────────────
        /// <summary>Backstory applied to the initiator pawn on arc success.</summary>
        public BackstoryDef? successInitiatorBackstory;

        /// <summary>Backstory applied to the partner pawn on arc success.</summary>
        public BackstoryDef? successPartnerBackstory;

        /// <summary>Backstory applied to the initiator pawn on arc failure.</summary>
        public BackstoryDef? failureInitiatorBackstory;

        /// <summary>Backstory applied to the partner pawn on arc failure.</summary>
        public BackstoryDef? failurePartnerBackstory;

        /// <summary>
        /// Arc-level mechanical outcomes - used when the resolved climax stage
        /// does not define its own per-pawn outcomes.
        /// </summary>
        public EpicOutcome? initiatorSuccessOutcome;
        public EpicOutcome? partnerSuccessOutcome;
        public EpicOutcome? initiatorFailureOutcome;
        public EpicOutcome? partnerFailureOutcome;
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A single stage within an EntangledArcDef's stage pool.
    /// Grammar resolves BOTH initiator and partner pawn symbols simultaneously.
    /// </summary>
    public class EntangledStageDef : Def
    {
        // ── Role flags ────────────────────────────────────────────────────────
        public bool isOpening = false;
        public bool isMiddle  = true;
        public bool isClimax  = false;

        /// <summary>
        /// For climax stages: true = success outcome, false = failure.
        /// </summary>
        public bool outcomeSetsSuccess = true;

        // ── Tag matching ──────────────────────────────────────────────────────
        /// <summary>Requirements checked against the initiator pawn's profile.</summary>
        public List<NarrativeTagRequirement> initiatorTagRequirements = new List<NarrativeTagRequirement>();

        /// <summary>Requirements checked against the partner pawn's profile.</summary>
        public List<NarrativeTagRequirement> partnerTagRequirements   = new List<NarrativeTagRequirement>();

        // ── Wait condition ownership ──────────────────────────────────────────
        /// <summary>
        /// Which pawn(s) must meet the wait condition before the arc can advance.
        /// </summary>
        public EntangledConditionMode conditionMode = EntangledConditionMode.InitiatorOnly;

        // ── Mechanical outcomes (applied on climax resolution) ─────────────────
        public EpicOutcome? initiatorSuccessOutcome;
        public EpicOutcome? partnerSuccessOutcome;
        public EpicOutcome? initiatorFailureOutcome;
        public EpicOutcome? partnerFailureOutcome;

        // ── Role string ───────────────────────────────────────────────────────
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

        // ── Scoring ───────────────────────────────────────────────────────────
        /// <summary>
        /// Combined match score against both profiles.
        /// Returns -1 if any hard requirement fails on either side.
        /// </summary>
        public float MatchScore(PawnNarrativeProfile initiator, PawnNarrativeProfile partner)
        {
            if (initiator == null || partner == null) return -1f;

            float iScore = ScoreSide(initiator, initiatorTagRequirements);
            if (iScore < 0f) return -1f;

            float pScore = ScoreSide(partner, partnerTagRequirements);
            if (pScore < 0f) return -1f;

            return iScore + pScore;
        }

        private static float ScoreSide(PawnNarrativeProfile profile, List<NarrativeTagRequirement> reqs)
        {
            if (reqs == null || reqs.Count == 0) return 0f;
            float total = 0f;
            foreach (var req in reqs)
            {
                if (req.tag == null) continue;
                float s = profile.GetScore(req.tag);
                if (req.minScore > 0 && s < req.minScore) return -1f;
                total += s * req.weight;
            }
            return total;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Controls which pawn(s) must satisfy the wait condition before the
    /// entangled arc stage can advance.
    /// </summary>
    public enum EntangledConditionMode
    {
        /// <summary>Only the initiator pawn's condition is checked.</summary>
        InitiatorOnly,

        /// <summary>Only the partner pawn's condition is checked.</summary>
        PartnerOnly,

        /// <summary>Either pawn meeting the condition is sufficient.</summary>
        EitherMeets,

        /// <summary>Both pawns must independently meet the condition.</summary>
        BothMustMeet
    }
}
