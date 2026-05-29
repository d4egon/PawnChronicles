using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    // =========================================================================
    //  PC_LetterQueue - GameComponent
    //
    //  Holds narrative letters that have been delayed (Suggestive mode).
    //  The incident fires immediately; the explanation arrives days later.
    //  Saved to disk so letters survive a save/load mid-delay.
    // =========================================================================

    public class PC_LetterQueue : GameComponent
    {
        private static PC_LetterQueue? _instance;
        public  static PC_LetterQueue? Instance => _instance;

        private List<PendingNarrativeLetter> _pending = new List<PendingNarrativeLetter>();

        public PC_LetterQueue(Game game) { _instance = this; }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Queue a letter to arrive after delayTicks.</summary>
        public void Enqueue(Pawn pawn, string label, string text, LetterDef letterDef, int delayTicks)
        {
            _pending.Add(new PendingNarrativeLetter
            {
                pawnId       = pawn.thingIDNumber,
                label        = label,
                text         = text,
                letterDefName = letterDef.defName,
                fireAtTick   = Find.TickManager.TicksGame + delayTicks,
            });
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        public override void GameComponentTick()
        {
            if (_pending.Count == 0) return;
            if (Find.TickManager.TicksGame % 500 != 0) return; // check every ~8s

            int now = Find.TickManager.TicksGame;
            var ready = _pending.Where(l => now >= l.fireAtTick).ToList();

            foreach (var pending in ready)
            {
                _pending.Remove(pending);

                var letterDef = DefDatabase<LetterDef>.GetNamedSilentFail(pending.letterDefName)
                                ?? LetterDefOf.NeutralEvent;

                // Try to find the pawn for a jump-to target
                Pawn? pawn = null;
                foreach (var map in Find.Maps)
                {
                    pawn = map.mapPawns.AllPawns
                        .FirstOrDefault(p => p.thingIDNumber == pending.pawnId);
                    if (pawn != null) break;
                }

                if (pawn != null)
                    Find.LetterStack.ReceiveLetter(pending.label, pending.text, letterDef, pawn);
                else
                    Find.LetterStack.ReceiveLetter(pending.label, pending.text, letterDef);
            }
        }

        // ── Save / Load ───────────────────────────────────────────────────────

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref _pending, "pc_pendingLetters", LookMode.Deep);
            _pending ??= new List<PendingNarrativeLetter>();
        }
    }

    // ── Data class ────────────────────────────────────────────────────────────

    public class PendingNarrativeLetter : IExposable
    {
        public int    pawnId;
        public string label        = "";
        public string text         = "";
        public string letterDefName = "NeutralEvent";
        public int    fireAtTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId,        "pawnId");
            Scribe_Values.Look(ref label,         "label",         "");
            Scribe_Values.Look(ref text,          "text",          "");
            Scribe_Values.Look(ref letterDefName, "letterDefName", "NeutralEvent");
            Scribe_Values.Look(ref fireAtTick,    "fireAtTick");
        }
    }
}
