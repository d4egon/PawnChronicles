using System.Collections.Generic;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// After all defs load, scans every BackstoryDef and injects PC_LuciferCartel
    /// into spawnCategories for any backstory whose title or identifier contains
    /// "smith" (gunsmith, blacksmith, weaponsmith ...) or "drug" (druglord, drugdealer ...).
    /// Case-insensitive substring match - not whole-word.
    ///
    /// This lets the faction's backstoryFilters reference PC_LuciferCartel as a
    /// category without requiring us to manually tag every vanilla backstory in XML.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BackstoryInjector
    {
        private const string CartelCategory = "PC_LuciferCartel";

        static BackstoryInjector()
        {
            int count = 0;

            foreach (var def in DefDatabase<BackstoryDef>.AllDefsListForReading)
            {
                if (IsCartelBackstory(def))
                {
                    def.spawnCategories ??= new List<string>();

                    if (!def.spawnCategories.Contains(CartelCategory))
                    {
                        def.spawnCategories.Add(CartelCategory);
                        count++;
                    }
                }
            }

            if (count > 0)
                Log.Message($"[PawnChronicles] BackstoryInjector: tagged {count} backstories as {CartelCategory}.");
        }

        private static bool IsCartelBackstory(BackstoryDef def)
        {
            string title = def.title?.ToLowerInvariant() ?? string.Empty;
            string id    = def.identifier?.ToLowerInvariant() ?? string.Empty;
            string both  = title + " " + id;

            // Adult backstories - trade/criminal vocations
            if (both.Contains("smith")   || both.Contains("drug")    || both.Contains("crim")
             || both.Contains("smug")  || both.Contains("deal")  || both.Contains("traff")
             || both.Contains("merch")   || both.Contains("trad")  || both.Contains("fence")
             || both.Contains("racket")  || both.Contains("cartel")  || both.Contains("synd"))
                return true;

            // Childhood backstories - rough origins that fit cartel recruitment
            if (both.Contains("street")  || both.Contains("gang")    || both.Contains("thief")
             || both.Contains("orphan")  || both.Contains("runaway") || both.Contains("refugee")
             || both.Contains("vagrant") || both.Contains("slave")   || both.Contains("poor")
             || both.Contains("guttersnipe") || both.Contains("urchin") || both.Contains("stray"))
                return true;

            return false;
        }
    }
}
