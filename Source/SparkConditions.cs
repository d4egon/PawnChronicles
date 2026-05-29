using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Checks pawn state for conditions that should trigger a Spark -
    /// a retroactive narrative moment stamped onto the pawn's history.
    ///
    /// Returns true + a trigger key if a spark should fire.
    /// The trigger key is used by CompPersonalChronicles.ApplySparkEffect
    /// to determine what small real-world consequence (if any) accompanies
    /// the narrative letter.
    ///
    /// Add new conditions here freely - each is a simple pawn state check.
    /// </summary>
    public static class SparkConditions
    {
        public static bool Check(Pawn pawn, CompPersonalChronicles comp, out string triggerKey)
        {
            triggerKey = "";

            if (pawn == null || pawn.Dead || !pawn.Spawned) return false;

            // ── First kill ────────────────────────────────────────────────────
            int kills = pawn.records?.GetAsInt(RecordDefOf.Kills) ?? 0;
            if (kills == 1)
            {
                triggerKey = "first_kill";
                return true;
            }

            // ── Many kills (milestone) ────────────────────────────────────────
            if (kills > 0 && kills % 25 == 0)
            {
                triggerKey = "many_kills";
                return true;
            }

            // ── Near death - was recently downed ─────────────────────────────
            int timesDowned = pawn.records?.GetAsInt(RecordDefOf.TimesInMentalState) ?? 0;
            if (pawn.health?.hediffSet?.PainTotal > 0.7f)
            {
                triggerKey = "near_death";
                return true;
            }

            // ── Skill breakthrough - just hit a passion skill level ───────────
            if (pawn.skills != null)
            {
                foreach (var skill in pawn.skills.skills)
                {
                    if (skill.passion == Passion.None) continue;
                    // Fire at level milestones: 5, 10, 15, 20
                    if (skill.Level == 5 || skill.Level == 10 ||
                        skill.Level == 15 || skill.Level == 20)
                    {
                        triggerKey = "skill_breakthrough";
                        return true;
                    }
                }
            }

            // ── New relationship formed ───────────────────────────────────────
            if (pawn.relations != null)
            {
                var lover = pawn.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Lover)
                         ?? pawn.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Spouse);
                if (lover != null && pawn.relations.OpinionOf(lover) > 60)
                {
                    triggerKey = "relationship_formed";
                    return true;
                }
            }

            return false;
        }
    }
}
