using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Defines a real-world event that fires alongside an arc stage's narrative text.
    /// A grammar-generated bridge letter explains WHY this specific event is
    /// happening to this specific pawn - the story text lands first, then the
    /// world changes to match it.
    ///
    /// Attach to QuestStageDef.onStartIncident for stage-start consequences,
    /// or to EpicOutcome.incident for completion consequences.
    /// </summary>
    public class NarrativeIncident
    {
        // ── What fires ───────────────────────────────────────────────────────
        /// <summary>Category of world event.</summary>
        public NarrativeIncidentType type = NarrativeIncidentType.ItemSpawn;

        /// <summary>0–1 chance this incident actually fires. 1 = always.</summary>
        public float chance = 1f;

        // ── Bridge letter ─────────────────────────────────────────────────────
        /// <summary>
        /// Grammar bridge role key. Resolves two rules from the grammar XML:
        ///   bridge_{bridgeRole}_title  -> appended to letter label
        ///   bridge_{bridgeRole}_body   -> appended after narrative body as separator + explanation
        ///
        /// Example: bridgeRole = "violence_confrontation" resolves
        ///   bridge_violence_confrontation_title / bridge_violence_confrontation_body
        /// These use all pawn symbols ([pawn_nameDef], [pc_relation_0_name], etc.)
        /// to make the explanation specific to THIS pawn's story.
        /// </summary>
        public string bridgeRole;

        // ── Grace period override ─────────────────────────────────────────────
        /// <summary>
        /// If true, this incident bypasses the early-game grace period guard.
        /// Set to true for climax incidents - they are earned and must always fire.
        /// </summary>
        public bool bypassGracePeriod = false;

        // ── Raids / hostile arrivals ──────────────────────────────────────────
        /// <summary>
        /// Threat points for raids. 200 ≈ 1–2 pawns. 400 ≈ 3–5 pawns.
        /// Scaled against the vanilla raid minimum automatically.
        /// </summary>
        public float incidentPoints = 200f;

        // ── Mental breaks ─────────────────────────────────────────────────────
        /// <summary>
        /// MentalStateDef defName to trigger. Null defaults to Wander_Sad.
        /// Options: Wander_Sad, Berserk, GiveUpExit, Crying, etc.
        /// </summary>
        public string mentalStateDef;

        // ── Item spawns ───────────────────────────────────────────────────────
        /// <summary>ThingDef defName to spawn near the pawn.</summary>
        public string thingDef;
        public int count = 1;
        public QualityCategory quality = QualityCategory.Normal;
        /// <summary>ThingDef defName for material (null = default stuff for that def).</summary>
        public string stuffDef;

        // ── Hediff application / removal ──────────────────────────────────────
        /// <summary>HediffDef defName to apply or remove.</summary>
        public string hediffDef;
        /// <summary>BodyPartDef defName for the body part to target. Null = whole body.</summary>
        public string bodyPartDef;
        /// <summary>Severity of the applied hediff. 0.15 = minor, 0.5 = serious, 1.0 = extreme.</summary>
        public float hediffSeverity = 0.2f;

        // ── Thought / memory injection ────────────────────────────────────────
        /// <summary>ThoughtDef defName to inject as a memory.</summary>
        public string thoughtDef;

        // ── Skill XP ─────────────────────────────────────────────────────────
        /// <summary>SkillDef defName to grant or drain XP from.</summary>
        public string skillDef;
        /// <summary>XP to grant (positive) or drain (negative). 2000 ≈ one level.</summary>
        public int skillXp = 0;

        // ── Inspiration ───────────────────────────────────────────────────────
        /// <summary>InspirationDef defName to try granting.</summary>
        public string inspirationDef;
    }

    public enum NarrativeIncidentType
    {
        /// <summary>Pawn suffers a mental break - trauma/grief made physical.</summary>
        MentalBreak,

        /// <summary>
        /// A single hostile presence walks onto the map.
        /// Uses RaidEnemy at low points (200) - typically 1–2 pawns.
        /// Represents someone from the pawn's past arriving with intent.
        /// </summary>
        HostileArrives,

        /// <summary>
        /// A wanderer joins the colony.
        /// Uses WandererJoin - someone the pawn was connected to arrives.
        /// </summary>
        FriendlyArrives,

        /// <summary>
        /// A trader caravan arrives.
        /// Uses TraderCaravanArrival - opportunity appears.
        /// </summary>
        TraderArrives,

        /// <summary>
        /// A small raid fires - the narrative consequence made into attackers.
        /// Uses RaidEnemy at moderate points.
        /// </summary>
        SmallRaid,

        /// <summary>
        /// A meaningful item appears near the pawn.
        /// The object represents something from the story.
        /// </summary>
        ItemSpawn,

        /// <summary>
        /// Apply a hediff directly to the pawn (wound, disease, or persistent narrative hediff).
        /// Fields: hediffDef, bodyPartDef (optional), hediffSeverity.
        /// The bridge letter narrates why. Great for cuts, burns, star-fever, ghost-haunting, etc.
        /// </summary>
        HediffApply,

        /// <summary>
        /// Remove a specific hediff from the pawn (arc resolves a condition).
        /// Fields: hediffDef.
        /// </summary>
        HediffRemove,

        /// <summary>
        /// Inject a thought/memory into the pawn's mood system.
        /// Fields: thoughtDef.
        /// Lighter than a full mental break - a flash of feeling, not a crisis.
        /// </summary>
        ThoughtApply,

        /// <summary>
        /// Grant or drain skill XP from the pawn.
        /// Fields: skillDef, skillXp (positive = grant, negative = drain).
        /// 2000 XP ≈ one level at mid-tier.
        /// </summary>
        SkillChange,

        /// <summary>
        /// Try to grant the pawn an inspiration.
        /// Fields: inspirationDef.
        /// Will silently skip if the pawn already has an inspiration or isn't eligible.
        /// </summary>
        InspirationGrant,
    }
}
