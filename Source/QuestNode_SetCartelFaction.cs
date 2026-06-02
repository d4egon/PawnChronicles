using Verse;
using RimWorld;
using RimWorld.QuestGen;

namespace PawnChronicles
{
    /// <summary>
    /// Sets $faction in the quest slate to PC_Faction_LucifersCartel,
    /// generating the faction first if it doesn't exist in the world.
    /// Must run before QuestNode_Root_Site so the site is assigned the
    /// correct faction from creation - avoids mid-quest SetFaction side effects.
    /// </summary>
    public class QuestNode_SetCartelFaction : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var faction = LuciferiumArcManager.EnsureCartelFactionExists();
            if (faction == null)
            {
                Log.Error("[PawnChronicles] QuestNode_SetCartelFaction: could not resolve cartel faction.");
                return;
            }
            QuestGen.slate.Set("faction", faction);
        }
    }
}
