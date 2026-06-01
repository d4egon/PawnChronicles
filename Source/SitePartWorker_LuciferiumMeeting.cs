using System.Collections.Generic;
using Verse;
using Verse.Grammar;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;

namespace PawnChronicles
{
    /// <summary>
    /// Generates the PC_Cartel_Broker during quest gen and stores them in
    /// part.things (so the GenStep can spawn them at map-centre) and in the
    /// quest slate as $broker (so QuestNode_LuciferiumExpeditionSetupBroker
    /// can wire QuestPart_BegForItems).
    /// Pattern mirrors SitePartWorker_PrisonerWillingToJoin.
    /// </summary>
    public class SitePartWorker_LuciferiumMeeting : SitePartWorker
    {
        public override void Notify_GeneratedByQuestGen(
            SitePart part,
            Slate slate,
            List<Rule> outExtraDescriptionRules,
            Dictionary<string, string> outExtraDescriptionConstants)
        {
            base.Notify_GeneratedByQuestGen(part, slate, outExtraDescriptionRules, outExtraDescriptionConstants);

            var brokerKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("PC_Cartel_Broker");
            // Use the site's faction (set by QuestNode_Root_Site to PC_Faction_LucifersCartel)
            Faction faction = part.site?.Faction;

            if (brokerKind == null || faction == null)
            {
                Log.Warning("[PawnChronicles] SitePartWorker_LuciferiumMeeting: missing PawnKindDef or faction.");
                return;
            }

            Pawn broker = PawnGenerator.GeneratePawn(brokerKind, faction);

            part.things = new ThingOwner<Pawn>(part, oneStackOnly: true);
            part.things.TryAdd(broker);

            // Store in slate so QuestNode_LuciferiumExpeditionSetupBroker can reference the broker
            slate.Set("broker", broker);
        }

        public override string GetPostProcessedThreatLabel(Site site, SitePart sitePart)
        {
            string label = base.GetPostProcessedThreatLabel(site, sitePart);
            if (sitePart.things != null && sitePart.things.Count > 0)
                label = $"{label}: {sitePart.things[0].LabelShortCap}";
            return label;
        }
    }
}
