using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    // =========================================================================
    //  PC_WorldEventResponder
    //
    //  Watches for specific world events and applies narrative hediffs to
    //  colonists in the right place at the right time:
    //
    //    AlienMark       - when an ancient cryptosleep casket opens
    //    CelestialSickness - ship chunk impacts, solar flares, or eclipse
    //
    //  Harmony hooks call the static Apply* methods directly.
    //  The Eclipse check runs here as a GameComponent tick poll because
    //  Eclipse is a long-duration GameCondition rather than a single event.
    // =========================================================================

    public class PC_WorldEventResponder : GameComponent
    {
        private const int TickInterval   = 2053;   // ~34 seconds, prime to avoid sync
        private const float EclipseMtbDays = 12f;  // per-colonist MTB while eclipse is active

        public PC_WorldEventResponder(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % TickInterval != 0) return;
            if (Current.ProgramState != ProgramState.Playing) return;

            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;

                // Eclipse active on this map? Roll per colonist.
                if (map.gameConditionManager.ConditionIsActive(GameConditionDefOf.Eclipse))
                {
                    foreach (var colonist in map.mapPawns.FreeColonists.ToList())
                    {
                        if (!colonist.Spawned || colonist.Dead) continue;
                        if (Rand.MTBEventOccurs(EclipseMtbDays, 60000f, TickInterval))
                            ApplyCelestialSickness(colonist, "eclipse");
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ANCIENT DANGER - AlienMark
        // Called from Harmony postfix on Building_AncientCryptosleepCasket.EjectContents
        // ─────────────────────────────────────────────────────────────────────

        public static void OnAncientCasketOpened(IntVec3 pos, Map map)
        {
            if (map == null) return;

            var def = DefDatabase<HediffDef>.GetNamedSilentFail("PC_Hediff_AlienMark");
            if (def == null) return;

            const float radius  = 16f;
            const float chance  = 0.30f;

            foreach (var colonist in map.mapPawns.FreeColonists.ToList())
            {
                if (!colonist.Spawned || colonist.Dead) continue;
                if (colonist.Position.DistanceTo(pos) > radius) continue;
                if (!Rand.Chance(chance)) continue;
                if (colonist.health.hediffSet.HasHediff(def)) continue; // already marked

                var hediff = HediffMaker.MakeHediff(def, colonist);
                hediff.Severity = 0.4f;
                colonist.health.AddHediff(hediff);

                Messages.Message(
                    $"Something that was sleeping took notice of {colonist.LabelShort}.",
                    colonist, MessageTypeDefOf.NeutralEvent, false);

                Log.Message($"[PawnChronicles] AlienMark applied to {colonist.LabelShort} (ancient danger opened)");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SHIP CHUNK DROP - CelestialSickness
        // Called from Harmony postfix on IncidentWorker_ShipChunkDrop.TryExecuteWorker
        // ─────────────────────────────────────────────────────────────────────

        public static void OnShipChunksLanded(Map map)
        {
            if (map == null) return;

            const float chance = 0.20f;

            foreach (var colonist in map.mapPawns.FreeColonists.ToList())
            {
                if (!colonist.Spawned || colonist.Dead) continue;
                if (!Rand.Chance(chance)) continue;
                ApplyCelestialSickness(colonist, "ship chunk impact");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SOLAR FLARE - CelestialSickness
        // Called from Harmony postfix on IncidentWorker_MakeGameCondition.TryExecuteWorker
        // ─────────────────────────────────────────────────────────────────────

        public static void OnSolarFlare(Map map)
        {
            if (map == null) return;

            const float chance = 0.15f;

            foreach (var colonist in map.mapPawns.FreeColonists.ToList())
            {
                if (!colonist.Spawned || colonist.Dead) continue;
                if (!Rand.Chance(chance)) continue;
                ApplyCelestialSickness(colonist, "solar flare");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SHARED HELPER
        // ─────────────────────────────────────────────────────────────────────

        private static void ApplyCelestialSickness(Pawn pawn, string source)
        {
            var def = DefDatabase<HediffDef>.GetNamedSilentFail("PC_Hediff_CelestialSickness");
            if (def == null) return;

            // Don't stack - just bump severity if already present
            var existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null)
            {
                existing.Severity = Mathf.Min(existing.Severity + 0.15f, 0.8f);
                return;
            }

            var hediff = HediffMaker.MakeHediff(def, pawn);
            hediff.Severity = 0.25f;
            pawn.health.AddHediff(hediff);

            Messages.Message(
                $"{pawn.LabelShort} was exposed to something celestial during the {source}. They look unwell.",
                pawn, MessageTypeDefOf.NegativeEvent, false);

            Log.Message($"[PawnChronicles] CelestialSickness applied to {pawn.LabelShort} (source: {source})");
        }
    }
}
