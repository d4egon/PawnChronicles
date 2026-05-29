using System.Collections.Generic;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Immediate mechanical consequence of picking a choice.
    /// Applies a skill level delta to the pawn right when they click.
    /// Positive delta = skill grows, negative = shrinks.
    /// </summary>
    public class ChoiceEffect : IExposable
    {
        /// <summary>SkillDef.defName - the skill this effect touches.</summary>
        public string skillDefName = "";

        /// <summary>
        /// How many levels to add (positive) or remove (negative).
        /// Clamped to 0-20 after application.
        /// </summary>
        public int levelDelta = 0;

        public ChoiceEffect() { }

        public ChoiceEffect(string skillDefName, int levelDelta)
        {
            this.skillDefName = skillDefName;
            this.levelDelta   = levelDelta;
        }

        /// <summary>Human-readable label shown in the choice button: "+2 Shooting" or "-1 Medicine".</summary>
        public string DisplayLabel
        {
            get
            {
                string sign = levelDelta >= 0 ? "+" : "";
                var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);
                string label = skillDef != null ? skillDef.label : skillDefName;
                return $"{sign}{levelDelta} {label}";
            }
        }

        /// <summary>Apply this effect to the pawn. Clamps result between 0 and 20.</summary>
        public void Apply(Pawn pawn)
        {
            if (pawn?.skills == null) return;
            var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);
            if (skillDef == null)
            {
                Log.Warning($"[PawnChronicles] ChoiceEffect: unknown SkillDef '{skillDefName}'");
                return;
            }
            var skill = pawn.skills.GetSkill(skillDef);
            if (skill == null || skill.TotallyDisabled) return;

            int newLevel = UnityEngine.Mathf.Clamp(skill.Level + levelDelta, 0, 20);
            skill.Level  = newLevel;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref skillDefName, "skillDefName", "");
            Scribe_Values.Look(ref levelDelta,   "levelDelta",   0);
        }
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// One player-selectable direction at a stage transition.
    /// Hard Road: isHardRoad = true -- fires incident, CompleteEpic(true)
    /// Easy Out:  isEasyOut  = true -- corrupted backstory, CompleteEpic(false)
    /// </summary>
    public class StageChoice : IExposable
    {
        public string tagDefName     = "";
        public string actionLabel    = "";
        public string mechanicalHint = "";

        public string conditionKey   = "";
        public string conditionLabel = "";
        public int    baseline       = 0;
        public int    targetDelta    = 1;

        public List<ChoiceEffect> effects = new List<ChoiceEffect>();

        /// <summary>Hard Road: fires narrative incident on advance, CompleteEpic(true).</summary>
        public bool isHardRoad = false;

        /// <summary>Easy Out: corrupted backstory immediately, CompleteEpic(false).</summary>
        public bool isEasyOut = false;

        public StageChoice() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tagDefName,     "tagDefName",     "");
            Scribe_Values.Look(ref actionLabel,    "actionLabel",    "");
            Scribe_Values.Look(ref mechanicalHint, "mechanicalHint", "");
            Scribe_Values.Look(ref conditionKey,   "conditionKey",   "");
            Scribe_Values.Look(ref conditionLabel, "conditionLabel", "");
            Scribe_Values.Look(ref baseline,       "baseline",       0);
            Scribe_Values.Look(ref targetDelta,    "targetDelta",    1);
            Scribe_Collections.Look(ref effects, "effects", LookMode.Deep);
            Scribe_Values.Look(ref isHardRoad,     "isHardRoad",     false);
            Scribe_Values.Look(ref isEasyOut,      "isEasyOut",      false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                effects ??= new List<ChoiceEffect>();
        }
    }
}
