using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using RimWorld;
using RimWorld.QuestGen;
using UnityEngine;

namespace PawnChronicles
{
    public class CompProperties_PersonalChronicles : CompProperties
    {
        public CompProperties_PersonalChronicles() => compClass = typeof(CompPersonalChronicles);
    }

    public class CompPersonalChronicles : ThingComp
    {
        // ─────────────────────────────────────────────────────────────────────
        // SAVE VERSIONING
        // ─────────────────────────────────────────────────────────────────────
        private const int CurrentSaveVersion = 4;
        private int _saveVersion = 0;

        // One-time migration log - static so it fires once per game session,
        // not once per pawn (which produced 98× spam on first load after upgrade).
        private static bool s_migrationLogged = false;

        // ─────────────────────────────────────────────────────────────────────
        // EVENTS  (not serialised - re-subscribe after load if needed)
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Fired when a stage entry is fully resolved.
        /// arg1 = the resolved ArcStageEntry  arg2 = success (true) or failure (false)
        /// </summary>
        public event Action<ArcStageEntry, bool>? OnStageResolved;

        // ─────────────────────────────────────────────────────────────────────
        // PROFILE CACHING
        // ─────────────────────────────────────────────────────────────────────
        private PawnNarrativeProfile? cachedProfile;
        private int lastProfileBuildTick = -999999;

        public PawnNarrativeProfile GetOrBuildProfile()
        {
            if (parent is not Pawn pawn)
                return PawnNarrativeProfile.BuildFor(null!);

            if (cachedProfile != null &&
                Find.TickManager.TicksGame - lastProfileBuildTick < 60000)
            {
                return cachedProfile;
            }

            cachedProfile = PawnNarrativeProfile.BuildFor(pawn);
            lastProfileBuildTick = Find.TickManager.TicksGame;
            return cachedProfile;
        }

        /// <summary>
        /// Force the profile to be rebuilt on the next access.
        /// Call this whenever a significant pawn event changes who they are:
        /// hediff added/removed, relation changed, skill levelled up.
        /// </summary>
        public void InvalidateProfile()
        {
            cachedProfile        = null;
            lastProfileBuildTick = -999999;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ARC STATE
        // ─────────────────────────────────────────────────────────────────────
        public bool hasActiveEpic = false;
        public PersonalEpicDef? currentEpic;
        public int currentStage = 0;
        public List<QuestStageDef> usedStages = new();
        public PawnNarrativeProfile? currentProfile;

        public List<ArcStageEntry> arcEntries = new();

        private int ticksSinceLastProgress = 0;
        private const int MaxIgnoredTicks = 1800000;
        private const int TickCheckInterval = 250;

        /// <summary>
        /// Game tick when this pawn first became an active colonist.
        /// Arcs are suppressed for SpawnGraceTicks after this point so new
        /// pawns (fresh start, refugees, recruits) aren't immediately hit.
        /// -1 = not yet set.
        /// </summary>
        private int _firstSpawnTick = -1;
        private const int SpawnGraceTicks = 5000; // 2 in-game hours

        // ── Performance telemetry (static — aggregate across all comps) ────────
        public static long LastTickUs  = 0;   // microseconds, last sampled tick (per pawn)
        public static long PeakTickUs  = 0;

        public string? PendingSignal = null;
        public int activeQuestId = -1;

        private QuestStageDef? _pendingRetryStage = null;
        private bool _pendingRetryIsOpening = false;
        private bool _pendingRetryIsClimax = false;
        private bool _pendingRetryClimaxResolved = false; // true = retry was triggered post-skill-check

        private List<string> completedEpicDefNames = new();

        /// <summary>
        /// The tagDefName picked at the opening (seed) stage.
        /// Determines which tradeoff pool and climax incident type are used for the rest of the arc.
        /// </summary>
        public string chosenPathTag = "";

        /// <summary>
        /// When true, all chronicle tracking is suspended for this pawn.
        /// Set via the Chronicles ITab toggle. Persisted across saves.
        /// </summary>
        public bool chroniclesDisabled = false;

        // ─────────────────────────────────────────────────────────────────────
        // WAIT CONDITION COUNTERS  (v4+)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Running total of social interactions this pawn has initiated or received.
        /// Incremented by Harmony patch on Pawn_InteractionsTracker.TryInteractWith.
        /// Used by the "social" wait condition.
        /// </summary>
        public int socialInteractionCount = 0;

        /// <summary>
        /// Running total of rituals this pawn has participated in.
        /// Incremented by Harmony patch on LordJob_Ritual.Cleanup.
        /// Used by the "ritual"/"prayer" wait conditions.
        /// </summary>
        public int ritualParticipationCount = 0;

        // ─────────────────────────────────────────────────────────────────────
        // NARRATIVE EPITHET  (v3+)
        // ─────────────────────────────────────────────────────────────────────
        private string _narrativeEpithet = null;
        private string _narrativeEpithetDesc = null;
        private bool   _narrativeEpithetSuccess = false;

        public string NarrativeEpithet       => _narrativeEpithet;
        public string NarrativeEpithetDesc   => _narrativeEpithetDesc;
        public bool   NarrativeEpithetSuccess => _narrativeEpithetSuccess;

        // Hellfire Chain
        private HellfireChainState? _hellfireChain;
        private int _hellfireLinkCooldownTicks;

        public HellfireChainState? HellfireChain
        {
            get => _hellfireChain;
            set => _hellfireChain = value;
        }

        public int HellfireLinkCooldownTicks
        {
            get => _hellfireLinkCooldownTicks;
            set => _hellfireLinkCooldownTicks = value;
        }

        // Legacy (unused for now)
        private int ticksSinceLastEmber = 0;
        private int ticksSinceLastSpark = 0;

        // ─────────────────────────────────────────────────────────────────────
        // PER-TAG COOLDOWNS
        // Prevents the same narrative tag from flooding the chronicle.
        // Key = NarrativeTagDef.defName, Value = tick at which the cooldown expires.
        // ─────────────────────────────────────────────────────────────────────
        private Dictionary<string, int> _tagCooldowns = new Dictionary<string, int>();
        private const int TagCooldownTicks = 120000; // 2 in-game days per tag

        // Accessors
        public ArcStageEntry? CurrentEntry => arcEntries.LastOrDefault(e => !e.IsResolved);
        public bool CanAdvance => CurrentEntry is { conditionMet: true, playerAdvanced: false };
        public EpicModus? ActiveModus => currentEpic?.modus;

        public IReadOnlyList<string> ChronicleLog => _chronicleEntries.AsReadOnly();
        private List<string> _chronicleEntries = new();

        // ── Entangled Arc ──────────────────────────────────────────────────────
        /// <summary>Returns this pawn's active entangled arc, or null.</summary>
        public EntangledArcState? GetEntangledArc()
        {
            if (parent is not Pawn p) return null;
            return EntangledArcManager.Instance?.GetActiveArcForPawn(p);
        }

        public bool IsInEntangledArc
        {
            get
            {
                if (parent is not Pawn p) return false;
                return EntangledArcManager.Instance?.IsInEntangledArc(p) == true;
            }
        }

        private int lastEventTick = -999999;

        // ─────────────────────────────────────────────────────────────────────
        // SAVE / LOAD
        // ─────────────────────────────────────────────────────────────────────
        public override void PostExposeData()
        {
            base.PostExposeData();

            // Save version - always first so migration can check it
            Scribe_Values.Look(ref _saveVersion, "saveVersion", 0);

            Scribe_Values.Look(ref hasActiveEpic, "hasActiveEpic", false);
            Scribe_Defs.Look(ref currentEpic, "currentEpic");
            Scribe_Values.Look(ref currentStage, "currentStage", 0);
            Scribe_Values.Look(ref ticksSinceLastProgress, "ticksSinceLastProgress", 0);
            Scribe_Values.Look(ref PendingSignal, "pendingSignal");
            Scribe_Values.Look(ref activeQuestId, "activeQuestId", -1);

            Scribe_Defs.Look(ref _pendingRetryStage, "pendingRetryStage");
            Scribe_Values.Look(ref _pendingRetryIsOpening, "pendingRetryIsOpening", false);
            Scribe_Values.Look(ref _pendingRetryIsClimax, "pendingRetryIsClimax", false);
            Scribe_Values.Look(ref _pendingRetryClimaxResolved, "pendingRetryClimaxResolved", false);
            Scribe_Values.Look(ref chosenPathTag,       "chosenPathTag",       "");
            Scribe_Values.Look(ref chroniclesDisabled,  "chroniclesDisabled",  false);

            Scribe_Collections.Look(ref completedEpicDefNames, "completedEpicDefNames", LookMode.Value);
            Scribe_Collections.Look(ref usedStages, "usedStages", LookMode.Def);
            // Deep-save calls are wrapped in try-catch because world pawns and dead pawns
            // may not be fully serialised, which can corrupt the collection on load.
            try { Scribe_Collections.Look(ref arcEntries,       "arcEntries",       LookMode.Deep);  }
            catch { arcEntries = null; }
            try { Scribe_Collections.Look(ref _chronicleEntries, "chronicleEntries", LookMode.Value); }
            catch { _chronicleEntries = null; }

            try { Scribe_Deep.Look(ref _hellfireChain, "hellfireChain"); }
            catch { _hellfireChain = null; }
            Scribe_Values.Look(ref _hellfireLinkCooldownTicks, "hellfireLinkCooldownTicks", 0);

            // Per-tag cooldowns (version 2+)
            if (_saveVersion >= 2)
                Scribe_Collections.Look(ref _tagCooldowns, "tagCooldowns",
                    LookMode.Value, LookMode.Value);

            // Narrative epithet (version 3+)
            if (_saveVersion >= 3)
            {
                Scribe_Values.Look(ref _narrativeEpithet, "narrativeEpithet", null);
                Scribe_Values.Look(ref _narrativeEpithetDesc, "narrativeEpithetDesc", null);
                Scribe_Values.Look(ref _narrativeEpithetSuccess, "narrativeEpithetSuccess", false);
            }

            // Wait condition counters (version 4+)
            if (_saveVersion >= 4)
            {
                Scribe_Values.Look(ref socialInteractionCount,  "socialInteractionCount",  0);
                Scribe_Values.Look(ref ritualParticipationCount, "ritualParticipationCount", 0);
                Scribe_Values.Look(ref _firstSpawnTick, "firstSpawnTick", -1);
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                completedEpicDefNames ??= new List<string>();
                usedStages            ??= new List<QuestStageDef>();
                arcEntries            ??= new List<ArcStageEntry>();
                _chronicleEntries     ??= new List<string>();
                _tagCooldowns         ??= new Dictionary<string, int>();

                // ── Migration ──────────────────────────────────────────────
                // v0 -> v2: tag cooldowns didn't exist; just initialise empty.
                // v2 -> v3: narrative epithet fields added; no data to migrate.
                // v3 -> v4: social and ritual counters added; start at zero.
                if (_saveVersion < CurrentSaveVersion)
                {
                    if (!s_migrationLogged)
                    {
                        s_migrationLogged = true;
                        Log.Message($"[PawnChronicles] Save data upgraded to v{CurrentSaveVersion}. (This message fires once per session.)");
                    }
                    _tagCooldowns = new Dictionary<string, int>();
                    _saveVersion  = CurrentSaveVersion;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // TICKING
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>True while the spawn grace period is still active.</summary>
        private bool InSpawnGrace
        {
            get
            {
                if (_firstSpawnTick < 0) return true; // not initialised yet
                return Find.TickManager.TicksGame - _firstSpawnTick < SpawnGraceTicks;
            }
        }

        public override void CompTick()
        {
            if (parent is not Pawn pawn || !pawn.IsFreeColonist || !pawn.Spawned || pawn.Dead)
                return;
            if (chroniclesDisabled)
                return;

            // Record the first tick this pawn was an active colonist
            if (_firstSpawnTick < 0)
                _firstSpawnTick = Find.TickManager.TicksGame;

            // Sample tick cost every TickCheckInterval to avoid Stopwatch overhead every tick
            bool sample = (Find.TickManager.TicksGame % TickCheckInterval == 0);
            System.Diagnostics.Stopwatch? _sw = sample ? System.Diagnostics.Stopwatch.StartNew() : null;

            TickArc(pawn);
            TickWaitCondition(pawn);

            ticksSinceLastProgress++;
            if (ticksSinceLastProgress < 60000)
            {
                if (_sw != null)
                {
                    _sw.Stop();
                    long us = _sw.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;
                    LastTickUs = us;
                    if (us > PeakTickUs) PeakTickUs = us;
                }
                return;
            }

            ticksSinceLastProgress = 0;

            // Sparks/embers fire from ANY job - driven by settings, not hardcoded.
            if (pawn.CurJob != null)
            {
                var profile = GetOrBuildProfile();
                if (Rand.Chance(PawnChroniclesMod.Settings.eventDailyChance))
                    TryTriggerFromJob(pawn.CurJob, profile);
            }

            if (_sw != null)
            {
                _sw.Stop();
                long us = _sw.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;
                LastTickUs = us;
                if (us > PeakTickUs) PeakTickUs = us;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SPARK / EMBER TRIGGER
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Called from CompTick - job is the pawn's current live job, workType extracted here.
        /// </summary>
        public void TryTriggerFromJob(Job job, PawnNarrativeProfile profile)
        {
            if (chroniclesDisabled) return;
            string workType = job?.workGiverDef?.workType?.defName ?? "";
            TryTriggerInternal(profile, workType, job);
        }

        /// <summary>
        /// Called from the Harmony EndCurrentJob postfix - job has already ended so we pass the
        /// workType that was captured in the Prefix while the job was still live.
        /// </summary>
        public void TryTriggerFromCompletedJob(string workType, PawnNarrativeProfile profile)
        {
            if (chroniclesDisabled) return;
            TryTriggerInternal(profile, workType, null);
        }

        private void TryTriggerInternal(PawnNarrativeProfile profile, string workType, Job job)
        {
            var s = PawnChroniclesMod.Settings;

            if (Find.TickManager.TicksGame - lastEventTick < (int)(s.eventCooldownDays * 60000f))
                return;

            // Per-tag cooldown: skip if the dominant tag fired recently
            string dominantTagKey = profile.DominantTag() ?? "";
            if (!string.IsNullOrEmpty(dominantTagKey) &&
                _tagCooldowns.TryGetValue(dominantTagKey, out int cooldownExpiry) &&
                Find.TickManager.TicksGame < cooldownExpiry)
                return;

            float chance = CalculateJobChance(profile, job);

            if (Rand.Chance(chance))
            {
                lastEventTick = Find.TickManager.TicksGame;

                // Mark this tag as cooling down
                if (!string.IsNullOrEmpty(dominantTagKey))
                    _tagCooldowns[dominantTagKey] = Find.TickManager.TicksGame + TagCooldownTicks;

                if (Rand.Chance(s.sparkRatio))
                    FireSparkFallback(profile);
                else
                    FireEmberFallback(profile, workType);
            }
        }

        private float CalculateJobChance(PawnNarrativeProfile profile, Job job)
        {
            float score = 0.25f;
            if (job?.def.joyKind != null) score += 0.15f;
            return Mathf.Clamp01(score);
        }

        private void FireSparkFallback(PawnNarrativeProfile profile)
        {
            if (parent is not Pawn pawn) return;
            if (!PawnChroniclesMod.Settings.sparksEnabled) return;

            // Grammar cascade: {tag}_spark_body -> spark_body -> default_body
            string text = NarrativeGrammarResolver.ResolveBody(pawn, profile, "spark");

            Messages.Message(text, pawn, MessageTypeDefOf.SilentInput, true);
            AddChronicleEntry($"Spark: {text}");

            // Small mood benefit (+3, 8 hours) - stage chosen by preferred/dominant tag
            int stage = TagToStageIndex(GetPreferredTagName(profile));
            ApplyMomentThought(pawn, "PC_Thought_SparkMoment", stage);
        }

        private void FireEmberFallback(PawnNarrativeProfile profile, string workType)
        {
            if (parent is not Pawn pawn) return;
            if (!PawnChroniclesMod.Settings.embersEnabled) return;

            // Grammar cascade: {tag}_ember_title / {tag}_ember_body -> ember_* -> default_*
            string title = NarrativeGrammarResolver.ResolveTitle(pawn, profile, "ember");
            string body  = NarrativeGrammarResolver.ResolveBody(pawn, profile, "ember");

            Messages.Message($"{pawn.LabelShort} - {title}", pawn,
                MessageTypeDefOf.NeutralEvent, true);
            // Store title + body so the diary detail pane shows the full moment.
            AddChronicleEntry($"Ember: {title}\n\n{body}");

            // Moderate mood benefit (+6, 1.5 days) - stage chosen by preferred/dominant tag
            int stage = TagToStageIndex(GetPreferredTagName(profile));
            ApplyMomentThought(pawn, "PC_Thought_EmberMoment", stage);
            // Stacking momentum buff - up to 3 deep, reflects the pawn's running streak
            ApplyMomentThought(pawn, "PC_EmberCompleted", stage);
            ApplyEmberSkillBoost(pawn, profile);

            // Physical ripple - job-matched item, wound, thought, or skill XP
            // workType drives pool selection so the ripple fits what the pawn was doing.
            EmberConsequenceFirer.TryFire(pawn, workType);
        }

        private static void ApplyMomentThought(Pawn pawn, string thoughtDefName, int stageIndex = 5)
        {
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtDefName);
            if (def == null) return;
            int clamped = Mathf.Clamp(stageIndex, 0, def.stages.Count - 1);
            var thought = ThoughtMaker.MakeThought(def, clamped);
            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(thought);
        }

        /// <summary>
        /// Returns the narrative tag defName most relevant to the pawn right now.
        /// Prefers the last player-chosen arc path tag, falls back to dominant profile tag.
        /// </summary>
        private string? GetPreferredTagName(PawnNarrativeProfile profile)
        {
            // Walk arc entries newest-first, find the last choice the player made
            for (int i = arcEntries.Count - 1; i >= 0; i--)
            {
                var entry = arcEntries[i];
                if (entry.chosenIndex >= 0 && entry.chosenIndex < entry.choices.Count)
                {
                    string tagName = entry.choices[entry.chosenIndex].tagDefName;
                    if (!string.IsNullOrEmpty(tagName)) return tagName;
                }
            }
            // No player choice on record - use dominant profile tag
            var dominant = profile.GetDominantTags(1);
            return dominant.Count > 0 ? dominant[0].defName : null;
        }

        /// <summary>
        /// Maps a NarrativeTagDef defName to a thought stage index (0–5).
        /// Matches the stage ordering in PC_Thought_SparkMoment / PC_Thought_EmberMoment.
        /// </summary>
        private static int TagToStageIndex(string? tagDefName)
        {
            if (string.IsNullOrEmpty(tagDefName)) return 5;
            string t = tagDefName.ToLowerInvariant().Replace("pc_tag_", "");
            return t switch
            {
                "violence" or "predator" or "primal"            => 0,
                "social"   or "nurture"  or "healer"
                           or "caretaker"                       => 1,
                "craft"    or "artisan"  or "builder"           => 2,
                "curiosity" or "scholar" or "intellectual"
                            or "inventor"                       => 3,
                "animalfriend" or "wilderness" or "wanderer"
                               or "survivor"                    => 4,
                _                                               => 5
            };
        }

        private static void ApplyEmberSkillBoost(Pawn pawn, PawnNarrativeProfile profile)
        {
            if (pawn.skills == null) return;
            var dominant = profile.GetDominantTags(1);
            if (dominant.Count == 0) return;

            string tagName = dominant[0].defName.ToLowerInvariant().Replace("pc_tag_", "");
            SkillDef target = tagName switch
            {
                "craft"        => SkillDefOf.Crafting,
                "animalfriend" => SkillDefOf.Animals,
                "nurture"      => SkillDefOf.Medicine,
                "violence"     => SkillDefOf.Melee,
                "curiosity"    => SkillDefOf.Intellectual,
                "scholar"      => SkillDefOf.Intellectual,
                "healer"       => SkillDefOf.Medicine,
                _              => null
            };
            if (target == null) return;

            var skill = pawn.skills.GetSkill(target);
            if (skill != null && !skill.TotallyDisabled)
                skill.Learn(300f, direct: true);
        }

        // Legacy overloads
        public void FireEmber(Pawn pawn) => FireEmberFallback(currentProfile ?? GetOrBuildProfile(), "");
        public void FireSpark(Pawn pawn, string triggerKey) => FireSparkFallback(currentProfile ?? GetOrBuildProfile());

        // ── Debug entry points (called by PawnChroniclesDebugActions) ──────────
        public void Debug_ForceSpark()     => FireSparkFallback(GetOrBuildProfile());
        public void Debug_ForceEmber()     => FireEmberFallback(GetOrBuildProfile(), "");
        public void Debug_ForceStartEpic() => EvaluateAndStartEpic();
        public void Debug_InvalidateProfile()
        {
            InvalidateProfile();
            Messages.Message($"[PawnChronicles] Profile invalidated for {((Pawn)parent).LabelShort}.",
                MessageTypeDefOf.SilentInput);
        }

        // ─────────────────────────────────────────────────────────────────────
        // ARC SYSTEM
        // ─────────────────────────────────────────────────────────────────────
        private void TickArc(Pawn pawn)
        {
            if (_pendingRetryStage != null)
            {
                var stage = _pendingRetryStage;
                bool opening = _pendingRetryIsOpening;
                bool climax = _pendingRetryIsClimax;
                bool climaxResolved = _pendingRetryClimaxResolved;

                _pendingRetryStage = null;
                _pendingRetryIsOpening = false;
                _pendingRetryIsClimax = false;
                _pendingRetryClimaxResolved = false;

                TryGenerateConsequenceQuest(stage, pawn, opening, climax, climaxResolved);
                return;
            }

            // Defensive: arc marked active but epic def is missing (e.g. def renamed between sessions)
            if (hasActiveEpic && currentEpic == null)
            {
                Log.Warning($"[PawnChronicles] {pawn.LabelShort} has hasActiveEpic=true but currentEpic is null - resetting arc.");
                hasActiveEpic = false;
                arcEntries.Clear();
                usedStages.Clear();
                currentStage = 0;
                activeQuestId = -1;
            }

            if (PendingSignal != null)
            {
                string signal = PendingSignal;
                PendingSignal = null;
                CompleteEpic(signal == "PawnEpic_Success");
                return;
            }

            if (hasActiveEpic && currentEpic != null)
            {
                ticksSinceLastProgress += TickCheckInterval;
                if (ticksSinceLastProgress > MaxIgnoredTicks)
                    CompleteEpic(false);
            }

            if (HellfireChain != null && !hasActiveEpic && HellfireLinkCooldownTicks > 0)
            {
                HellfireLinkCooldownTicks -= TickCheckInterval;
                if (HellfireLinkCooldownTicks <= 0)
                {
                    HellfireLinkCooldownTicks = 0;
                    var nextEpic = HellfireEvaluator.SelectNextLinkEpic(pawn, this, HellfireChain);
                    if (nextEpic != null)
                        StartEpic(nextEpic);
                    else
                        HellfireEvaluator.OnLinkCompleted(pawn, this, HellfireChain, false);
                }
            }
        }

        private void TickWaitCondition(Pawn pawn)
        {
            if (!hasActiveEpic) return;
            var entry = CurrentEntry;
            if (entry == null || entry.conditionMet) return;

            // Waiting for player to pick a direction
            if (entry.AwaitingChoice)
            {
                if (PawnChroniclesMod.Settings.autopilotEnabled)
                    TickAutopilotChoice(pawn, entry);
                return;
            }

            if (StageWaitCondition.CheckConditionMet(pawn, entry))
            {
                entry.conditionMet = true;

                if (pawn.Spawned)
                    Messages.Message(
                        $"{pawn.LabelShort}'s arc is ready - open Chronicles to continue.",
                        pawn, MessageTypeDefOf.PositiveEvent, true);
                return;
            }

            // Climax skill-check deadline - auto-fail when the 30-day window expires
            if (entry.isClimax && entry.isSkillCheck
                && entry.climaxDeadlineTick > 0
                && Find.TickManager.TicksGame > entry.climaxDeadlineTick)
            {
                if (pawn.Spawned)
                    Messages.Message(
                        $"{pawn.LabelShort}'s window has closed - the arc ends in failure.",
                        pawn, MessageTypeDefOf.NegativeEvent, true);
                entry.playerAdvanced = true;
                CompleteEpic(false);
            }
        }

        /// <summary>
        /// Called from the UI when the player picks a stage choice.
        ///
        /// Seed stage:       records chosenPathTag, starts 2-day condition.
        /// Middle stage:     applies ChoiceEffects immediately, starts condition.
        // ─────────────────────────────────────────────────────────────────────
        // AUTOPILOT
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called from TickWaitCondition when autopilot is enabled and a choice is pending.
        /// Picks a choice index based on the current autopilot mode and calls PlayerMakeChoice.
        /// </summary>
        private void TickAutopilotChoice(Pawn pawn, ArcStageEntry entry)
        {
            if (entry.choices == null || entry.choices.Count == 0) return;

            int idx;
            if (PawnChroniclesMod.Settings.autopilotMode == AutopilotMode.StatBased)
            {
                var profile = GetOrBuildProfile();
                idx = BestChoiceByProfile(entry.choices, profile);
            }
            else
            {
                idx = Rand.Range(0, entry.choices.Count);
            }

            PlayerMakeChoice(idx);
        }

        /// <summary>
        /// Returns the index of the choice whose tagDefName best matches the pawn's profile.
        /// Falls back to index 0 if no tags match.
        /// </summary>
        private static int BestChoiceByProfile(List<StageChoice> choices, PawnNarrativeProfile profile)
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
        // PLAYER CHOICES
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Climax Easy Out:  applies corrupted backstory right now, arc closes.
        /// Climax Hard Road: starts 2-day condition; incident fires on Next click.
        /// </summary>
        public void PlayerMakeChoice(int choiceIndex)
        {
            var entry = CurrentEntry;
            if (entry == null || !entry.AwaitingChoice) return;
            if (choiceIndex < 0 || choiceIndex >= entry.choices.Count) return;

            var choice = entry.choices[choiceIndex];
            entry.chosenIndex = choiceIndex;

            var pawn = (Pawn)parent;

            // Apply immediate stat effects (legacy skill-only path)
            if (choice.effects != null)
                foreach (var fx in choice.effects)
                    fx.Apply(pawn);

            // Apply pool-drawn effects (positive + negative pair)
            choice.PositiveEntry?.Apply(pawn);
            choice.NegativeEntry?.Apply(pawn);

            // Record the chosen path tag from the seed stage
            if (string.IsNullOrEmpty(chosenPathTag) && !string.IsNullOrEmpty(choice.tagDefName))
                chosenPathTag = choice.tagDefName;

            // Easy Out: arc closes immediately with the corrupted backstory - no waiting
            if (choice.isEasyOut)
            {
                entry.conditionMet   = true;
                entry.playerAdvanced = true;
                CompleteEpic(false);
                return;
            }

            // Activate the chosen wait condition (Hard Road, seed, or tradeoff)
            entry.waitConditionKey   = choice.conditionKey;
            entry.waitConditionLabel = choice.conditionLabel;
            entry.waitBaselineValue  = choice.baseline;
            entry.waitTargetDelta    = choice.targetDelta;
        }

        /// <summary>
        /// Called from the UI "Abandon Arc" button on a skill-check climax.
        /// The player gives up - arc closes as a failure and the corrupted backstory is applied.
        /// </summary>
        public void PlayerAbandonClimax()
        {
            var entry = CurrentEntry;
            if (entry == null || !entry.isClimax || !entry.isSkillCheck) return;

            entry.conditionMet   = false;  // skill was NOT met - drives success=false
            entry.playerAdvanced = true;
            ticksSinceLastProgress = 0;

            try { OnStageResolved?.Invoke(entry, false); }
            catch (Exception ex) { Log.Error($"[PawnChronicles] OnStageResolved (abandon) threw: {ex}"); }

            CompleteEpic(false);
        }

        /// <summary>
        /// Fires the thematically appropriate narrative incident for the climax Hard Road.
        /// Derives incident type from the pawn's chosen path tag.
        /// </summary>
        private void FireClimaxIncident(Pawn pawn)
        {
            if (currentEpic == null) return;

            string tag = (chosenPathTag ?? "").ToLowerInvariant().Replace("pc_tag_", "");
            string incidentTypeKey = StageWaitCondition.GetClimaxIncidentType(tag);

            // Scale incident points with arc intensity.
            // Sum the top-2 tag scores (0–200 range) and map to 300–900 points.
            // A Hellfire pawn (two tags at ~80–100) hits 700–900; a Kindle pawn hits 300–400.
            var profile   = currentProfile ?? GetOrBuildProfile();
            float topSum  = profile.Scores.Values
                .OrderByDescending(v => v).Take(2).Sum();           // 0–200
            float points  = Mathf.Lerp(300f, 900f, Mathf.Clamp01(topSum / 160f));

            var incident = new NarrativeIncident
            {
                type = incidentTypeKey switch
                {
                    "SmallRaid"       => NarrativeIncidentType.SmallRaid,
                    "MentalBreak"     => NarrativeIncidentType.MentalBreak,
                    "FriendlyArrives" => NarrativeIncidentType.FriendlyArrives,
                    "HostileArrives"  => NarrativeIncidentType.HostileArrives,
                    _                 => NarrativeIncidentType.SmallRaid
                },
                chance             = 1f,
                incidentPoints     = points,
                bypassGracePeriod  = true   // climax is earned - always fires
            };

            string body = $"{pawn.LabelShort}'s arc reaches its crisis point.";
            NarrativeIncidentFirer.Fire(pawn, incident, body, profile);
            Log.Message($"[PawnChronicles] Climax incident: {incidentTypeKey} {points:F0}pts (topSum={topSum:F0}) for {pawn.LabelShort}");
        }

        /// <summary>
        /// Guaranteed Hard Road cost - a mood debuff that always fires regardless of
        /// whether the narrative incident succeeded. The incident is the drama;
        /// this is the weight. Applied before CompleteEpic so it shows up immediately.
        /// </summary>
        /// <summary>
        /// Extracts a short substance tag from an addiction hediff defName.
        /// E.g. "AlcoholAddiction" -> "alcohol", "PsychiteAddiction" -> "psychite".
        /// Falls back to "general" if nothing matches.
        /// </summary>
        private static string DeriveSubstanceTag(string hediffDefName)
        {
            if (string.IsNullOrEmpty(hediffDefName)) return "general";
            string lower = hediffDefName.ToLowerInvariant();
            // Strip common suffixes/prefixes to get the substance name
            foreach (string suffix in new[] { "addiction", "addicted", "tolerance" })
                lower = lower.Replace(suffix, "").Trim('_').Trim();
            return string.IsNullOrEmpty(lower) ? "general" : lower;
        }

        private static void ApplyHardRoadCost(Pawn pawn)
        {
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail("PC_Thought_HardRoadCost");
            if (def == null)
            {
                Log.Warning("[PawnChronicles] PC_Thought_HardRoadCost not found - Hard Road cost skipped.");
                return;
            }
            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(def);
        }

        public void PlayerAdvanceStage(bool devMode = false)
        {
            var entry = CurrentEntry;
            if (entry == null) return;
            if (!devMode && !entry.conditionMet) return;

            // Capture whether the skill threshold was actually met before we force conditionMet=true.
            bool skillCheckPassed = entry.conditionMet;
            entry.conditionMet   = true;
            entry.playerAdvanced = true;
            ticksSinceLastProgress = 0;

            // Determine success:
            // - Skill-check climax: threshold was met (or dev override)
            // - Two-door climax: Hard Road was chosen
            // - Regular stages: role is "success"
            bool success;
            if (entry.isClimax && entry.isSkillCheck)
                success = devMode || skillCheckPassed;
            else if (entry.isClimax && entry.choices != null && entry.choices.Count > 0)
            {
                var chosen = (entry.chosenIndex >= 0 && entry.chosenIndex < entry.choices.Count)
                    ? entry.choices[entry.chosenIndex] : null;
                success = devMode || (chosen?.isHardRoad == true);
            }
            else
                success = entry.stageRole == NarrativeGrammarResolver.RoleSuccess;

            // Fire the event so other systems (entangled arc, future hooks) can react
            try { OnStageResolved?.Invoke(entry, success); }
            catch (Exception ex) { Log.Error($"[PawnChronicles] OnStageResolved handler threw: {ex}"); }

            // Carry the chosen tag forward to bias the next stage selection
            string? preferredTag = (entry.chosenIndex >= 0 && entry.choices != null && entry.choices.Count > entry.chosenIndex)
                ? entry.choices[entry.chosenIndex].tagDefName
                : null;

            if (entry.isClimax)
            {
                if (entry.isSkillCheck)
                {
                    // Old skill-check path: quest fires after skill check passes
                    if (success)
                    {
                        var climaxStage = usedStages.LastOrDefault();
                        if (climaxStage != null)
                            TryGenerateConsequenceQuest(climaxStage, (Pawn)parent,
                                isOpening: false, isClimax: true, climaxResolved: true);
                    }
                }
                else if (success && entry.choices != null && entry.choices.Count > 0)
                {
                    // Two-door path: Hard Road fires the narrative climax incident
                    ApplyHardRoadCost((Pawn)parent);
                    FireClimaxIncident((Pawn)parent);
                }
                CompleteEpic(success);
            }
            else
                ProgressEpic(preferredTag);
        }

        public void EvaluateAndStartEpic()
        {
            if (chroniclesDisabled) return;
            if (InSpawnGrace) return;
            if (parent is not Pawn pawn || !pawn.IsFreeColonist || hasActiveEpic)
                return;

            // Colony cap - 0 = unlimited
            int cap = PawnChroniclesMod.Settings.maxActiveEpicsPerColony;
            if (cap > 0 && pawn.Map != null)
            {
                int active = pawn.Map.mapPawns.FreeColonists
                    .Count(p => p.GetComp<CompPersonalChronicles>()?.hasActiveEpic == true);
                if (active >= cap) return;
            }

            var (_, epic) = PremiseEvaluator.FindBestMatch(pawn);
            if (epic != null)
                StartEpic(epic);
        }

        /// <summary>
        /// Called when a pawn gains an addiction hediff. Finds the matching
        /// addiction arc def and starts it. Falls back to PC_Arc_Addiction_Chemical
        /// for any substance without a dedicated arc.
        /// </summary>
        public void TryStartAddictionArc(HediffDef addictionDef)
        {
            if (addictionDef == null || hasActiveEpic || chroniclesDisabled) return;
            if (InSpawnGrace) return;
            if (parent is not Pawn pawn || !pawn.IsFreeColonist) return;

            var arcDef = DefDatabase<PersonalEpicDef>.AllDefsListForReading
                .FirstOrDefault(d => d.addictionHediffDef == addictionDef.defName)
                ?? DefDatabase<PersonalEpicDef>.GetNamedSilentFail("PC_Arc_Addiction_Chemical");

            if (arcDef == null)
            {
                Log.Warning($"[PawnChronicles] No addiction arc found for {addictionDef.defName} and no chemical fallback.");
                return;
            }

            StartEpic(arcDef);
        }

        public void StartEpic(PersonalEpicDef epic, PawnNarrativeProfile? profile = null)
        {
            if (!epic.IsFixed && epic.stagePool.NullOrEmpty())
            {
                Log.Warning($"[PawnChronicles] Epic '{epic.defName}' has no stage pool.");
                return;
            }
            if (epic.IsFixed && epic.fixedStageSequence.Count == 0)
            {
                Log.Warning($"[PawnChronicles] Epic '{epic.defName}' has empty fixedStageSequence.");
                return;
            }

            currentEpic = epic;
            hasActiveEpic = true;
            currentStage = 0;
            chosenPathTag = "";
            usedStages.Clear();
            arcEntries.Clear();
            ticksSinceLastProgress = 0;
            PendingSignal = null;
            activeQuestId = -1;

            currentProfile = profile ?? PawnNarrativeProfile.BuildFor((Pawn)parent);

            TriggerNextStage(isOpening: true);
        }

        public void ProgressEpic(string? preferredTagDefName = null)
        {
            if (currentEpic == null) return;

            ticksSinceLastProgress = 0;
            currentStage++;

            if (currentEpic.dynamicProfile)
                currentProfile = PawnNarrativeProfile.BuildFor((Pawn)parent);

            bool isFinal = currentStage >= currentEpic.stageCount - 1;

            if (currentStage < currentEpic.stageCount)
                TriggerNextStage(isClimax: isFinal, preferredTagDefName: preferredTagDefName);
            else
                CompleteEpic(true);
        }

        private void TriggerNextStage(bool isOpening = false, bool isClimax = false,
            string? preferredTagDefName = null)
        {
            if (currentEpic == null) return;
            if (currentProfile == null)
                currentProfile = PawnNarrativeProfile.BuildFor((Pawn)parent);

            // Fixed-sequence arcs (e.g. addiction) take their stage directly by index.
            // Standard arcs use the tag-weighted selector.
            QuestStageDef stage;
            if (currentEpic.IsFixed)
            {
                int idx = currentStage;
                if (idx < 0 || idx >= currentEpic.fixedStageSequence.Count)
                {
                    CompleteEpic(true);
                    return;
                }
                stage = currentEpic.fixedStageSequence[idx];
            }
            else
            {
                stage = PremiseEvaluator.SelectNextStage(
                    currentEpic, currentProfile, usedStages, isClimax, isOpening,
                    preferredTagDefName);
            }

            if (stage == null)
            {
                CompleteEpic(true);
                return;
            }

            usedStages.Add(stage);

            var pawn = (Pawn)parent;
            // Use GrammarRole so addiction stages override the role key.
            string role = stage.GrammarRole;

            var snapshot = StageWaitCondition.GetPawnSnapshot(pawn);

            string title = NarrativeGrammarResolver.ResolveTitle(pawn, currentProfile, role, snapshot);
            string body  = NarrativeGrammarResolver.ResolveBody(pawn, currentProfile, role, snapshot);

            // Resolve the active path tag: prefer what the player chose at the seed stage,
            // fall back to the preferred tag passed in, then to the profile's dominant tag.
            string activePathTag = !string.IsNullOrEmpty(chosenPathTag) ? chosenPathTag
                : !string.IsNullOrEmpty(preferredTagDefName) ? preferredTagDefName
                : currentProfile.DominantTag() ?? "";

            ArcStageEntry entry;
            if (currentEpic.IsFixed)
            {
                if (isClimax)
                {
                    // Addiction climax: hard road (sobriety) vs easy out (immediate failure).
                    var choices = StageWaitCondition.BuildAddictionClimaxDoors(pawn);
                    entry = new ArcStageEntry(
                        title, body, role,
                        waitConditionLabel: "PC_Wait_ChoosePathForward".Translate(),
                        waitConditionKey:   "",
                        waitBaselineValue:  0,
                        waitTargetDelta:    0,
                        isClimax:           true);
                    entry.isSkillCheck = false;
                    entry.choices      = choices;
                }
                else
                {
                    // Fixed middle/opening stages: pool-drawn choices with stage-appropriate effects.
                    string stageTag = EffectPoolDrawer.AddictionStageTag(role);
                    // Derive substance tag from the arc's addiction hediff name (e.g. "alcohol", "psychite")
                    string substanceTag = DeriveSubstanceTag(currentEpic?.addictionHediffDef ?? "");
                    var poolTags = new[] { "addiction", stageTag, substanceTag };

                    var (condKey, condLabel, condBaseline, condDelta) =
                        StageWaitCondition.BuildForAddiction(pawn, role);

                    var choices = EffectPoolDrawer.DrawChoices(
                        pawn, poolTags, count: 3,
                        waitDays: condKey == "time" ? condDelta / 60000f : 5f);

                    // Override the condition on each choice to match this stage's actual wait
                    foreach (var c in choices)
                    {
                        c.conditionKey   = condKey;
                        c.conditionLabel = condLabel;
                        c.baseline       = condBaseline;
                        c.targetDelta    = condDelta;
                    }

                    entry = new ArcStageEntry(
                        title, body, role,
                        waitConditionLabel: "PC_Wait_ChooseProceed".Translate(),
                        waitConditionKey:   "",
                        waitBaselineValue:  0,
                        waitTargetDelta:    0,
                        isClimax:           false);
                    entry.choices = choices;
                }
            }
            else if (isOpening)
            {
                // Seed stage: 3 tag-path choices, no immediate effects, picks the arc direction.
                var choices = StageWaitCondition.BuildSeedChoices(pawn, currentProfile);
                entry = new ArcStageEntry(
                    title, body, role,
                    waitConditionLabel: "PC_Wait_ChoosePathBegin".Translate(),
                    waitConditionKey:   "",
                    waitBaselineValue:  0,
                    waitTargetDelta:    0,
                    isClimax:           false);
                entry.choices = choices;
            }
            else if (isClimax)
            {
                // Climax: two doors - Hard Road (redeemed backstory) vs Easy Out (corrupted backstory).
                var choices = StageWaitCondition.BuildClimaxDoors(pawn, currentEpic!, activePathTag);
                entry = new ArcStageEntry(
                    title, body, role,
                    waitConditionLabel: "PC_Wait_ChoosePathForward".Translate(),
                    waitConditionKey:   "",
                    waitBaselineValue:  0,
                    waitTargetDelta:    0,
                    isClimax:           true);
                entry.isSkillCheck = false;
                entry.choices      = choices;
            }
            else
            {
                // Middle stages: pool-drawn tradeoff choices (positive + negative paired).
                // No walk-away - the player committed when they chose a path.
                string midTag = activePathTag.ToLowerInvariant().Replace("pc_tag_", "");
                var choices = EffectPoolDrawer.DrawChoices(
                    pawn,
                    tags: new[] { midTag, "general" },
                    count: 3,
                    waitDays: PawnChroniclesMod.Settings.middleWaitDays);
                entry = new ArcStageEntry(
                    title, body, role,
                    waitConditionLabel: "PC_Wait_ChooseProceed".Translate(),
                    waitConditionKey:   "",
                    waitBaselineValue:  0,
                    waitTargetDelta:    0,
                    isClimax:           false);
                entry.choices = choices;
            }
            entry.snapshot = snapshot;
            arcEntries.Add(entry);

            AddChronicleEntry($"Arc [{role}]: {title}");

            // ── Narrative incident fires before the vanilla notification ───────────────
            // If a stage has an onStartIncident, the incident firer sends the combined
            // letter (narrative body + bridge explanation + incident fires).
            // If not, we send the standard opening/middle/climax notification.
            bool incidentHandlesLetter = false;
            if (stage.onStartIncident != null && pawn.Spawned)
            {
                NarrativeIncidentFirer.Fire(pawn, stage.onStartIncident, body, currentProfile);
                incidentHandlesLetter = true;
            }

            if (!incidentHandlesLetter && pawn.Spawned)
            {
                if (isOpening)
                {
                    Find.LetterStack.ReceiveLetter(
                        $"{pawn.LabelShort} - {title}", body,
                        LetterDefOf.NeutralEvent, pawn);
                }
                else if (isClimax)
                {
                    // Join only non-empty parts so we never get stacked separator lines
                    // when body or waitConditionLabel failed to resolve.
                    string letterBody = string.Join("\n\n",
                        new[] { title, body, entry.waitConditionLabel }
                            .Where(s => !string.IsNullOrWhiteSpace(s)));
                    Find.LetterStack.ReceiveLetter(
                        $"{pawn.LabelShort}: the arc reaches its end",
                        letterBody,
                        LetterDefOf.NeutralEvent, pawn);
                }
                else
                {
                    Messages.Message(
                        $"{pawn.LabelShort} - {title}",
                        pawn, MessageTypeDefOf.NeutralEvent, true);
                }
            }

            TryGenerateConsequenceQuest(stage, pawn, isOpening, isClimax);
        }

        private void TryGenerateConsequenceQuest(QuestStageDef stage, Pawn pawn,
            bool isOpening, bool isClimax, bool climaxResolved = false)
        {
            if (QuestGen.Working)
            {
                _pendingRetryStage = stage;
                _pendingRetryIsOpening = isOpening;
                _pendingRetryIsClimax = isClimax;
                _pendingRetryClimaxResolved = climaxResolved;
                return;
            }

            // Climax quests are only generated after the skill check passes (climaxResolved=true).
            // Non-climax stages require the WorldConsequence branch tag.
            if (isClimax && !climaxResolved) return;
            if (!isClimax && !stage.HasBranchTag("WorldConsequence")) return;
            if (!PawnChroniclesMod.Settings.worldConsequencesEnabled) return;

            var questScript = stage.QuestScript;
            if (questScript == null) return;

            var slate = new Slate();
            slate.Set("pawn", pawn);
            slate.Set("epicStageRole", stage.StageRole);
            slate.Set("currentQuestStageDef", stage);

            if (currentProfile != null)
            {
                var dominant = currentProfile.GetDominantTags();
                var activeLabels = currentProfile.GetDominantTags(30).Select(t => t.label);
                if (dominant.Count > 0) slate.Set("epicPrimaryTag", dominant[0].label);
                if (dominant.Count > 1) slate.Set("epicSecondaryTag", dominant[1].label);
                slate.Set("epicActiveTags", string.Join(", ", activeLabels));
            }

            Quest? quest = QuestGen.Generate(questScript, slate);
            if (quest != null)
            {
                Find.QuestManager.Add(quest);
                activeQuestId = quest.id;
            }
        }

        public void CompleteEpic(bool success)
        {
            if (currentEpic == null) return;

            var pawn      = (Pawn)parent;
            var epic      = currentEpic;
            var lastStage = usedStages.LastOrDefault();

            // Fire the resolved event for the climax entry
            var climaxEntry = arcEntries.LastOrDefault();
            if (climaxEntry != null)
            {
                try { OnStageResolved?.Invoke(climaxEntry, success); }
                catch (Exception ex) { Log.Error($"[PawnChronicles] OnStageResolved (climax) threw: {ex}"); }
            }

            if (!completedEpicDefNames.Contains(epic.defName))
                completedEpicDefNames.Add(epic.defName);

            currentProfile?.ApplyStageOutcome(success, lastStage);

            // Apply mechanical outcome (mood, items, hediffs, skills)
            if (lastStage != null)
            {
                var outcome = success ? lastStage.successOutcome : lastStage.failureOutcome;
                if (outcome != null)
                    EpicOutcomeApplicator.Apply(pawn, outcome, success);
            }

            // Addiction cure: remove the addiction hediff on successful completion.
            if (success && !string.IsNullOrEmpty(epic.addictionHediffDef))
            {
                var addictionHediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(epic.addictionHediffDef);
                if (addictionHediffDef != null)
                {
                    var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(addictionHediffDef);
                    if (hediff != null)
                    {
                        pawn.health.RemoveHediff(hediff);
                        Messages.Message(
                            "PC_AddictionCured".Translate(pawn.LabelShort),
                            pawn, MessageTypeDefOf.PositiveEvent, true);
                    }
                }
            }

            // Capture BEFORE clearing - both are consumed by StoreNarrativeEpithet
            var outcomeProfile = currentProfile ?? GetOrBuildProfile();
            var outcomePathTag = chosenPathTag;

            hasActiveEpic          = false;
            currentEpic            = null;
            currentStage           = 0;
            chosenPathTag          = "";
            ticksSinceLastProgress = 0;
            currentProfile         = null;
            PendingSignal          = null;
            activeQuestId          = -1;
            _pendingRetryStage     = null;
            usedStages.Clear();

            StoreNarrativeEpithet(pawn, epic, success, outcomeProfile, outcomePathTag);

            if (HellfireChain != null)
            {
                HellfireEvaluator.OnLinkCompleted(pawn, this, HellfireChain, success);
                return;
            }

            EvaluateAndStartEpic();
        }

        public void AddChronicleEntry(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return;

            _chronicleEntries.Add(
                $"[{GenDate.DateFullStringAt(GenTicks.TicksAbs, Find.WorldGrid.LongLatOf(((Pawn)parent).Tile))}] {entry}");

            while (_chronicleEntries.Count > PawnChroniclesMod.Settings.maxChronicleEntriesPerPawn)
                _chronicleEntries.RemoveAt(0);
        }

        private void StoreNarrativeEpithet(Pawn pawn, PersonalEpicDef epic, bool success,
            PawnNarrativeProfile profile, string pathTag = "")
        {
            var (title, body) = GenerateArcOutcomeNarrative(pawn, epic, success, profile, pathTag);
            _narrativeEpithet        = title;
            _narrativeEpithetSuccess = success;
            _narrativeEpithetDesc    = body;
            ApplyBackstorySkillDelta(pawn, success);
            Messages.Message($"{pawn.LabelShort} - {title}", pawn,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent);
            AddChronicleEntry($"Arc complete: {title}");
        }

        /// <summary>
        /// Builds the runtime narrative epithet (title) and body text for arc completion.
        ///
        /// Title cascade:
        ///   1. Tagged epithet matching the chosen path tag
        ///   2. Epic's redeemedEpithet / corruptedEpithet
        ///   3. Pawn's adulthood backstory title (e.g. "Former Coal Miner")
        ///   4. Epic label fallback
        ///
        /// Body:
        ///   One-sentence hook from the adulthood description, then a grammar-resolved
        ///   outcome tail ("But now...") that uses the pawn's tags and profile.
        /// </summary>
        private static (string title, string body) GenerateArcOutcomeNarrative(
            Pawn pawn, PersonalEpicDef epic, bool success, PawnNarrativeProfile profile,
            string pathTag = "")
        {
            // ── Title ─────────────────────────────────────────────────────────────
            string title = null;
            var tagged = epic.taggedEpithets
                ?.Where(e => e.onSuccess == success &&
                             (e.tag == null || e.tag == pathTag) &&
                             e.epithet != null)
                .OrderByDescending(e => e.tag != null ? 1 : 0)
                .FirstOrDefault();

            if (tagged?.epithet != null)
                title = tagged.epithet;
            else if (success)
                title = epic.redeemedEpithet;
            else
                title = epic.corruptedEpithet;

            // Fall back to pawn's adulthood backstory title
            if (string.IsNullOrEmpty(title))
            {
                var adulthood = pawn.story?.Adulthood;
                if (adulthood != null)
                    title = adulthood.TitleCapFor(pawn.gender);
            }

            if (string.IsNullOrEmpty(title))
                title = epic.label ?? "PC_ArcComplete".Translate();

            // ── Body ──────────────────────────────────────────────────────────────
            // Take one sentence from the adulthood description as a narrative hook,
            // then append a grammar-resolved outcome tail.
            string backstoryHook = "";
            var adulthoodDef = pawn.story?.Adulthood;
            if (adulthoodDef != null && !string.IsNullOrEmpty(adulthoodDef.description))
                backstoryHook = TrimToOneSentence(adulthoodDef.description);

            // Use custom grammar role for outcome body if the epic specifies one
            // (e.g. addiction arcs use "addiction_alcohol_success" rather than "success").
            string role = success
                ? (epic.successGrammarRole ?? NarrativeGrammarResolver.RoleSuccess)
                : (epic.failureGrammarRole ?? NarrativeGrammarResolver.RoleFailure);
            string outcomeTail = NarrativeGrammarResolver.ResolveBody(pawn, profile, role);

            string body = string.IsNullOrEmpty(backstoryHook)
                ? outcomeTail
                : $"{backstoryHook}\n\n{outcomeTail}";

            return (title, body);
        }

        /// <summary>
        /// Trims a multi-sentence string to just the first sentence.
        /// Strips basic XML tags before trimming.
        /// </summary>
        private static string TrimToOneSentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Strip common rich-text tags
            text = text.Replace("<b>", "").Replace("</b>", "")
                       .Replace("<i>", "").Replace("</i>", "").Trim();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '.' && c != '!' && c != '?') continue;

                // Accept if at end of string, or next char is whitespace/newline
                if (i == text.Length - 1 || char.IsWhiteSpace(text[i + 1]) || text[i + 1] == '\n')
                    return text.Substring(0, i + 1);
            }

            return text; // no sentence boundary found - return whole string
        }

        /// <summary>
        /// On arc success: grants a small XP boost to each positive skill gain on the
        /// pawn's adulthood backstory (things they were already good at deepen further).
        /// On failure: applies a minor erosion to those same skills.
        /// </summary>
        private static void ApplyBackstorySkillDelta(Pawn pawn, bool success)
        {
            if (pawn.skills == null || pawn.story?.Adulthood == null) return;

            var gains = pawn.story.Adulthood.skillGains;
            if (gains.NullOrEmpty()) return;

            foreach (var gain in gains)
            {
                if (gain.skill == null || gain.amount <= 0) continue;

                var skill = pawn.skills.GetSkill(gain.skill);
                if (skill == null || skill.TotallyDisabled) continue;

                // Success: solidify strengths (+12000 XP per point of skill gain).
                // Failure: erode slightly (−1000 XP per point - noticeable but not crippling).
                float xp = success ? gain.amount * 12000f : gain.amount * -10000f;
                skill.Learn(xp, direct: true);
            }
        }

        public int ActiveEmberCount => 0;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DebugSettings.godMode) yield break;
            if (parent is not Pawn pawn) yield break;

            foreach (var g in PawnChroniclesDebugGizmos.GetGizmos(pawn, this))
                yield return g;
        }

        public IEnumerable<PersonalEpicDef> CompletedEpics =>
            completedEpicDefNames
                .Select(n => DefDatabase<PersonalEpicDef>.GetNamedSilentFail(n))
                .Where(d => d != null)!;

        public bool HasCompletedEpic(PersonalEpicDef epic) =>
            epic != null && completedEpicDefNames.Contains(epic.defName);
    }

    public static class PawnChroniclesExtensions
    {
        public static PawnNarrativeProfile GetNarrativeProfile(this Pawn pawn)
        {
            if (pawn == null)
                return PawnNarrativeProfile.BuildFor(null!);

            return pawn.GetComp<CompPersonalChronicles>()?.GetOrBuildProfile()
                   ?? PawnNarrativeProfile.BuildFor(pawn);
        }
    }
}
