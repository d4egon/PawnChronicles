using System.Collections.Generic;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Defines a small physical consequence that can fire when an ember resolves.
    /// Consequence is filtered by the pawn's current WorkTypeDef and whether it's
    /// a positive or negative outcome - giving mining a different feel from cooking.
    ///
    /// Add new entries in XML - no C# changes required.
    /// WorkTypes: empty list = fires for any job.
    /// </summary>
    public class EmberConsequenceDef : Def
    {
        // ── Filtering ─────────────────────────────────────────────────────────
        /// <summary>
        /// WorkTypeDef defNames this consequence can fire for.
        /// Empty list = universal, fires regardless of current job.
        /// </summary>
        public List<string> workTypes = new List<string>();

        /// <summary>True = positive ripple; false = negative ripple (minor cost).</summary>
        public bool isPositive = true;

        /// <summary>Relative weight for selection within the matching pool.</summary>
        public float weight = 1f;

        // ── What fires ────────────────────────────────────────────────────────
        /// <summary>
        /// Type of world consequence. Supported: ItemSpawn, HediffApply, ThoughtApply, SkillChange.
        /// Other NarrativeIncidentType values are ignored (too large for an ember).
        /// </summary>
        public NarrativeIncidentType type = NarrativeIncidentType.ItemSpawn;

        // ── Item spawn ────────────────────────────────────────────────────────
        /// <summary>ThingDef defName to spawn near the pawn.</summary>
        public string thingDef;
        public int count = 1;
        public QualityCategory quality = QualityCategory.Normal;
        /// <summary>ThingDef defName for material. Null = default stuff.</summary>
        public string stuffDef;

        // ── Hediff ────────────────────────────────────────────────────────────
        /// <summary>Primary HediffDef defName to apply. Keep severity ≤ 0.10 for ember-scale wounds.</summary>
        public string hediffDef;
        /// <summary>Severity of the primary hediff. 0.05 ≈ very minor bruise.</summary>
        public float hediffSeverity = 0.05f;

        /// <summary>
        /// Optional second hediff applied at the same time as the primary.
        /// Useful for e.g. "poison sting" - ToxicBuildup + Scratch simultaneously.
        /// </summary>
        public string secondaryHediffDef;
        public float secondaryHediffSeverity = 0.02f;

        // ── Thought ───────────────────────────────────────────────────────────
        /// <summary>ThoughtDef defName to inject into mood memory.</summary>
        public string thoughtDef;

        // ── Skill XP ─────────────────────────────────────────────────────────
        /// <summary>SkillDef defName to grant XP to.</summary>
        public string skillDef;
        /// <summary>XP to grant. 4000 ≈ a productive half-hour. 20000 ≈ one level.</summary>
        public int skillXp = 4000;
    }
}
