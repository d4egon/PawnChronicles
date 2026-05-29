using System;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnChronicles
{
    public abstract class NarrativeCondition
    {
        public abstract bool IsMet(Pawn pawn);
    }

    // ── BODY / HEALTH ─────────────────────────────────────────────────────────

    public class Condition_MissingBodyPart : NarrativeCondition
    {
        public string bodyPartTag = "";

        public override bool IsMet(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return false;

            return pawn.health.hediffSet.hediffs.Any(h =>
                h is Hediff_MissingPart &&
                (string.IsNullOrEmpty(bodyPartTag) ||
                 (h.Part?.def.tags != null &&
                  h.Part.def.tags.Any(t =>
                      t.defName.Equals(bodyPartTag, StringComparison.OrdinalIgnoreCase)))));
        }
    }

    public class Condition_HasHediff : NarrativeCondition
    {
        public HediffDef? hediffDef;
        public float minSeverity = 0f;

        public override bool IsMet(Pawn pawn) =>
            pawn?.health?.hediffSet?.hediffs.Any(h =>
                h.def == hediffDef && h.Severity >= minSeverity) ?? false;
    }

    public class Condition_HasAddiction : NarrativeCondition
    {
        public ChemicalDef? specificDrug;

        public override bool IsMet(Pawn pawn) =>
            pawn?.health?.hediffSet?.hediffs.Any(h =>
                h.def.IsAddiction &&
                (specificDrug == null ||
                 (h is Hediff_Addiction addiction && addiction.Chemical == specificDrug))) ?? false;
    }

    public class Condition_HasBionic : NarrativeCondition
    {
        public bool requireSupernatural = false;

        public override bool IsMet(Pawn pawn) =>
            pawn?.health?.hediffSet?.hediffs.Any(h =>
                h is Hediff_AddedPart added &&
                (!requireSupernatural ||
                 (added.def.addedPartProps != null && added.def.addedPartProps.partEfficiency > 1f))) ?? false;
    }

    // ── TRAITS ────────────────────────────────────────────────────────────────

    public class Condition_HasTrait : NarrativeCondition
    {
        public TraitDef? trait;
        public int degree = int.MinValue;

        public override bool IsMet(Pawn pawn)
        {
            if (pawn?.story?.traits == null || trait == null) return false;
            if (degree == int.MinValue) return pawn.story.traits.HasTrait(trait);
            return pawn.story.traits.GetTrait(trait)?.Degree == degree;
        }
    }

    public class Condition_IncapableOf : NarrativeCondition
    {
        public WorkTags workTag;

        public override bool IsMet(Pawn pawn) => pawn?.WorkTagIsDisabled(workTag) ?? false;
    }

    // ── SKILLS ────────────────────────────────────────────────────────────────

    public class Condition_SkillLevel : NarrativeCondition
    {
        public SkillDef? skill;
        public int minLevel = 8;

        public override bool IsMet(Pawn pawn) =>
            pawn?.skills?.GetSkill(skill)?.Level >= minLevel;
    }

    public class Condition_HasPassion : NarrativeCondition
    {
        public SkillDef? skill;
        public Passion minPassion = Passion.Minor;

        public override bool IsMet(Pawn pawn) =>
            pawn?.skills?.GetSkill(skill)?.passion >= minPassion;
    }

    // ── RELATIONS ────────────────────────────────────────────────────────────

    public class Condition_RelationDead : NarrativeCondition
    {
        public PawnRelationDef? relationDef;

        public override bool IsMet(Pawn pawn) =>
            pawn?.relations?.DirectRelations.Any(r =>
                r.otherPawn != null && r.otherPawn.Dead &&
                (relationDef == null || r.def == relationDef)) ?? false;
    }

    // ── POWER / TITLES ────────────────────────────────────────────────────────

    public class Condition_PsylinkLevel : NarrativeCondition
    {
        public int minLevel = 1;

        public override bool IsMet(Pawn pawn)
        {
            if (!ModLister.RoyaltyInstalled) return false;
            return pawn.GetPsylinkLevel() >= minLevel;
        }
    }

    public class Condition_RoyalTitle : NarrativeCondition
    {
        public int minSeniority = 1;

        public override bool IsMet(Pawn pawn)
        {
            if (!ModLister.RoyaltyInstalled || pawn?.royalty == null) return false;
            var title = pawn.royalty.MostSeniorTitle;
            return title != null && title.def.seniority >= minSeniority;
        }
    }

    // ── IDEOLOGY ─────────────────────────────────────────────────────────────

    public class Condition_IdeoCertainty : NarrativeCondition
    {
        public float minCertainty = 0.7f;

        public override bool IsMet(Pawn pawn)
        {
            if (!ModLister.IdeologyInstalled) return false;
            return pawn?.ideo?.Certainty >= minCertainty;
        }
    }

    public class Condition_HasIdeoRole : NarrativeCondition
    {
        public PreceptDef? roleDef;

        public override bool IsMet(Pawn pawn)
        {
            if (!ModLister.IdeologyInstalled || pawn?.ideo?.Ideo == null) return false;
            var role = pawn.ideo.Ideo.GetRole(pawn);
            return role != null && (roleDef == null || role.def == roleDef);
        }
    }

    // ── NARRATIVE TAG CONDITIONS ──────────────────────────────────────────────

    public class Condition_TagScore : NarrativeCondition
    {
        public NarrativeTagDef? tag;
        public float minScore = 40f;

        public override bool IsMet(Pawn pawn)
        {
            if (tag == null || pawn == null) return false;
            var profile = pawn.GetNarrativeProfile();
            return profile.GetScore(tag) >= minScore;
        }
    }

    public class Condition_CompletedEpic : NarrativeCondition
    {
        public PersonalEpicDef? epic;

        public override bool IsMet(Pawn pawn)
        {
            if (epic == null || pawn == null) return false;
            var comp = pawn.GetComp<CompPersonalChronicles>();
            return comp != null && comp.HasCompletedEpic(epic);
        }
    }
}