using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnChronicles
{
    public class PersonalEpicDef : Def
    {
        // ── Modus ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Intensity tier. Set in XML. Determines evaluator, stage count,
        /// and world consequences. Defaults to Fire.
        /// </summary>
        public EpicModus modus = EpicModus.Fire;

        // ── Stage pool ────────────────────────────────────────────────────────

        public List<QuestStageDef> stagePool = new List<QuestStageDef>();
        public List<NarrativeTagRequirement> tagRequirements = new();
        public int stageCount = 3;

        // ── Outcomes ──────────────────────────────────────────────────────────

        /// <summary>Short epithet shown in the Bio tab pill on success. Falls back to epic.label.</summary>
        public string? redeemedEpithet;
        /// <summary>Short epithet shown in the Bio tab pill on failure. Falls back to epic.label.</summary>
        public string? corruptedEpithet;

        /// <summary>
        /// Tag-keyed epithet pool for dynamic outcome selection.
        /// Takes priority over redeemedEpithet/corruptedEpithet when
        /// the pawn's dominant tag matches an entry.
        /// </summary>
        public List<TaggedEpithetEntry> taggedEpithets = new List<TaggedEpithetEntry>();

        // ── Narrative identity ────────────────────────────────────────────────

        public List<string> narrativeTags = new List<string>();
        public string successSignal = "PawnEpic_Success";
        public string failureSignal = "PawnEpic_Failure";
        public float generationWeight = 10f;
        public bool dynamicProfile = false;

        // ── Resolved tags ─────────────────────────────────────────────────────

        private List<NarrativeTagDef>? _resolvedTags;

        public List<NarrativeTagDef> ResolvedNarrativeTags
        {
            get
            {
                if (_resolvedTags != null) return _resolvedTags;

                _resolvedTags = new List<NarrativeTagDef>();
                foreach (var tagName in narrativeTags)
                {
                    var def = DefDatabase<NarrativeTagDef>.GetNamedSilentFail(tagName);
                    if (def != null)
                        _resolvedTags.Add(def);
                    else
                        Log.Warning($"[PawnChronicles] Epic '{defName}' references unknown tag '{tagName}'.");
                }
                return _resolvedTags;
            }
        }
    }

    public class TaggedEpithetEntry
    {
        /// <summary>NarrativeTagDef defName this entry applies to. Null = wildcard fallback.</summary>
        public string? tag;
        public bool onSuccess = true;
        public float weight = 1f;
        /// <summary>Short epithet title, e.g. "Soldier Reborn" or "The Shattered".</summary>
        public string? epithet;
    }
}
