using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.Grammar;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// GameComponent that owns all active EntangledArcStates.
    ///
    /// Responsibilities:
    ///   • Ticks every active arc (wait-condition checks, timeouts)
    ///   • Periodically evaluates free-colonist pairs for new arc eligibility
    ///   • Routes pawn death into arc resolution
    ///   • Saves and loads all arc state
    ///
    /// Access via EntangledArcManager.Instance (set on construction + load).
    /// </summary>
    public class EntangledArcManager : GameComponent
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        private static EntangledArcManager? _instance;
        public  static EntangledArcManager? Instance => _instance;

        // ── State ─────────────────────────────────────────────────────────────
        private List<EntangledArcState> _arcs            = new List<EntangledArcState>();
        private HashSet<string>         _usedArcDefNames = new HashSet<string>();
        private int                     _lastEvalTick    = -999999;

        // Evaluate new pairs every 3 in-game days
        private const int EvalInterval    = 180000;
        // Keep completed arcs for 2 days for the UI to display completion
        private const int PurgeAfterTicks = 120000;

        // ─────────────────────────────────────────────────────────────────────
        //  CONSTRUCTION / INIT
        // ─────────────────────────────────────────────────────────────────────

        public EntangledArcManager(Game game)
        {
            _instance = this;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PUBLIC QUERY API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Returns the active (non-completed) entangled arc for this pawn, or null.</summary>
        public EntangledArcState? GetActiveArcForPawn(Pawn pawn) =>
            _arcs.FirstOrDefault(a => !a.isCompleted && a.InvolvesPawn(pawn));

        /// <summary>Returns the most-recent completed arc for this pawn, or null.</summary>
        public EntangledArcState? GetCompletedArcForPawn(Pawn pawn) =>
            _arcs.LastOrDefault(a => a.isCompleted && a.InvolvesPawn(pawn));

        public bool IsInEntangledArc(Pawn pawn) =>
            GetActiveArcForPawn(pawn) != null;

        public IReadOnlyList<EntangledArcState> AllArcs => _arcs.AsReadOnly();

        public bool IsArcDefUsed(string defName) => _usedArcDefNames.Contains(defName);

        // ─────────────────────────────────────────────────────────────────────
        //  TICK
        // ─────────────────────────────────────────────────────────────────────

        public override void GameComponentTick()
        {
            int tick = Find.TickManager.TicksGame;

            // Tick each active arc
            foreach (var arc in _arcs.Where(a => !a.isCompleted).ToList())
                TickArc(arc, tick);

            // Purge stale completed arcs (we keep them briefly for UI)
            _arcs.RemoveAll(a =>
                a.isCompleted && tick - a.startedAtTick > PurgeAfterTicks * 10);

            // Periodic pair evaluation
            if (tick - _lastEvalTick >= EvalInterval)
            {
                _lastEvalTick = tick;
                TryEvaluateNewPairs();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ARC TICKING
        // ─────────────────────────────────────────────────────────────────────

        private void TickArc(EntangledArcState arc, int tick)
        {
            // Sanity - dead or missing pawns collapse the arc immediately
            if (arc.initiator == null || arc.partner == null)
            {
                arc.isCompleted = true;
                return;
            }

            if (arc.initiator.Dead || arc.partner.Dead)
            {
                ResolveArcOnDeath(arc);
                return;
            }

            // Timeout (no progress for 30 days)
            if (arc.TimedOut)
            {
                CompleteArc(arc, success: false);
                return;
            }

            // Check wait condition for the current stage
            var entry = arc.CurrentEntry;
            if (entry == null || entry.conditionMet) return;

            // Waiting for the player to pick a direction
            if (entry.AwaitingChoice)
            {
                if (PawnChroniclesMod.Settings.autopilotEnabled)
                    AutopilotChoiceForArc(arc, entry);
                return;
            }

            var stageDef = arc.GetCurrentStageDef();
            if (stageDef == null) return;

            bool condMet = EvaluateCondition(arc, entry, stageDef);
            if (condMet)
            {
                entry.conditionMet = true;

                if (PawnChroniclesMod.Settings.autopilotEnabled)
                {
                    // Autopilot advances immediately without bothering the player
                    PlayerAdvanceArc(arc);
                }
                else
                {
                    // Notify both pawns - player must click Advance
                    var targets = BuildLookTargets(arc);
                    Messages.Message(
                        $"The shared arc between {arc.initiator.LabelShort} and {arc.partner.LabelShort} is ready to continue.",
                        targets, MessageTypeDefOf.PositiveEvent, historical: true);
                }
            }
        }

        private bool EvaluateCondition(EntangledArcState arc, ArcStageEntry entry, EntangledStageDef stageDef)
        {
            return stageDef.conditionMode switch
            {
                EntangledConditionMode.InitiatorOnly =>
                    StageWaitCondition.CheckConditionMet(arc.initiator!, entry),
                EntangledConditionMode.PartnerOnly =>
                    StageWaitCondition.CheckConditionMet(arc.partner!, entry),
                EntangledConditionMode.EitherMeets =>
                    StageWaitCondition.CheckConditionMet(arc.initiator!, entry) ||
                    StageWaitCondition.CheckConditionMet(arc.partner!, entry),
                EntangledConditionMode.BothMustMeet =>
                    StageWaitCondition.CheckConditionMet(arc.initiator!, entry) &&
                    StageWaitCondition.CheckConditionMet(arc.partner!, entry),
                _ =>
                    StageWaitCondition.CheckConditionMet(arc.initiator!, entry)
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PLAYER ADVANCE
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called from the UI when the player picks one of the 3 path choices on a shared arc.
        /// Copies the chosen condition into the entry. Walk-away closes the arc gracefully.
        /// </summary>
        public void PlayerMakeChoiceArc(EntangledArcState arc, int choiceIndex)
        {
            if (arc.isCompleted) return;
            var entry = arc.CurrentEntry;
            if (entry == null || !entry.AwaitingChoice) return;
            if (choiceIndex < 0 || choiceIndex >= entry.choices.Count) return;

            var choice = entry.choices[choiceIndex];
            entry.chosenIndex = choiceIndex;
            entry.waitConditionKey   = choice.conditionKey;
            entry.waitConditionLabel = choice.conditionLabel;
            entry.waitBaselineValue  = choice.baseline;
            entry.waitTargetDelta    = choice.targetDelta;
        }

        /// <summary>
        /// Called when the player clicks "Advance Arc" after a choice has been made
        /// and the wait condition is met.
        /// </summary>
        public void PlayerAdvanceArc(EntangledArcState arc, bool devMode = false)
        {
            if (arc.isCompleted) return;

            var entry = arc.CurrentEntry;
            if (entry == null) return;
            if (!devMode && !entry.conditionMet) return;

            entry.conditionMet  = true;
            entry.playerAdvanced = true;

            // Reset timeout on each advance
            arc.timeoutTick = Find.TickManager.TicksGame + 1800000;

            // Carry chosen tag forward to bias the next stage
            string? preferredTag = (entry.chosenIndex >= 0 && entry.choices.Count > entry.chosenIndex)
                ? entry.choices[entry.chosenIndex].tagDefName
                : null;

            if (entry.isClimax)
            {
                CompleteArc(arc, entry.stageRole == NarrativeGrammarResolver.RoleSuccess);
            }
            else
            {
                arc.currentStage++;
                var def = arc.ArcDef;
                if (def != null)
                    TriggerNextStage(arc, def, preferredTagDefName: preferredTag);
                else
                    CompleteArc(arc, success: true);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  START ARC
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Tries to start a new entangled arc between two pawns.
        /// Fails silently if either pawn is already in one.
        /// </summary>
        public bool TryStartArc(Pawn initiator, Pawn partner, EntangledArcDef def)
        {
            if (IsInEntangledArc(initiator) || IsInEntangledArc(partner))
                return false;

            var state = EntangledArcState.Create(initiator, partner, def);
            _arcs.Add(state);
            _usedArcDefNames.Add(def.defName);

            Log.Message($"[PawnChronicles] Entangled arc '{def.defName}' started: " +
                        $"{initiator.LabelShort} ↔ {partner.LabelShort}");

            TriggerNextStage(state, def, isOpening: true);
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  STAGE TRIGGERING
        // ─────────────────────────────────────────────────────────────────────

        private void TriggerNextStage(EntangledArcState arc, EntangledArcDef def,
            bool isOpening = false, string? preferredTagDefName = null)
        {
            bool isClimax = arc.currentStage >= def.stageCount - 1;

            var stage = EntangledArcEvaluator.SelectNextStage(
                def,
                arc.InitiatorProfile,
                arc.PartnerProfile,
                arc.GetUsedStages(),
                isOpening,
                isClimax,
                preferredTagDefName);

            if (stage == null)
            {
                CompleteArc(arc, success: true);
                return;
            }

            arc.RecordUsedStage(stage);

            // Choices are built from the initiator's profile (they drive the arc)
            var conditionPawn    = arc.initiator!;
            var conditionProfile = arc.InitiatorProfile;

            var snapshot = StageWaitCondition.GetPawnSnapshot(conditionPawn);

            // Generate narrative referencing BOTH pawns
            string title = NarrativeGrammarResolver.ResolveEntangledTitle(
                arc.initiator!, arc.partner!,
                arc.InitiatorProfile, arc.PartnerProfile,
                stage.StageRole, def.arcType);

            string body = NarrativeGrammarResolver.ResolveEntangledBody(
                arc.initiator!, arc.partner!,
                arc.InitiatorProfile, arc.PartnerProfile,
                stage.StageRole, def.arcType);

            // Build choices from initiator's top tags - condition activates on player pick
            var choices = StageWaitCondition.BuildChoicesFor(
                conditionPawn, conditionProfile, stage.StageRole, isClimax);

            var entry = new ArcStageEntry(
                title, body, stage.StageRole,
                waitConditionLabel: "Choose a path to continue",
                waitConditionKey:   "",
                waitBaselineValue:  0,
                waitTargetDelta:    0,
                isClimax:           isClimax);

            entry.choices      = choices;
            entry.snapshot     = snapshot;
            entry.stageDefName = stage.defName;

            arc.sharedEntries.Add(entry);

            // Send a letter to both pawns
            SendArcLetter(arc, entry, isOpening, isClimax);
        }

        private void SendArcLetter(EntangledArcState arc, ArcStageEntry entry,
            bool isOpening, bool isClimax)
        {
            bool eiSpawned = arc.initiator?.Spawned == true;
            bool paSpawned = arc.partner?.Spawned   == true;
            if (!eiSpawned && !paSpawned) return;

            var targets = BuildLookTargets(arc);

            if (isOpening)
            {
                Find.LetterStack.ReceiveLetter(
                    $"{arc.initiator!.LabelShort} & {arc.partner!.LabelShort} - {entry.title}",
                    entry.body,
                    LetterDefOf.NeutralEvent,
                    targets);
            }
            else if (isClimax)
            {
                Find.LetterStack.ReceiveLetter(
                    $"{arc.initiator!.LabelShort} & {arc.partner!.LabelShort}: the arc resolves",
                    $"{entry.title}\n\n{entry.body}\n\n{entry.waitConditionLabel}",
                    LetterDefOf.NeutralEvent,
                    targets);
            }
            else
            {
                Messages.Message(
                    $"{arc.initiator!.LabelShort} & {arc.partner!.LabelShort} - {entry.title}",
                    targets, MessageTypeDefOf.NeutralEvent, historical: true);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ARC COMPLETION
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Dev-accessible overload - called by debug gizmos.</summary>
        public void ForceCompleteArc(EntangledArcState arc, bool success) => CompleteArc(arc, success);

        private void CompleteArc(EntangledArcState arc, bool success)
        {
            arc.isCompleted     = true;
            if (!success)
            {
                var failureRules = new List<Rule>();
                Lexicon.EmitMechanicalFailureReason(arc, failureRules);
                
                var lastEntry = arc.sharedEntries.LastOrDefault();
                if (lastEntry != null)
                    lastEntry.mechanicalFailureReason = failureRules.Count > 0 
                        ? failureRules[0].Generate() 
                        : "unknown factors";
            }
                        // Generate mechanical reason for success or failure
            if (success)
            {
                var successRules = new List<Rule>();
                Lexicon.EmitMechanicalSuccessReason(arc, successRules);
                
                var lastEntry = arc.sharedEntries.LastOrDefault();
                if (lastEntry != null)
                    lastEntry.mechanicalSuccessReason = successRules.Count > 0 
                        ? successRules[0].Generate() 
                        : "conditions were met";
            }
            arc.resolvedSuccess = success;

            var def = arc.ArcDef;
            if (def == null) return;

            var lastStage = arc.GetUsedStages().LastOrDefault();

            // Resolve outcomes (stage takes priority; arc def is the fallback)
            var iOutcome = ResolveOutcome(def, lastStage, success, isInitiator: true);
            var pOutcome = ResolveOutcome(def, lastStage, success, isInitiator: false);

            // Apply outcomes to initiator
            if (arc.initiator != null && !arc.initiator.Dead)
            {
                if (iOutcome != null) EpicOutcomeApplicator.Apply(arc.initiator, iOutcome, success);
                ApplyBackstory(arc.initiator, def, success, isInitiator: true);
            }

            // Apply outcomes to partner
            if (arc.partner != null && !arc.partner.Dead)
            {
                if (pOutcome != null) EpicOutcomeApplicator.Apply(arc.partner, pOutcome, success);
                ApplyBackstory(arc.partner, def, success, isInitiator: false);
            }

            // Final letter — upgrade from Message to ReceiveLetter with full outcome detail
            string name1 = arc.initiator?.LabelShort ?? "Unknown";
            string name2 = arc.partner?.LabelShort   ?? "Unknown";

            string heading = success
                ? $"The shared story between {name1} and {name2} has reached its resolution."
                : $"The bond between {name1} and {name2} has fractured beyond repair.";

            string letterBody =
                $"{heading}\n\n" +
                BuildOutcomeSummary(name1, iOutcome) + "\n\n" +
                BuildOutcomeSummary(name2, pOutcome);

            var letterDef = success ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent;
            var targets   = BuildLookTargets(arc);

            Find.LetterStack.ReceiveLetter(
                $"{name1} & {name2}: arc resolved",
                letterBody,
                letterDef,
                targets);

            Log.Message($"[PawnChronicles] Entangled arc '{arc.arcDefName}' completed " +
                        $"(success={success}): {name1} ↔ {name2}");
        }

        /// <summary>
        /// Returns the EpicOutcome to apply for this pawn role.
        /// Stage-level outcome takes priority; arc def outcome is the fallback.
        /// This ensures outcomes defined on the arc def are used even when
        /// the climax stage has no per-pawn outcome of its own.
        /// </summary>
        private static EpicOutcome? ResolveOutcome(
            EntangledArcDef def, EntangledStageDef? stage, bool success, bool isInitiator)
        {
            // Stage outcome takes priority
            if (stage != null)
            {
                var stageOutcome = (success, isInitiator) switch
                {
                    (true,  true)  => stage.initiatorSuccessOutcome,
                    (true,  false) => stage.partnerSuccessOutcome,
                    (false, true)  => stage.initiatorFailureOutcome,
                    (false, false) => stage.partnerFailureOutcome
                };
                if (stageOutcome != null) return stageOutcome;
            }

            // Arc def fallback
            return (success, isInitiator) switch
            {
                (true,  true)  => def.initiatorSuccessOutcome,
                (true,  false) => def.partnerSuccessOutcome,
                (false, true)  => def.initiatorFailureOutcome,
                (false, false) => def.partnerFailureOutcome
            };
        }

        private void ApplyBackstory(Pawn pawn, EntangledArcDef def, bool success, bool isInitiator)
        {
            BackstoryDef? bs = (success, isInitiator) switch
            {
                (true,  true)  => def.successInitiatorBackstory,
                (true,  false) => def.successPartnerBackstory,
                (false, true)  => def.failureInitiatorBackstory,
                (false, false) => def.failurePartnerBackstory
            };

            if (bs == null) return;

            pawn.story.Adulthood = bs;
            pawn.Notify_DisabledWorkTypesChanged();
            pawn.workSettings?.EnableAndInitialize();
            PortraitsCache.SetDirty(pawn);
        }

        private void ResolveArcOnDeath(EntangledArcState arc)
        {
            var dead  = arc.initiator?.Dead == true ? arc.initiator : arc.partner;
            var alive = arc.initiator?.Dead == true ? arc.partner   : arc.initiator;

            CompleteArc(arc, success: false);

            if (dead != null && alive?.Spawned == true)
            {
                Messages.Message(
                    $"The shared story of {dead.LabelShort} and {alive.LabelShort} ended in death.",
                    alive, MessageTypeDefOf.NegativeEvent);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PAIR EVALUATION
        // ─────────────────────────────────────────────────────────────────────

        private void TryEvaluateNewPairs()
        {
            if (Find.Maps == null) return;

            foreach (var map in Find.Maps)
            {
                var free = map.mapPawns.FreeColonists
                    .Where(p => !p.Dead && p.Spawned && !IsInEntangledArc(p))
                    .Where(p => p.GetComp<CompPersonalChronicles>()?.chroniclesDisabled != true)
                    .ToList();

                EntangledArcEvaluator.EvaluatePairs(free, this);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  AUTOPILOT
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called from TickArc when autopilot is enabled and a shared arc entry
        /// is awaiting a choice. Picks a choice using the initiator's profile
        /// (stat-based mode) or randomly, then commits it.
        /// </summary>
        private void AutopilotChoiceForArc(EntangledArcState arc, ArcStageEntry entry)
        {
            if (entry.choices == null || entry.choices.Count == 0) return;

            int idx;
            if (PawnChroniclesMod.Settings.autopilotMode == AutopilotMode.StatBased)
            {
                var profile = arc.InitiatorProfile;
                idx = BestChoiceIndexByProfile(entry.choices, profile);
            }
            else
            {
                idx = Rand.Range(0, entry.choices.Count);
            }

            PlayerMakeChoiceArc(arc, idx);
        }

        private static int BestChoiceIndexByProfile(List<StageChoice> choices, PawnNarrativeProfile profile)
        {
            float bestScore = -1f;
            int   bestIdx   = 0;

            for (int i = 0; i < choices.Count; i++)
            {
                var c = choices[i];
                if (string.IsNullOrEmpty(c.tagDefName)) continue;

                var tagDef = DefDatabase<NarrativeTagDef>.GetNamedSilentFail(c.tagDefName);
                if (tagDef == null) continue;

                if (profile.Scores.TryGetValue(tagDef, out float score) && score > bestScore)
                {
                    bestScore = score;
                    bestIdx   = i;
                }
            }

            return bestIdx;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts an EpicOutcome into a human-readable summary line for inclusion
        /// in the arc completion letter. Shows skill XP, inspiration chance, and mood.
        /// </summary>
        private static string BuildOutcomeSummary(string pawnName, EpicOutcome? outcome)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(pawnName + ":");

            if (outcome == null)
            {
                sb.Append("  No notable effects.");
                return sb.ToString();
            }

            if (outcome.skillGains != null)
            {
                foreach (var gain in outcome.skillGains)
                {
                    var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(gain.skill);
                    string skillLabel = skillDef?.LabelCap ?? gain.skill;
                    sb.AppendLine($"  +{gain.xp:N0} {skillLabel} XP");
                }
            }

            if (outcome.bestSkillXP > 0)
                sb.AppendLine($"  +{outcome.bestSkillXP:N0} XP (best skill)");

            if (!string.IsNullOrEmpty(outcome.inspirationDef) && outcome.inspirationChance > 0f)
                sb.AppendLine($"  {(int)(outcome.inspirationChance * 100)}% chance of inspiration");

            if (!string.IsNullOrEmpty(outcome.moodThought))
            {
                var thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(outcome.moodThought);
                string thoughtLabel = thoughtDef?.LabelCap ?? outcome.moodThought;
                sb.Append($"  Mood: {thoughtLabel}");
            }

            return sb.ToString().TrimEnd();
        }

        private static LookTargets BuildLookTargets(EntangledArcState arc)
        {
            var spawned = new List<Thing>();
            if (arc.initiator?.Spawned == true) spawned.Add(arc.initiator);
            if (arc.partner?.Spawned   == true) spawned.Add(arc.partner);
            return spawned.Count > 0
                ? new LookTargets(spawned)
                : LookTargets.Invalid;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SAVE / LOAD
        // ─────────────────────────────────────────────────────────────────────

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref _arcs,            "entangledArcs",    LookMode.Deep);
            Scribe_Collections.Look(ref _usedArcDefNames, "usedArcDefNames",  LookMode.Value);
            Scribe_Values.Look(ref _lastEvalTick,         "lastEvalTick",     -999999);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                _arcs            ??= new List<EntangledArcState>();
                _usedArcDefNames ??= new HashSet<string>();
                _instance = this;
            }
        }
    }
}
