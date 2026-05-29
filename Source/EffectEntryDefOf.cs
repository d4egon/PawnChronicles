using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Strongly-typed references to EffectEntryDef instances defined in XML.
    /// Add entries here as needed for direct code access; the pool drawer
    /// queries all defs from the DefDatabase and does not require registration here.
    /// </summary>
    [DefOf]
    public static class EffectEntryDefOf
    {
        // ── Positive ─────────────────────────────────────────────────────────────
        public static EffectEntryDef PC_Effect_Pos_GainPassion;
        public static EffectEntryDef PC_Effect_Pos_CraftingSkill;
        public static EffectEntryDef PC_Effect_Pos_SocialSkill;
        public static EffectEntryDef PC_Effect_Pos_MedicineSkill;
        public static EffectEntryDef PC_Effect_Pos_FindStash;

        // ── Negative ─────────────────────────────────────────────────────────────
        public static EffectEntryDef PC_Effect_Neg_LosePassion;
        public static EffectEntryDef PC_Effect_Neg_OpinionDrop;
        public static EffectEntryDef PC_Effect_Neg_SmallRaid;
        public static EffectEntryDef PC_Effect_Neg_Hangover;

        static EffectEntryDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(EffectEntryDefOf));
    }
}
