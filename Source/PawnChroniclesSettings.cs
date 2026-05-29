using UnityEngine;
using Verse;

namespace PawnChronicles
{

    // ── Autopilot mode ────────────────────────────────────────────────────────
    public enum AutopilotMode
    {
        Random    = 0,
        StatBased = 1,
    }

    // ── Narrative transparency modes ──────────────────────────────────────────
    public enum NarrativeTransparencyMode
    {
        Explicit   = 0,
        Suggestive = 1,
    }

    /// <summary>
    /// Persistent mod settings. Saved to Mods/PawnChronicles/Config.xml.
    ///
    /// Player-tunable values that control how the narrative engine behaves.
    /// All values are read at runtime by the relevant evaluators and comp.
    /// </summary>
    public class PawnChroniclesSettings : ModSettings
    {
        public static PawnChroniclesSettings Current => PawnChroniclesMod.Settings;

        // ── Spark ─────────────────────────────────────────────────────────────
        public bool  sparksEnabled        = true;
        public float sparksChanceFactor   = 1f;
        public float sparkCooldownDays    = 0.25f;
        public int   maxSparksPerDay      = 2;

        // ── Ember ─────────────────────────────────────────────────────────────
        public bool  embersEnabled        = true;
        public float embersChanceFactor   = 1f;
        public float emberCooldownDays    = 0.5f;
        public int   maxActiveEmbers      = 3;
        public float emberDurationDays    = 2f;

        // ── Ember Consequences ────────────────────────────────────────────────
        public float emberConsequenceChance         = 0.15f;
        public float emberConsequencePositiveRatio  = 0.75f;

        // ── Arc thresholds ────────────────────────────────────────────────────
        public float kindleMinWeight      = 20f;
        public float flameMinWeight       = 50f;
        public float fireMinWeight        = 80f;
        public float infernoMinWeight     = 120f;
        public float hellfireMinWeight    = 180f;

        // ── Arc timing ────────────────────────────────────────────────────────
        public float arcStartDelayDaysMin = 1f;
        public float arcStartDelayDaysMax = 5f;
        public float arcChanceFactor      = 1f;
        public float arcTimeoutDays       = 30f;

        // ── Stage wait times ──────────────────────────────────────────────────
        public float seedWaitDays      = 10f;
        public float middleWaitDays    = 10f;
        public float hardRoadWaitDays  = 10f;

        // ── Daily moments (sparks / embers) ───────────────────────────────────
        public float eventCooldownDays = 2f;
        public float eventDailyChance  = 0.25f;
        public float sparkRatio        = 0.75f;

        // ── Colony scale cap ──────────────────────────────────────────────────
        public int   maxActiveEpicsPerColony = 3;

        // ── World consequence ─────────────────────────────────────────────────
        public bool  worldConsequencesEnabled = true;
        public bool  factionRelationShifts    = true;
        public bool  siteSpawnsEnabled        = true;
        public bool  mapEventsEnabled         = true;

        // ── Scraper ───────────────────────────────────────────────────────────
        public int   scraperDepth           = 1;
        public int   scraperCacheMinutes    = 30;
        public int   scraperMaxRulesPerPawn = 400;
        public bool  scraperLoggingEnabled  = false;

        // ── Chronicle ─────────────────────────────────────────────────────────
        public bool  chronicleEnabled           = true;
        public int   maxChronicleEntriesPerPawn = 50;

        // ── Autopilot ─────────────────────────────────────────────────────────
        public bool         autopilotEnabled = false;
        public AutopilotMode autopilotMode   = AutopilotMode.Random;

        // ── Chronicles window size ────────────────────────────────────────────
        public float chroniclesWindowWidth  = 1020f;
        public float chroniclesWindowHeight = 650f;

        // ── Narrative mode ────────────────────────────────────────────────────
        public bool medievalMode = false;
        public NarrativeTransparencyMode transparencyMode   = NarrativeTransparencyMode.Explicit;
        public float revelationDelayDaysMin = 3f;
        public float revelationDelayDaysMax = 7f;

        // ── UI - Layout ───────────────────────────────────────────────────────
        public float uiPadding    = 12f;   // inner margin inside each pane
        public float uiPaneSplit  = 8f;    // gap between left and right pane
        public float uiLeftRatio  = 0.34f; // fraction of width given to left pane
        public float uiArcRowH    = 54f;   // height of an arc chapter row
        public float uiDiaryRowH  = 28f;   // height of a diary row
        public float uiTagRowH    = 22f;   // height of a narrative tag row

        // ── UI - Typography ───────────────────────────────────────────────────
        /// <summary>0 = Tiny, 1 = Small. Controls body / narrative text size.</summary>
        public int uiBodyFont = 1;
        public GameFont BodyFont => uiBodyFont == 0 ? GameFont.Tiny : GameFont.Small;

        // ── UI - Colors (stored as individual floats for Scribe compatibility) ─
        // Accent / gold (headers, active arc label, selected items)
        public float uiGoldR = 0.95f; public float uiGoldG = 0.82f; public float uiGoldB = 0.55f;
        // Body text
        public float uiBodyR = 0.88f; public float uiBodyG = 0.88f; public float uiBodyB = 0.88f;
        // Dim / secondary text
        public float uiDimR  = 0.48f; public float uiDimG  = 0.48f; public float uiDimB  = 0.50f;
        // Diary / spark entries
        public float uiDiaryR = 0.65f; public float uiDiaryG = 0.80f; public float uiDiaryB = 1.00f;
        // Pane background
        public float uiBgR = 0.06f; public float uiBgG = 0.06f; public float uiBgB = 0.07f; public float uiBgA = 0.96f;

        // Derived Color properties - not saved, computed on demand
        public Color ColorGold  => new Color(uiGoldR,  uiGoldG,  uiGoldB);
        public Color ColorBody  => new Color(uiBodyR,  uiBodyG,  uiBodyB);
        public Color ColorDim   => new Color(uiDimR,   uiDimG,   uiDimB);
        public Color ColorDiary => new Color(uiDiaryR, uiDiaryG, uiDiaryB);
        public Color ColorBg    => new Color(uiBgR,    uiBgG,    uiBgB,  uiBgA);

        // ── Reset UI to defaults ──────────────────────────────────────────────
        public void ResetUIDefaults()
        {
            uiPadding   = 12f; uiPaneSplit = 8f; uiLeftRatio = 0.34f;
            uiArcRowH   = 54f; uiDiaryRowH = 28f; uiTagRowH   = 22f;
            uiBodyFont  = 1;
            uiGoldR  = 0.95f; uiGoldG  = 0.82f; uiGoldB  = 0.55f;
            uiBodyR  = 0.88f; uiBodyG  = 0.88f; uiBodyB  = 0.88f;
            uiDimR   = 0.48f; uiDimG   = 0.48f; uiDimB   = 0.50f;
            uiDiaryR = 0.65f; uiDiaryG = 0.80f; uiDiaryB = 1.00f;
            uiBgR    = 0.06f; uiBgG    = 0.06f; uiBgB    = 0.07f; uiBgA = 0.96f;
            chroniclesWindowWidth  = 1020f;
            chroniclesWindowHeight = 650f;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref sparksEnabled,         "sparksEnabled",         true);
            Scribe_Values.Look(ref sparksChanceFactor,    "sparksChanceFactor",    1f);
            Scribe_Values.Look(ref sparkCooldownDays,     "sparkCooldownDays",     0.25f);
            Scribe_Values.Look(ref maxSparksPerDay,       "maxSparksPerDay",       2);

            Scribe_Values.Look(ref embersEnabled,         "embersEnabled",         true);
            Scribe_Values.Look(ref embersChanceFactor,    "embersChanceFactor",    1f);
            Scribe_Values.Look(ref emberCooldownDays,     "emberCooldownDays",     0.5f);
            Scribe_Values.Look(ref maxActiveEmbers,       "maxActiveEmbers",       3);
            Scribe_Values.Look(ref emberDurationDays,     "emberDurationDays",     2f);
            Scribe_Values.Look(ref emberConsequenceChance,        "emberConsequenceChance",        0.15f);
            Scribe_Values.Look(ref emberConsequencePositiveRatio, "emberConsequencePositiveRatio", 0.75f);

            Scribe_Values.Look(ref kindleMinWeight,    "kindleMinWeight",    20f);
            Scribe_Values.Look(ref flameMinWeight,     "flameMinWeight",     50f);
            Scribe_Values.Look(ref fireMinWeight,      "fireMinWeight",      80f);
            Scribe_Values.Look(ref infernoMinWeight,   "infernoMinWeight",   120f);
            Scribe_Values.Look(ref hellfireMinWeight,  "hellfireMinWeight",  180f);

            Scribe_Values.Look(ref arcStartDelayDaysMin, "arcStartDelayDaysMin", 1f);
            Scribe_Values.Look(ref arcStartDelayDaysMax, "arcStartDelayDaysMax", 5f);
            Scribe_Values.Look(ref arcTimeoutDays,       "arcTimeoutDays",       30f);
            Scribe_Values.Look(ref arcChanceFactor,      "arcChanceFactor",      1f);
            Scribe_Values.Look(ref seedWaitDays,         "seedWaitDays",         2f);
            Scribe_Values.Look(ref middleWaitDays,       "middleWaitDays",       2f);
            Scribe_Values.Look(ref hardRoadWaitDays,     "hardRoadWaitDays",     2f);

            Scribe_Values.Look(ref eventCooldownDays,      "eventCooldownDays",      2f);
            Scribe_Values.Look(ref eventDailyChance,       "eventDailyChance",       0.25f);
            Scribe_Values.Look(ref sparkRatio,             "sparkRatio",             0.75f);
            Scribe_Values.Look(ref maxActiveEpicsPerColony,"maxActiveEpicsPerColony", 3);

            Scribe_Values.Look(ref worldConsequencesEnabled, "worldConsequencesEnabled", true);
            Scribe_Values.Look(ref factionRelationShifts,    "factionRelationShifts",    true);
            Scribe_Values.Look(ref siteSpawnsEnabled,        "siteSpawnsEnabled",        true);
            Scribe_Values.Look(ref mapEventsEnabled,         "mapEventsEnabled",         true);

            Scribe_Values.Look(ref scraperDepth,           "scraperDepth",           1);
            Scribe_Values.Look(ref scraperCacheMinutes,    "scraperCacheMinutes",    30);
            Scribe_Values.Look(ref scraperMaxRulesPerPawn, "scraperMaxRulesPerPawn", 400);
            Scribe_Values.Look(ref scraperLoggingEnabled,  "scraperLoggingEnabled",  false);

            Scribe_Values.Look(ref chronicleEnabled,           "chronicleEnabled",           true);
            Scribe_Values.Look(ref maxChronicleEntriesPerPawn, "maxChronicleEntriesPerPawn", 50);

            Scribe_Values.Look(ref autopilotEnabled, "autopilotEnabled", false);
            Scribe_Values.Look(ref autopilotMode,    "autopilotMode",    AutopilotMode.Random);

            Scribe_Values.Look(ref medievalMode,           "medievalMode",           false);
            Scribe_Values.Look(ref transparencyMode,       "transparencyMode",       NarrativeTransparencyMode.Explicit);
            Scribe_Values.Look(ref revelationDelayDaysMin, "revelationDelayDaysMin", 3f);
            Scribe_Values.Look(ref revelationDelayDaysMax, "revelationDelayDaysMax", 7f);

            Scribe_Values.Look(ref chroniclesWindowWidth,  "chroniclesWindowWidth",  1020f);
            Scribe_Values.Look(ref chroniclesWindowHeight, "chroniclesWindowHeight", 650f);

            // UI
            Scribe_Values.Look(ref uiPadding,   "uiPadding",   12f);
            Scribe_Values.Look(ref uiPaneSplit, "uiPaneSplit", 8f);
            Scribe_Values.Look(ref uiLeftRatio, "uiLeftRatio", 0.34f);
            Scribe_Values.Look(ref uiArcRowH,   "uiArcRowH",   54f);
            Scribe_Values.Look(ref uiDiaryRowH, "uiDiaryRowH", 28f);
            Scribe_Values.Look(ref uiTagRowH,   "uiTagRowH",   22f);
            Scribe_Values.Look(ref uiBodyFont,  "uiBodyFont",  1);

            Scribe_Values.Look(ref uiGoldR,  "uiGoldR",  0.95f); Scribe_Values.Look(ref uiGoldG,  "uiGoldG",  0.82f); Scribe_Values.Look(ref uiGoldB,  "uiGoldB",  0.55f);
            Scribe_Values.Look(ref uiBodyR,  "uiBodyR",  0.88f); Scribe_Values.Look(ref uiBodyG,  "uiBodyG",  0.88f); Scribe_Values.Look(ref uiBodyB,  "uiBodyB",  0.88f);
            Scribe_Values.Look(ref uiDimR,   "uiDimR",   0.48f); Scribe_Values.Look(ref uiDimG,   "uiDimG",   0.48f); Scribe_Values.Look(ref uiDimB,   "uiDimB",   0.50f);
            Scribe_Values.Look(ref uiDiaryR, "uiDiaryR", 0.65f); Scribe_Values.Look(ref uiDiaryG, "uiDiaryG", 0.80f); Scribe_Values.Look(ref uiDiaryB, "uiDiaryB", 1.00f);
            Scribe_Values.Look(ref uiBgR,    "uiBgR",    0.06f); Scribe_Values.Look(ref uiBgG,    "uiBgG",    0.06f); Scribe_Values.Look(ref uiBgB,    "uiBgB",    0.07f); Scribe_Values.Look(ref uiBgA, "uiBgA", 0.96f);
        }
    }
}
