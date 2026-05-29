using System;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Fires narrative-driven world events tied to arc stages.
    ///
    /// Flow:
    ///   1. Resolve bridge grammar -> personalized "why this is happening" text
    ///   2. Send ONE combined letter: [narrative body] + separator + [bridge explanation]
    ///      with suppressed vanilla incident letter (sendLetter = false)
    ///   3. Fire the actual RimWorld incident
    ///
    /// The result: the player reads Rego's story, then reads why a raid is coming,
    /// then the raiders walk in. The fiction and the gameplay are the same event.
    /// </summary>
    public static class NarrativeIncidentFirer
    {
        private const string Separator = "\n\n──────────────────────────────────────\n\n";

        // ─────────────────────────────────────────────────────────────────────
        // ENTRY POINT
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fire a narrative incident for a pawn at the given stage.
        /// </summary>
        /// <param name="narrativeBody">Stage body text already generated - becomes the first half of the letter.</param>
        public static void Fire(
            Pawn              pawn,
            NarrativeIncident incident,
            string            narrativeBody,
            PawnNarrativeProfile profile)
        {
            if (incident == null) return;
            if (!Rand.Chance(incident.chance)) return;
            if (!pawn.Spawned || pawn.Map == null) return;

            // Grace period: don't fire hostile or mental-break incidents in the first 3 days.
            // Prevents scars/trauma arcs from immediately cratering a fresh colony.
            // Climax incidents are earned and always bypass this guard.
            const int GracePeriodTicks = 60000 * 3;
            if (!incident.bypassGracePeriod && Find.TickManager.TicksGame < GracePeriodTicks)
            {
                switch (incident.type)
                {
                    case NarrativeIncidentType.MentalBreak:
                    case NarrativeIncidentType.HostileArrives:
                    case NarrativeIncidentType.SmallRaid:
                        return;
                }
            }

            try
            {
                // ── Resolve the bridge (personalized "why") ──
                string bridgeTitle = ResolveBridge(pawn, profile, incident.bridgeRole, "title");
                string bridgeBody  = ResolveBridge(pawn, profile, incident.bridgeRole, "body");

                // ── Build combined letter ──
                string letterLabel = bridgeTitle.NullOrEmpty()
                    ? pawn.LabelShort
                    : $"{pawn.LabelShort} - {bridgeTitle}";

                string letterText = narrativeBody.NullOrEmpty()
                    ? bridgeBody
                    : narrativeBody + Separator + bridgeBody;

                LetterDef letterType = incident.type is NarrativeIncidentType.HostileArrives
                                                     or NarrativeIncidentType.SmallRaid
                    ? LetterDefOf.ThreatSmall
                    : LetterDefOf.NeutralEvent;

                // ── Transparency mode routing ──
                var mode = PawnChroniclesSettings.Current.transparencyMode;
                if (mode == NarrativeTransparencyMode.Suggestive)
                {
                    // Fire a short ambient hint now - hold the explanation
                    string hint = ResolveHint(pawn, profile, incident.bridgeRole);
                    if (!hint.NullOrEmpty())
                        Messages.Message(hint, pawn, MessageTypeDefOf.NeutralEvent, false);

                    // Queue the full revelation letter for later
                    var settings = PawnChroniclesSettings.Current;
                    int delayTicks = (int)(Rand.Range(
                        settings.revelationDelayDaysMin,
                        settings.revelationDelayDaysMax) * 60000f);

                    PC_LetterQueue.Instance?.Enqueue(pawn, letterLabel, letterText, letterType, delayTicks);
                }
                else
                {
                    // Explicit mode: letter fires with the incident
                    Find.LetterStack.ReceiveLetter(letterLabel, letterText, letterType, pawn);
                }

                Log.Message($"[PawnChronicles] NarrativeIncident firing: {incident.type} for {pawn.LabelShort} (bridge: {incident.bridgeRole}, mode: {mode})");

                // ── Fire world event ──
                switch (incident.type)
                {
                    case NarrativeIncidentType.MentalBreak:     FireMentalBreak(pawn, incident);     break;
                    case NarrativeIncidentType.HostileArrives:  FireHostile(pawn, incident);         break;
                    case NarrativeIncidentType.FriendlyArrives: FireFriendly(pawn);                  break;
                    case NarrativeIncidentType.TraderArrives:   FireTrader(pawn);                    break;
                    case NarrativeIncidentType.SmallRaid:       FireSmallRaid(pawn, incident);       break;
                    case NarrativeIncidentType.HediffApply:     FireHediffApply(pawn, incident);     break;
                    case NarrativeIncidentType.HediffRemove:    FireHediffRemove(pawn, incident);    break;
                    case NarrativeIncidentType.ThoughtApply:    FireThoughtApply(pawn, incident);    break;
                    case NarrativeIncidentType.SkillChange:     FireSkillChange(pawn, incident);     break;
                    case NarrativeIncidentType.InspirationGrant:FireInspirationGrant(pawn, incident);break;
                    case NarrativeIncidentType.ItemSpawn:      FireItemSpawn(pawn, incident);   break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[PawnChronicles] NarrativeIncidentFirer failed for {pawn.LabelShort}: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // GRAMMAR BRIDGE
        // ─────────────────────────────────────────────────────────────────────

        private static string ResolveBridge(Pawn pawn, PawnNarrativeProfile profile, string role, string type)
        {
            if (string.IsNullOrEmpty(role)) return "";
            string grammarRole = $"bridge_{role}";
            return type == "title"
                ? NarrativeGrammarResolver.ResolveTitle(pawn, profile, grammarRole)
                : NarrativeGrammarResolver.ResolveBody(pawn, profile, grammarRole);
        }


        // ─────────────────────────────────────────────────────────────────────
        // HINT RESOLUTION (Suggestive mode)
        // Returns a short ambient observation - one line, no explanation.
        // Falls back to a generic notice if no specific hint is defined.
        // ─────────────────────────────────────────────────────────────────────

        private static string ResolveHint(Pawn pawn, PawnNarrativeProfile profile, string role)
        {
            if (!string.IsNullOrEmpty(role))
            {
                string specific = NarrativeGrammarResolver.ResolveBody(pawn, profile, $"hint_{role}");
                // Only use if it actually resolved (doesn't contain the key itself)
                if (!specific.NullOrEmpty() && !specific.Contains($"hint_{role}"))
                    return specific;
            }

            // Generic fallback hints - short, dry, observational
            string[] fallbacks = new[]
            {
                $"{pawn.LabelShort} seems off today.",
                $"{pawn.LabelShort} didn't say much at dinner.",
                $"Something's been on {pawn.LabelShort}'s mind.",
                $"{pawn.LabelShort} checked the perimeter before sleeping.",
                $"{pawn.LabelShort} was quieter than usual.",
                $"{pawn.LabelShort} kept looking at the door.",
                $"{pawn.LabelShort} hasn't been sleeping well.",
            };
            return fallbacks.RandomElement();
        }

        // ─────────────────────────────────────────────────────────────────────
        // MENTAL BREAK
        // Trauma, grief, and loss arcs made physical.
        // ─────────────────────────────────────────────────────────────────────

        private static void FireMentalBreak(Pawn pawn, NarrativeIncident incident)
        {
            MentalStateDef def = null;
            if (!string.IsNullOrEmpty(incident.mentalStateDef))
                def = DefDatabase<MentalStateDef>.GetNamedSilentFail(incident.mentalStateDef);
            def ??= DefDatabase<MentalStateDef>.GetNamedSilentFail("Wander_Sad");
            if (def == null) return;

            pawn.mindState?.mentalStateHandler?.TryStartMentalState(def, forced: true);
            Log.Message($"[PawnChronicles] Mental break fired: {def.defName} on {pawn.LabelShort}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // HOSTILE ARRIVAL
        // Someone from the pawn's past walks onto the map.
        // Low point raid = 1–2 pawns max. The letter explains who and why.
        // ─────────────────────────────────────────────────────────────────────

        private static void FireHostile(Pawn pawn, NarrativeIncident incident)
        {
            var def = IncidentDefOf.RaidEnemy;

            Faction faction = BestHostileFaction(pawn);

            var parms = new IncidentParms
            {
                target     = pawn.Map,
                forced     = true,
                points     = Mathf.Max(incident.incidentPoints, 50f),
                sendLetter = false,   // our bridge letter IS the notification
                faction    = faction
            };

            bool success = def.Worker.TryExecute(parms);
            Log.Message($"[PawnChronicles] HostileArrives fired (points={parms.points}, faction={faction?.Name ?? "null"}): {success}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // FRIENDLY ARRIVAL
        // The connection the pawn was looking for walks in.
        // Uses WandererJoin - a new colonist who feels narratively motivated.
        // ─────────────────────────────────────────────────────────────────────

        private static void FireFriendly(Pawn pawn)
        {
            var def = IncidentDefOf.WandererJoin;

            var parms = new IncidentParms
            {
                target     = pawn.Map,
                forced     = true,
                sendLetter = false
            };

            bool success = def.Worker.TryExecute(parms);
            Log.Message($"[PawnChronicles] FriendlyArrives (WandererJoin) fired: {success}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // TRADER ARRIVAL
        // The materials, tools, or connections the pawn needed show up.
        // ─────────────────────────────────────────────────────────────────────

        private static void FireTrader(Pawn pawn)
        {
            var def = IncidentDefOf.TraderCaravanArrival;

            var parms = new IncidentParms
            {
                target     = pawn.Map,
                forced     = true,
                sendLetter = false
            };

            bool success = def.Worker.TryExecute(parms);
            Log.Message($"[PawnChronicles] TraderArrives (TraderCaravanArrival) fired: {success}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // SMALL RAID
        // The narrative consequence arrives in force. Not overwhelming - targeted.
        // ─────────────────────────────────────────────────────────────────────

        private static void FireSmallRaid(Pawn pawn, NarrativeIncident incident)
        {
            var def = IncidentDefOf.RaidEnemy;

            Faction faction = BestHostileFaction(pawn);

            var parms = new IncidentParms
            {
                target     = pawn.Map,
                forced     = true,
                points     = Mathf.Max(incident.incidentPoints, 100f),
                sendLetter = false,
                faction    = faction
            };

            bool success = def.Worker.TryExecute(parms);
            Log.Message($"[PawnChronicles] SmallRaid fired (points={parms.points}, faction={faction?.Name ?? "null"}): {success}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // ITEM SPAWN
        // An object appears that means something. The letter explained what.
        // ─────────────────────────────────────────────────────────────────────

        private static void FireItemSpawn(Pawn pawn, NarrativeIncident incident)
        {
            if (string.IsNullOrEmpty(incident.thingDef)) return;

            var def = DefDatabase<ThingDef>.GetNamedSilentFail(incident.thingDef);
            if (def == null)
            {
                Log.Warning($"[PawnChronicles] ItemSpawn: ThingDef '{incident.thingDef}' not found.");
                return;
            }

            ThingDef stuff = null;
            if (!string.IsNullOrEmpty(incident.stuffDef))
                stuff = DefDatabase<ThingDef>.GetNamedSilentFail(incident.stuffDef);
            if (stuff == null && def.MadeFromStuff)
                stuff = GenStuff.DefaultStuffFor(def);

            var thing = ThingMaker.MakeThing(def, stuff);
            thing.stackCount = Mathf.Clamp(incident.count, 1, def.stackLimit);

            if (thing.TryGetComp<CompQuality>() is CompQuality qc)
                qc.SetQuality(incident.quality, ArtGenerationContext.Colony);

            bool placed = GenPlace.TryPlaceThing(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            Log.Message($"[PawnChronicles] ItemSpawn: {def.defName} x{incident.count} placed={placed} near {pawn.LabelShort}");
        }

        // ---------------------------------------------------------------------
        // PAWN-LEVEL DIRECT EFFECTS
        // Smaller than world events -- the arc reaches into the body and mind.
        // ---------------------------------------------------------------------

        private static void FireHediffApply(Pawn pawn, NarrativeIncident incident)
        {
            if (string.IsNullOrEmpty(incident.hediffDef)) return;

            var def = DefDatabase<HediffDef>.GetNamedSilentFail(incident.hediffDef);
            if (def == null)
            {
                Log.Warning($"[PawnChronicles] HediffApply: HediffDef '{incident.hediffDef}' not found.");
                return;
            }

            // Don't double-apply permanent hediffs
            if (pawn.health.hediffSet.HasHediff(def) && def.isBad == false) return;

            var hediff = HediffMaker.MakeHediff(def, pawn);
            if (incident.hediffSeverity > 0f)
                hediff.Severity = incident.hediffSeverity;

            pawn.health.AddHediff(hediff);
            Log.Message($"[PawnChronicles] HediffApply: {def.defName} (sev={hediff.Severity:F2}) on {pawn.LabelShort}");
        }

        private static void FireHediffRemove(Pawn pawn, NarrativeIncident incident)
        {
            if (string.IsNullOrEmpty(incident.hediffDef)) return;

            var def = DefDatabase<HediffDef>.GetNamedSilentFail(incident.hediffDef);
            if (def == null) return;

            var existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing == null) return;

            pawn.health.RemoveHediff(existing);
            Log.Message($"[PawnChronicles] HediffRemove: {def.defName} removed from {pawn.LabelShort}");
        }

        private static void FireThoughtApply(Pawn pawn, NarrativeIncident incident)
        {
            if (string.IsNullOrEmpty(incident.thoughtDef)) return;

            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(incident.thoughtDef);
            if (def == null)
            {
                Log.Warning($"[PawnChronicles] ThoughtApply: ThoughtDef '{incident.thoughtDef}' not found.");
                return;
            }

            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(def);
            Log.Message($"[PawnChronicles] ThoughtApply: {def.defName} on {pawn.LabelShort}");
        }

        private static void FireSkillChange(Pawn pawn, NarrativeIncident incident)
        {
            if (string.IsNullOrEmpty(incident.skillDef)) return;

            var def = DefDatabase<SkillDef>.GetNamedSilentFail(incident.skillDef);
            if (def == null)
            {
                Log.Warning($"[PawnChronicles] SkillChange: SkillDef '{incident.skillDef}' not found.");
                return;
            }

            var skill = pawn.skills?.GetSkill(def);
            if (skill == null) return;

            // xpChange: positive = grant XP, negative = drain passion (we just grant XP)
            float xp = incident.skillXp != 0 ? incident.skillXp : 500f;
            skill.Learn(xp, direct: true);
            Log.Message($"[PawnChronicles] SkillChange: {def.defName} +{xp} XP on {pawn.LabelShort}");
        }

        private static void FireInspirationGrant(Pawn pawn, NarrativeIncident incident)
        {
            if (pawn.mindState == null) return;

            InspirationDef def = null;
            if (!string.IsNullOrEmpty(incident.inspirationDef))
                def = DefDatabase<InspirationDef>.GetNamedSilentFail(incident.inspirationDef);

            // Fall back to a random valid inspiration if none specified
            if (def == null)
                def = DefDatabase<InspirationDef>.AllDefsListForReading
                    .Where(d => d.Worker != null && d.Worker.InspirationCanOccur(pawn))
                    .RandomElementWithFallback();

            if (def == null) return;

            pawn.mindState.inspirationHandler?.TryStartInspiration(def);
            Log.Message($"[PawnChronicles] InspirationGrant: {def.defName} on {pawn.LabelShort}");
        }

        // ---------------------------------------------------------------------
        // UTILITY
        // ---------------------------------------------------------------------

        private static Faction BestHostileFaction(Pawn pawn)
        {
            // Prefer a faction that is already hostile to the player
            var hostile = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.defeated && f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction)
                .ToList();

            if (hostile.Count > 0)
                return hostile.RandomElement();

            // Fall back to any non-player humanlike faction
            return Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.defeated && f.def.humanlikeFaction)
                .RandomElementWithFallback();
        }
    }
}
