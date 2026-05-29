using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnChronicles
{
    /// <summary>
    /// Live runtime state of an entangled arc between two pawns.
    /// Owned and ticked by EntangledArcManager (a GameComponent).
    ///
    /// Both pawns see this arc's shared entries in their Chronicles tab.
    /// Either pawn can trigger the advance from their tab.
    /// </summary>
    public class EntangledArcState : IExposable
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public string arcDefName = "";

        // ── Pawn references ───────────────────────────────────────────────────
        public Pawn? initiator;
        public Pawn? partner;

        // ── Progress ──────────────────────────────────────────────────────────
        public int                   currentStage  = 0;
        public List<ArcStageEntry>   sharedEntries = new List<ArcStageEntry>();
        private List<EntangledStageDef> _usedStages = new List<EntangledStageDef>();

        // ── Lifecycle state ───────────────────────────────────────────────────
        public bool isCompleted     = false;
        public bool resolvedSuccess = false;
        public int  startedAtTick   = 0;
        public int  timeoutTick     = 0;

        // ── Mechanical failure reason (visible to player) ───────────────────
        public string mechanicalFailureReason = "";

        // ── Mechanical success reason (visible to player) ───────────────────
        public string mechanicalSuccessReason = "";
        
        // 30 in-game days before the arc auto-fails if no progress is made
        private const int TimeoutDuration = 1800000;

        // ── Cached profiles (rebuilt on demand, not saved) ─────────────────────
        private PawnNarrativeProfile? _initiatorProfile;
        private PawnNarrativeProfile? _partnerProfile;

        // ─────────────────────────────────────────────────────────────────────
        //  FACTORY
        // ─────────────────────────────────────────────────────────────────────

        public static EntangledArcState Create(Pawn initiator, Pawn partner, EntangledArcDef def)
        {
            return new EntangledArcState
            {
                arcDefName   = def.defName,
                initiator    = initiator,
                partner      = partner,
                startedAtTick = Find.TickManager.TicksGame,
                timeoutTick  = Find.TickManager.TicksGame + TimeoutDuration,
                sharedEntries = new List<ArcStageEntry>(),
                _usedStages   = new List<EntangledStageDef>()
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ACCESSORS
        // ─────────────────────────────────────────────────────────────────────

        public EntangledArcDef? ArcDef =>
            DefDatabase<EntangledArcDef>.GetNamedSilentFail(arcDefName);

        /// <summary>The last stage that hasn't been fully resolved yet.</summary>
        public ArcStageEntry? CurrentEntry =>
            sharedEntries.LastOrDefault(e => !e.IsResolved);

        public bool CanAdvance =>
            CurrentEntry is { conditionMet: true, playerAdvanced: false };

        public bool TimedOut =>
            Find.TickManager.TicksGame > timeoutTick;

        public bool InvolvesPawn(Pawn pawn) =>
            pawn != null && (pawn == initiator || pawn == partner);

        public Pawn? OtherPawn(Pawn pawn)
        {
            if (pawn == initiator) return partner;
            if (pawn == partner)   return initiator;
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PROFILE ACCESS (cached, not serialised)
        // ─────────────────────────────────────────────────────────────────────

        public PawnNarrativeProfile InitiatorProfile
        {
            get
            {
                if (_initiatorProfile == null && initiator != null)
                    _initiatorProfile = initiator.GetComp<CompPersonalChronicles>()?.GetOrBuildProfile()
                                        ?? PawnNarrativeProfile.BuildFor(initiator);
                return _initiatorProfile ?? PawnNarrativeProfile.BuildFor(initiator!);
            }
        }

        public PawnNarrativeProfile PartnerProfile
        {
            get
            {
                if (_partnerProfile == null && partner != null)
                    _partnerProfile = partner.GetComp<CompPersonalChronicles>()?.GetOrBuildProfile()
                                      ?? PawnNarrativeProfile.BuildFor(partner);
                return _partnerProfile ?? PawnNarrativeProfile.BuildFor(partner!);
            }
        }

        /// <summary>Call when either pawn's profile changes so it is re-built next access.</summary>
        public void InvalidateProfiles()
        {
            _initiatorProfile = null;
            _partnerProfile   = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  STAGE TRACKING
        // ─────────────────────────────────────────────────────────────────────

        public void RecordUsedStage(EntangledStageDef stage)
        {
            if (stage != null && !_usedStages.Contains(stage))
                _usedStages.Add(stage);
        }

        public List<EntangledStageDef> GetUsedStages() => _usedStages;

        public EntangledStageDef? GetCurrentStageDef()
        {
            var entry = CurrentEntry;
            if (entry == null) return null;
            return DefDatabase<EntangledStageDef>.GetNamedSilentFail(entry.stageDefName);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SAVE / LOAD
        // ─────────────────────────────────────────────────────────────────────

        public void ExposeData()
        {
            Scribe_Values.Look(ref arcDefName,      "arcDefName",      "");
            Scribe_References.Look(ref initiator,   "initiator");
            Scribe_References.Look(ref partner,     "partner");
            Scribe_Values.Look(ref currentStage,    "currentStage",    0);
            Scribe_Values.Look(ref isCompleted,     "isCompleted",     false);
            Scribe_Values.Look(ref resolvedSuccess, "resolvedSuccess", false);
            Scribe_Values.Look(ref startedAtTick,   "startedAtTick",   0);
            Scribe_Values.Look(ref timeoutTick,     "timeoutTick",     0);

            Scribe_Collections.Look(ref sharedEntries, "sharedEntries", LookMode.Deep);
            Scribe_Collections.Look(ref _usedStages,   "usedStages",    LookMode.Def);

            Scribe_Values.Look(ref mechanicalFailureReason, "mechanicalFailureReason", "");
            Scribe_Values.Look(ref mechanicalSuccessReason, "mechanicalSuccessReason", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sharedEntries ??= new List<ArcStageEntry>();
                _usedStages   ??= new List<EntangledStageDef>();
                // Profiles re-built lazily on next access
                _initiatorProfile = null;
                _partnerProfile   = null;
            }
        }
    }
}
