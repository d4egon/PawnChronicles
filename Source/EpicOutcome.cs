using System.Collections.Generic;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Mechanical consequences for an epic climax resolution.
    /// Attached as successOutcome / failureOutcome on QuestStageDef.
    /// Deserialized directly from the Def XML via RimWorld's loader.
    /// </summary>
    public class EpicOutcome
    {
        // ── Mood ─────────────────────────────────────────────────────────────
        /// <summary>ThoughtDef defName to grant the pawn on resolution.</summary>
        public string moodThought;

        // ── Skill XP ─────────────────────────────────────────────────────────
        /// <summary>Grant XP to specific skills by SkillDef defName.</summary>
        public List<EpicSkillGain> skillGains;

        /// <summary>Grant this XP to the pawn's currently highest skill (dynamic).</summary>
        public int bestSkillXP = 0;

        // ── Inspiration ───────────────────────────────────────────────────────
        /// <summary>InspirationDef defName to attempt granting.</summary>
        public string inspirationDef;

        /// <summary>0 to 1 chance of the inspiration firing. 0 = never.</summary>
        public float inspirationChance = 0f;

        // ── Hediffs ───────────────────────────────────────────────────────────
        /// <summary>Hediffs to apply to the pawn on resolution.</summary>
        public List<EpicHediffGain> hediffsApplied;

        /// <summary>HediffDef defNames to remove from the pawn (first match wins).</summary>
        public List<string> hediffsRemoved;

        /// <summary>If true, remove the worst visible bad non-missing-part hediff.</summary>
        public bool removeOneBadHediff = false;

        // ── Items ─────────────────────────────────────────────────────────────
        /// <summary>Things to drop near the pawn on resolution.</summary>
        public List<EpicItemDrop> itemDrops;

        /// <summary>If true, drop a quality item appropriate to the pawn's best skill.</summary>
        public bool dropSkillRewardItem = false;

        /// <summary>Quality of any skill-based reward drop. Default: Good.</summary>
        public QualityCategory skillRewardQuality = QualityCategory.Good;

        // ── Letter override ───────────────────────────────────────────────────
        /// <summary>Override LetterDef defName. Null uses default positive/negative.</summary>
        public string letterDefName;
    }

    public class EpicSkillGain
    {
        public string skill;   // SkillDef defName
        public int xp = 20000;
    }

    public class EpicHediffGain
    {
        public string hediff;        // HediffDef defName
        public float severity = 0.1f;
        public string bodyPart;      // BodyPartDef defName - null = whole body
    }

    public class EpicItemDrop
    {
        public string thingDef;      // ThingDef defName
        public int count = 1;
        public QualityCategory quality = QualityCategory.Normal;
        public string stuffDef;      // ThingDef defName for material - null = default
    }
}
