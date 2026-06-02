using System.Linq;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Builds a 7x7 roofed meeting room at the map centre:
    ///   - Slate walls, concrete floor, constructed roof
    ///   - Steel Table1x2c + 2x DiningChair + TorchLamp
    ///   - Broker from sitePart.things spawned at the north seat
    ///   - Gap (no wall/door) in the south wall as the entrance
    ///
    /// Layout (7x7, walls = W, interior = ., broker = B, table = T,
    ///         chairs = C, torch = L, door gap = _):
    ///
    ///   W W W W W W W
    ///   W . . L . . W      L = torch (near NE corner)
    ///   W . . C . . W      C = north chair (faces south)
    ///   W . . T . . W      T = table (1 wide, extends N from here)
    ///   W . . T . . W
    ///   W . . C . . W      C = south chair (faces north)
    ///   W W W _ W W W      _ = entrance gap
    /// </summary>
    public class GenStep_LuciferiumMeeting : GenStep
    {
        // ── Def names ─────────────────────────────────────────────────────────
        private const string WallDefName      = "Wall";
        private const string TableDefName     = "Table1x2c";
        private const string ChairDefName     = "DiningChair";
        private const string TorchDefName     = "TorchLamp";        // VERIFY: correct vanilla defName?
        private const string FloorDefName     = "Concrete";         // VERIFY: correct TerrainDef defName?
        private const string StuffDefName     = "Steel";
        private const string WallStuffName    = "BlocksSlate";

        // ── Room dimensions ───────────────────────────────────────────────────
        // Half-extent including walls. Room is (2*Extent+1) x (2*Extent+1).
        // Extent = 3 -> 7x7 total, 5x5 interior.
        private const int Extent = 3;

        public override int SeedPart => 928374651;

        public override void Generate(Map map, GenStepParams parms)
        {
            IntVec3 centre = map.Center;
            BuildRoom(map, centre);
            SpawnBroker(map, centre, parms);
            SpawnFurniture(map, centre);
        }

        // ── Room shell ────────────────────────────────────────────────────────

        private static void BuildRoom(Map map, IntVec3 c)
        {
            var wallDef     = DefDatabase<ThingDef>.GetNamedSilentFail(WallDefName);
            var wallStuff   = DefDatabase<ThingDef>.GetNamedSilentFail(WallStuffName);
            var floorTerr   = DefDatabase<TerrainDef>.GetNamedSilentFail(FloorDefName);

            for (int dx = -Extent; dx <= Extent; dx++)
            {
                for (int dz = -Extent; dz <= Extent; dz++)
                {
                    IntVec3 cell = c + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map)) continue;

                    bool isWall = dx == -Extent || dx == Extent
                               || dz == -Extent || dz == Extent;
                    // Door gap: south wall (dz == +Extent in our N-up layout),
                    // centre x. We want the gap on the south face so dz == -Extent.
                    bool isDoor = dz == -Extent && dx == 0;

                    // Concrete floor under everything (walls sit on it too)
                    if (floorTerr != null)
                        map.terrainGrid.SetTerrain(cell, floorTerr);

                    if (isWall && !isDoor)
                    {
                        // Remove any existing things at this cell first
                        cell.GetThingList(map).ToList()
                            .ForEach(t => { if (!(t is Pawn)) t.Destroy(); });

                        if (wallDef != null)
                        {
                            var wall = wallStuff != null
                                ? ThingMaker.MakeThing(wallDef, wallStuff)
                                : ThingMaker.MakeThing(wallDef);
                            GenSpawn.Spawn(wall, cell, map);
                        }
                    }

                    // Constructed roof for interior + wall cells (not the gap)
                    if (!isDoor)
                        map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                }
            }
        }

        // ── Broker spawn ──────────────────────────────────────────────────────

        private static void SpawnBroker(Map map, IntVec3 c, GenStepParams parms)
        {
            if (parms.sitePart?.things == null || parms.sitePart.things.Count == 0)
            {
                Log.Warning("[PawnChronicles] GenStep_LuciferiumMeeting: no broker in sitePart.things.");
                return;
            }

            // North side of the table - the broker's seat
            IntVec3 brokerCell = c + new IntVec3(0, 0, 2);
            if (!brokerCell.InBounds(map))
                brokerCell = c;

            parms.sitePart.things.TryDropAll(brokerCell, map, ThingPlaceMode.Near);
        }

        // ── Furniture ─────────────────────────────────────────────────────────

        private static void SpawnFurniture(Map map, IntVec3 c)
        {
            var steelDef = DefDatabase<ThingDef>.GetNamedSilentFail(StuffDefName);
            TrySpawnStuffed(TableDefName, steelDef, c + new IntVec3(0, 0, 0), map, Rot4.North);
            TrySpawnStuffed(ChairDefName, steelDef, c + new IntVec3(0, 0, -1), map, Rot4.North);   // south chair, faces north
            TrySpawnStuffed(ChairDefName, steelDef, c + new IntVec3(0, 0,  2), map, Rot4.South);   // north chair, faces south
            TrySpawnUnstuffed(TorchDefName,          c + new IntVec3(2, 0,  1), map, Rot4.West);    // near NE wall
        }

        private static void TrySpawnStuffed(string defName, ThingDef stuff, IntVec3 cell, Map map, Rot4 rot)
        {
            if (!cell.InBounds(map)) return;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) { Log.Warning($"[PawnChronicles] GenStep_LuciferiumMeeting: def '{defName}' not found."); return; }
            var thing = (def.MadeFromStuff && stuff != null)
                ? ThingMaker.MakeThing(def, stuff)
                : ThingMaker.MakeThing(def);
            GenSpawn.Spawn(thing, cell, map, rot);
        }

        private static void TrySpawnUnstuffed(string defName, IntVec3 cell, Map map, Rot4 rot)
        {
            if (!cell.InBounds(map)) return;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) { Log.Warning($"[PawnChronicles] GenStep_LuciferiumMeeting: def '{defName}' not found."); return; }
            GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map, rot);
        }
    }
}
