using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// One discrete effect that can be drawn from the positive or negative pool
    /// when building randomised tradeoff choices.
    ///
    /// Set exactly one effect block per entry. The XML field name is the effect type:
    ///
    ///   <skillLevelDef>Crafting</skillLevelDef>  <skillLevel>2</skillLevel>
    ///   <skillXpDef>Shooting</skillXpDef>        <skillXP>5000</skillXP>
    ///   <anyPassion>1</anyPassion>               <!-- +1 = gain random passion, -1 = lose -->
    ///   <passionSkillDef>Medicine</passionSkillDef> <passion>1</passion>
    ///   <socialOpinion>-15</socialOpinion>
    ///   <mentalBreakDef>Berserk</mentalBreakDef>
    ///   <incidentDef>RaidEnemy</incidentDef>
    ///   <spawnItemDef>Beer</spawnItemDef>        <spawnCount>6</spawnCount>
    ///   <hediffDef>Hangover</hediffDef>          <hediffSeverity>0.5</hediffSeverity>
    ///   <raidPoints>300</raidPoints>
    /// </summary>
    public class EffectEntryDef : Def
    {
        // ── Display ───────────────────────────────────────────────────────────────

        // label is inherited from Def - use that as the choice button text.

        /// <summary>One-line flavour consequence shown in the hint block.</summary>
        [MustTranslate]
        public string consequence = "";

        // ── Pool membership ───────────────────────────────────────────────────────

        /// <summary>true = positive pool, false = negative pool.</summary>
        public bool isPositive = true;

        /// <summary>Tags used to filter which pools an arc can draw from.
        /// E.g. "addiction", "dependency", "general", "crisis".</summary>
        public List<string> tags = new List<string>();

        // ── Effect fields (set at most one block) ─────────────────────────────────

        // Skill level delta
        public string skillLevelDef = "";
        public int skillLevel = 0;

        // Raw skill XP
        public string skillXpDef = "";
        public float skillXP = 0f;

        // Specific-skill passion change (+1 add / -1 remove)
        public string passionSkillDef = "";
        public int passion = 0;

        // Random-skill passion (+1 add a random passion / -1 remove a random passion)
        public int anyPassion = 0;

        // Opinion delta with best friend or random colonist
        public int socialOpinion = 0;

        // Mental break to trigger (MentalBreakDef defName)
        public string mentalBreakDef = "";

        // Incident to fire (IncidentDef defName)
        public string incidentDef = "";

        // Spawn items near the pawn
        public string spawnItemDef = "";
        public int spawnCount = 1;

        // Apply a hediff to the pawn
        public string hediffDef = "";
        public float hediffSeverity = 0.5f;

        // Trigger a small raid sized by these points
        public int raidPoints = 0;

        // ── Application ───────────────────────────────────────────────────────────

        /// <summary>Apply this effect to the pawn.</summary>
        public void Apply(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return;

            TryApplySkillLevel(pawn);
            TryApplySkillXP(pawn);
            TryApplyPassion(pawn);
            TryApplyAnyPassion(pawn);
            TryApplySocialOpinion(pawn);
            TryApplyMentalBreak(pawn);
            TryApplyIncident(pawn);
            TryApplySpawnItem(pawn);
            TryApplyHediff(pawn);
            TryApplyRaid(pawn);
        }

        private void TryApplySkillLevel(Pawn pawn)
        {
            if (skillLevel == 0 || string.IsNullOrEmpty(skillLevelDef)) return;
            var def = DefDatabase<SkillDef>.GetNamedSilentFail(skillLevelDef);
            if (def == null) { Log.Warning($"[PawnChronicles] EffectEntryDef {defName}: unknown SkillDef '{skillLevelDef}'"); return; }
            var skill = pawn.skills?.GetSkill(def);
            if (skill == null || skill.TotallyDisabled) return;
            skill.Level = UnityEngine.Mathf.Clamp(skill.Level + skillLevel, 0, 20);
        }

        private void TryApplySkillXP(Pawn pawn)
        {
            if (skillXP == 0f || string.IsNullOrEmpty(skillXpDef)) return;
            var def = DefDatabase<SkillDef>.GetNamedSilentFail(skillXpDef);
            if (def == null) { Log.Warning($"[PawnChronicles] EffectEntryDef {defName}: unknown SkillDef '{skillXpDef}'"); return; }
            var skill = pawn.skills?.GetSkill(def);
            if (skill == null || skill.TotallyDisabled) return;
            skill.Learn(skillXP, direct: true);
        }

        private void TryApplyPassion(Pawn pawn)
        {
            if (passion == 0 || string.IsNullOrEmpty(passionSkillDef)) return;
            var def = DefDatabase<SkillDef>.GetNamedSilentFail(passionSkillDef);
            if (def == null) { Log.Warning($"[PawnChronicles] EffectEntryDef {defName}: unknown SkillDef '{passionSkillDef}'"); return; }
            var skill = pawn.skills?.GetSkill(def);
            if (skill == null || skill.TotallyDisabled) return;

            if (passion > 0)
            {
                // Step up: None->Minor->Major (cap at Major)
                if (skill.passion == Passion.None) skill.passion = Passion.Minor;
                else if (skill.passion == Passion.Minor) skill.passion = Passion.Major;
            }
            else
            {
                // Step down: Major->Minor->None (floor at None)
                if (skill.passion == Passion.Major) skill.passion = Passion.Minor;
                else if (skill.passion == Passion.Minor) skill.passion = Passion.None;
            }
        }

        private void TryApplyAnyPassion(Pawn pawn)
        {
            if (anyPassion == 0 || pawn.skills == null) return;

            if (anyPassion > 0)
            {
                // Pick a random skill without Major passion to step up
                var candidates = pawn.skills.skills
                    .Where(s => !s.TotallyDisabled && s.passion != Passion.Major)
                    .ToList();
                if (candidates.Count == 0) return;
                var skill = candidates.RandomElement();
                skill.passion = skill.passion == Passion.None ? Passion.Minor : Passion.Major;
            }
            else
            {
                // Pick a random skill with any passion to step down
                var candidates = pawn.skills.skills
                    .Where(s => !s.TotallyDisabled && s.passion != Passion.None)
                    .ToList();
                if (candidates.Count == 0) return;
                var skill = candidates.RandomElement();
                skill.passion = skill.passion == Passion.Major ? Passion.Minor : Passion.None;
            }
        }

        private void TryApplySocialOpinion(Pawn pawn)
        {
            if (socialOpinion == 0) return;
            if (pawn.Map == null) return;

            // Find the colonist the pawn has the highest opinion of
            Pawn target = pawn.Map.mapPawns.FreeColonistsSpawned
                .Where(p => p != pawn)
                .OrderByDescending(p => pawn.relations?.OpinionOf(p) ?? 0)
                .FirstOrDefault();
            if (target == null) return;

            // Positive: use a custom def if available, else skip positive path
            // Negative: use Insulted (confirmed vanilla social thought)
            string thoughtName = socialOpinion > 0 ? "PC_Thought_SocialBond" : "Insulted";
            ThoughtDef td = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtName);
            if (td == null || !td.IsSocial) return;

            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(td, target);
            target.needs?.mood?.thoughts?.memories?.TryGainMemory(td, pawn);
        }

        private void TryApplyMentalBreak(Pawn pawn)
        {
            if (string.IsNullOrEmpty(mentalBreakDef)) return;
            var def = DefDatabase<MentalBreakDef>.GetNamedSilentFail(mentalBreakDef);
            if (def == null) { Log.Warning($"[PawnChronicles] EffectEntryDef {defName}: unknown MentalBreakDef '{mentalBreakDef}'"); return; }
            def.Worker?.TryStart(pawn, null, causedByMood: false);
        }

        private void TryApplyIncident(Pawn pawn)
        {
            if (string.IsNullOrEmpty(incidentDef)) return;
            var def = DefDatabase<IncidentDef>.GetNamedSilentFail(incidentDef);
            if (def == null) { Log.Warning($"[PawnChronicles] EffectEntryDef {defName}: unknown IncidentDef '{incidentDef}'"); return; }
            var parms = StorytellerUtility.DefaultParmsNow(def.category, pawn.Map ?? Find.AnyPlayerHomeMap);
            parms.forced = true;
            def.Worker?.TryExecute(parms);
        }

        private void TryApplySpawnItem(Pawn pawn)
        {
            if (string.IsNullOrEmpty(spawnItemDef) || spawnCount <= 0) return;
            if (pawn.Map == null) return;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(spawnItemDef);
            if (def == null) { Log.Warning($"[PawnChronicles] EffectEntryDef {defName}: unknown ThingDef '{spawnItemDef}'"); return; }

            int remaining = spawnCount;
            while (remaining > 0)
            {
                int stack = System.Math.Min(remaining, def.stackLimit > 0 ? def.stackLimit : remaining);
                var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                thing.stackCount = stack;
                GenPlace.TryPlaceThing(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                remaining -= stack;
            }
        }

        private void TryApplyHediff(Pawn pawn)
        {
            if (string.IsNullOrEmpty(hediffDef)) return;
            var def = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDef);
            if (def == null) { Log.Warning($"[PawnChronicles] EffectEntryDef {defName}: unknown HediffDef '{hediffDef}'"); return; }
            var hediff = HediffMaker.MakeHediff(def, pawn);
            hediff.Severity = hediffSeverity;
            pawn.health.AddHediff(hediff);
        }

        private void TryApplyRaid(Pawn pawn)
        {
            if (raidPoints <= 0) return;
            if (pawn.Map == null) return;
            var raidDef = IncidentDefOf.RaidEnemy;
            if (raidDef == null) return;
            var parms = StorytellerUtility.DefaultParmsNow(raidDef.category, pawn.Map);
            parms.points = raidPoints;
            parms.forced = true;
            raidDef.Worker?.TryExecute(parms);
        }

        // ── Display helpers ───────────────────────────────────────────────────────

        /// <summary>Short stat line shown alongside this entry in the choice hint.</summary>
        public string DisplayLabel
        {
            get
            {
                if (skillLevel != 0 && !string.IsNullOrEmpty(skillLevelDef))
                {
                    var def = DefDatabase<SkillDef>.GetNamedSilentFail(skillLevelDef);
                    string sign = skillLevel > 0 ? "+" : "";
                    return $"{sign}{skillLevel} {def?.label ?? skillLevelDef}";
                }
                if (skillXP != 0f && !string.IsNullOrEmpty(skillXpDef))
                {
                    var def = DefDatabase<SkillDef>.GetNamedSilentFail(skillXpDef);
                    string sign = skillXP > 0 ? "+" : "";
                    return $"{sign}{(int)skillXP} XP ({def?.label ?? skillXpDef})";
                }
                if (anyPassion != 0)
                    return anyPassion > 0
                        ? "PC_Effect_Display_GainPassion".Translate()
                        : "PC_Effect_Display_LosePassion".Translate();
                if (passion != 0 && !string.IsNullOrEmpty(passionSkillDef))
                {
                    var def = DefDatabase<SkillDef>.GetNamedSilentFail(passionSkillDef);
                    string skillLabel = def?.label ?? passionSkillDef;
                    return passion > 0
                        ? "PC_Effect_Display_PassionGain".Translate(skillLabel)
                        : "PC_Effect_Display_PassionLose".Translate(skillLabel);
                }
                if (socialOpinion != 0)
                {
                    string sign = socialOpinion > 0 ? "+" : "";
                    return "PC_Effect_Display_Opinion".Translate($"{sign}{socialOpinion}");
                }
                if (!string.IsNullOrEmpty(mentalBreakDef))
                    return "PC_Effect_Display_MentalBreak".Translate();
                if (!string.IsNullOrEmpty(incidentDef))
                    return "PC_Effect_Display_Incident".Translate();
                if (!string.IsNullOrEmpty(spawnItemDef))
                {
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(spawnItemDef);
                    return "PC_Effect_Display_SpawnItem".Translate(spawnCount, def?.label ?? spawnItemDef);
                }
                if (!string.IsNullOrEmpty(hediffDef))
                {
                    var def = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDef);
                    return def?.label ?? hediffDef;
                }
                if (raidPoints > 0)
                    return "PC_Effect_Display_Raid".Translate();
                return consequence;
            }
        }
    }
}
