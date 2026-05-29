using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace PawnChronicles
{
    /// <summary>
    /// Renders the Chronicles tab content for a pawn.
    ///
    /// Layout:
    ///   ┌─────────────────────────────────────────┐
    ///   │  [Epic title]                           │
    ///   │  Tag profile line                       │
    ///   │  ─────────────────────────────          │
    ///   │  [Stage 1 title]                        │
    ///   │  Stage 1 prose body                     │
    ///   │  ─────────────────────────────          │
    ///   │  [Stage 2 title]  (if unlocked)         │
    ///   │  Stage 2 prose body                     │
    ///   │  ─────────────────────────────          │
    ///   │  ▸ Waiting: "after Nagam's next kill"   │  ← condition not yet met
    ///   │    [Next ▶]  [Dev: Next]                │  ← Next only when met / dev always
    ///   └─────────────────────────────────────────┘
    ///
    /// Called from the pawn's Chronicles tab (ITab_Chronicles or equivalent).
    /// </summary>
    public static class ChroniclesTabUI
    {
        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color TitleColor     = new Color(0.90f, 0.80f, 0.50f);
        private static readonly Color BodyColor      = new Color(0.88f, 0.88f, 0.88f);
        private static readonly Color DimColor       = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color WaitColor      = new Color(0.60f, 0.75f, 0.90f);
        private static readonly Color MetColor       = new Color(0.50f, 0.90f, 0.50f);
        private static readonly Color SectionLine    = new Color(0.30f, 0.30f, 0.30f);
        private static readonly Color EntryBg        = new Color(0.08f, 0.08f, 0.08f, 0.6f);
        private static readonly Color ActiveEntryBg  = new Color(0.10f, 0.12f, 0.10f, 0.8f);

        private static Vector2 _scroll = Vector2.zero;

        // ─────────────────────────────────────────────────────────────────────
        //  ENTRY POINT
        // ─────────────────────────────────────────────────────────────────────

        public static void Draw(Rect rect, Pawn pawn, CompPersonalChronicles comp)
        {
            if (pawn == null || comp == null) return;

            Widgets.DrawBoxSolid(rect, new Color(0.07f, 0.07f, 0.07f, 0.9f));

            float contentWidth = rect.width - 20f;

            // ── Measure total height needed ───────────────────────────────────
            float totalH = MeasureContent(comp, contentWidth);
            var   viewRect = new Rect(0, 0, contentWidth, Mathf.Max(totalH, rect.height));

            Widgets.BeginScrollView(rect, ref _scroll, viewRect);
            float y = 0f;

            DrawEpicHeader(ref y, contentWidth, pawn, comp);
            DrawArcEntries(ref y, contentWidth, pawn, comp);
            DrawWaitAndAdvance(ref y, contentWidth, pawn, comp);
            DrawEmberSection(ref y, contentWidth, comp);

            Widgets.EndScrollView();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  EPIC HEADER
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawEpicHeader(
            ref float y, float w, Pawn pawn, CompPersonalChronicles comp)
        {
            // Tag profile line
            var profile = comp.currentProfile ?? comp.GetOrBuildProfile();
            var topTags = profile.Scores
                .Where(kv => kv.Value >= PawnNarrativeProfile.ActiveThreshold)
                .OrderByDescending(kv => kv.Value)
                .Take(5)
                .Select(kv => $"{kv.Key.label} ({(int)kv.Value})")
                .ToList();

            string tagLine = topTags.Count > 0
                ? string.Join(" · ", topTags)
                : "No strong narrative threads yet.";

            Text.Font = GameFont.Tiny;
            GUI.color = DimColor;
            float tagH = Text.CalcHeight(tagLine, w - 8f);
            Widgets.Label(new Rect(4f, y, w - 8f, tagH), tagLine);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += tagH + 6f;

            // Active epic title
            if (comp.hasActiveEpic && comp.currentEpic != null)
            {
                DrawHRule(y, w);
                y += 6f;

                string epicLabel = comp.currentEpic.label.ToUpper();
                Text.Font = GameFont.Small;
                GUI.color = TitleColor;
                float epicH = Text.CalcHeight(epicLabel, w - 8f);
                Widgets.Label(new Rect(4f, y, w - 8f, epicH), epicLabel);
                GUI.color = Color.white;
                y += epicH;

                // Stage progress
                int total   = comp.currentEpic.stageCount;
                int current = comp.arcEntries.Count(e => e.IsResolved) + 1;
                current = Mathf.Min(current, total);
                string stageStr = $"Stage {current} / {total}";
                Text.Font = GameFont.Tiny;
                GUI.color = DimColor;
                Widgets.Label(new Rect(4f, y, w - 8f, 18f), stageStr);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += 22f;
            }
            else if (!comp.hasActiveEpic)
            {
                DrawHRule(y, w);
                y += 6f;
                Text.Font = GameFont.Tiny;
                GUI.color = DimColor;
                Widgets.Label(new Rect(4f, y, w - 8f, 18f),
                    "No active arc. A story will begin when the time is right.");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += 24f;
            }

            DrawHRule(y, w);
            y += 8f;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ARC ENTRIES - one block per resolved + current stage
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawArcEntries(
            ref float y, float w, Pawn pawn, CompPersonalChronicles comp)
        {
            if (comp.arcEntries == null || comp.arcEntries.Count == 0) return;

            foreach (var entry in comp.arcEntries)
            {
                bool isCurrent = !entry.IsResolved;
                bool isResolved = entry.IsResolved;

                // Background
                float entryH = MeasureEntry(entry, w);
                Widgets.DrawBoxSolid(
                    new Rect(0, y, w, entryH),
                    isCurrent ? ActiveEntryBg : EntryBg);

                float ey = y + 6f;

                // Role tag
                Text.Font = GameFont.Tiny;
                GUI.color = RoleColor(entry.stageRole);
                string roleTag = $"[ {entry.stageRole.ToUpper()} ]";
                Widgets.Label(new Rect(6f, ey, w - 12f, 16f), roleTag);
                GUI.color = Color.white;
                ey += 18f;

                // Title
                Text.Font = GameFont.Small;
                GUI.color = isResolved ? new Color(TitleColor.r, TitleColor.g, TitleColor.b, 0.7f)
                                       : TitleColor;
                float titleH = Text.CalcHeight(entry.title, w - 12f);
                Widgets.Label(new Rect(6f, ey, w - 12f, titleH), entry.title);
                GUI.color = Color.white;
                ey += titleH + 4f;

                // Body
                Text.Font = GameFont.Small;
                GUI.color = isResolved ? new Color(BodyColor.r, BodyColor.g, BodyColor.b, 0.6f)
                                       : BodyColor;
                float bodyH = Text.CalcHeight(entry.body, w - 12f);
                Widgets.Label(new Rect(6f, ey, w - 12f, bodyH), entry.body);
                GUI.color = Color.white;
                ey += bodyH + 6f;

                y += entryH + 4f;
                DrawHRule(y, w);
                y += 6f;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  WAIT CONDITION + NEXT BUTTON
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawWaitAndAdvance(
            ref float y, float w, Pawn pawn, CompPersonalChronicles comp)
        {
            if (!comp.hasActiveEpic) return;

            var entry = comp.CurrentEntry;
            if (entry == null) return;
            if (entry.IsResolved) return;

            bool condMet  = entry.conditionMet;
            bool devMode  = DebugSettings.godMode;

            // Wait condition text
            Text.Font = GameFont.Tiny;
            string waitText = condMet
                ? $"✓ {entry.waitConditionLabel}"
                : $"▸ {entry.waitConditionLabel}";
            GUI.color = condMet ? MetColor : WaitColor;
            float waitH = Text.CalcHeight(waitText, w - 12f);
            Widgets.Label(new Rect(6f, y, w - 12f, waitH), waitText);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += waitH + 8f;

            // Buttons
            float btnH  = 30f;
            float btnW  = 120f;
            float gap   = 8f;
            float totalBtnW = condMet ? (devMode ? btnW * 2f + gap : btnW)
                                       : (devMode ? btnW : 0f);
            float btnX  = (w - totalBtnW) / 2f;

            if (condMet)
            {
                var nextRect = new Rect(btnX, y, btnW, btnH);
                if (DrawStyledButton(nextRect, "Next  ▶", TitleColor))
                    comp.PlayerAdvanceStage(devMode: false);
                btnX += btnW + gap;
            }

            if (devMode)
            {
                var devRect = new Rect(btnX, y, btnW, btnH);
                GUI.color = new Color(0.9f, 0.5f, 0.2f);
                if (DrawStyledButton(devRect, "Dev: Next", new Color(0.9f, 0.5f, 0.2f)))
                    comp.PlayerAdvanceStage(devMode: true);
                GUI.color = Color.white;
            }

            y += btnH + 12f;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  EMBER SECTION - chronicle log entries (embers / sparks)
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawEmberSection(
            ref float y, float w, CompPersonalChronicles comp)
        {
            var log = comp.ChronicleLog;

            // Show only ember/spark entries (not arc entries - those are above)
            var emberEntries = log
                .Where(e => e.Contains("Ember:") || e.Contains("Spark ["))
                .Reverse()
                .Take(10)
                .ToList();

            if (emberEntries.Count == 0) return;

            DrawHRule(y, w);
            y += 8f;

            Text.Font = GameFont.Tiny;
            GUI.color = DimColor;
            Widgets.Label(new Rect(4f, y, w - 8f, 16f), "DAILY MOMENTS");
            GUI.color = Color.white;
            y += 20f;

            foreach (var entry in emberEntries)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(BodyColor.r, BodyColor.g, BodyColor.b, 0.65f);
                float h = Text.CalcHeight(entry, w - 12f);
                Widgets.Label(new Rect(6f, y, w - 12f, h), entry);
                GUI.color = Color.white;
                y += h + 3f;
            }

            Text.Font = GameFont.Small;
            y += 8f;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  MEASUREMENT (for scroll view height)
        // ─────────────────────────────────────────────────────────────────────

        private static float MeasureContent(CompPersonalChronicles comp, float w)
        {
            float h = 80f; // header estimate

            if (comp.arcEntries != null)
                foreach (var e in comp.arcEntries)
                    h += MeasureEntry(e, w) + 10f;

            h += 80f; // wait + button area
            h += 120f; // ember section estimate
            return h;
        }

        private static float MeasureEntry(ArcStageEntry entry, float w)
        {
            Text.Font = GameFont.Tiny;
            float roleH = 18f;
            Text.Font = GameFont.Small;
            float titleH = Text.CalcHeight(entry.title, w - 12f);
            float bodyH  = Text.CalcHeight(entry.body,  w - 12f);
            return 6f + roleH + 4f + titleH + 4f + bodyH + 6f;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static void DrawHRule(float y, float w)
        {
            Widgets.DrawBoxSolid(new Rect(0, y, w, 1f), SectionLine);
        }

        private static bool DrawStyledButton(Rect rect, string label, Color borderColor)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f));
            Widgets.DrawBox(rect);

            using (new TextAnchorScope(TextAnchor.MiddleCenter))
            {
                GUI.color = borderColor;
                Widgets.Label(rect, label);
                GUI.color = Color.white;
            }

            return Widgets.ButtonInvisible(rect);
        }

        private static Color RoleColor(string role) => role switch
        {
            NarrativeGrammarResolver.RoleOpening => new Color(0.70f, 0.85f, 1.00f),
            NarrativeGrammarResolver.RoleMiddle  => new Color(0.85f, 0.85f, 0.70f),
            NarrativeGrammarResolver.RoleSuccess => new Color(0.60f, 0.90f, 0.60f),
            NarrativeGrammarResolver.RoleFailure => new Color(0.90f, 0.55f, 0.55f),
            "ember"                              => new Color(0.75f, 0.65f, 0.90f),
            _                                    => DimColor
        };
    }
}
