using RimWorld;
using UnityEngine;
using System;
using System.Linq;
using Verse;

namespace PawnChronicles
{
    /// <summary>
    /// Base class for all narrative tag scorers.
    ///
    /// Scores should be in the range 0–100:
    ///   0  = tag is not relevant to this pawn at all
    ///   40 = noticeable presence
    ///   70 = strongly defines this pawn
    ///   100 = this tag is the core of who this pawn is
    ///
    /// Scores are clamped to 0–100 by PawnNarrativeProfile.
    /// </summary>
    public abstract class NarrativeTagScorer
    {
        public abstract float Score(Pawn pawn);
        // Add this so PawnNarrativeProfile can call it
        public virtual float ScoreTrait(Trait trait) => 0f;
    }


    // ─────────────────────────────────────────────────────────────────────────
    // TRAUMA - Scars, missing parts, chronic pain, severe hediffs
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Trauma : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff is Hediff_MissingPart)
                    score += 30f;
                else if (hediff.IsPermanent() && hediff.Severity > 0.1f)
                    score += hediff.Severity * 20f;
                else if (hediff.def == HediffDefOf.Heatstroke && hediff.Severity > 0.3f)
                    score += 15f;
            }
            if (pawn.health.hediffSet.PainTotal > 0.2f)
                score += pawn.health.hediffSet.PainTotal * 25f;
            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // LOSS - Dead relations, widowed
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Loss : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            if (pawn.relations == null) return 0f;

            foreach (var rel in pawn.relations.DirectRelations)
            {
                if (rel.otherPawn?.Dead == true)
                {
                    if      (rel.def == PawnRelationDefOf.Spouse)  score += 45f;
                    else if (rel.def == PawnRelationDefOf.Child)   score += 35f;
                    else if (rel.def == PawnRelationDefOf.Parent)  score += 25f;
                    else if (rel.def == PawnRelationDefOf.Sibling) score += 20f;
                    else                                           score += 10f;
                }
            }
            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // GRIEF - Loss that is recent or unprocessed
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Grief : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            if (pawn.needs?.mood == null) return 0f;

            foreach (var thought in pawn.needs.mood.thoughts.memories.Memories)
            {
                if (thought.def.defName.Contains("Died")  ||
                    thought.def.defName.Contains("Death") ||
                    thought.def.defName.Contains("Lost")  ||
                    thought.def.defName.Contains("Mourn"))
                {
                    score += Math.Abs(thought.MoodOffset()) * 8f;
                }
            }
            float moodLevel = pawn.needs.mood.CurLevel;
            if (moodLevel < 0.35f) score *= 1.4f;
            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // AUGMENTATION - Installed bionics and implants
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Augmentation : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff is Hediff_AddedPart added)
                    score += added.def.addedPartProps?.partEfficiency > 1f ? 30f : 20f;
            }
            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // UNDERWORLD - Criminal/pirate background, addiction
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Underworld : NarrativeTagScorer
    {
        private static readonly string[] UnderworldTags =
            { "Pirate", "Criminal", "Outlaw", "Thief", "Smuggler", "Gang" };

        public override float Score(Pawn pawn)
        {
            float score = 0f;

            if (pawn.story?.Childhood != null)
                foreach (var tag in UnderworldTags)
                    if (pawn.story.Childhood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 20f;

            if (pawn.story?.Adulthood != null)
                foreach (var tag in UnderworldTags)
                    if (pawn.story.Adulthood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 25f;

            foreach (var hediff in pawn.health.hediffSet.hediffs)
                if (hediff.def.IsAddiction) score += 30f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // DUTY - Military background, royal title
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Duty : NarrativeTagScorer
    {
        private static readonly string[] DutyTags =
            { "Soldier", "Guard", "Officer", "Marine", "Military", "Veteran", "Mercenary" };

        public override float Score(Pawn pawn)
        {
            float score = 0f;

            if (pawn.story?.Adulthood != null)
                foreach (var tag in DutyTags)
                    if (pawn.story.Adulthood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 30f;

            // Royal title requires Royalty DLC
            if (ModLister.RoyaltyInstalled)
            {
                var title = pawn.royalty?.MostSeniorTitle;
                if (title != null) score += 20f + (title.def.seniority * 0.5f);
            }

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // POWER - Psylink level, royal title tier, anima bond
    // FIX: pawn.connections only exists with Biotech - guard with ModLister
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Power : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;

            // Psylink requires Royalty DLC
            if (ModLister.RoyaltyInstalled)
            {
                int psyLevel = pawn.GetPsylinkLevel();
                if (psyLevel > 0) score += psyLevel * 20f;

                var title = pawn.royalty?.MostSeniorTitle;
                if (title != null) score += 15f + (title.def.seniority * 0.3f);
            }

            // Gauranlen/anima bond requires Ideology DLC
            if (ModLister.IdeologyInstalled && pawn.connections != null)
            {
                var treeComp = pawn.connections.ConnectedThings?
                    .FirstOrDefault(t => t.TryGetComp<CompTreeConnection>() != null)
                    ?.TryGetComp<CompTreeConnection>();
                if (treeComp != null) score += treeComp.ConnectionStrength * 40f;
            }

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // DEVOTION - Ideological certainty, precept adherence, assigned role
    // FIX: guard pawn.ideo access with ModLister.IdeologyInstalled
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Devotion : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            if (!ModLister.IdeologyInstalled || pawn.ideo == null) return 0f;

            float score = pawn.ideo.Certainty * 60f;
            if (pawn.ideo.Ideo?.GetRole(pawn) != null) score += 25f;
            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // DECAY - Addiction, dementia, age degeneration
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Decay : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff.def.IsAddiction) score += 25f;
                if (hediff.def.defName.Contains("Dementia") ||
                    hediff.def.defName.Contains("Alzheimer")) score += 35f;
                if (hediff.def == HediffDefOf.DrugOverdose) score += 20f;
            }

            if (pawn.ageTracker?.AgeBiologicalYears > 60)
                score += (pawn.ageTracker.AgeBiologicalYears - 60) * 1.5f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // ISOLATION - Loner traits, no relationships, low social skill
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Isolation : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;

            if (pawn.story?.traits != null)
            {
                if (pawn.story.traits.HasTrait(TraitDefOf.Psychopath))   score += 35f;
                if (pawn.story.traits.HasTrait(TraitDefOf.DislikesMen) ||
                    pawn.story.traits.HasTrait(TraitDefOf.DislikesWomen)) score += 15f;
            }

            int relCount = pawn.relations?.DirectRelations?.Count ?? 0;
            if (relCount == 0)      score += 30f;
            else if (relCount <= 2) score += 15f;

            int social = pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            if (social <= 3) score += 20f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // SURVIVAL - Refugee/drifter backstory, starvation
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Survival : NarrativeTagScorer
    {
        private static readonly string[] SurvivalTags =
            { "Refugee", "Survivor", "Scavenger", "Drifter", "Exile", "Castaway" };

        public override float Score(Pawn pawn)
        {
            float score = 0f;

            var hunger = pawn.needs?.food;
            if (hunger != null && hunger.CurCategory <= HungerCategory.UrgentlyHungry)
                score += 30f;

            if (pawn.story?.Childhood != null)
                foreach (var tag in SurvivalTags)
                    if (pawn.story.Childhood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 25f;

            if (pawn.story?.Adulthood != null)
                foreach (var tag in SurvivalTags)
                    if (pawn.story.Adulthood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 20f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // VIOLENCE - High kill count, Bloodlust trait, berserker breaks
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Violence : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;

            if (pawn.story?.traits != null)
            {
                if (pawn.story.traits.HasTrait(TraitDefOf.Bloodlust)) score += 40f;
                if (pawn.story.traits.HasTrait(TraitDefOf.Brawler))   score += 20f;
            }

            int kills = pawn.records?.GetAsInt(RecordDefOf.Kills) ?? 0;
            score += Mathf.Min(kills * 3f, 40f);

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // PACIFISM - Incapable of violence, Kind trait, pacifist ideology precepts
    // FIX: guard pawn.ideo access with ModLister.IdeologyInstalled
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Pacifism : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;

            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) score += 50f;
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Kind) == true) score += 25f;

            if (ModLister.IdeologyInstalled && pawn.ideo?.Ideo != null)
                foreach (var precept in pawn.ideo.Ideo.PreceptsListForReading)
                    if (precept.def.defName.Contains("Pacif") ||
                        precept.def.defName.Contains("Violence_Prohibited"))
                        score += 20f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // KINSHIP - Many living relations, married with living spouse
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Kinship : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            if (pawn.relations == null) return 0f;

            var spouse = pawn.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Spouse);
            if (spouse != null && !spouse.Dead) score += 35f;

            int positiveRels = 0;
            foreach (var rel in pawn.relations.DirectRelations)
                if (rel.otherPawn != null && !rel.otherPawn.Dead) positiveRels++;
            score += Mathf.Min(positiveRels * 8f, 35f);

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // CRAFT - High construction/crafting/artistic skill
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Craft : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            if (pawn.skills == null) return 0f;

            int crafting     = pawn.skills.GetSkill(SkillDefOf.Crafting)?.Level     ?? 0;
            int construction = pawn.skills.GetSkill(SkillDefOf.Construction)?.Level ?? 0;
            int artistic     = pawn.skills.GetSkill(SkillDefOf.Artistic)?.Level     ?? 0;

            score += Mathf.Max(crafting, construction, artistic) * 5f;

            if (pawn.skills.GetSkill(SkillDefOf.Crafting)?.passion == Passion.Major ||
                pawn.skills.GetSkill(SkillDefOf.Artistic)?.passion == Passion.Major)
                score += 20f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // CURIOSITY - High intellectual skill, multiple passions
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Curiosity : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            if (pawn.skills == null) return 0f;

            int intellectual = pawn.skills.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            score += intellectual * 5f;

            int burningPassions = pawn.skills.skills.Count(s => s.passion == Passion.Major);
            score += burningPassions * 10f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // NURTURE - High medicine skill, animal bonds, gauranlen connection
    // FIX: pawn.connections only exists with Ideology - guard with ModLister
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Nurture : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            if (pawn.skills == null) return 0f;

            int medicine = pawn.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
            int animals  = pawn.skills.GetSkill(SkillDefOf.Animals)?.Level  ?? 0;
            score += Mathf.Max(medicine, animals) * 4f;

            if (pawn.relations != null)
                foreach (var rel in pawn.relations.DirectRelations)
                    if (rel.def == PawnRelationDefOf.Bond && !rel.otherPawn.Dead)
                        score += 20f;

            // Gauranlen/anima bond requires Ideology DLC
            if (ModLister.IdeologyInstalled && pawn.connections != null)
            {
                var treeComp = pawn.connections.ConnectedThings?
                    .FirstOrDefault(t => t.TryGetComp<CompTreeConnection>() != null)
                    ?.TryGetComp<CompTreeConnection>();
                if (treeComp != null) score += treeComp.ConnectionStrength * 30f;
            }

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // FAITH - High ideological certainty, moralist/leader role
    // FIX: guard pawn.ideo access with ModLister.IdeologyInstalled
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Faith : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            if (!ModLister.IdeologyInstalled || pawn.ideo == null) return 0f;

            float score = pawn.ideo.Certainty * 50f;

            var role = pawn.ideo.Ideo?.GetRole(pawn);
            if (role != null)
            {
                if (role.def.defName.Contains("Leader") ||
                    role.def.defName.Contains("Moral"))
                    score += 35f;
                else
                    score += 20f;
            }

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // RESILIENCE - Survived mental breaks, long colony tenure, tough traits
    // FIX: cache trait defs statically - DefDatabase lookup is not free and
    //      Score() is called for every pawn on every profile build.
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Resilience : NarrativeTagScorer
    {
        // Cached once on first use. GetNamedSilentFail returns null if the def
        // doesn't exist in this version - checked safely below.
        private static TraitDef? _tough;
        private static TraitDef? _nerves;
        private static TraitDef? _industrious;
        private static TraitDef? _hardWorker;
        private static bool _traitDefsResolved = false;

        private static void ResolveTraitDefs()
        {
            if (_traitDefsResolved) return;
            _tough       = DefDatabase<TraitDef>.GetNamedSilentFail("Tough");
            _nerves      = DefDatabase<TraitDef>.GetNamedSilentFail("Nerves");
            _industrious = DefDatabase<TraitDef>.GetNamedSilentFail("Industrious");
            _hardWorker  = DefDatabase<TraitDef>.GetNamedSilentFail("HardWorker");
            _traitDefsResolved = true;
        }

        public override float Score(Pawn pawn)
        {
            ResolveTraitDefs();

            float score = 0f;

            int nearDeaths = pawn.records?.GetAsInt(RecordDefOf.TimesInMentalState) ?? 0;
            score += Mathf.Min(nearDeaths * 10f, 30f);

            if (pawn.story?.traits != null)
            {
                if (_tough       != null && pawn.story.traits.HasTrait(_tough))       score += 25f;
                if (_nerves      != null && pawn.story.traits.HasTrait(_nerves))      score += 15f;
                if (_industrious != null && pawn.story.traits.HasTrait(_industrious)) score += 15f;
                if (_hardWorker  != null && pawn.story.traits.HasTrait(_hardWorker))  score += 15f;
            }

            int daysInGame = pawn.records?.GetAsInt(RecordDefOf.TimeAsColonistOrColonyAnimal) ?? 0;
            score += Mathf.Min(daysInGame * 0.05f, 25f);

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // WANDERING - Refugee/drifter backstory, no faction loyalty
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Wandering : NarrativeTagScorer
    {
        private static readonly string[] WanderingTags =
            { "Drifter", "Wanderer", "Nomad", "Traveller", "Outcast", "Exile", "Tribal" };

        public override float Score(Pawn pawn)
        {
            float score = 0f;

            if (pawn.story?.Childhood != null)
                foreach (var tag in WanderingTags)
                    if (pawn.story.Childhood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 20f;

            if (pawn.story?.Adulthood != null)
                foreach (var tag in WanderingTags)
                    if (pawn.story.Adulthood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 25f;

            if (pawn.Faction == null) score += 20f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // SCHOLAR - High intellectual, researcher passion
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Scholar : NarrativeTagScorer
    {
        private static readonly string[] ScholarTags =
            { "Researcher", "Scholar", "Doctor", "Scientist", "Medic", "Intellectual" };

        public override float Score(Pawn pawn)
        {
            float score = 0f;
            if (pawn.skills == null) return 0f;

            int intellectual = pawn.skills.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            score += intellectual * 4f;

            if (pawn.skills.GetSkill(SkillDefOf.Intellectual)?.passion == Passion.Major)
                score += 20f;

            if (pawn.story?.Adulthood != null)
                foreach (var tag in ScholarTags)
                    if (pawn.story.Adulthood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 20f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // BETRAYAL - Trust broken by others or by the pawn themselves
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Betrayal : NarrativeTagScorer
    {
        private static readonly string[] BetrayalTags =
            { "Traitor", "Spy", "Betrayer", "Deserter", "Turncoat" };

        public override float Score(Pawn pawn)
        {
            float score = 0f;

            if (pawn.story?.Adulthood != null)
                foreach (var tag in BetrayalTags)
                    if (pawn.story.Adulthood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 30f;

            // Psychopath trait can indicate willingness to betray
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Psychopath) == true)
                score += 20f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // REFUGEE - Driven from home by war, disaster, or persecution
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Refugee : NarrativeTagScorer
    {
        private static readonly string[] RefugeeTags =
            { "Refugee", "Exile", "Outcast", "Displaced", "Fleeing" };

        public override float Score(Pawn pawn)
        {
            float score = 0f;

            if (pawn.story?.Childhood != null)
                foreach (var tag in RefugeeTags)
                    if (pawn.story.Childhood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 25f;

            if (pawn.story?.Adulthood != null)
                foreach (var tag in RefugeeTags)
                    if (pawn.story.Adulthood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 20f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // LEADERSHIP - Natural commander, someone others follow
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Leadership : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            if (pawn.skills == null) return 0f;

            int social = pawn.skills.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            score += social * 3f;

            if (pawn.skills.GetSkill(SkillDefOf.Social)?.passion == Passion.Major)
                score += 20f;

            // Royal faction leadership with Royalty DLC
            if (ModLister.RoyaltyInstalled && pawn.royalty?.MostSeniorTitle != null)
                score += 25f;

            // Ideology leadership role
            if (ModLister.IdeologyInstalled && pawn.ideo?.Ideo != null)
            {
                var role = pawn.ideo.Ideo.GetRole(pawn);
                if (role != null && role.def.defName.Contains("Leader"))
                    score += 30f;
            }

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // HEALER - Doctor, medic, natural caretaker
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Healer : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            if (pawn.skills == null) return 0f;

            int medicine = pawn.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
            score += medicine * 5f;

            if (pawn.skills.GetSkill(SkillDefOf.Medicine)?.passion == Passion.Major)
                score += 20f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // ANIMAL FRIEND - Deep bond with animals
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_AnimalFriend : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            if (pawn.skills == null) return 0f;

            int animals = pawn.skills.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
            score += animals * 4f;

            if (pawn.relations != null)
                foreach (var rel in pawn.relations.DirectRelations)
                    if (rel.def == PawnRelationDefOf.Bond && !rel.otherPawn.Dead)
                        score += 25f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // ARTIST - Expresses through beauty, music, or creation
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Artist : NarrativeTagScorer
    {
        public override float Score(Pawn pawn)
        {
            float score = 0f;
            if (pawn.skills == null) return 0f;

            int artistic = pawn.skills.GetSkill(SkillDefOf.Artistic)?.Level ?? 0;
            score += artistic * 5f;

            if (pawn.skills.GetSkill(SkillDefOf.Artistic)?.passion == Passion.Major)
                score += 25f;

            return score;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // NOBLE - High-born blood, royal lineage, fallen empire
    // ─────────────────────────────────────────────────────────────────────────
    public class TagScorer_Noble : NarrativeTagScorer
    {
        private static readonly string[] NobleTags =
            { "Noble", "Lord", "Lady", "Duke", "Duchess", "Baron", "Count", "Heir", "Royal" };

        public override float Score(Pawn pawn)
        {
            float score = 0f;

            if (pawn.story?.Childhood != null)
                foreach (var tag in NobleTags)
                    if (pawn.story.Childhood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 20f;

            if (pawn.story?.Adulthood != null)
                foreach (var tag in NobleTags)
                    if (pawn.story.Adulthood.title?.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 25f;

            if (ModLister.RoyaltyInstalled && pawn.royalty?.MostSeniorTitle != null)
                score += 30f;

            return score;
        }
    }
}