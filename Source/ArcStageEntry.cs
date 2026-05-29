using System;
using System.Collections.Generic;
using Verse;

namespace PawnChronicles
{
    /// <summary>
    /// One resolved stage in a pawn's personal arc.
    /// Saved to disk. Rendered in the Chronicles tab.
    ///
    /// Lifecycle:
    ///   1. Stage fires -> ArcStageEntry created with resolved prose + wait condition
    ///   2. Wait condition ticks in CompPersonalChronicles
    ///   3. When condition met -> conditionMet = true -> "Next" button appears
    ///   4. Player clicks Next (or dev skips) -> playerAdvanced = true -> next stage fires
    /// </summary>
    public class ArcStageEntry : IExposable
    {
        public Dictionary<string, float> snapshot = new Dictionary<string, float>();

        // ── Narrative content ─────────────────────────────────────────────────
        public string title         = "";
        public string body          = "";
        public string stageRole     = "";  // opening / middle / success / failure
        public int    writtenAtTick = 0;

        // ── Wait condition ────────────────────────────────────────────────────
        public string waitConditionLabel = ""; // e.g. "after Nagam's next kill"
        public string waitConditionKey   = ""; // internal key for condition checker
        public int    waitBaselineValue  = 0;  // snapshot of the relevant counter at stage start
        public int    waitTargetDelta    = 1;  // how much the counter must increase

        // ── State ─────────────────────────────────────────────────────────────
        public bool conditionMet     = false;
        public bool playerAdvanced   = false;
        public bool isClimax         = false;
        public bool isSkillCheck     = false;   // true  -> climax is a skill-threshold test
        public int  climaxDeadlineTick = -1;    // tick at which the 30-day window expires
        public string stageDefName   = ""; // QuestStageDef.defName - for outcome lookup on completion

        // ── Resolution context ────────────────────────────────────────────────
        public string mechanicalFailureReason = ""; // populated on arc failure
        public string mechanicalSuccessReason = ""; // populated on arc success

        // ── Player choice ─────────────────────────────────────────────────────
        /// <summary>
        /// Three options generated from the pawn's top narrative tags, plus a walk-away.
        /// Empty on legacy entries (pre-choice system) - those fall back to legacy ticking.
        /// </summary>
        public List<StageChoice> choices     = new List<StageChoice>();

        /// <summary>
        /// Index into choices that the player selected. -1 = no choice made yet.
        /// Once set, the chosen condition is copied into waitCondition* fields.
        /// </summary>
        public int chosenIndex = -1;

        /// <summary>True when choices are present but none has been picked yet.</summary>
        public bool AwaitingChoice => choices.Count > 0 && chosenIndex < 0;

        public ArcStageEntry() { }

        public ArcStageEntry(
            string title,
            string body,
            string stageRole,
            string waitConditionLabel,
            string waitConditionKey,
            int    waitBaselineValue,
            int    waitTargetDelta = 1,
            bool   isClimax = false)
        {
            this.title              = title;
            this.body               = body;
            this.stageRole          = stageRole;
            this.writtenAtTick      = Find.TickManager.TicksGame;
            this.waitConditionLabel = waitConditionLabel;
            this.waitConditionKey   = waitConditionKey;
            this.waitBaselineValue  = waitBaselineValue;
            this.waitTargetDelta    = waitTargetDelta;
            this.isClimax           = isClimax;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref title,              "title",              "");
            Scribe_Values.Look(ref body,               "body",               "");
            Scribe_Values.Look(ref stageRole,          "stageRole",          "");
            Scribe_Values.Look(ref writtenAtTick,      "writtenAtTick",      0);
            Scribe_Values.Look(ref waitConditionLabel, "waitConditionLabel", "");
            Scribe_Values.Look(ref waitConditionKey,   "waitConditionKey",   "");
            Scribe_Values.Look(ref waitBaselineValue,  "waitBaselineValue",  0);
            Scribe_Values.Look(ref waitTargetDelta,    "waitTargetDelta",    1);
            Scribe_Values.Look(ref conditionMet,       "conditionMet",       false);
            Scribe_Values.Look(ref playerAdvanced,     "playerAdvanced",     false);
            Scribe_Values.Look(ref isClimax,           "isClimax",           false);
            Scribe_Values.Look(ref isSkillCheck,       "isSkillCheck",       false);
            Scribe_Values.Look(ref climaxDeadlineTick, "climaxDeadlineTick", -1);
            Scribe_Values.Look(ref stageDefName,            "stageDefName",            "");
            Scribe_Values.Look(ref mechanicalFailureReason, "mechanicalFailureReason", "");
            Scribe_Values.Look(ref mechanicalSuccessReason, "mechanicalSuccessReason", "");
            Scribe_Collections.Look(ref choices, "choices", LookMode.Deep);
            Scribe_Values.Look(ref chosenIndex, "chosenIndex", -1);
            Scribe_Collections.Look(ref snapshot, "snapshot", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                snapshot ??= new Dictionary<string, float>();
                choices  ??= new List<StageChoice>();
            }
        }

        /// <summary>Age of this entry in in-game days.</summary>
        public float AgeDays =>
            (Find.TickManager.TicksGame - writtenAtTick) / 60000f;

        /// <summary>True if this stage is fully resolved (player has advanced past it).</summary>
        /// Skill-check climaxes stay interactive until the player resolves or abandons them.
        /// Choice-based climaxes (isClimax + choices.Count > 0) also stay interactive until picked.
        public bool IsResolved => playerAdvanced || (isClimax && !isSkillCheck && (choices == null || choices.Count == 0));
    }



}
