using System.Collections.Generic;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Snapshots goodwill for all non-hostile factions when inSignalSnapshot fires
    /// (i.e. when the caravan enters the site map). When inSignalRestore fires
    /// (quest success, fail, or timeout), any faction whose goodwill dropped below
    /// their snapshot value is silently restored to that value.
    ///
    /// This prevents vanilla proximity/territory penalties from permanently
    /// souring factions that had nothing to do with the encounter.
    /// </summary>
    public class QuestPart_RestoreGoodwillOnComplete : QuestPart
    {
        public string inSignalSnapshot;
        public string inSignalRestore;

        // defName -> goodwill at snapshot time
        private Dictionary<string, int> _snapshot = new Dictionary<string, int>();
        private bool _snapshotTaken = false;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);

            if (signal.tag == inSignalSnapshot && !_snapshotTaken)
            {
                TakeSnapshot();
            }
            else if (signal.tag == inSignalRestore && _snapshotTaken)
            {
                RestoreGoodwill();
            }
        }

        private void TakeSnapshot()
        {
            _snapshot.Clear();
            foreach (var faction in Find.FactionManager.AllFactions)
            {
                if (faction.IsPlayer || faction.defeated || !faction.HasGoodwill) continue;
                if (faction.HostileTo(Faction.OfPlayer)) continue;
                _snapshot[faction.def.defName + "_" + faction.loadID] = Faction.OfPlayer.GoodwillWith(faction);
            }
            _snapshotTaken = true;
            Log.Message($"[PawnChronicles] QuestPart_RestoreGoodwillOnComplete: snapshot taken for {_snapshot.Count} factions.");
        }

        private void RestoreGoodwill()
        {
            foreach (var faction in Find.FactionManager.AllFactions)
            {
                if (faction.IsPlayer || faction.defeated || !faction.HasGoodwill) continue;
                string key = faction.def.defName + "_" + faction.loadID;
                if (!_snapshot.TryGetValue(key, out int savedGoodwill)) continue;

                int current = Faction.OfPlayer.GoodwillWith(faction);
                if (current < savedGoodwill)
                {
                    // Silently restore - no letter, no hostility message
                    Faction.OfPlayer.TryAffectGoodwillWith(faction, savedGoodwill - current,
                        canSendMessage: false, canSendHostilityLetter: false);
                    Log.Message($"[PawnChronicles] Restored goodwill with {faction.Name}: {current} -> {savedGoodwill}");
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignalSnapshot, "inSignalSnapshot");
            Scribe_Values.Look(ref inSignalRestore, "inSignalRestore");
            Scribe_Values.Look(ref _snapshotTaken, "snapshotTaken");
            Scribe_Collections.Look(ref _snapshot, "snapshot", LookMode.Value, LookMode.Value);
            _snapshot ??= new Dictionary<string, int>();
        }
    }
}
