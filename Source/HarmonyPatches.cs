using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace PawnChronicles
{
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            var harmony = new Harmony("com.pawnchronicles.mod");
            harmony.PatchAll();
            Log.Message("[PawnChronicles] Harmony patches applied successfully.");
        }
    }

    // =========================================================================
    // PATCH 1: Pawn generation (explicit overload to avoid ambiguity)
    // =========================================================================
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_PawnGenerator_GeneratePawn
    {
        public static void Postfix(Pawn __result)
        {
            if (__result == null || __result.story == null || !__result.RaceProps.Humanlike) return;
            if (Current.ProgramState != ProgramState.Playing) return;
            if (Find.WorldPawns?.Contains(__result) == true) return;

            var comp = __result.GetComp<CompPersonalChronicles>();
            if (comp == null || comp.hasActiveEpic) return;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (__result.IsFreeColonist)
                    comp.EvaluateAndStartEpic();
            });
        }
    }

    // =========================================================================
    // PATCH 2: Quest signal routing
    // =========================================================================
    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    public static class Patch_Quest_End
    {
        public static void Postfix(Quest __instance, QuestEndOutcome outcome)
        {
            if (__instance == null) return;

            foreach (var map in Find.Maps)
            {
                foreach (var colonist in map.mapPawns.FreeColonists)
                {
                    var comp = colonist.GetComp<CompPersonalChronicles>();
                    if (comp == null) continue;

                    if (comp.hasActiveEpic && comp.activeQuestId == __instance.id)
                    {
                        bool isClimax = comp.currentStage >= (comp.currentEpic?.stageCount - 1 ?? 0);

                        comp.PendingSignal = outcome == QuestEndOutcome.Success
                            ? (isClimax ? "PawnEpic_Success" : "PawnEpic_StageComplete")
                            : "PawnEpic_Failure";

                        comp.activeQuestId = -1;
                        return;
                    }
                }
            }
        }
    }

    // =========================================================================
    // PATCH 3: Pawns joining mid-game
    // =========================================================================
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    public static class Patch_Pawn_SetFaction
    {
        public static void Postfix(Pawn __instance, Faction newFaction)
        {
            if (__instance == null || newFaction != Faction.OfPlayer || !__instance.RaceProps.Humanlike) return;
            if (__instance.story == null) return;

            var comp = __instance.GetComp<CompPersonalChronicles>();
            if (comp == null || comp.hasActiveEpic) return;

            comp.EvaluateAndStartEpic();
        }
    }

    // =========================================================================
    // PATCH 4: Re-evaluate on game load (FIXED - safe iteration)
    // =========================================================================
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class Patch_Game_FinalizeInit
    {
        public static void Postfix()
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                if (Find.Maps == null) return;

                // Use ToList() to create a snapshot - prevents "Collection was modified" error
                foreach (var map in Find.Maps.ToList())
                {
                    foreach (var pawn in map.mapPawns.FreeColonists.ToList())
                    {
                        var comp = pawn.GetComp<CompPersonalChronicles>();
                        if (comp == null || comp.hasActiveEpic) continue;

                        comp.EvaluateAndStartEpic();
                    }
                }
            }, "PawnChronicles_EvaluateOnLoad", false, null);
        }
    }

    // =========================================================================
    // PATCH 5: Job success -> Sparks / Embers
    //
    // By the time the Postfix runs, pawn.CurJob is already the NEXT job (or null)
    // so we can't read the workType that just finished.
    // Prefix captures it while the job is still live; Postfix reads the cached value.
    // Safe: RimWorld ticks are single-threaded.
    // =========================================================================
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_JobSuccess_TriggerChronicles
    {
        private static readonly FieldInfo _pawnField =
            AccessTools.Field(typeof(Pawn_JobTracker), "pawn");

        private static string _capturedWorkType = "";

        public static void Prefix(Pawn_JobTracker __instance)
        {
            try
            {
                Pawn pawn = (Pawn)_pawnField.GetValue(__instance);
                _capturedWorkType = pawn?.CurJob?.workGiverDef?.workType?.defName ?? "";
            }
            catch { _capturedWorkType = ""; }
        }

        public static void Postfix(Pawn_JobTracker __instance, JobCondition condition)
        {
            if (condition != JobCondition.Succeeded) return;

            Pawn pawn = (Pawn)_pawnField.GetValue(__instance);
            if (pawn == null || !pawn.IsFreeColonist || !pawn.Spawned || pawn.Dead)
                return;

            var comp = pawn.GetComp<CompPersonalChronicles>();
            if (comp == null) return;

            var profile = pawn.GetNarrativeProfile();

            // Use workType captured in Prefix - pawn.CurJob is a different job by now
            comp.TryTriggerFromCompletedJob(_capturedWorkType, profile);
        }
    }

    // =========================================================================
    // PATCH 6: Hediff added -> InvalidateProfile
    // Heavy health changes (scars, implants, new diseases) reshape who a pawn is.
    // =========================================================================
    // PATCH 15: Addiction gained -> TryStartAddictionArc
    // =========================================================================
    [HarmonyPatch(typeof(Hediff), nameof(Hediff.PostAdd))]
    public static class Patch_Hediff_PostAdd_InvalidateProfile
    {
        public static void Postfix(Hediff __instance)
        {
            try
            {
                var pawn = __instance.pawn;
                if (pawn == null || !pawn.RaceProps.Humanlike) return;

                // Only invalidate for significant hediffs (permanent, high severity, or addictions)
                bool significant = __instance.IsPermanent()
                    || __instance.def.IsAddiction
                    || __instance is Hediff_MissingPart
                    || __instance is Hediff_AddedPart
                    || __instance.Severity >= 0.5f;

                if (!significant) return;

                var comp = pawn.GetComp<CompPersonalChronicles>();
                comp?.InvalidateProfile();

                // Also invalidate any entangled partner
                EntangledArcManager.Instance?.GetActiveArcForPawn(pawn)?.InvalidateProfiles();

                // Patch 15: start an addiction arc when a new addiction hediff is added.
                if (__instance is Hediff_Addiction && pawn.IsFreeColonist
                    && comp != null && !comp.hasActiveEpic && !comp.chroniclesDisabled)
                {
                    LongEventHandler.ExecuteWhenFinished(() =>
                        comp.TryStartAddictionArc(__instance.def));
                }
            }
            catch { /* never crash the game over a narrative update */ }
        }
    }

    // =========================================================================
    // PATCH 7: Relation added -> InvalidateProfile (for both pawns)
    // New bonds, marriages, rivalries all shift the Kinship / Loss / Grief tags.
    // =========================================================================
    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation))]
    public static class Patch_RelationsTracker_AddDirectRelation_InvalidateProfile
    {
        public static void Postfix(Pawn_RelationsTracker __instance, Pawn otherPawn)
        {
            try
            {
                Pawn self = (Pawn)AccessTools.Field(typeof(Pawn_RelationsTracker), "pawn")
                    .GetValue(__instance);

                self?.GetComp<CompPersonalChronicles>()?.InvalidateProfile();
                otherPawn?.GetComp<CompPersonalChronicles>()?.InvalidateProfile();

                // Cascade to entangled arc profiles
                EntangledArcManager.Instance?.GetActiveArcForPawn(self)?.InvalidateProfiles();
                if (otherPawn != null)
                    EntangledArcManager.Instance?.GetActiveArcForPawn(otherPawn)?.InvalidateProfiles();
            }
            catch { }
        }
    }

    // =========================================================================
    // PATCH 8: Skill level up -> InvalidateProfile
    // A level-up can promote a skill from dormant to dominant, reshaping the arc.
    // =========================================================================
    [HarmonyPatch(typeof(Pawn_SkillTracker), nameof(Pawn_SkillTracker.Learn))]
    public static class Patch_SkillTracker_Learn_InvalidateProfile
    {
        private static readonly FieldInfo _pawnField =
            AccessTools.Field(typeof(Pawn_SkillTracker), "pawn");

        public static void Postfix(Pawn_SkillTracker __instance, float xp)
        {
            // Only fire on noticeable learning bursts (not every tiny tick)
            if (xp < 100f) return;

            try
            {
                Pawn pawn = (Pawn)_pawnField.GetValue(__instance);
                if (pawn == null || !pawn.IsFreeColonist) return;

                pawn.GetComp<CompPersonalChronicles>()?.InvalidateProfile();
            }
            catch { }
        }
    }

    // =========================================================================
    // PATCH 9: Pawn death -> resolve any active entangled arc
    // =========================================================================
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_ResolveEntangledArc
    {
        public static void Postfix(Pawn __instance)
        {
            try
            {
                if (__instance == null) return;
                var arc = EntangledArcManager.Instance?.GetActiveArcForPawn(__instance);
                if (arc != null)
                    Log.Message($"[PawnChronicles] {__instance.LabelShort} died with an active entangled arc. Resolving on next tick.");
            }
            catch { }
        }
    }

    // =========================================================================
    // PATCH 10: Ancient cryptosleep casket opens => AlienMark nearby colonists
    // =========================================================================
    [HarmonyPatch(typeof(Building_AncientCryptosleepCasket), nameof(Building_AncientCryptosleepCasket.EjectContents))]
    public static class Patch_AncientCasket_EjectContents
    {
        public static void Postfix(Building_AncientCryptosleepCasket __instance)
        {
            try
            {
                if (__instance?.Map == null) return;
                PC_WorldEventResponder.OnAncientCasketOpened(__instance.Position, __instance.Map);
            }
            catch (Exception ex)
            {
                Log.Warning("[PawnChronicles] Patch_AncientCasket_EjectContents: " + ex.Message);
            }
        }
    }

    // =========================================================================
    // PATCH 11: Ship chunk drop => CelestialSickness on colonists
    // =========================================================================
    [HarmonyPatch(typeof(IncidentWorker_ShipChunkDrop), "TryExecuteWorker")]
    public static class Patch_ShipChunkDrop_TryExecute
    {
        public static void Postfix(bool __result, IncidentParms parms)
        {
            try
            {
                if (!__result) return;
                if (parms.target is Map map)
                    PC_WorldEventResponder.OnShipChunksLanded(map);
            }
            catch (Exception ex)
            {
                Log.Warning("[PawnChronicles] Patch_ShipChunkDrop_TryExecute: " + ex.Message);
            }
        }
    }

    // =========================================================================
    // PATCH 12: Solar flare => CelestialSickness on colonists
    // =========================================================================
    [HarmonyPatch(typeof(IncidentWorker_MakeGameCondition), "TryExecuteWorker")]
    public static class Patch_MakeGameCondition_SolarFlare
    {
        public static void Postfix(bool __result, IncidentWorker __instance, IncidentParms parms)
        {
            try
            {
                if (!__result) return;
                if (__instance.def?.defName != "SolarFlare") return;
                if (parms.target is Map map)
                    PC_WorldEventResponder.OnSolarFlare(map);
            }
            catch (Exception ex)
            {
                Log.Warning("[PawnChronicles] Patch_MakeGameCondition_SolarFlare: " + ex.Message);
            }
        }
    }

    // =========================================================================
    // PATCH 10: Bio tab - arc outcome row, pixel-matched to vanilla backstory slots
    //
    // Vanilla draws each backstory slot as:
    //   x=0       "Adulthood" / "Childhood" label    (MiddleLeft, GameFont.Small)
    //   x=90f     title pill  (StackElementBackground bg, MiddleCenter text)
    //   hover     FullDescriptionFor tooltip
    //   row height 22f, gap 4f
    //
    // We add one more row below the last backstory slot in the same style,
    // with a subtle success/failure tint on the pill background and a tooltip
    // that shows the narrative body + skill delta summary.
    // =========================================================================
    [HarmonyPatch(typeof(CharacterCardUtility), "DoLeftSection")]
    public static class Patch_CharacterCard_DrawNarrativeEpithet
    {
        private const float RowH  = 22f;
        private const float RowGap = 4f;
        private const float LabelX = 0f;
        private const float PillX  = 90f;

        public static void Postfix(Rect leftRect, Pawn pawn)
        {
            try
            {
                if (pawn?.story == null) return;
                var comp = pawn.GetComp<CompPersonalChronicles>();
                if (comp?.NarrativeEpithet == null) return;

                // DoLeftSection already ended its own group; open a fresh one so
                // our coordinates are relative to leftRect (same as vanilla).
                GUI.BeginGroup(leftRect);

                int   slotCount = Enum.GetValues(typeof(BackstorySlot)).Length;
                float y         = slotCount * (RowH + RowGap); // 52f for the standard 2 slots

                // ── "Arc Outcome" label (identical style to "Adulthood" / "Childhood") ──
                Text.Font   = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(LabelX, y, leftRect.width, RowH),
                    "PC_ArcOutcome".Translate());
                Text.Anchor = TextAnchor.UpperLeft;

                // ── Title pill ──────────────────────────────────────────────────
                string title   = comp.NarrativeEpithet;
                float  pillW   = Text.CalcSize(title).x + 10f;
                Rect   pillRect = new Rect(PillX, y, pillW, RowH);

                // Vanilla: StackElementBackground (no tint).
                // We add a gentle tint so success/failure is readable at a glance.
                Color tint = comp.NarrativeEpithetSuccess
                    ? new Color(0.70f, 1.00f, 0.70f, 1f)   // soft green
                    : new Color(1.00f, 0.65f, 0.65f, 1f);  // soft red
                Color prev = GUI.color;
                GUI.color  = CharacterCardUtility.StackElementBackground * tint;
                GUI.DrawTexture(pillRect, BaseContent.WhiteTex);
                GUI.color  = prev;

                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(pillRect, title.Truncate(pillRect.width));
                Text.Anchor = TextAnchor.UpperLeft;

                // ── Hover highlight + tooltip (desc + skill stat lines) ──────────
                if (Mouse.IsOver(pillRect))
                {
                    Widgets.DrawHighlight(pillRect);
                    TooltipHandler.TipRegion(pillRect, BuildTooltip(pawn, comp));
                }

                GUI.EndGroup();
            }
            catch (Exception ex)
            {
                Log.Warning($"[PawnChronicles] Patch_CharacterCard_DrawNarrativeEpithet: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the hover tooltip: narrative body + one line per affected skill
        /// (formatted the same way BackstoryDef.FullDescriptionFor does it).
        /// </summary>
        private static string BuildTooltip(Pawn pawn, CompPersonalChronicles comp)
        {
            var sb = new System.Text.StringBuilder();

            // Narrative body
            if (!string.IsNullOrEmpty(comp.NarrativeEpithetDesc))
                sb.AppendLine(comp.NarrativeEpithetDesc);

            // Skill delta summary - re-derive from adulthood skillGains + outcome
            var adulthood = pawn.story?.Adulthood;
            if (adulthood?.skillGains != null && adulthood.skillGains.Count > 0)
            {
                sb.AppendLine();
                bool success = comp.NarrativeEpithetSuccess;
                foreach (var gain in adulthood.skillGains)
                {
                    if (gain.skill == null || gain.amount <= 0) continue;
                    // Mirror ApplyBackstorySkillDelta: +12000 xp/pt success, -10000 xp/pt failure
                    // Express as a signed level-equivalent (one level ≈ 1000 xp mid-game)
                    int delta = success ? gain.amount : -gain.amount / 4;
                    if (delta == 0) continue;
                    string sign = delta > 0 ? "+" : "";
                    sb.AppendLine($"{gain.skill.skillLabel.CapitalizeFirst()}:   {sign}{delta * 100} xp");
                }
            }

            return sb.ToString().TrimEndNewlines();
        }
    }

    // =========================================================================
    // PATCH 13: Track social interactions for the "social" wait condition.
    // Increments socialInteractionCount each time this colonist completes a
    // social interaction (either as initiator or recipient).
    // =========================================================================
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
    public static class Patch_InteractionsTracker_TrackSocialCount
    {
        private static readonly FieldInfo _pawnField =
            AccessTools.Field(typeof(Pawn_InteractionsTracker), "pawn");

        public static void Postfix(bool __result, Pawn_InteractionsTracker __instance, Pawn recipient)
        {
            try
            {
                if (!__result) return;

                // Credit the initiator
                Pawn initiator = (Pawn)_pawnField.GetValue(__instance);
                if (initiator != null && initiator.IsFreeColonist)
                {
                    var comp = initiator.GetComp<CompPersonalChronicles>();
                    if (comp != null) comp.socialInteractionCount++;
                }

                // Credit the recipient too - both sides participated
                if (recipient != null && recipient.IsFreeColonist)
                {
                    var comp = recipient.GetComp<CompPersonalChronicles>();
                    if (comp != null) comp.socialInteractionCount++;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[PawnChronicles] Patch_InteractionsTracker_TrackSocialCount: " + ex.Message);
            }
        }
    }

    // =========================================================================
    // PATCH 14: Track ritual participation for the "ritual"/"prayer" wait condition.
    // When a ritual concludes, every colonist that was part of the lord gets
    // their ritualParticipationCount incremented.
    // =========================================================================
    [HarmonyPatch(typeof(LordJob_Ritual), "Cleanup")]
    public static class Patch_LordJobRitual_TrackParticipation
    {
        public static void Postfix(LordJob_Ritual __instance)
        {
            try
            {
                if (!ModsConfig.IdeologyActive) return;
                var pawns = __instance.lord?.ownedPawns;
                if (pawns == null) return;
                foreach (Pawn pawn in pawns)
                {
                    if (!pawn.IsFreeColonist) continue;
                    var comp = pawn.GetComp<CompPersonalChronicles>();
                    if (comp == null) continue;
                    comp.ritualParticipationCount++;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[PawnChronicles] Patch_LordJobRitual_TrackParticipation: " + ex.Message);
            }
        }
    }
}
