using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Debug gizmos for PawnChronicles.
    /// Appear in the pawn's gizmo bar when selected in god mode.
    /// All gizmos are strictly dev-only - nothing fires in production.
    /// </summary>
    public static class PawnChroniclesDebugGizmos
    {
        public static IEnumerable<Gizmo> GetGizmos(Pawn pawn, CompPersonalChronicles comp)
        {
            // ─────────────────────────────────────────────────────────────────
            //  MOMENTS - spark / ember
            // ─────────────────────────────────────────────────────────────────

            yield return new Command_Action
            {
                defaultLabel = "PC: Force Spark",
                defaultDesc  = "Fire a spark on this pawn right now (grammar resolved).",
                action       = () => comp.Debug_ForceSpark(),
            };

            yield return new Command_Action
            {
                defaultLabel = "PC: Force Ember",
                defaultDesc  = "Fire an ember on this pawn right now (+mood, +skill XP).",
                action       = () => comp.Debug_ForceEmber(),
            };

            // ─────────────────────────────────────────────────────────────────
            //  ARC - start by MODUS tier (the main testing shortcut)
            // ─────────────────────────────────────────────────────────────────

            if (!comp.hasActiveEpic)
            {
                yield return new Command_Action
                {
                    defaultLabel = "PC: Start Arc by Tier",
                    defaultDesc  = "Pick a modus tier -> start a random matching epic immediately, "
                                 + "bypassing all tag-weight requirements.",
                    action       = () =>
                    {
                        var tiers = new[]
                        {
                            EpicModus.Kindle,
                            EpicModus.Flame,
                            EpicModus.Fire,
                            EpicModus.Inferno,
                            EpicModus.Hellfire,
                        };

                        var opts = tiers.Select(m => new FloatMenuOption(m.ToString(), () =>
                        {
                            // Hellfire is a meta-chain - it picks from Fire/Inferno epics.
                            if (m == EpicModus.Hellfire)
                            {
                                var chainPool = DefDatabase<PersonalEpicDef>.AllDefs
                                    .Where(e => (e.modus == EpicModus.Fire || e.modus == EpicModus.Inferno)
                                                && !e.stagePool.NullOrEmpty())
                                    .ToList();

                                if (chainPool.Count == 0)
                                {
                                    Messages.Message(
                                        "[PC DEV] No Fire/Inferno epics available to seed a Hellfire chain.",
                                        MessageTypeDefOf.RejectInput);
                                    return;
                                }

                                var firstEpic = chainPool.RandomElement();
                                var chain     = HellfireEvaluator.BeginChain(pawn, firstEpic);
                                comp.HellfireChain = chain;
                                comp.StartEpic(firstEpic);
                                Messages.Message(
                                    $"[PC DEV] {pawn.LabelShort}: HELLFIRE chain started - link 1/{chain.linkCount} [{firstEpic.defName}]",
                                    pawn, MessageTypeDefOf.SilentInput);
                                return;
                            }

                            var pool = DefDatabase<PersonalEpicDef>.AllDefs
                                .Where(e => e.modus == m && !e.stagePool.NullOrEmpty())
                                .ToList();

                            if (pool.Count == 0)
                            {
                                Messages.Message(
                                    $"[PC DEV] No epics defined for modus {m}.",
                                    MessageTypeDefOf.RejectInput);
                                return;
                            }

                            var epic = pool.RandomElement();
                            comp.StartEpic(epic);
                            Messages.Message(
                                $"[PC DEV] {pawn.LabelShort}: started [{m}] {epic.defName}",
                                pawn, MessageTypeDefOf.SilentInput);
                        })).ToList();

                        Find.WindowStack.Add(new FloatMenu(opts));
                    },
                };

                // ── Pick any specific epic by defName ─────────────────────────
                yield return new Command_Action
                {
                    defaultLabel = "PC: Start Specific Epic",
                    defaultDesc  = "Choose any PersonalEpicDef by name and start it immediately.",
                    action       = () =>
                    {
                        var opts = DefDatabase<PersonalEpicDef>.AllDefs
                            .Where(e => !e.stagePool.NullOrEmpty())
                            .OrderBy(e => (int)e.modus)
                            .ThenBy(e => e.defName)
                            .Select(e => new FloatMenuOption(
                                $"[{e.modus}]  {e.defName}",
                                () =>
                                {
                                    comp.StartEpic(e);
                                    Messages.Message(
                                        $"[PC DEV] {pawn.LabelShort}: started {e.defName}",
                                        pawn, MessageTypeDefOf.SilentInput);
                                }))
                            .ToList();

                        if (opts.Count == 0)
                        {
                            Messages.Message("[PC DEV] No PersonalEpicDefs found.",
                                MessageTypeDefOf.RejectInput);
                            return;
                        }

                        Find.WindowStack.Add(new FloatMenu(opts));
                    },
                };

                // ── Inflate tag weights so premise evaluation can find higher tiers ──
                yield return new Command_Action
                {
                    defaultLabel = "PC: Pump Tag Weights +50",
                    defaultDesc  = "Add +50 to every tag score in the pawn's profile, "
                                 + "then force-rebuild it. Lets standard arc evaluation "
                                 + "reach higher modus tiers without manually picking an epic.",
                    action       = () =>
                    {
                        var profile = comp.GetOrBuildProfile();
                        var tags    = profile.Scores.Keys.ToList();
                        foreach (var tag in tags)
                            profile.Scores[tag] = profile.Scores[tag] + 50f;

                        comp.InvalidateProfile();

                        // Immediately try to start an arc with the boosted profile
                        comp.Debug_ForceStartEpic();

                        var sb = new StringBuilder();
                        sb.AppendLine($"[PC DEV] Tag weights pumped for {pawn.LabelShort}:");
                        foreach (var kv in profile.Scores.OrderByDescending(x => x.Value).Take(8))
                            sb.AppendLine($"  {kv.Key.defName,-30} {kv.Value,6:F0}");
                        Log.Message(sb.ToString());
                        Messages.Message(
                            $"[PC DEV] Pumped {pawn.LabelShort}'s tags - see console.",
                            pawn, MessageTypeDefOf.SilentInput);
                    },
                };
            }

            // ─────────────────────────────────────────────────────────────────
            //  ARC - controls for an ACTIVE epic
            // ─────────────────────────────────────────────────────────────────

            if (comp.hasActiveEpic)
            {
                yield return new Command_Action
                {
                    defaultLabel = "PC: Advance Stage (dev)",
                    defaultDesc  = "Skip the wait condition and immediately advance to the next stage.",
                    action       = () => comp.PlayerAdvanceStage(devMode: true),
                };

                yield return new Command_Action
                {
                    defaultLabel = "PC: Complete - SUCCESS",
                    defaultDesc  = "Immediately resolve the active arc as success (applies backstory + outcome).",
                    action       = () =>
                    {
                        comp.CompleteEpic(success: true);
                        Messages.Message($"[PC DEV] {pawn.LabelShort}: arc -> SUCCESS",
                            pawn, MessageTypeDefOf.SilentInput);
                    },
                };

                yield return new Command_Action
                {
                    defaultLabel = "PC: Complete - FAILURE",
                    defaultDesc  = "Immediately resolve the active arc as failure (applies backstory + outcome).",
                    action       = () =>
                    {
                        comp.CompleteEpic(success: false);
                        Messages.Message($"[PC DEV] {pawn.LabelShort}: arc -> FAILURE",
                            pawn, MessageTypeDefOf.SilentInput);
                    },
                };

                yield return new Command_Action
                {
                    defaultLabel = "PC: Abandon Arc (no outcome)",
                    defaultDesc  = "Hard-cancel the active arc with no backstory change. "
                                 + "Use to reset a pawn for re-testing.",
                    action       = () =>
                    {
                        // Null out everything without calling CompleteEpic (which applies outcomes)
                        comp.hasActiveEpic          = false;
                        comp.currentEpic            = null;
                        comp.currentStage           = 0;
                        comp.currentProfile         = null;
                        comp.PendingSignal          = null;
                        comp.activeQuestId          = -1;
                        comp.usedStages.Clear();
                        comp.arcEntries.Clear();
                        Messages.Message($"[PC DEV] {pawn.LabelShort}: arc abandoned (no outcome applied)",
                            pawn, MessageTypeDefOf.SilentInput);
                    },
                };
            }

            // ─────────────────────────────────────────────────────────────────
            //  ENTANGLED ARC - shared arc between two pawns
            // ─────────────────────────────────────────────────────────────────

            bool inEntangledArc = comp.IsInEntangledArc;

            if (!inEntangledArc)
            {
                yield return new Command_Action
                {
                    defaultLabel = "PC: Start Entangled Arc",
                    defaultDesc  = "Pick a partner colonist and an EntangledArcDef - fires the shared arc immediately.",
                    action       = () =>
                    {
                        // Step 1: pick partner
                        var colonists = pawn.Map?.mapPawns.FreeColonists
                            .Where(p => p != pawn && !p.Dead)
                            .ToList();
                        if (colonists == null || colonists.Count == 0)
                        {
                            Messages.Message("[PC DEV] No other colonists available.",
                                MessageTypeDefOf.RejectInput);
                            return;
                        }

                        var partnerOpts = colonists.Select(partner => new FloatMenuOption(
                            partner.LabelShort,
                            () =>
                            {
                                // Step 2: pick arc def
                                var arcDefs = DefDatabase<EntangledArcDef>.AllDefs
                                    .Where(d => !d.stagePool.NullOrEmpty())
                                    .OrderBy(d => d.arcType.ToString())
                                    .ThenBy(d => d.defName)
                                    .ToList();

                                if (arcDefs.Count == 0)
                                {
                                    Messages.Message("[PC DEV] No EntangledArcDefs found.",
                                        MessageTypeDefOf.RejectInput);
                                    return;
                                }

                                var arcOpts = arcDefs.Select(arcDef => new FloatMenuOption(
                                    $"[{arcDef.arcType}]  {arcDef.defName}",
                                    () =>
                                    {
                                        var mgr = EntangledArcManager.Instance;
                                        if (mgr == null)
                                        {
                                            Messages.Message("[PC DEV] EntangledArcManager not found.",
                                                MessageTypeDefOf.RejectInput);
                                            return;
                                        }
                                        mgr.TryStartArc(pawn, partner, arcDef);
                                        Messages.Message(
                                            $"[PC DEV] Entangled arc started: {pawn.LabelShort} ↔ "
                                            + $"{partner.LabelShort}  [{arcDef.defName}]",
                                            pawn, MessageTypeDefOf.SilentInput);
                                    }))
                                    .ToList();

                                Find.WindowStack.Add(new FloatMenu(arcOpts));
                            }))
                            .ToList();

                        Find.WindowStack.Add(new FloatMenu(partnerOpts));
                    },
                };
            }
            else
            {
                // Controls for an active entangled arc
                yield return new Command_Action
                {
                    defaultLabel = "PC: Advance Entangled Stage",
                    defaultDesc  = "Skip the wait condition on the shared arc and advance to the next stage.",
                    action       = () =>
                    {
                        var arc = comp.GetEntangledArc();
                        EntangledArcManager.Instance?.PlayerAdvanceArc(arc!, devMode: true);
                    },
                };

                yield return new Command_Action
                {
                    defaultLabel = "PC: Complete Entangled - SUCCESS",
                    defaultDesc  = "Immediately resolve the shared arc as success for both pawns.",
                    action       = () =>
                    {
                        var arc = comp.GetEntangledArc();
                        if (arc != null)
                        {
                            EntangledArcManager.Instance?.ForceCompleteArc(arc, success: true);
                            Messages.Message("[PC DEV] Entangled arc -> SUCCESS",
                                pawn, MessageTypeDefOf.SilentInput);
                        }
                    },
                };

                yield return new Command_Action
                {
                    defaultLabel = "PC: Complete Entangled - FAILURE",
                    defaultDesc  = "Immediately resolve the shared arc as failure for both pawns.",
                    action       = () =>
                    {
                        var arc = comp.GetEntangledArc();
                        if (arc != null)
                        {
                            EntangledArcManager.Instance?.ForceCompleteArc(arc, success: false);
                            Messages.Message("[PC DEV] Entangled arc -> FAILURE",
                                pawn, MessageTypeDefOf.SilentInput);
                        }
                    },
                };
            }

            // ─────────────────────────────────────────────────────────────────
            //  DIAGNOSTICS
            // ─────────────────────────────────────────────────────────────────

            yield return new Command_Action
            {
                defaultLabel = "PC: Log Full Profile",
                defaultDesc  = "Dump all narrative tag scores + arc state to the dev console.",
                action       = () =>
                {
                    var profile = comp.GetOrBuildProfile();
                    var sb      = new StringBuilder();
                    sb.AppendLine($"=== PC Profile: {pawn.LabelShort} ===");
                    sb.AppendLine($"  Active epic : {(comp.hasActiveEpic ? comp.currentEpic?.defName ?? "???" : "none")}");
                    sb.AppendLine($"  Modus       : {(comp.hasActiveEpic ? comp.currentEpic?.modus.ToString() ?? "?" : "-")}");
                    sb.AppendLine($"  Stage       : {comp.currentStage}");
                    sb.AppendLine($"  Chronicle   : {comp.ChronicleLog.Count} entries");
                    sb.AppendLine($"  Entangled   : {comp.IsInEntangledArc}");
                    sb.AppendLine("  Tag scores (all):");
                    foreach (var kv in profile.Scores.OrderByDescending(x => x.Value))
                    {
                        bool active = kv.Value >= PawnNarrativeProfile.ActiveThreshold;
                        sb.AppendLine($"    {(active ? "▶" : " ")} {kv.Key.defName,-30} {kv.Value,7:F1}");
                    }
                    Log.Message(sb.ToString());
                    Messages.Message($"[PC] Profile logged for {pawn.LabelShort} - see console.",
                        pawn, MessageTypeDefOf.SilentInput);
                },
            };

            yield return new Command_Action
            {
                defaultLabel = "PC: Test Grammar",
                defaultDesc  = "Resolve opening/spark/ember grammar and log results.",
                action       = () =>
                {
                    var profile = comp.GetOrBuildProfile();
                    var sb      = new StringBuilder();
                    sb.AppendLine($"=== PC Grammar test: {pawn.LabelShort} ===");

                    foreach (var role in new[] { "spark", "ember", "opening", "middle", "success", "failure" })
                    {
                        string title = NarrativeGrammarResolver.ResolveTitle(pawn, profile, role);
                        string body  = NarrativeGrammarResolver.ResolveBody(pawn, profile, role);
                        sb.AppendLine($"  [{role}]");
                        sb.AppendLine($"    Title: {title}");
                        sb.AppendLine($"    Body:  {body}");
                    }

                    Log.Message(sb.ToString());
                    Messages.Message($"[PC] Grammar test logged for {pawn.LabelShort} - see console.",
                        pawn, MessageTypeDefOf.SilentInput);
                },
            };

            yield return new Command_Action
            {
                defaultLabel = "PC: Invalidate Profile",
                defaultDesc  = "Force the narrative profile to rebuild on next access.",
                action       = () =>
                {
                    comp.InvalidateProfile();
                    Messages.Message($"[PC DEV] Profile invalidated for {pawn.LabelShort}.",
                        pawn, MessageTypeDefOf.SilentInput);
                },
            };
        }
    }
}
