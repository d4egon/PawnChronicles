using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.QuestGen;

namespace PawnChronicles
{
    /// <summary>
    /// Generates small, disposable "ember" quests from a pawn's current state.
    ///
    /// Embers are NOT arc stages. They are daily flavor - small moments that
    /// pop up based on what the scraper finds right now:
    ///   - Relationships (visit grandmother, share meal with friend, avoid rival)
    ///   - Health (tend to a wound, rest an old injury, seek medicine)
    ///   - Mood (find solitude, seek company, process a thought)
    ///   - Skills (practice a passion, teach someone, study something)
    ///   - World (react to weather, season, biome)
    ///
    /// Embers appear in the quest UI with 1-3 day timers. They expire silently
    /// if ignored. A pawn can have 1-3 active embers at once. Completing them
    /// gives small mood buffs. Ignoring them costs nothing.
    ///
    /// The ember system runs independently of the Flame/Inferno arc system.
    /// A pawn can have active embers AND an active epic simultaneously.
    /// </summary>
    public static class EmberEvaluator
    {
        // Maximum active embers per pawn
        public const int MaxActiveEmbers = 3;

        // Cooldown between ember generation attempts (in ticks)
        // ~0.5 days - embers should feel frequent but not spammy
        public const int EmberCooldownTicks = 30000;

        // How long an ember quest lasts before auto-expiring (in days)
        public const float EmberDurationDays = 2f;

        /// <summary>
        /// Attempt to generate a new ember quest for this pawn.
        /// Returns the generated quest, or null if no suitable ember was found
        /// or the pawn already has max embers.
        /// </summary>
        public static Quest? TryGenerateEmber(Pawn pawn, CompPersonalChronicles comp)
        {
            if (pawn == null || comp == null) return null;
            if (comp.ActiveEmberCount >= MaxActiveEmbers) return null;
            if (QuestGen.Working) return null;

            // Pick the best ember type for this pawn right now
            var emberType = SelectEmberType(pawn);
            if (emberType == null) return null;

            // Generate the quest
            var questScript = DefDatabase<QuestScriptDef>.GetNamedSilentFail("PC_Quest_Ember");
            if (questScript == null)
            {
                Log.Warning("[PawnChronicles] PC_Quest_Ember quest script not found.");
                return null;
            }

            Slate slate = new Slate();
            slate.Set("pawn", pawn);
            slate.Set("epicStageRole", "ember");
            slate.Set("emberType", emberType);

            // Build a profile for grammar resolution
            var profile = comp.GetOrBuildProfile();
            var dominant = profile.GetDominantTags();
            if (dominant.Count > 0) slate.Set("epicPrimaryTag", dominant[0].label);
            if (dominant.Count > 1) slate.Set("epicSecondaryTag", dominant[1].label);

            try
            {
                Quest quest = QuestGen.Generate(questScript, slate);
                if (quest != null)
                {
                    Find.QuestManager.Add(quest);
                    return quest;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[PawnChronicles] Ember generation failed for {pawn.LabelShort}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Selects the best ember type string for this pawn based on their
        /// current state. The type string is passed to the grammar resolver
        /// as context. Returns null if no good ember fits right now.
        /// </summary>
        private static string? SelectEmberType(Pawn pawn)
        {
            var candidates = new List<(string type, float weight)>();

            // ── RELATIONSHIP EMBERS ──────────────────────────────────────
            if (pawn.relations != null)
            {
                // Sick/injured family member
                foreach (var rel in pawn.relations.DirectRelations)
                {
                    if (rel.otherPawn == null || rel.otherPawn.Dead) continue;
                    if (!rel.otherPawn.Spawned) continue;

                    bool isClose = rel.def == PawnRelationDefOf.Spouse ||
                                   rel.def == PawnRelationDefOf.Lover ||
                                   rel.def == PawnRelationDefOf.Parent ||
                                   rel.def == PawnRelationDefOf.Child;

                    if (isClose && rel.otherPawn.health?.hediffSet?.hediffs
                            .Any(h => h.Visible && h.def.isBad && h.Severity > 0.2f) == true)
                        candidates.Add(("relation_sick", 3f));

                    if (isClose)
                        candidates.Add(("relation_visit", 1f));
                }

                // Rival tension
                var rival = pawn.relations.PotentiallyRelatedPawns
                    .Where(p => p.Spawned && p.RaceProps.Humanlike &&
                                pawn.relations.OpinionOf(p) < -30)
                    .FirstOrDefault();
                if (rival != null)
                    candidates.Add(("rival_tension", 2f));

                // Friend bonding
                var friend = pawn.relations.PotentiallyRelatedPawns
                    .Where(p => p.Spawned && p.RaceProps.Humanlike &&
                                pawn.relations.OpinionOf(p) > 40)
                    .FirstOrDefault();
                if (friend != null)
                    candidates.Add(("friend_moment", 1.5f));
            }

            // ── HEALTH EMBERS ────────────────────────────────────────────
            if (pawn.health?.hediffSet != null)
            {
                // Chronic pain moment
                if (pawn.health.hediffSet.PainTotal > 0.15f)
                    candidates.Add(("pain_moment", 2f));

                // Old scar reflection
                if (pawn.health.hediffSet.hediffs.Any(h => h.IsPermanent()))
                    candidates.Add(("scar_reflection", 1.5f));

                // Good health - appreciation
                if (pawn.health.hediffSet.hediffs.All(h => !h.def.isBad || !h.Visible))
                    candidates.Add(("health_gratitude", 0.5f));
            }

            // ── MOOD EMBERS ──────────────────────────────────────────────
            if (pawn.needs?.mood != null)
            {
                float mood = pawn.needs.mood.CurLevelPercentage;

                if (mood < 0.3f)
                    candidates.Add(("low_mood_solitude", 2.5f));
                else if (mood > 0.8f)
                    candidates.Add(("high_mood_gratitude", 1f));

                // Strongest thought as ember fuel
                var thoughts = pawn.needs.mood.thoughts?.memories?.Memories;
                if (thoughts != null && thoughts.Count > 0)
                {
                    var strongest = thoughts
                        .Where(t => t != null && Math.Abs(t.MoodOffset()) > 5f)
                        .OrderByDescending(t => Math.Abs(t.MoodOffset()))
                        .FirstOrDefault();
                    if (strongest != null)
                        candidates.Add(("thought_processing", 2f));
                }
            }

            // ── SKILL EMBERS ─────────────────────────────────────────────
            if (pawn.skills != null)
            {
                // Practice a passion
                var passionSkill = pawn.skills.skills
                    .Where(s => !s.TotallyDisabled && s.passion != Passion.None)
                    .RandomElementWithFallback();
                if (passionSkill != null)
                    candidates.Add(("skill_practice", 1f));

                // High skill - teach moment
                var expertSkill = pawn.skills.skills
                    .Where(s => s.Level >= 15)
                    .RandomElementWithFallback();
                if (expertSkill != null)
                    candidates.Add(("skill_teach", 1.5f));
            }

            // ── WORLD EMBERS ─────────────────────────────────────────────
            if (pawn.Map != null)
            {
                // Weather reflection
                candidates.Add(("weather_moment", 0.5f));

                // Season change
                candidates.Add(("season_reflection", 0.3f));
            }

            // ── TRAIT EMBERS ─────────────────────────────────────────────
            if (pawn.story?.traits != null && pawn.story.traits.allTraits.Count > 0)
            {
                candidates.Add(("trait_moment", 1f));
            }

            if (candidates.Count == 0) return null;

            // Weighted random selection
            return candidates.RandomElementByWeight(c => c.weight).type;
        }
    }

    /// <summary>
    /// Tracks a single active ember quest on a pawn.
    /// Stored in CompPersonalChronicles.activeEmbers.
    /// </summary>
    public class ActiveEmber : IExposable
    {
        public int questId = -1;
        public int expiryTick = -1;
        public string emberType = "";

        public ActiveEmber() { }

        public ActiveEmber(int questId, int expiryTick, string emberType)
        {
            this.questId = questId;
            this.expiryTick = expiryTick;
            this.emberType = emberType;
        }

        public bool IsExpired => Find.TickManager.TicksGame >= expiryTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref questId, "questId", -1);
            Scribe_Values.Look(ref expiryTick, "expiryTick", -1);
            Scribe_Values.Look(ref emberType, "emberType", "");
        }
    }
}