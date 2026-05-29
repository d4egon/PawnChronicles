using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Grammar;
using RimWorld;

namespace PawnChronicles
{
    public static class PawnChroniclesSettingsWindow
    {
        // ── Tab state ─────────────────────────────────────────────────────────
        private static int   _tab          = 0;
        private static Pawn? _selectedPawn = null;
        private static int   _debugTab     = 0;

        // ── Scroll positions - each panel has its OWN vector ──────────────────
        private static Vector2 _settingsScroll;
        private static Vector2 _uiScroll;

        // ── Cached content heights - measured on first draw, reused every frame ─
        private static float _settingsViewHeight = 2000f; // safe initial guess
        private static float _uiViewHeight       = 2000f;
        private static Vector2 _tagListScroll;   // left panel: tag scores
        private static Vector2 _pawnRulesScroll; // right panel: raw pawn symbols
        private static Vector2 _talesScroll;     // tales list
        private static Vector2 _chronicleScroll; // chronicle log

        // ── Cached scraper output ─────────────────────────────────────────────
        private static List<Rule>?           _cachedPawnRules  = null;
        private static PawnNarrativeProfile? _cachedProfile    = null;
        private static string?               _cachedPawnName   = null;

        // ── Filter ────────────────────────────────────────────────────────────
        private static string _pawnFilter  = "";

        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color HeaderColor  = new Color(0.85f, 0.75f, 0.45f);
        private static readonly Color KeyColor     = new Color(0.6f,  0.85f, 0.6f);
        private static readonly Color ValueColor   = Color.white;
        private static readonly Color TagHighColor = new Color(0.9f,  0.6f,  0.3f);
        private static readonly Color DimColor     = new Color(0.55f, 0.55f, 0.55f);

        // ─────────────────────────────────────────────────────────────────────
        //  ENTRY POINT
        // ─────────────────────────────────────────────────────────────────────

        public static void DrawSettings(Rect inRect, PawnChroniclesSettings settings)
        {
            // Guard: settings can be null during the very first render frame before the
            // mod's constructor has finished (timing edge-case reported in feedback).
            if (settings == null) return;

            var tabRect     = new Rect(inRect.x, inRect.y, inRect.width, 32f);
            DrawTabBar(tabRect);

            var contentRect = new Rect(inRect.x, inRect.y + 36f,
                inRect.width, inRect.height - 36f);

            if      (_tab == 0) DrawSettingsTab(contentRect, settings);
            else if (_tab == 1) DrawUITab(contentRect, settings);
            else                DrawDebugTab(contentRect);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  TAB BAR
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawTabBar(Rect rect)
        {
            float w = rect.width / 3f;
            DrawTabButton(new Rect(rect.x,          rect.y, w, rect.height), "PC_Tab_Settings".Translate(), 0);
            DrawTabButton(new Rect(rect.x + w,      rect.y, w, rect.height), "PC_Tab_UI".Translate(),       1);
            DrawTabButton(new Rect(rect.x + w * 2f, rect.y, w, rect.height), "PC_Tab_Debug".Translate(),    2);
        }

        private static void DrawTabButton(Rect rect, string label, int index)
        {
            bool active = _tab == index;
            Widgets.DrawBoxSolid(rect,
                active ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.15f, 0.15f, 0.15f));
            if (active) Widgets.DrawBox(rect);
            using (new TextAnchorScope(TextAnchor.MiddleCenter))
            {
                GUI.color = active ? Color.white : DimColor;
                Widgets.Label(rect, label);
                GUI.color = Color.white;
            }
            if (!active && Widgets.ButtonInvisible(rect)) _tab = index;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SETTINGS TAB
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawSettingsTab(Rect rect, PawnChroniclesSettings s)
        {
            var viewRect = new Rect(0, 0, rect.width - 20f, _settingsViewHeight);

            Widgets.BeginScrollView(rect, ref _settingsScroll, viewRect);
            var ls = new Listing_Standard();
            ls.Begin(viewRect);

            SectionHeader(ls, "PC_Settings_Section_DailyMoments".Translate());
            ls.CheckboxLabeled("PC_Settings_EnableSparks".Translate(), ref s.sparksEnabled,
                "PC_Settings_EnableSparks_Tip".Translate());
            ls.CheckboxLabeled("PC_Settings_EnableEmbers".Translate(), ref s.embersEnabled,
                "PC_Settings_EnableEmbers_Tip".Translate());
            ls.Gap(2f);
            ls.Label("PC_Settings_EmberConsequenceChance".Translate(
                (s.emberConsequenceChance * 100f).ToString("F0")));
            s.emberConsequenceChance = ls.Slider(s.emberConsequenceChance, 0f, 1f);
            ls.Label("PC_Settings_ConsequenceBalance".Translate(
                (s.emberConsequencePositiveRatio * 100f).ToString("F0"),
                ((1f - s.emberConsequencePositiveRatio) * 100f).ToString("F0")));
            s.emberConsequencePositiveRatio = ls.Slider(s.emberConsequencePositiveRatio, 0f, 1f);
            ls.Gap(4f);

            ls.Label("PC_Settings_EventCooldown".Translate(s.eventCooldownDays.ToString("F1")));
            s.eventCooldownDays = ls.Slider(s.eventCooldownDays, 0.25f, 14f);

            ls.Label("PC_Settings_DailyChance".Translate((s.eventDailyChance * 100f).ToString("F0")));
            s.eventDailyChance = ls.Slider(s.eventDailyChance, 0.05f, 1f);

            ls.Label("PC_Settings_SparkRatio".Translate(
                (s.sparkRatio * 100f).ToString("F0"),
                ((1f - s.sparkRatio) * 100f).ToString("F0")));
            s.sparkRatio = ls.Slider(s.sparkRatio, 0f, 1f);

            ls.Gap(4f);
            string freqPreview = EstimateEventFrequency(s);
            GUI.color = new Color(0.7f, 0.9f, 0.7f);
            ls.Label("PC_Settings_Estimate".Translate(freqPreview));
            GUI.color = Color.white;

            SectionHeader(ls, "PC_Settings_Section_ModusThresholds".Translate());
            ls.Label("PC_Settings_KindleMin".Translate(s.kindleMinWeight.ToString("F0")));
            s.kindleMinWeight = ls.Slider(s.kindleMinWeight, 10f, 50f);
            ls.Label("PC_Settings_FlameMin".Translate(s.flameMinWeight.ToString("F0")));
            s.flameMinWeight = ls.Slider(s.flameMinWeight, 30f, 100f);
            ls.Label("PC_Settings_FireMin".Translate(s.fireMinWeight.ToString("F0")));
            s.fireMinWeight = ls.Slider(s.fireMinWeight, 50f, 150f);
            ls.Label("PC_Settings_InfernoMin".Translate(s.infernoMinWeight.ToString("F0")));
            s.infernoMinWeight = ls.Slider(s.infernoMinWeight, 80f, 200f);
            ls.Label("PC_Settings_HellfireMin".Translate(s.hellfireMinWeight.ToString("F0")));
            s.hellfireMinWeight = ls.Slider(s.hellfireMinWeight, 120f, 300f);
            ls.Gap();

            SectionHeader(ls, "PC_Settings_Section_ArcTiming".Translate());
            ls.Label("PC_Settings_ArcStartDelayMin".Translate(s.arcStartDelayDaysMin.ToString("F1")));
            s.arcStartDelayDaysMin = ls.Slider(s.arcStartDelayDaysMin, 0.5f, 5f);
            ls.Label("PC_Settings_ArcStartDelayMax".Translate(s.arcStartDelayDaysMax.ToString("F1")));
            s.arcStartDelayDaysMax = ls.Slider(s.arcStartDelayDaysMax, 1f, 15f);
            ls.Label("PC_Settings_ArcTimeout".Translate(s.arcTimeoutDays.ToString("F0")));
            s.arcTimeoutDays = ls.Slider(s.arcTimeoutDays, 10f, 60f);

            ls.Gap(6f);
            ls.Label("PC_Settings_StageWaitTimes".Translate());
            ls.Label("PC_Settings_SeedWait".Translate(s.seedWaitDays.ToString("F1")));
            TooltipHandler.TipRegion(ls.GetRect(0f), "PC_Settings_SeedWait_Tip".Translate());
            s.seedWaitDays = ls.Slider(s.seedWaitDays, 0.5f, 7f);
            ls.Label("PC_Settings_MiddleWait".Translate(s.middleWaitDays.ToString("F1")));
            TooltipHandler.TipRegion(ls.GetRect(0f), "PC_Settings_MiddleWait_Tip".Translate());
            s.middleWaitDays = ls.Slider(s.middleWaitDays, 0.5f, 7f);
            ls.Label("PC_Settings_HardRoadWait".Translate(s.hardRoadWaitDays.ToString("F1")));
            TooltipHandler.TipRegion(ls.GetRect(0f), "PC_Settings_HardRoadWait_Tip".Translate());
            s.hardRoadWaitDays = ls.Slider(s.hardRoadWaitDays, 0.5f, 7f);

            ls.Gap(4f);
            string maxArcsLabel = s.maxActiveEpicsPerColony == 0
                ? "PC_Settings_MaxActiveArcs_Unlimited".Translate()
                : s.maxActiveEpicsPerColony.ToString();
            ls.Label("PC_Settings_MaxActiveArcs".Translate(maxArcsLabel));
            s.maxActiveEpicsPerColony = (int)ls.Slider(s.maxActiveEpicsPerColony, 0, 15);
            ls.Gap();

            SectionHeader(ls, "PC_Settings_Section_WorldConsequences".Translate());
            ls.CheckboxLabeled("PC_Settings_EnableWorldConsequences".Translate(),
                ref s.worldConsequencesEnabled,
                "PC_Settings_EnableWorldConsequences_Tip".Translate());
            if (s.worldConsequencesEnabled)
            {
                ls.CheckboxLabeled("PC_Settings_FactionRelationShifts".Translate(), ref s.factionRelationShifts);
                ls.CheckboxLabeled("PC_Settings_SiteSpawns".Translate(),             ref s.siteSpawnsEnabled);
                ls.CheckboxLabeled("PC_Settings_MapEvents".Translate(),              ref s.mapEventsEnabled);
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                DrawDisabledCheckbox(ls, "PC_Settings_FactionRelationShifts".Translate(), s.factionRelationShifts);
                DrawDisabledCheckbox(ls, "PC_Settings_SiteSpawns".Translate(),            s.siteSpawnsEnabled);
                DrawDisabledCheckbox(ls, "PC_Settings_MapEvents".Translate(),             s.mapEventsEnabled);
                GUI.color = Color.white;
            }
            ls.Gap();

            SectionHeader(ls, "PC_Settings_Section_Scraper".Translate());

            // Depth
            string[] depthLabels =
            {
                "PC_Settings_ScraperDepth_0".Translate(),
                "PC_Settings_ScraperDepth_1".Translate(),
                "PC_Settings_ScraperDepth_2".Translate()
            };
            int depthIdx = Mathf.Clamp(s.scraperDepth, 0, 2);
            ls.Label("PC_Settings_ScraperDepthLabel".Translate(depthLabels[depthIdx]));
            s.scraperDepth = (int)ls.Slider(s.scraperDepth, 0f, 2f);
            ls.Gap(4f);

            // Pawn cache
            ls.Label("PC_Settings_PawnCache".Translate(s.scraperCacheMinutes.ToString()));
            s.scraperCacheMinutes = (int)ls.Slider(s.scraperCacheMinutes, 0f, 120f);
            ls.Gap(4f);

            // Rules cap
            string capLabel = s.scraperMaxRulesPerPawn == 0
                ? "PC_Settings_MaxRules_Unlimited".Translate()
                : "PC_Settings_MaxRules_Count".Translate(s.scraperMaxRulesPerPawn.ToString());
            ls.Label("PC_Settings_MaxRules".Translate(capLabel));
            s.scraperMaxRulesPerPawn = (int)ls.Slider(s.scraperMaxRulesPerPawn, 0f, 600f);
            ls.Gap(4f);

            // Logging
            ls.CheckboxLabeled("PC_Settings_LogScraperCount".Translate(),
                ref s.scraperLoggingEnabled);
            ls.Gap();


            SectionHeader(ls, "PC_Settings_Section_NarrativeMode".Translate());

            ls.CheckboxLabeled(
                "PC_Settings_MedievalMode".Translate(),
                ref s.medievalMode,
                "PC_Settings_MedievalMode_Tip".Translate());
            ls.Gap(4f);

            // Mode toggle - two radio buttons
            bool isExplicit   = s.transparencyMode == NarrativeTransparencyMode.Explicit;
            bool isSuggestive = s.transparencyMode == NarrativeTransparencyMode.Suggestive;

            ls.Label("PC_Settings_NarrativeModeQuestion".Translate());

            if (ls.RadioButton(
                "PC_Settings_NarrativeExplicit".Translate(),
                isExplicit, 0f,
                "PC_Settings_NarrativeExplicit_Tip".Translate()))
                s.transparencyMode = NarrativeTransparencyMode.Explicit;

            if (ls.RadioButton(
                "PC_Settings_NarrativeSuggestive".Translate(),
                isSuggestive, 0f,
                "PC_Settings_NarrativeSuggestive_Tip".Translate()))
                s.transparencyMode = NarrativeTransparencyMode.Suggestive;

            GUI.color = isSuggestive ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            GUI.enabled = isSuggestive;
            ls.Label("PC_Settings_RevelationDelay".Translate(
                s.revelationDelayDaysMin.ToString("F0"), s.revelationDelayDaysMax.ToString("F0")));
            float newMin = (float)System.Math.Round(ls.Slider(s.revelationDelayDaysMin, 1f, 14f), 0);
            float newMax = (float)System.Math.Round(ls.Slider(s.revelationDelayDaysMax, s.revelationDelayDaysMin + 1f, 21f), 0);
            if (isSuggestive) { s.revelationDelayDaysMin = newMin; s.revelationDelayDaysMax = newMax; }
            GUI.enabled = true;
            GUI.color   = Color.white;

            SectionHeader(ls, "PC_Settings_Section_Chronicle".Translate());
            ls.CheckboxLabeled("PC_Settings_EnableChronicle".Translate(), ref s.chronicleEnabled);
            ls.Label("PC_Settings_MaxChronicleEntries".Translate(s.maxChronicleEntriesPerPawn.ToString()));
            s.maxChronicleEntriesPerPawn =
                (int)ls.Slider(s.maxChronicleEntriesPerPawn, 10, 200);

            // ── Autopilot ─────────────────────────────────────────────────────
            SectionHeader(ls, "PC_Settings_Section_Autopilot".Translate());
            ls.CheckboxLabeled(
                "PC_Settings_AutopilotEnabled".Translate(),
                ref s.autopilotEnabled,
                "PC_Settings_AutopilotEnabled_Tip".Translate());

            // Sub-options always rendered (keeps scroll height stable); greyed when disabled
            GUI.color   = s.autopilotEnabled ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            GUI.enabled = s.autopilotEnabled;

            ls.Label("PC_Settings_AutopilotMode".Translate());

            bool isRandom    = s.autopilotMode == AutopilotMode.Random;
            bool isStatBased = s.autopilotMode == AutopilotMode.StatBased;

            if (ls.RadioButton(
                "PC_Settings_AutopilotRandom".Translate(),
                isRandom, 0f,
                "PC_Settings_AutopilotRandom_Tip".Translate()))
                s.autopilotMode = AutopilotMode.Random;

            if (ls.RadioButton(
                "PC_Settings_AutopilotStatBased".Translate(),
                isStatBased, 0f,
                "PC_Settings_AutopilotStatBased_Tip".Translate()))
                s.autopilotMode = AutopilotMode.StatBased;

            GUI.enabled = true;
            GUI.color   = Color.white;
            ls.Gap(4f);

            // Capture real content height so the scrollbar stays exact next frame
            _settingsViewHeight = ls.CurHeight + 20f;
            ls.End();
            Widgets.EndScrollView();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  UI TAB
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawUITab(Rect rect, PawnChroniclesSettings s)
        {
            var viewRect = new Rect(0, 0, rect.width - 20f, _uiViewHeight);

            Widgets.BeginScrollView(rect, ref _uiScroll, viewRect);
            var ls = new Listing_Standard();
            ls.Begin(viewRect);

            // ── Window size ───────────────────────────────────────────────────
            SectionHeader(ls, "PC_Settings_Section_WindowSize".Translate());
            ls.Label("PC_Settings_WindowWidth".Translate(s.chroniclesWindowWidth.ToString("F0")));
            s.chroniclesWindowWidth = Mathf.Round(ls.Slider(s.chroniclesWindowWidth, 720f, 2600f));
            ls.Label("PC_Settings_WindowHeight".Translate(s.chroniclesWindowHeight.ToString("F0")));
            s.chroniclesWindowHeight = Mathf.Round(ls.Slider(s.chroniclesWindowHeight, 450f, 950f));

            // ── Layout ────────────────────────────────────────────────────────
            SectionHeader(ls, "PC_Settings_Section_Layout".Translate());
            ls.Label("PC_Settings_InnerPadding".Translate(s.uiPadding.ToString("F0")));
            s.uiPadding = Mathf.Round(ls.Slider(s.uiPadding, 4f, 28f));

            ls.Label("PC_Settings_PaneGap".Translate(s.uiPaneSplit.ToString("F0")));
            s.uiPaneSplit = Mathf.Round(ls.Slider(s.uiPaneSplit, 2f, 24f));

            ls.Label("PC_Settings_LeftPaneWidth".Translate((s.uiLeftRatio * 100f).ToString("F0")));
            s.uiLeftRatio = ls.Slider(s.uiLeftRatio, 0.20f, 0.55f);

            ls.Gap(4f);
            ls.Label("PC_Settings_ArcRowHeight".Translate(s.uiArcRowH.ToString("F0")));
            s.uiArcRowH = Mathf.Round(ls.Slider(s.uiArcRowH, 36f, 80f));

            ls.Label("PC_Settings_DiaryRowHeight".Translate(s.uiDiaryRowH.ToString("F0")));
            s.uiDiaryRowH = Mathf.Round(ls.Slider(s.uiDiaryRowH, 16f, 48f));

            ls.Label("PC_Settings_TagRowHeight".Translate(s.uiTagRowH.ToString("F0")));
            s.uiTagRowH = Mathf.Round(ls.Slider(s.uiTagRowH, 14f, 36f));

            // ── Typography ────────────────────────────────────────────────────
            SectionHeader(ls, "PC_Settings_Section_Typography".Translate());
            ls.Label("PC_Settings_BodyFontLabel".Translate());
            if (ls.RadioButton("PC_Settings_FontTiny".Translate(),  s.uiBodyFont == 0))
                s.uiBodyFont = 0;
            if (ls.RadioButton("PC_Settings_FontSmall".Translate(), s.uiBodyFont == 1))
                s.uiBodyFont = 1;

            // ── Colors ────────────────────────────────────────────────────────
            SectionHeader(ls, "PC_Settings_Section_Colors".Translate());

            DrawColorGroup(ls, "PC_Settings_ColorAccent".Translate(),
                ref s.uiGoldR, ref s.uiGoldG, ref s.uiGoldB);

            DrawColorGroup(ls, "PC_Settings_ColorBody".Translate(),
                ref s.uiBodyR, ref s.uiBodyG, ref s.uiBodyB);

            DrawColorGroup(ls, "PC_Settings_ColorDim".Translate(),
                ref s.uiDimR, ref s.uiDimG, ref s.uiDimB);

            DrawColorGroup(ls, "PC_Settings_ColorDiary".Translate(),
                ref s.uiDiaryR, ref s.uiDiaryG, ref s.uiDiaryB);

            DrawColorGroupRGBA(ls, "PC_Settings_ColorBg".Translate(),
                ref s.uiBgR, ref s.uiBgG, ref s.uiBgB, ref s.uiBgA);

            // ── Reset ─────────────────────────────────────────────────────────
            ls.Gap(8f);
            if (ls.ButtonText("PC_Settings_ResetUI".Translate()))
                s.ResetUIDefaults();

            // Capture real content height so the scrollbar stays exact next frame
            _uiViewHeight = ls.CurHeight + 20f;
            ls.End();
            Widgets.EndScrollView();
        }

        /// <summary>RGB color group with swatch preview.</summary>
        private static void DrawColorGroup(Listing_Standard ls, string label,
            ref float r, ref float g, ref float b)
        {
            // Header row + swatch
            Rect headerRow = ls.GetRect(22f);
            GUI.color = DimColor;
            Widgets.Label(new Rect(headerRow.x, headerRow.y, headerRow.width - 34f, 22f), label);
            GUI.color = Color.white;
            Widgets.DrawBoxSolid(new Rect(headerRow.xMax - 30f, headerRow.y + 2f, 30f, 18f),
                new Color(r, g, b));
            Widgets.DrawBox(new Rect(headerRow.xMax - 30f, headerRow.y + 2f, 30f, 18f));

            ls.Label($"  R  {r:F2}"); r = ls.Slider(r, 0f, 1f);
            ls.Label($"  G  {g:F2}"); g = ls.Slider(g, 0f, 1f);
            ls.Label($"  B  {b:F2}"); b = ls.Slider(b, 0f, 1f);
            ls.Gap(4f);
        }

        /// <summary>RGBA color group with swatch preview (includes alpha slider).</summary>
        private static void DrawColorGroupRGBA(Listing_Standard ls, string label,
            ref float r, ref float g, ref float b, ref float a)
        {
            Rect headerRow = ls.GetRect(22f);
            GUI.color = DimColor;
            Widgets.Label(new Rect(headerRow.x, headerRow.y, headerRow.width - 34f, 22f), label);
            GUI.color = Color.white;
            Widgets.DrawBoxSolid(new Rect(headerRow.xMax - 30f, headerRow.y + 2f, 30f, 18f),
                new Color(r, g, b, 1f)); // show full-opacity preview so swatch is always visible
            Widgets.DrawBox(new Rect(headerRow.xMax - 30f, headerRow.y + 2f, 30f, 18f));

            ls.Label($"  R  {r:F2}"); r = ls.Slider(r, 0f, 1f);
            ls.Label($"  G  {g:F2}"); g = ls.Slider(g, 0f, 1f);
            ls.Label($"  B  {b:F2}"); b = ls.Slider(b, 0f, 1f);
            ls.Label($"  A  {a:F2}  (opacity)"); a = ls.Slider(a, 0f, 1f);
            ls.Gap(4f);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  DEBUG TAB
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawDebugTab(Rect rect)
        {
            float subTabH = 28f;
            DrawDebugSubTabBar(new Rect(rect.x, rect.y, rect.width, subTabH));

            var content = new Rect(rect.x, rect.y + subTabH + 4f,
                rect.width, rect.height - subTabH - 4f);

            float controlH = 30f;
            DrawPawnSelector(new Rect(content.x, content.y, content.width, controlH));

            var panelRect = new Rect(content.x, content.y + controlH + 4f,
                content.width, content.height - controlH - 4f);

            if      (_debugTab == 0) DrawPawnPanel(panelRect);
            else if (_debugTab == 1) DrawTalesPanel(panelRect);
            else                     DrawPerfPanel(panelRect);
        }

        private static void DrawDebugSubTabBar(Rect rect)
        {
            float w = rect.width / 3f;
            DrawDebugTabButton(new Rect(rect.x,          rect.y, w, rect.height), "PC_Debug_Sub_PawnData".Translate(), 0);
            DrawDebugTabButton(new Rect(rect.x + w,      rect.y, w, rect.height), "PC_Debug_Sub_TalesLog".Translate(), 1);
            DrawDebugTabButton(new Rect(rect.x + w * 2f, rect.y, w, rect.height), "PC_Debug_Sub_Perf".Translate(),    2);
        }

        private static void DrawDebugTabButton(Rect rect, string label, int index)
        {
            bool active = _debugTab == index;
            Widgets.DrawBoxSolid(rect,
                active ? new Color(0.2f, 0.25f, 0.2f) : new Color(0.15f, 0.15f, 0.15f));
            if (active) Widgets.DrawBox(rect);
            using (new TextAnchorScope(TextAnchor.MiddleCenter))
            {
                GUI.color = active ? Color.white : DimColor;
                Widgets.Label(rect, label);
                GUI.color = Color.white;
            }
            if (!active && Widgets.ButtonInvisible(rect)) _debugTab = index;
        }

        private static void DrawPawnSelector(Rect rect)
        {
            float btnW   = 100f;
            float exportW = 120f;
            var   lbl    = new Rect(rect.x, rect.y, rect.width - btnW - exportW - 16f, rect.height);
            var   btn    = new Rect(rect.xMax - btnW - exportW - 8f, rect.y, btnW, rect.height);
            var   export = new Rect(rect.xMax - exportW, rect.y, exportW, rect.height);

            string pawnLabel = _selectedPawn?.LabelShort ?? "PC_Debug_NoPawnSelected".Translate();
            if (Widgets.ButtonText(lbl, pawnLabel))
                OpenPawnSelector();

            if (Widgets.ButtonText(btn, "PC_Debug_Refresh".Translate()))
                RefreshCache();

            GUI.enabled = _cachedPawnRules != null;
            if (Widgets.ButtonText(export, "PC_Debug_ExportCSV".Translate()))
                ExportPawnSymbolsCSV();
            GUI.enabled = true;
        }

        private static void ExportPawnSymbolsCSV()
        {
            if (_cachedPawnRules == null || _selectedPawn == null) return;

            try
            {
                var allRules = new System.Collections.Generic.List<Rule>(_cachedPawnRules);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("keyword,value");
                foreach (var rule in allRules.OfType<Rule_String>())
                {
                    string key = $"[{rule.keyword ?? ""}]";
                    string val = (rule.Generate() ?? "").Replace("\"", "\"\""); // escape quotes
                    sb.AppendLine($"\"{key}\",\"{val}\"");
                }

                string fileName  = $"PC_Symbols_{_selectedPawn.LabelShort}_{System.DateTime.Now:yyyyMMdd_HHmm}.csv";
                // Save to RimWorld's save-data folder - same place as saves/screenshots,
                // so it works for local installs, workshop, and any OS.
                string exportDir = System.IO.Path.Combine(
                    GenFilePaths.SaveDataFolderPath, "PawnChronicles");
                System.IO.Directory.CreateDirectory(exportDir);
                string path = System.IO.Path.Combine(exportDir, fileName);

                System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);

                // Open the containing folder so the user can find the file immediately
                Application.OpenURL("file://" + exportDir);
                Messages.Message($"[PawnChronicles] Symbols exported -> {path}",
                    MessageTypeDefOf.SilentInput, false);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[PawnChronicles] CSV export failed: {ex.Message}");
            }
        }

        private static void OpenPawnSelector()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            var options = map.mapPawns.FreeColonists
                .Select(p => new FloatMenuOption(p.LabelShort, () =>
                {
                    _selectedPawn = p;
                    RefreshCache();
                }))
                .ToList();

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void RefreshCache()
        {
            if (_selectedPawn == null || _selectedPawn.Dead || !_selectedPawn.Spawned)
            {
                _cachedPawnRules = null;
                _cachedProfile   = null;
                _cachedPawnName  = null;
                return;
            }

            _cachedPawnName  = _selectedPawn.LabelShort;
            _cachedPawnRules = PawnDataScraper.ScrapeAll(_selectedPawn);
            _cachedProfile   = PawnNarrativeProfile.BuildFor(_selectedPawn);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PAWN PANEL - left = profile, right = raw symbols (independent scroll)
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawPawnPanel(Rect rect)
        {
            if (_cachedPawnRules == null || _selectedPawn == null)
            {
                using (new TextAnchorScope(TextAnchor.MiddleCenter))
                    Widgets.Label(rect, "PC_Debug_SelectPrompt".Translate());
                return;
            }

            float splitX  = rect.width * 0.38f;
            var   leftRect  = new Rect(rect.x,           rect.y, splitX - 4f,          rect.height);
            var   rightRect = new Rect(rect.x + splitX,  rect.y, rect.width - splitX,  rect.height);

            DrawProfilePanel(leftRect);
            // NOTE: rightRect gets its own _pawnRulesScroll - not shared with tag list
            DrawRawRulesPanel(rightRect, _cachedPawnRules, ref _pawnRulesScroll,
                ref _pawnFilter, "pc_");
        }

        private static void DrawProfilePanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.8f));
            Widgets.DrawBox(rect);

            float y = rect.y + 6f;
            float x = rect.x + 6f;
            float w = rect.width - 12f;

            DrawColoredLabel(new Rect(x, y, w, 22f),
                $"◆ {_cachedPawnName}", HeaderColor, FontSize.Small);
            y += 24f;

            var comp = _selectedPawn?.GetComp<CompPersonalChronicles>();
            if (comp != null)
            {
                string epicStatus = comp.hasActiveEpic && comp.currentEpic != null
                    ? $"{comp.currentEpic.modus}  Stage {comp.currentStage + 1}/{comp.currentEpic.stageCount}"
                    : "PC_Debug_NoArc".Translate();

                DrawColoredLabel(new Rect(x, y, w, 18f), "PC_Debug_ArcLabel".Translate(), DimColor, FontSize.Tiny);
                y += 18f;
                DrawColoredLabel(new Rect(x + 8f, y, w - 8f, 18f),
                    epicStatus,
                    comp.hasActiveEpic ? TagHighColor : DimColor, FontSize.Tiny);
                y += 22f;

                DrawColoredLabel(new Rect(x, y, w, 18f),
                    "PC_Debug_ActiveEmbers".Translate(comp.ActiveEmberCount.ToString()), DimColor, FontSize.Tiny);
                y += 22f;

                DrawColoredLabel(new Rect(x, y, w, 18f),
                    "PC_Debug_CompletedEpics".Translate(comp.CompletedEpics.Count().ToString()), DimColor, FontSize.Tiny);
                y += 24f;
            }

            if (_cachedProfile != null)
            {
                DrawColoredLabel(new Rect(x, y, w, 18f),
                    "PC_Debug_NarrativeProfile".Translate(), HeaderColor, FontSize.Tiny);
                y += 20f;

                float total = _cachedProfile.TotalTagWeight();
                DrawColoredLabel(new Rect(x, y, w, 16f),
                    "PC_Debug_TotalWeight".Translate(total.ToString("F0")), DimColor, FontSize.Tiny);
                y += 18f;

                string? dominant = _cachedProfile.DominantTag();
                DrawColoredLabel(new Rect(x, y, w, 16f),
                    "PC_Debug_Dominant".Translate(dominant ?? "none"), TagHighColor, FontSize.Tiny);
                y += 20f;

                var sorted = _cachedProfile.Scores
                    .OrderByDescending(kv => kv.Value)
                    .ToList();

                float remaining   = rect.yMax - y - 6f;
                var   tagScrollRect = new Rect(x, y, w, remaining);
                var   tagViewRect   = new Rect(0, 0, w - 16f, sorted.Count * 20f);

                // ← uses _tagListScroll, NOT _pawnRulesScroll
                Widgets.BeginScrollView(tagScrollRect, ref _tagListScroll, tagViewRect);
                float ty = 0f;
                foreach (var kv in sorted)
                {
                    bool  active = kv.Value >= PawnNarrativeProfile.ActiveThreshold;
                    Color col    = active ? KeyColor : DimColor;

                    float barW = (kv.Value / 100f) * (tagViewRect.width - 60f);
                    Widgets.DrawBoxSolid(
                        new Rect(0, ty + 4f, barW, 12f),
                        active ? new Color(0.2f, 0.5f, 0.2f, 0.4f)
                               : new Color(0.3f, 0.3f, 0.3f, 0.2f));

                    DrawColoredLabel(
                        new Rect(0, ty, tagViewRect.width - 55f, 20f),
                        kv.Key.label ?? kv.Key.defName, col, FontSize.Tiny);
                    DrawColoredLabel(
                        new Rect(tagViewRect.width - 52f, ty, 50f, 20f),
                        kv.Value.ToString("F0"), col, FontSize.Tiny,
                        TextAnchor.MiddleRight);
                    ty += 20f;
                }
                Widgets.EndScrollView();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  TALES & LOG PANEL
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawTalesPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.08f, 0.8f));
            Widgets.DrawBox(rect);

            float y = rect.y + 6f;
            float x = rect.x + 6f;
            float w = rect.width - 12f;

            DrawColoredLabel(new Rect(x, y, w, 20f), "PC_Debug_RecentTales".Translate(), HeaderColor, FontSize.Small);
            y += 30f;

            var tales = Find.TaleManager?.AllTalesListForReading;
            if (tales != null && tales.Count > 0)
            {
                var recent = tales
                    .Where(t => t != null && !t.hidden)
                    .OrderBy(t => t.AgeTicks)
                    .Take(15)
                    .ToList();

                float taleH        = recent.Count * 36f + 8f;
                float taleAreaH    = Mathf.Min(taleH, 220f);
                var   taleScrollRect = new Rect(x, y, w, taleAreaH);
                var   taleViewRect   = new Rect(0, 0, w - 16f, taleH);

                Widgets.BeginScrollView(taleScrollRect, ref _talesScroll, taleViewRect);
                float ty = 0f;
                foreach (var tale in recent)
                {
                    string ageStr   = $"{(int)(tale.AgeTicks / 60000f)}d ago";
                    string interest = tale.InterestLevel.ToString("F2");
                    string pawnName = tale.DominantPawn?.LabelShort ?? "?";

                    DrawColoredLabel(new Rect(0, ty, taleViewRect.width * 0.6f, 30f),
                        tale.ShortSummary, KeyColor, FontSize.Tiny);
                    DrawColoredLabel(
                        new Rect(taleViewRect.width * 0.6f, ty, taleViewRect.width * 0.4f, 30f),
                        $"★{interest}  {ageStr}", DimColor, FontSize.Tiny, TextAnchor.MiddleRight);
                    ty += 18f;
                    DrawColoredLabel(new Rect(8f, ty, taleViewRect.width, 16f),
                        $"{tale.GetType().Name}  ·  {pawnName}", DimColor, FontSize.Tiny);
                    ty += 30f;
                }
                Widgets.EndScrollView();
                y += taleAreaH + 8f;
            }
            else
            {
                DrawColoredLabel(new Rect(x, y, w, 20f), "PC_Debug_NoTales".Translate(), DimColor, FontSize.Tiny);
                y += 24f;
            }

            if (_selectedPawn != null)
            {
                DrawColoredLabel(new Rect(x, y, w, 20f),
                    "PC_Debug_Ch-onicleOf".Translate(_selectedPawn.LabelShort), HeaderColor, FontSize.Small);
                y += 22f;

                var comp = _selectedPawn.GetComp<CompPersonalChronicles>();
                var log  = comp?.ChronicleLog;

                if (log != null && log.Count > 0)
                {
                    float remaining = rect.yMax - y - 6f;
                    var   logScroll = new Rect(x, y, w, remaining);
                    var   logView   = new Rect(0, 0, w - 16f, log.Count * 22f);

                    // ← uses _chronicleScroll, NOT a local Vector2
                    Widgets.BeginScrollView(logScroll, ref _chronicleScroll, logView);
                    float ly = 0f;
                    foreach (var entry in log.Reverse())
                    {
                        DrawColoredLabel(new Rect(0, ly, logView.width, 20f),
                            entry, ValueColor, FontSize.Tiny);
                        ly += 22f;
                    }
                    Widgets.EndScrollView();
                }
                else
                {
                    DrawColoredLabel(new Rect(x, y, w, 20f),
                        "PC_Debug_NoChronicle".Translate(), DimColor, FontSize.Tiny);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PERFORMANCE PANEL
        // ─────────────────────────────────────────────────────────────────────

        private static Color PerfColor(long value, long warnThreshold, long badThreshold)
        {
            if (value >= badThreshold)  return new Color(1f, 0.35f, 0.35f);   // red
            if (value >= warnThreshold) return new Color(1f, 0.85f, 0.2f);    // yellow
            return new Color(0.4f, 0.9f, 0.4f);                               // green
        }

        private static void DrawPerfRow(float x, ref float y, float w,
            string label, string value, Color valueColor)
        {
            float labelW = w * 0.62f;
            GUI.color = DimColor;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(x, y, labelW, 18f), label);
            GUI.color = valueColor;
            using (new TextAnchorScope(TextAnchor.MiddleRight))
                Widgets.Label(new Rect(x + labelW, y, w - labelW, 18f), value);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 19f;
        }

        private static void DrawPerfPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.08f, 0.8f));
            Widgets.DrawBox(rect);

            float x    = rect.x + 10f;
            float y    = rect.y + 8f;
            float w    = rect.width - 20f;
            float colW = w * 0.48f;
            float gap  = w * 0.04f;

            // ── Compute derived values up front ───────────────────────────────
            int activeComps = Find.CurrentMap?.mapPawns
                ?.FreeColonists?.Count(p => p.GetComp<CompPersonalChronicles>() != null) ?? 0;

            long tickUs      = CompPersonalChronicles.LastTickUs;
            long peakUs      = CompPersonalChronicles.PeakTickUs;
            long colonyUs    = tickUs * activeComps;  // estimated total per-tick budget

            long gramLast    = NarrativeGrammarResolver.LastResolveMs;
            long gramPeak    = NarrativeGrammarResolver.PeakResolveMs;
            int  gramCnt     = NarrativeGrammarResolver.ResolveCount;
            int  gramFall    = NarrativeGrammarResolver.FallbackCount;
            int  gramOk      = gramCnt - gramFall;
            string fallRate  = gramCnt > 0
                ? $"{gramFall}/{gramCnt}  ({gramFall * 100 / gramCnt}% fallback)"
                : "—";
            Color fallColor  = gramCnt == 0   ? Color.white
                             : gramFall == 0  ? new Color(0.4f, 0.9f, 0.4f)
                             : gramFall < gramCnt / 2 ? new Color(1f, 0.85f, 0.2f)
                             : new Color(1f, 0.35f, 0.35f);

            long pawnLast    = PawnDataScraper.LastScrapeMs;
            long pawnPeak    = PawnDataScraper.PeakScrapeMs;
            int  pawnCnt     = PawnDataScraper.ScrapeCount;

            // ── Header ────────────────────────────────────────────────────────
            DrawColoredLabel(new Rect(x, y, w, 22f),
                "PC_Debug_Perf_Title".Translate(), HeaderColor, FontSize.Small);
            y += 26f;

            GUI.color = DimColor;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(x, y, w, 16f), "PC_Debug_Perf_Hint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;

            // Two-column layout
            float leftX  = x;
            float rightX = x + colW + gap;
            float leftY  = y;
            float rightY = y;

            // ── LEFT: CompTick ────────────────────────────────────────────────
            DrawColoredLabel(new Rect(leftX, leftY, colW, 18f),
                "PC_Debug_Perf_CompTick".Translate(), HeaderColor, FontSize.Tiny);
            leftY += 20f;

            DrawPerfRow(leftX, ref leftY, colW,
                "PC_Debug_Perf_ActiveComps".Translate(),
                activeComps.ToString(),
                Color.white);
            DrawPerfRow(leftX, ref leftY, colW,
                "PC_Debug_Perf_Last".Translate(),
                $"{tickUs} µs",
                PerfColor(tickUs, 500, 5000));
            DrawPerfRow(leftX, ref leftY, colW,
                "PC_Debug_Perf_Peak".Translate(),
                $"{peakUs} µs",
                PerfColor(peakUs, 500, 5000));
            DrawPerfRow(leftX, ref leftY, colW,
                "PC_Debug_Perf_ColonyBudget".Translate(),
                $"~{colonyUs / 1000f:F1} ms/tick",
                PerfColor(colonyUs / 1000, 1, 10));
            leftY += 8f;

            // ── LEFT: Grammar Resolver ────────────────────────────────────────
            DrawColoredLabel(new Rect(leftX, leftY, colW, 18f),
                "PC_Debug_Perf_Grammar".Translate(), HeaderColor, FontSize.Tiny);
            leftY += 20f;

            DrawPerfRow(leftX, ref leftY, colW,
                "PC_Debug_Perf_Last".Translate(),
                $"{gramLast} ms",
                PerfColor(gramLast, 5, 50));
            DrawPerfRow(leftX, ref leftY, colW,
                "PC_Debug_Perf_Peak".Translate(),
                $"{gramPeak} ms",
                PerfColor(gramPeak, 5, 50));
            DrawPerfRow(leftX, ref leftY, colW,
                "PC_Debug_Perf_FallbackRate".Translate(),
                fallRate,
                fallColor);

            // ── RIGHT: Pawn Scraper ───────────────────────────────────────────
            DrawColoredLabel(new Rect(rightX, rightY, colW, 18f),
                "PC_Debug_Perf_PawnScraper".Translate(), HeaderColor, FontSize.Tiny);
            rightY += 20f;

            DrawPerfRow(rightX, ref rightY, colW,
                "PC_Debug_Perf_Last".Translate(),
                $"{pawnLast} ms",
                PerfColor(pawnLast, 5, 50));
            DrawPerfRow(rightX, ref rightY, colW,
                "PC_Debug_Perf_Peak".Translate(),
                $"{pawnPeak} ms",
                PerfColor(pawnPeak, 5, 50));
            DrawPerfRow(rightX, ref rightY, colW,
                "PC_Debug_Perf_Count".Translate(),
                pawnCnt.ToString(),
                Color.white);

            // ── Reset button ──────────────────────────────────────────────────
            float bottomY = Mathf.Max(leftY, rightY) + 16f;
            if (Widgets.ButtonText(new Rect(x, bottomY, 120f, 26f),
                "PC_Debug_Perf_Reset".Translate()))
            {
                CompPersonalChronicles.LastTickUs          = 0;
                CompPersonalChronicles.PeakTickUs          = 0;
                PawnDataScraper.LastScrapeMs               = 0;
                PawnDataScraper.PeakScrapeMs               = 0;
                PawnDataScraper.ScrapeCount                = 0;
                NarrativeGrammarResolver.LastResolveMs     = 0;
                NarrativeGrammarResolver.PeakResolveMs     = 0;
                NarrativeGrammarResolver.ResolveCount      = 0;
                NarrativeGrammarResolver.FallbackCount     = 0;
            }

            // ── Legend ────────────────────────────────────────────────────────
            float legX = x + 130f;
            float legY = bottomY + 4f;
            GUI.color = new Color(0.4f, 0.9f, 0.4f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(legX,        legY, 80f, 18f), "PC_Debug_Perf_Good".Translate());
            GUI.color = new Color(1f, 0.85f, 0.2f);
            Widgets.Label(new Rect(legX + 80f,  legY, 80f, 18f), "PC_Debug_Perf_Warn".Translate());
            GUI.color = new Color(1f, 0.35f, 0.35f);
            Widgets.Label(new Rect(legX + 160f, legY, 80f, 18f), "PC_Debug_Perf_Bad".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SHARED RAW RULES PANEL
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawRawRulesPanel(
            Rect rect, List<Rule> rules,
            ref Vector2 scroll, ref string filter,
            string defaultPrefix)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.08f, 0.8f));
            Widgets.DrawBox(rect);

            float x = rect.x + 6f;
            float y = rect.y + 6f;
            float w = rect.width - 12f;

            var filterRect = new Rect(x, y, w, 24f);
            filter = Widgets.TextField(filterRect, filter);
            if (string.IsNullOrEmpty(filter))
            {
                GUI.color = DimColor;
                using (new TextAnchorScope(TextAnchor.MiddleLeft))
                    Widgets.Label(new Rect(x + 4f, y, w, 24f), "PC_Debug_FilterPlaceholder".Translate());
                GUI.color = Color.white;
            }
            y += 28f;

            var rawStrings = rules.OfType<Rule_String>().ToList();
            string filterCopy = filter;
            var filtered = string.IsNullOrEmpty(filterCopy)
                ? rawStrings
                : rawStrings.Where(r =>
                    r.keyword.IndexOf(filterCopy, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (r.Generate()?.IndexOf(filterCopy,
                        System.StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                .ToList();

            DrawColoredLabel(new Rect(x, y, w, 18f),
                "PC_Debug_SymbolCount".Translate(filtered.Count.ToString(), rawStrings.Count.ToString()), DimColor, FontSize.Tiny);
            y += 20f;

            float lineH    = 40f;
            float viewH    = filtered.Count * lineH;
            var   listRect = new Rect(x, y, w, rect.yMax - y - 6f);
            var   viewRect = new Rect(0, 0, w - 20f, Mathf.Max(viewH, listRect.height));

            Widgets.BeginScrollView(listRect, ref scroll, viewRect);

            float vy   = 0f;
            float colW = viewRect.width * 0.65f;
            foreach (var rule in filtered)
            {
                string val = rule.Generate() ?? "";
                DrawColoredLabel(new Rect(0, vy, colW, lineH),
                    rule.keyword, KeyColor, FontSize.Tiny);
                DrawColoredLabel(new Rect(colW + 4f, vy, viewRect.width - colW - 4f, lineH),
                    val, ValueColor, FontSize.Tiny);
                vy += lineH;
            }

            Widgets.EndScrollView();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────────────────────

                /// <summary>Estimates how often events fire per pawn per day given current settings.</summary>
        private static string EstimateEventFrequency(PawnChroniclesSettings s)
        {
            float minGap    = s.eventCooldownDays;
            float avgGap    = 1f / Mathf.Max(s.eventDailyChance, 0.001f);
            float effective = Mathf.Max(minGap, avgGap);
            if (!s.sparksEnabled && !s.embersEnabled) return "PC_Settings_MomentsDisabled".Translate();
            if (effective < 1f) return "PC_Settings_MomentsPerDay".Translate((1f / effective).ToString("F1"));
            return "PC_Settings_MomentsEvery".Translate(effective.ToString("F0"));
        }

        /// <summary>
        /// Draws a checkbox row that looks disabled and ignores all clicks.
        /// Uses Widgets.CheckboxDraw(disabled:true) so the control is visually
        /// greyed-out and no input events are consumed.
        /// </summary>
        private static void DrawDisabledCheckbox(Listing_Standard ls, string label, bool value)
        {
            Rect row = ls.GetRect(Text.LineHeight);
            Widgets.CheckboxDraw(row.x, row.y, value, disabled: true);
            Widgets.Label(new Rect(row.x + 30f, row.y, row.width - 30f, row.height), label);
        }

        private static void SectionHeader(Listing_Standard ls, string label)
        {
            ls.Gap(4f);
            GUI.color = HeaderColor;
            ls.Label(label);
            GUI.color = Color.white;
            ls.GapLine(4f);
        }

        private static void DrawColoredLabel(
            Rect rect, string text, Color color,
            FontSize size = FontSize.Small,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            Text.Font = size == FontSize.Tiny ? GameFont.Tiny : GameFont.Small;
            GUI.color = color;
            using (new TextAnchorScope(anchor))
                Widgets.Label(rect, text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private enum FontSize { Tiny, Small }
    }

    internal struct TextAnchorScope : System.IDisposable
    {
        private readonly TextAnchor _prev;
        public TextAnchorScope(TextAnchor anchor) { _prev = Text.Anchor; Text.Anchor = anchor; }
        public void Dispose() => Text.Anchor = _prev;
    }
}
