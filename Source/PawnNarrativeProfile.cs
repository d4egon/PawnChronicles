using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    public class PawnNarrativeProfile
    {
        public const float ActiveThreshold = 20f;

        public readonly Pawn Pawn;
        public readonly Dictionary<NarrativeTagDef, float> Scores = new();

        private PawnNarrativeProfile(Pawn pawn) { Pawn = pawn; }

        public static PawnNarrativeProfile BuildFor(Pawn pawn)
        {
            var profile = new PawnNarrativeProfile(pawn);

            foreach (var tagDef in DefDatabase<NarrativeTagDef>.AllDefsListForReading)
            {
                var scorer = tagDef.GetScorer();
                if (scorer == null) continue;

                float raw = 0f;
                try
                {
                    raw = scorer.Score(pawn);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[PawnChronicles] Scorer for tag '{tagDef.defName}' threw on pawn '{pawn.LabelShort}': {ex.Message}");
                }

                profile.Scores[tagDef] = Mathf.Clamp(raw, 0f, 100f);
            }

            // Supplementary pass 1: mechanical trait classification.
            // Reads what each trait actually does (stats, skills, aptitudes) and
            // adds weight to the matching narrative tags. Works for vanilla AND
            // modded traits automatically - no hardcoded defName checks needed.
            ApplyTraitClassification(pawn, profile);

            // Supplementary pass 2: backstory classification.
            // Childhood and adulthood backstories carry skillGains, workDisables,
            // and title/description text - all strong identity signals that most
            // scorers currently ignore.
            ApplyBackstoryClassification(pawn, profile);

            if (Prefs.DevMode)
                LogProfile(pawn, profile);

            return profile;
        }

        /// <summary>
        /// Runs TraitTagClassifier over every trait the pawn has and folds the
        /// results into the profile scores. Applied at 60% scale so it is additive
        /// without overwhelming the dedicated per-tag scorers for vanilla pawns.
        /// Modded traits that no scorer recognises get this as their primary signal.
        /// </summary>
        private static void ApplyTraitClassification(Pawn pawn, PawnNarrativeProfile profile)
        {
            if (pawn.story?.traits == null) return;

            // 0.6 keeps classifier contributions additive rather than dominant.
            // Vanilla traits already scored by NarrativeTagScorer subclasses get
            // a small supplementary bump; fully modded traits get the full inferred value.
            const float Scale = 0.6f;

            foreach (var trait in pawn.story.traits.allTraits)
            {
                var contributions = TraitTagClassifier.Classify(trait.def);
                foreach (var kv in contributions)
                {
                    var tagDef = DefDatabase<NarrativeTagDef>.GetNamedSilentFail("pc_tag_" + kv.Key);
                    if (tagDef == null) continue;

                    if (!profile.Scores.ContainsKey(tagDef)) profile.Scores[tagDef] = 0f;
                    profile.Scores[tagDef] = Mathf.Clamp(
                        profile.Scores[tagDef] + kv.Value * Scale, 0f, 100f);
                }
            }
        }

        /// <summary>
        /// Runs TraitTagClassifier.ClassifyBackstory over the pawn's childhood and
        /// adulthood backstories and folds the results into the profile scores.
        ///
        /// Backstories contribute through three channels:
        ///   • skillGains  - what the pawn was trained in before arrival
        ///   • workDisables - what their background prevented them from doing
        ///   • title + description keywords - narrative framing of their past
        ///
        /// Childhood is weighted at 70% of adulthood - formative but more distant.
        /// </summary>
        private static void ApplyBackstoryClassification(Pawn pawn, PawnNarrativeProfile profile)
        {
            if (pawn.story == null) return;

            const float AdulthoodScale  = 0.7f;
            const float ChildhoodScale  = 0.5f; // childhood shapes but doesn't define

            void ApplyOne(BackstoryDef def, float scale)
            {
                if (def == null) return;
                var contributions = TraitTagClassifier.ClassifyBackstory(def);
                foreach (var kv in contributions)
                {
                    var tagDef = DefDatabase<NarrativeTagDef>.GetNamedSilentFail("pc_tag_" + kv.Key);
                    if (tagDef == null) continue;
                    if (!profile.Scores.ContainsKey(tagDef)) profile.Scores[tagDef] = 0f;
                    profile.Scores[tagDef] = Mathf.Clamp(
                        profile.Scores[tagDef] + kv.Value * scale, 0f, 100f);
                }
            }

            ApplyOne(pawn.story.Childhood, ChildhoodScale);
            ApplyOne(pawn.story.Adulthood, AdulthoodScale);
        }

        public IEnumerable<NarrativeTagDef> ActiveTags =>
            Scores.Where(kv => kv.Value >= ActiveThreshold).Select(kv => kv.Key);

        public List<NarrativeTagDef> GetDominantTags(int maxCount = 2) =>
            Scores.OrderByDescending(kv => kv.Value).Take(maxCount).Select(kv => kv.Key).ToList();

        public float GetScore(NarrativeTagDef tag) =>
            Scores.TryGetValue(tag, out float v) ? v : 0f;

        public float GetScore(string tagDefName)
        {
            var def = DefDatabase<NarrativeTagDef>.GetNamedSilentFail(tagDefName);
            return def != null ? GetScore(def) : 0f;
        }

        public float MatchScore(List<NarrativeTagRequirement> requirements)
        {
            if (requirements == null || requirements.Count == 0) return 0f;

            float total = 0f;
            float weightSum = 0f;

            foreach (var req in requirements)
            {
                if (req.tag == null) return -1f;
                float score = GetScore(req.tag);
                if (score < req.minScore) return -1f;
                total += score * req.weight;
                weightSum += req.weight;
            }

            return weightSum > 0f ? total / weightSum : 0f;
        }

        /// <summary>
        /// Returns real contributors to a tag's score.
        /// This is used by the Chronicles tab breakdown.
        /// </summary>
        public List<(string Source, float Value)> GetContributors(NarrativeTagDef tag)
        {
            var result = new List<(string Source, float Value)>();
            if (tag == null || Pawn == null) return result;

            string t = tag.defName?.ToLowerInvariant().Replace("pc_tag_", "") ?? "";

            switch (t)
            {
                case "trauma":
                case "decay":
                    if (Pawn.health?.hediffSet != null)
                    {
                        foreach (var h in Pawn.health.hediffSet.hediffs.Where(h => h.Visible))
                        {
                            float c = h is Hediff_MissingPart ? 30f
                                    : h.IsPermanent() && h.Severity > 0.1f ? Mathf.Min(h.Severity * 20f, 40f)
                                    : 0f;
                            if (c > 0f) result.Add((h.LabelCap, c));
                        }
                        float pain = Pawn.health.hediffSet.PainTotal;
                        if (pain > 0.1f) result.Add(("Chronic pain", pain * 25f));
                        if (t == "decay")
                        {
                            foreach (var h in Pawn.health.hediffSet.hediffs.Where(h => h.def.IsAddiction))
                                result.Add((h.LabelCap, 25f));
                            int age = Pawn.ageTracker?.AgeBiologicalYears ?? 0;
                            if (age > 60) result.Add(($"Age {age}", (age - 60) * 1.5f));
                        }
                    }
                    break;

                case "loss":
                case "grief":
                    if (Pawn.relations != null)
                    {
                        foreach (var rel in Pawn.relations.DirectRelations.Where(r => r.otherPawn?.Dead == true))
                        {
                            float c = rel.def == PawnRelationDefOf.Spouse  ? 45f
                                    : rel.def == PawnRelationDefOf.Child   ? 35f
                                    : rel.def == PawnRelationDefOf.Parent  ? 25f
                                    : rel.def == PawnRelationDefOf.Sibling ? 20f : 10f;
                            result.Add(($"Lost: {rel.otherPawn.LabelShort}", c));
                        }
                    }
                    if (t == "grief" && Pawn.needs?.mood?.thoughts?.memories != null)
                    {
                        var thought = Pawn.needs.mood.thoughts.memories.Memories
                            .Where(m => m.def.defName.Contains("Died") || m.def.defName.Contains("Death") || m.def.defName.Contains("Lost"))
                            .OrderByDescending(m => Math.Abs(m.MoodOffset()))
                            .FirstOrDefault();
                        if (thought != null)
                            result.Add((thought.LabelCap, Math.Abs(thought.MoodOffset()) * 8f));
                        float mood = Pawn.needs.mood.CurLevelPercentage;
                        if (mood < 0.35f) result.Add(("Low mood", 20f));
                    }
                    break;

                case "violence":
                    int kills = Pawn.records?.GetAsInt(RecordDefOf.Kills) ?? 0;
                    if (kills > 0) result.Add(($"{kills} kills", Mathf.Min(kills * 3f, 40f)));
                    if (Pawn.story?.traits?.HasTrait(TraitDefOf.Bloodlust) == true) result.Add(("Bloodlust", 40f));
                    if (Pawn.story?.traits?.HasTrait(TraitDefOf.Brawler)   == true) result.Add(("Brawler",   20f));
                    break;

                case "craft":
                    AddSkillContrib(result, Pawn, SkillDefOf.Crafting,     5f);
                    AddSkillContrib(result, Pawn, SkillDefOf.Construction, 5f);
                    AddSkillContrib(result, Pawn, SkillDefOf.Artistic,     5f);
                    break;

                case "artist":  AddSkillContrib(result, Pawn, SkillDefOf.Artistic,     5f); break;
                case "healer":  AddSkillContrib(result, Pawn, SkillDefOf.Medicine,     5f); break;

                case "nurture":
                    AddSkillContrib(result, Pawn, SkillDefOf.Medicine, 4f);
                    AddSkillContrib(result, Pawn, SkillDefOf.Animals,  4f);
                    if (Pawn.relations != null)
                    {
                        int bonds = Pawn.relations.DirectRelations.Count(r => r.def == PawnRelationDefOf.Bond && !r.otherPawn.Dead);
                        if (bonds > 0) result.Add(($"Animal bonds ({bonds})", bonds * 20f));
                    }
                    break;

                case "animalfriend":
                    AddSkillContrib(result, Pawn, SkillDefOf.Animals, 4f);
                    if (Pawn.relations != null)
                    {
                        int bonds = Pawn.relations.DirectRelations.Count(r => r.def == PawnRelationDefOf.Bond && !r.otherPawn.Dead);
                        if (bonds > 0) result.Add(($"Bonded animals ({bonds})", bonds * 25f));
                    }
                    break;

                case "kinship":
                    if (Pawn.relations != null)
                    {
                        var spouse = Pawn.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Spouse);
                        if (spouse != null && !spouse.Dead) result.Add(($"Spouse: {spouse.LabelShort}", 35f));
                        int living = Pawn.relations.DirectRelations.Count(r => r.otherPawn != null && !r.otherPawn.Dead);
                        if (living > 0) result.Add(($"{living} living relations", Mathf.Min(living * 8f, 35f)));
                    }
                    break;

                case "devotion":
                case "faith":
                    if (ModLister.IdeologyInstalled && Pawn.ideo != null)
                    {
                        result.Add(("Certainty", Pawn.ideo.Certainty * 60f));
                        var role = Pawn.ideo.Ideo?.GetRole(Pawn);
                        if (role != null) result.Add(($"Role: {role.LabelCap}", 25f));
                    }
                    break;

                case "resilience":
                    int breaks = Pawn.records?.GetAsInt(RecordDefOf.TimesInMentalState) ?? 0;
                    if (breaks > 0) result.Add(($"{breaks} mental breaks survived", Mathf.Min(breaks * 10f, 30f)));
                    var toughDef = DefDatabase<TraitDef>.GetNamedSilentFail("Tough");
                    if (toughDef != null && Pawn.story?.traits?.HasTrait(toughDef) == true)
                        result.Add(("Tough", 25f));
                    int daysIn = Pawn.records?.GetAsInt(RecordDefOf.TimeAsColonistOrColonyAnimal) ?? 0;
                    if (daysIn > 60000) result.Add(($"{daysIn / 60000} days in colony", Mathf.Min(daysIn * 0.05f / 60000f * 60000f, 25f)));
                    break;

                case "curiosity":
                case "scholar":
                    AddSkillContrib(result, Pawn, SkillDefOf.Intellectual, t == "scholar" ? 4f : 5f);
                    if (t == "curiosity" && Pawn.skills != null)
                    {
                        int passions = Pawn.skills.skills.Count(s => s.passion == Passion.Major);
                        if (passions > 0) result.Add(($"Burning passions ({passions})", passions * 10f));
                    }
                    break;

                case "leadership":  AddSkillContrib(result, Pawn, SkillDefOf.Social, 3f); break;

                case "isolation":
                    if (Pawn.story?.traits?.HasTrait(TraitDefOf.Psychopath) == true) result.Add(("Psychopath", 35f));
                    int relCount = Pawn.relations?.DirectRelations?.Count ?? 0;
                    if (relCount == 0)      result.Add(("No relations",      30f));
                    else if (relCount <= 2) result.Add(($"Few relations ({relCount})", 15f));
                    int socialLv = Pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                    if (socialLv <= 3) result.Add(($"Social skill (lv {socialLv})", 20f));
                    break;

                case "augmentation":
                    if (Pawn.health?.hediffSet != null)
                        foreach (var h in Pawn.health.hediffSet.hediffs.OfType<Hediff_AddedPart>())
                            result.Add((h.LabelCap, (h.def.addedPartProps?.partEfficiency ?? 1f) > 1f ? 30f : 20f));
                    break;

                case "underworld":
                    if (Pawn.health?.hediffSet != null)
                        foreach (var h in Pawn.health.hediffSet.hediffs.Where(h => h.def.IsAddiction))
                            result.Add((h.LabelCap, 30f));
                    break;

                case "duty":
                    if (Pawn.story?.Adulthood != null)
                        result.Add(($"Backstory: {Pawn.story.Adulthood.title}", 30f));
                    if (ModLister.RoyaltyInstalled && Pawn.royalty?.MostSeniorTitle != null)
                        result.Add(($"Royal title: {Pawn.royalty.MostSeniorTitle.def.GetLabelFor(Pawn)}", 20f));
                    break;
            }

            return result
                .Where(c => c.Value > 0.5f)
                .OrderByDescending(c => c.Value)
                .Take(6)
                .ToList();
        }

        private static void AddSkillContrib(
            List<(string Source, float Value)> result, Pawn pawn, SkillDef def, float mult)
        {
            var skill = pawn.skills?.GetSkill(def);
            if (skill == null || skill.TotallyDisabled || skill.Level == 0) return;
            float val = skill.Level * mult;
            if (skill.passion == Passion.Major) val += 20f;
            else if (skill.passion == Passion.Minor) val += 10f;
            result.Add(($"{def.label.CapitalizeFirst()} (lv {skill.Level})", val));
        }

        public void ApplyStageOutcome(bool success, QuestStageDef? stage)
        {
            if (stage == null) return;

            float delta = success ? 14f : -9f;

            foreach (var req in stage.tagRequirements)
            {
                if (req.tag == null) continue;

                if (Scores.TryGetValue(req.tag, out float current))
                    Scores[req.tag] = Mathf.Clamp(current + delta, 0f, 100f);
            }
        }

        public float TotalTagWeight()
        {
            float total = 0f;
            foreach (var kv in Scores)
                total += kv.Value;
            return total;
        }

        public string? DominantTag()
        {
            if (Scores.Count == 0) return null;
            return Scores.OrderByDescending(kv => kv.Value).First().Key.defName;
        }

        private static void LogProfile(Pawn pawn, PawnNarrativeProfile profile)
        {
            var active = profile.ActiveTags
                .OrderByDescending(t => profile.GetScore(t))
                .Select(t => $"{t.defName}:{profile.GetScore(t):F0}");
            Log.Message($"[PawnChronicles] Profile for {pawn.LabelShort}: {string.Join(", ", active)}");
        }
    }

    public class NarrativeTagRequirement
    {
        public NarrativeTagDef? tag;
        public float minScore = 0f;
        public float weight = 1f;
    }
}