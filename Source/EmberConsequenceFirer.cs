using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Picks and fires a small physical consequence when an ember resolves.
    ///
    /// POOL SELECTION (scored, not hard-filtered):
    ///   Every EmberConsequenceDef with the right isPositive gets a score:
    ///     score = def.weight
    ///           × workTypeBonus   (3× if job matches, 1× if universal, 0.15× if wrong job)
    ///           × skillBonus      (1 + pawnSkillLevel × 0.07 for the def's relevant skill)
    ///
    ///   Job-matched consequences dominate, but off-job ones still appear.
    ///   A pawn with high Plants will see more plant-related ripples even while
    ///   doing other work.
    ///
    /// MESSAGES:
    ///   Every consequence sends a Messages.Message so the player always knows
    ///   what happened ("Labarlallo - burned a hand on the stove.").
    /// </summary>
    public static class EmberConsequenceFirer
    {
        // WorkTypeDef defName -> SkillDef defName for skill-weight bonus.
        private static readonly Dictionary<string, string> WorkTypeToSkillName =
            new Dictionary<string, string>
            {
                { "Mining",       "Mining"       },
                { "Crafting",     "Crafting"     },
                { "Smithing",     "Crafting"     },
                { "Cooking",      "Cooking"      },
                { "Doctor",       "Medicine"     },
                { "Construction", "Construction" },
                { "Hunting",      "Shooting"     },
                { "Growing",      "Plants"       },
                { "PlantCutting", "Plants"       },
                { "Handling",     "Animals"      },
                { "Research",     "Intellectual" },
                { "Warden",       "Social"       },
            };

        // ─────────────────────────────────────────────────────────────────────

        public static void TryFire(Pawn pawn, string workType = "")
        {
            var s = PawnChroniclesMod.Settings;
            if (!Rand.Chance(s.emberConsequenceChance)) return;
            if (!pawn.Spawned || pawn.Map == null) return;

            bool positive = Rand.Chance(s.emberConsequencePositiveRatio);

            // Score every def - nothing is hard-excluded.
            var pool = DefDatabase<EmberConsequenceDef>.AllDefsListForReading
                .Where(d => d.isPositive == positive)
                .Select(d => (def: d, score: ScoreDef(pawn, d, workType)))
                .Where(x => x.score > 0f)
                .ToList();

            if (pool.Count == 0) return;

            var chosen = pool.RandomElementByWeight(x => x.score).def;
            Fire(pawn, chosen);
        }

        // ── Scoring ───────────────────────────────────────────────────────────

        private static float ScoreDef(Pawn pawn, EmberConsequenceDef def, string workType)
        {
            float score = def.weight;

            // WorkType multiplier
            if (def.workTypes.Count == 0)
            {
                // Universal - neutral weight across all jobs
                score *= 1.0f;
            }
            else if (!string.IsNullOrEmpty(workType) && def.workTypes.Contains(workType))
            {
                // Exact job match - strongly preferred
                score *= 3.0f;
            }
            else
            {
                // Wrong job type - excluded entirely
                return 0f;
            }

            // Skill multiplier: find the skill relevant to this def's work context.
            string contextWorkType = def.workTypes.Count > 0 ? def.workTypes[0] : workType;
            if (!string.IsNullOrEmpty(contextWorkType) &&
                WorkTypeToSkillName.TryGetValue(contextWorkType, out string skillName))
            {
                var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(skillName);
                if (skillDef != null)
                {
                    var skill = pawn.skills?.GetSkill(skillDef);
                    if (skill != null && !skill.TotallyDisabled)
                        score *= 1f + skill.levelInt * 0.07f; // +7%/level -> +140% max at 20
                }
            }

            return score;
        }

        // ── Dispatch ─────────────────────────────────────────────────────────

        private static void Fire(Pawn pawn, EmberConsequenceDef def)
        {
            try
            {
                switch (def.type)
                {
                    case NarrativeIncidentType.ItemSpawn:    FireItemSpawn(pawn, def);    break;
                    case NarrativeIncidentType.HediffApply:  FireHediffApply(pawn, def);  break;
                    case NarrativeIncidentType.ThoughtApply: FireThoughtApply(pawn, def); break;
                    case NarrativeIncidentType.SkillChange:  FireSkillChange(pawn, def);  break;
                    default:
                        Log.Warning($"[PawnChronicles] EmberConsequence '{def.defName}': type {def.type} not supported.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[PawnChronicles] EmberConsequenceFirer failed ({def.defName}) for {pawn.LabelShort}: {ex.Message}");
            }
        }

        // ── Item Spawn ────────────────────────────────────────────────────────

        private static void FireItemSpawn(Pawn pawn, EmberConsequenceDef def)
        {
            if (def.thingDef.NullOrEmpty()) return;

            var thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(def.thingDef);
            if (thingDef == null)
            {
                Log.Warning($"[PawnChronicles] EmberConsequence '{def.defName}': ThingDef '{def.thingDef}' not found.");
                return;
            }

            ThingDef stuff = null;
            if (!def.stuffDef.NullOrEmpty())
                stuff = DefDatabase<ThingDef>.GetNamedSilentFail(def.stuffDef);
            if (stuff == null && thingDef.MadeFromStuff)
                stuff = GenStuff.DefaultStuffFor(thingDef);

            var thing = ThingMaker.MakeThing(thingDef, stuff);
            thing.stackCount = Math.Max(1, def.count);

            if (thing.TryGetComp<CompQuality>() is CompQuality qc)
                qc.SetQuality(def.quality, ArtGenerationContext.Colony);

            GenPlace.TryPlaceThing(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near);

            // Notify - e.g. "Labarlallo - gold dust in the seam. 4× gold dropped nearby."
            string countLabel = thing.stackCount == 1
                ? $"a {thingDef.label}"
                : $"{thing.stackCount}× {thingDef.label}";
            string msg = $"{pawn.LabelShort} - {def.label}. {countLabel.CapitalizeFirst()} dropped nearby.";
            Messages.Message(msg, pawn, def.isPositive ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, false);

            // Combat log entry - e.g. "Labarlallo - gold dust in the seam (4× gold dropped nearby)"
            PostToCombatLog(pawn, $"{pawn.LabelShort} - {def.label} ({countLabel} dropped nearby)");
        }

        // ── Hediff Apply ──────────────────────────────────────────────────────

        private static void FireHediffApply(Pawn pawn, EmberConsequenceDef def)
        {
            BodyPartRecord primaryPart = ApplySingleHediff(pawn, def.defName, def.hediffDef, def.hediffSeverity);

            // Optional secondary hediff (e.g. ToxicBuildup + Cut for a poison sting)
            if (!def.secondaryHediffDef.NullOrEmpty())
                ApplySingleHediff(pawn, def.defName, def.secondaryHediffDef, def.secondaryHediffSeverity);

            // Notify - e.g. "Labarlallo - burned a hand on the stove."
            if (!def.hediffDef.NullOrEmpty())
            {
                string msg = $"{pawn.LabelShort} - {def.label}.";
                Messages.Message(msg, pawn, def.isPositive ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.NegativeEvent, false);

                // Combat log entry with hediff name, severity, and body part when available.
                // e.g. "Cedric - hit their thumb with a hammer (bruise, 0.05 severity on left index finger)"
                var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(def.hediffDef);
                string hediffLabel  = hediffDef?.label ?? def.hediffDef;
                string partSuffix   = primaryPart != null ? $" on {primaryPart.LabelCap}" : "";
                string combatLogMsg = $"{pawn.LabelShort} - {def.label} ({hediffLabel}, {def.hediffSeverity:F2} severity{partSuffix})";
                PostToCombatLog(pawn, combatLogMsg);
            }
        }

        // Returns the BodyPartRecord the hediff landed on (null for systemic hediffs).
        private static BodyPartRecord ApplySingleHediff(Pawn pawn, string sourceDef, string hediffDefName, float severity)
        {
            if (hediffDefName.NullOrEmpty()) return null;

            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
            if (hediffDef == null)
            {
                Log.Warning($"[PawnChronicles] EmberConsequence '{sourceDef}': HediffDef '{hediffDefName}' not found.");
                return null;
            }

            var hediff = HediffMaker.MakeHediff(hediffDef, pawn);
            if (severity > 0f)
                hediff.Severity = severity;

            // Wounds (cuts, burns, bruises) land on a specific external body part.
            // This keeps injuries local - worst case is an amputation, not death.
            // Systemic hediffs (ToxicBuildup, etc.) stay whole-body.
            bool isInjury = typeof(Hediff_Injury).IsAssignableFrom(hediffDef.hediffClass);
            BodyPartRecord part = isInjury ? PickExternalPart(pawn) : null;
            pawn.health.AddHediff(hediff, part);
            return part;
        }

        /// <summary>
        /// Picks a random external, non-root body part, weighted toward smaller parts
        /// so fingers, toes, and hands come up more often than the torso.
        /// </summary>
        private static BodyPartRecord PickExternalPart(Pawn pawn)
        {
            // External parts only - skip root body node (parent == null)
            var parts = pawn.health.hediffSet.GetNotMissingParts()
                .Where(p => p.depth == BodyPartDepth.Outside && p.parent != null)
                .ToList();

            // Fallback: any non-missing part (handles edge cases like custom bodies)
            if (parts.Count == 0)
                parts = pawn.health.hediffSet.GetNotMissingParts().ToList();

            if (parts.Count == 0) return null;

            // Weight by 1/hitPoints - small parts (fingers, toes) hit more than torso
            return parts.RandomElementByWeight(p => 1f / Mathf.Max(1f, p.def.hitPoints));
        }

        // ── Thought Apply ─────────────────────────────────────────────────────

        private static void FireThoughtApply(Pawn pawn, EmberConsequenceDef def)
        {
            if (def.thoughtDef.NullOrEmpty()) return;

            var thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(def.thoughtDef);
            if (thoughtDef == null)
            {
                Log.Warning($"[PawnChronicles] EmberConsequence '{def.defName}': ThoughtDef '{def.thoughtDef}' not found.");
                return;
            }

            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(thoughtDef);

            // Negative thought ripples get a message; positive mood buffs are already
            // announced by the ember itself so we skip those to avoid double-notice.
            if (!def.isPositive)
            {
                string msg = $"{pawn.LabelShort} - {def.label}.";
                Messages.Message(msg, pawn, MessageTypeDefOf.NegativeEvent, false);
            }

            // Always log to the combat log regardless of polarity, so the player
            // has a record of mood-shaping events alongside physical ones.
            // e.g. "Labarlallo - darkened mood (distracted, -5 mood)"
            string moodDelta = thoughtDef.stages?.Count > 0
                ? $", {thoughtDef.stages[0].baseMoodEffect:+0;-0} mood"
                : "";
            PostToCombatLog(pawn, $"{pawn.LabelShort} - {def.label} ({thoughtDef.label}{moodDelta})");
        }

        // ── Skill Change ──────────────────────────────────────────────────────

        private static void FireSkillChange(Pawn pawn, EmberConsequenceDef def)
        {
            if (def.skillDef.NullOrEmpty()) return;

            var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(def.skillDef);
            if (skillDef == null)
            {
                Log.Warning($"[PawnChronicles] EmberConsequence '{def.defName}': SkillDef '{def.skillDef}' not found.");
                return;
            }

            var skill = pawn.skills?.GetSkill(skillDef);
            if (skill == null || skill.TotallyDisabled) return;

            skill.Learn(def.skillXp, direct: true);

            // Notify - e.g. "Labarlallo - rhythm in the stone. (+400 mining xp)"
            string sign = def.skillXp >= 0 ? "+" : "";
            string msg = $"{pawn.LabelShort} - {def.label}. ({sign}{def.skillXp} {skillDef.skillLabel} xp)";
            Messages.Message(msg, pawn, def.isPositive ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.NegativeEvent, false);

            // Combat log entry - e.g. "Labarlallo - rhythm in the stone (+400 mining xp)"
            PostToCombatLog(pawn, $"{pawn.LabelShort} - {def.label} ({sign}{def.skillXp} {skillDef.skillLabel} xp)");
        }

        // ── Combat log helper ─────────────────────────────────────────────────

        /// <summary>
        /// Posts a plain-text entry to the combat log so the player can review
        /// all ember consequence events in one place, filtered by pawn.
        /// Silently swallows any exceptions - this is a quality-of-life feature
        /// and should never crash the game.
        /// </summary>
        private static void PostToCombatLog(Pawn pawn, string message)
        {
            try
            {
                Find.BattleLog?.Add(new PC_LogEntry(message, pawn));
            }
            catch (Exception ex)
            {
                Log.Warning($"[PawnChronicles] PostToCombatLog failed for {pawn?.LabelShort}: {ex.Message}");
            }
        }
    }
}
