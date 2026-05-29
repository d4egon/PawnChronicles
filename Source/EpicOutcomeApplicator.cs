using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Applies EpicOutcome effects to a pawn when an arc climax resolves.
    /// Called from CompPersonalChronicles.CompleteEpic().
    ///
    /// Every mechanical consequence posts a plain-language Messages.Message
    /// so the player always knows exactly what the arc caused.
    /// </summary>
    public static class EpicOutcomeApplicator
    {
        public static void Apply(Pawn pawn, EpicOutcome outcome, bool isSuccess)
        {
            if (pawn == null || outcome == null) return;

            ApplyMoodThought(pawn, outcome);
            ApplySkillGains(pawn, outcome);
            ApplyInspiration(pawn, outcome);
            ApplyHediffs(pawn, outcome);
            ApplyItemDrops(pawn, outcome);

            Log.Message($"[PawnChronicles] Applied {(isSuccess ? "success" : "failure")} outcome to {pawn.LabelShort}.");
        }

        // ── Mood ─────────────────────────────────────────────────────────────

        private static void ApplyMoodThought(Pawn pawn, EpicOutcome outcome)
        {
            if (string.IsNullOrEmpty(outcome.moodThought)) return;
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(outcome.moodThought);
            if (def == null)
            {
                Log.Warning($"[PawnChronicles] EpicOutcome: ThoughtDef '{outcome.moodThought}' not found.");
                return;
            }
            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(def);

            // Tell the player what emotional mark this left
            string label = def.label.NullOrEmpty() ? def.defName : def.label;
            Messages.Message(
                $"The arc left a mark on {pawn.LabelShort} - they carry a new memory: \"{label}\".",
                pawn, MessageTypeDefOf.NeutralEvent, false);
        }

        // ── Skills ────────────────────────────────────────────────────────────

        private static void ApplySkillGains(Pawn pawn, EpicOutcome outcome)
        {
            if (pawn.skills == null) return;

            if (outcome.skillGains != null)
            {
                foreach (var gain in outcome.skillGains)
                {
                    if (string.IsNullOrEmpty(gain.skill)) continue;
                    var def = DefDatabase<SkillDef>.GetNamedSilentFail(gain.skill);
                    if (def == null) continue;
                    var skill = pawn.skills.GetSkill(def);
                    if (skill == null || skill.TotallyDisabled) continue;
                    skill.Learn(gain.xp, direct: true);

                    // Only announce notable XP (avoids spamming for tiny amounts)
                    if (gain.xp >= 500)
                    {
                        Messages.Message(
                            $"The arc sharpened {pawn.LabelShort}'s {def.label} - gained {gain.xp:N0} XP.",
                            pawn, MessageTypeDefOf.NeutralEvent, false);
                    }
                }
            }

            if (outcome.bestSkillXP > 0)
            {
                var best = pawn.skills.skills
                    .Where(s => !s.TotallyDisabled)
                    .OrderByDescending(s => s.Level)
                    .FirstOrDefault();
                if (best != null)
                {
                    best.Learn(outcome.bestSkillXP, direct: true);
                    if (outcome.bestSkillXP >= 500)
                    {
                        Messages.Message(
                            $"The arc honed {pawn.LabelShort}'s strongest skill: {best.def.label} (+{outcome.bestSkillXP:N0} XP).",
                            pawn, MessageTypeDefOf.NeutralEvent, false);
                    }
                }
            }
        }

        // ── Inspiration ───────────────────────────────────────────────────────

        private static void ApplyInspiration(Pawn pawn, EpicOutcome outcome)
        {
            if (string.IsNullOrEmpty(outcome.inspirationDef)) return;
            if (!Rand.Chance(outcome.inspirationChance)) return;
            var def = DefDatabase<InspirationDef>.GetNamedSilentFail(outcome.inspirationDef);
            if (def == null) return;

            bool started = pawn.mindState?.inspirationHandler?.TryStartInspiration(def, null) ?? false;
            if (started)
            {
                string label = def.label.NullOrEmpty() ? def.defName : def.label;
                Messages.Message(
                    $"The arc sparked an inspiration in {pawn.LabelShort}: {label}!",
                    pawn, MessageTypeDefOf.PositiveEvent, false);
            }
        }

        // ── Hediffs ───────────────────────────────────────────────────────────

        private static void ApplyHediffs(Pawn pawn, EpicOutcome outcome)
        {
            if (pawn.health?.hediffSet == null) return;

            // Remove named hediffs first
            if (outcome.hediffsRemoved != null)
            {
                foreach (string defName in outcome.hediffsRemoved)
                {
                    if (string.IsNullOrEmpty(defName)) continue;
                    var def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                    if (def == null) continue;
                    var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                    if (hediff == null) continue;
                    pawn.health.RemoveHediff(hediff);

                    string label = def.label.NullOrEmpty() ? def.defName : def.label;
                    Messages.Message(
                        $"The arc resolved something in {pawn.LabelShort} - {label} has lifted.",
                        pawn, MessageTypeDefOf.PositiveEvent, false);
                }
            }

            // Remove the worst bad non-missing-part hediff
            if (outcome.removeOneBadHediff)
            {
                var worst = pawn.health.hediffSet.hediffs
                    .Where(h => h.Visible && h.def.isBad && !(h is Hediff_MissingPart))
                    .OrderByDescending(h => h.Severity)
                    .FirstOrDefault();
                if (worst != null)
                {
                    string label = worst.def.label.NullOrEmpty() ? worst.def.defName : worst.def.label;
                    pawn.health.RemoveHediff(worst);
                    Messages.Message(
                        $"The arc lifted {pawn.LabelShort}'s worst burden - {label} is gone.",
                        pawn, MessageTypeDefOf.PositiveEvent, false);
                }
            }

            // Apply new hediffs
            if (outcome.hediffsApplied != null)
            {
                foreach (var gain in outcome.hediffsApplied)
                {
                    if (string.IsNullOrEmpty(gain.hediff)) continue;
                    var def = DefDatabase<HediffDef>.GetNamedSilentFail(gain.hediff);
                    if (def == null) continue;

                    BodyPartRecord part = null;
                    if (!string.IsNullOrEmpty(gain.bodyPart))
                    {
                        var bpd = DefDatabase<BodyPartDef>.GetNamedSilentFail(gain.bodyPart);
                        if (bpd != null)
                            part = pawn.RaceProps.body.AllParts
                                .FirstOrDefault(p => p.def == bpd);
                    }

                    var hediff = HediffMaker.MakeHediff(def, pawn, part);
                    hediff.Severity = gain.severity;
                    pawn.health.AddHediff(hediff, part);

                    string label    = def.label.NullOrEmpty() ? def.defName : def.label;
                    string partName = part != null ? $" ({part.def.label})" : "";
                    var    msgType  = def.isBad ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.NeutralEvent;
                    Messages.Message(
                        $"The arc marked {pawn.LabelShort} - they now bear {label}{partName}.",
                        pawn, msgType, false);
                }
            }
        }

        // ── Items ─────────────────────────────────────────────────────────────

        private static void ApplyItemDrops(Pawn pawn, EpicOutcome outcome)
        {
            if (!pawn.Spawned || pawn.Map == null) return;

            if (outcome.itemDrops != null)
            {
                foreach (var drop in outcome.itemDrops)
                {
                    string label = TryDrop(pawn, drop.thingDef, drop.count, drop.quality, drop.stuffDef);
                    if (label != null)
                    {
                        string countStr = drop.count > 1 ? $" x{drop.count}" : "";
                        Messages.Message(
                            $"Something fell near {pawn.LabelShort}: {label}{countStr}.",
                            pawn, MessageTypeDefOf.NeutralEvent, false);
                    }
                }
            }

            if (outcome.dropSkillRewardItem && pawn.skills != null)
            {
                var best = pawn.skills.skills
                    .Where(s => !s.TotallyDisabled)
                    .OrderByDescending(s => s.Level)
                    .FirstOrDefault();
                if (best != null)
                {
                    string rewardDefName = SkillRewardDef(best.def.defName);
                    string label = TryDrop(pawn, rewardDefName, 1, outcome.skillRewardQuality, null);
                    if (label != null)
                    {
                        Messages.Message(
                            $"{pawn.LabelShort}'s mastery in {best.def.label} earned them {label}.",
                            pawn, MessageTypeDefOf.PositiveEvent, false);
                    }
                }
            }
        }

        /// <summary>
        /// Places a thing near the pawn. Returns the item's label on success, null on failure.
        /// </summary>
        private static string TryDrop(
            Pawn pawn, string defName, int count,
            QualityCategory quality, string stuffDefName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Log.Warning($"[PawnChronicles] EpicOutcome: ThingDef '{defName}' not found - skipping drop.");
                return null;
            }

            ThingDef stuff = null;
            if (!string.IsNullOrEmpty(stuffDefName))
                stuff = DefDatabase<ThingDef>.GetNamedSilentFail(stuffDefName);
            if (stuff == null && def.MadeFromStuff)
                stuff = GenStuff.DefaultStuffFor(def);

            var thing = ThingMaker.MakeThing(def, stuff);
            thing.stackCount = Mathf.Clamp(count, 1, def.stackLimit);

            if (thing.TryGetComp<CompQuality>() is CompQuality qc)
                qc.SetQuality(quality, ArtGenerationContext.Colony);

            bool placed = GenPlace.TryPlaceThing(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            return placed ? thing.Label : null;
        }

        /// <summary>Maps best skill defName to a sensible reward ThingDef defName.</summary>
        private static string SkillRewardDef(string skillDefName) => skillDefName switch
        {
            "Crafting"     => "ComponentIndustrial",
            "Construction" => "ComponentIndustrial",
            "Medicine"     => "MedicineIndustrial",
            "Cooking"      => "MealFine",
            "Animals"      => "Kibble",
            "Shooting"     => "Shell_LowVelocity",
            "Mining"       => "Plasteel",
            "Plants"       => "RawBerries",
            _              => "Silver"
        };
    }
}
