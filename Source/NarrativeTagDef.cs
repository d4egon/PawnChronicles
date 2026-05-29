using System;
using Verse;

namespace PawnChronicles
{
    /// <summary>
    /// Defines a single narrative tag - a thematic dimension of a pawn's identity.
    /// Tags are fully XML-driven and extensible by any mod.
    ///
    /// To add a new tag from another mod:
    ///   1. Define a NarrativeTagDef in your XML with a unique defName
    ///   2. Write a class that extends NarrativeTagScorer
    ///   3. Reference it via scorerClass
    ///   No changes to PawnChronicles source required.
    /// </summary>
    public class NarrativeTagDef : Def
    {
        /// <summary>
        /// The scorer class responsible for evaluating this tag against a pawn.
        /// Must extend NarrativeTagScorer. If null, tag always scores 0.
        /// </summary>
        public Type? scorerClass;

        // Cached scorer instance - created once on first use.
        private NarrativeTagScorer? _scorer;

        public NarrativeTagScorer? GetScorer()
        {
            if (_scorer != null) return _scorer;

            if (scorerClass == null)
            {
                Log.Warning($"[PawnChronicles] NarrativeTagDef '{defName}' has no scorerClass defined.");
                return null;
            }

            if (!typeof(NarrativeTagScorer).IsAssignableFrom(scorerClass))
            {
                Log.Error($"[PawnChronicles] scorerClass '{scorerClass}' on '{defName}' does not extend NarrativeTagScorer.");
                return null;
            }

            _scorer = (NarrativeTagScorer)Activator.CreateInstance(scorerClass);
            return _scorer;
        }
    }
}