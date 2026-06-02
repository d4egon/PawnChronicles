using Verse;

namespace PawnChronicles
{
    /// <summary>
    /// Extension methods for RimWorld types used throughout PawnChronicles.
    /// </summary>
    public static class PawnChroniclesExtensions
    {
        /// <summary>
        /// Returns the pawn's narrative profile, building it fresh if not already cached.
        /// Returns a blank profile if the pawn has no CompPersonalChronicles.
        /// </summary>
        public static PawnNarrativeProfile GetNarrativeProfile(this Pawn pawn)
        {
            var comp = pawn?.GetComp<CompPersonalChronicles>();
            return comp?.GetOrBuildProfile() ?? PawnNarrativeProfile.BuildFor(pawn);
        }
    }
}
