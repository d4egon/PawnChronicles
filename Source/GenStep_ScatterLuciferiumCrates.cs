using System.Collections.Generic;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Scatters AncientHermeticCrates filled with luciferium across the garrison map.
    /// Each crate holds 1-3 doses. Crates are placed at evenly distributed positions
    /// across the map, skipping cells that are already occupied or impassable.
    /// </summary>
    public class GenStep_ScatterLuciferiumCrates : GenStep
    {
        public int crateCount     = 20;
        public int minPerCrate    = 1;
        public int maxPerCrate    = 3;

        public override int SeedPart => 394857621;

        public override void Generate(Map map, GenStepParams parms)
        {
            var crateDef      = DefDatabase<ThingDef>.GetNamed("AncientHermeticCrate");
            var luciferiumDef = DefDatabase<ThingDef>.GetNamed("Luciferium");

            if (crateDef == null || luciferiumDef == null)
            {
                Log.Warning("[PawnChronicles] GenStep_ScatterLuciferiumCrates: could not find required defs.");
                return;
            }

            var candidates = BuildCandidateCells(map, crateDef);
            int placed     = 0;

            foreach (var cell in candidates)
            {
                if (placed >= crateCount) break;

                var crate = (Building_Crate)ThingMaker.MakeThing(crateDef);

                int count   = Rand.RangeInclusive(minPerCrate, maxPerCrate);
                var luc     = ThingMaker.MakeThing(luciferiumDef);
                luc.stackCount = count;
                crate.TryAcceptThing(luc, allowSpecialEffects: false);

                if (GenPlace.TryPlaceThing(crate, cell, map, ThingPlaceMode.Direct))
                    placed++;
            }

            if (placed < crateCount)
                Log.Message($"[PawnChronicles] GenStep_ScatterLuciferiumCrates: placed {placed}/{crateCount} crates (map may be crowded).");
        }

        private static List<IntVec3> BuildCandidateCells(Map map, ThingDef crateDef)
        {
            // Divide the map into a grid of zones and pick a random cell from each.
            // This gives broad coverage rather than clustering.
            int gridSize = 6; // 6x6 = 36 zones, we pick up to 20
            var result   = new List<IntVec3>(36);
            int zoneW    = map.Size.x / gridSize;
            int zoneH    = map.Size.z / gridSize;

            for (int gx = 0; gx < gridSize; gx++)
            {
                for (int gz = 0; gz < gridSize; gz++)
                {
                    int x0 = gx * zoneW + 2;
                    int z0 = gz * zoneH + 2;

                    // Try several random cells within this zone
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        var cell = new IntVec3(
                            Rand.Range(x0, x0 + zoneW - 2),
                            0,
                            Rand.Range(z0, z0 + zoneH - 2)
                        );

                        if (!cell.InBounds(map))            continue;
                        if (!cell.Standable(map))           continue;
                        if (cell.GetEdifice(map) != null)   continue;
                        if (cell.GetFirstItem(map) != null) continue;
                        // Keep a buffer from map centre (avoid the GenStep_LuciferiumMeeting room)
                        if ((cell - map.Center).LengthHorizontal < 8f) continue;

                        result.Add(cell);
                        break;
                    }
                }
            }

            result.Shuffle();
            return result;
        }
    }
}
