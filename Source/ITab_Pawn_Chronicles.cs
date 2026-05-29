using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Grammar;
using RimWorld;

namespace PawnChronicles
{
    public class ITab_Pawn_Chronicles : ITab
    {
        // REWRITTEN - clean two-pane layout
        // Left : arc chapter list + diary (spark/ember only)
        // Right: profile + tag breakdown  /  arc detail  /  diary detail
        // No ChroniclesTabUI.Draw calls. No cycling tag button. No snapshot bleed.
        // Each pane uses GUI.BeginGroup for 0-based local coords.

        // ── Layout - driven by UI settings ───────────────────────────────────
        private static float Pad       => PawnChroniclesMod.Settings.uiPadding;
        private static float LeftRatio => PawnChroniclesMod.Settings.uiLeftRatio;
        private static float PaneSplit => PawnChroniclesMod.Settings.uiPaneSplit;
        private static float ArcRowH   => PawnChroniclesMod.Settings.uiArcRowH;
        private static float DiaryRowH => PawnChroniclesMod.Settings.uiDiaryRowH;
        private static float TagRowH   => PawnChroniclesMod.Settings.uiTagRowH;
        private const  float PortraitW = 118f;
        private const  float PortraitH = 148f;

        // ── Resize ────────────────────────────────────────────────────────────
        private enum ResizeEdge { None, Right, Top }
        private ResizeEdge _resizeEdge      = ResizeEdge.None;
        private Vector2    _resizeDragStart = Vector2.zero;  // screen space, updated each frame
        private static readonly Vector2 SizeMin     = new Vector2(720f,  450f);
        private static readonly Vector2 SizeMax     = new Vector2(1600f, 950f);
        private const           float   EdgeHit     = 12f;

        // ── Scroll ────────────────────────────────────────────────────────────
        private Vector2 _leftScroll = Vector2.zero;
        private Vector2 _tagScroll  = Vector2.zero;
        private Vector2 _snapScroll = Vector2.zero;

        // ── Selection ─────────────────────────────────────────────────────────
        private ArcStageEntry?   _selEntry       = null;
        private ArcStageEntry?   _selSharedEntry = null;  // entangled arc entry
        private string?          _selDiary = null;
        private NarrativeTagDef? _selTag   = null;

        // ── Colors - user-facing ones driven by UI settings ──────────────────
        private static Color CG  => PawnChroniclesMod.Settings.ColorGold;   // gold / accent
        private static Color CB  => PawnChroniclesMod.Settings.ColorBody;   // body text
        private static Color CD  => PawnChroniclesMod.Settings.ColorDim;    // dim / secondary
        private static Color CDy => PawnChroniclesMod.Settings.ColorDiary;  // diary blue
        private static Color CBg => PawnChroniclesMod.Settings.ColorBg;     // pane background
        // Semantic / functional colors - hardcoded
        private static readonly Color CR     = new Color(0.28f, 0.28f, 0.30f);  // rule / divider
        private static readonly Color COk    = new Color(0.50f, 0.90f, 0.50f);  // green
        private static readonly Color CFail  = new Color(0.90f, 0.50f, 0.50f);  // red
        private static readonly Color CWait  = new Color(0.60f, 0.75f, 0.90f);  // wait
        private static readonly Color CEnt   = new Color(0.85f, 0.65f, 1.00f);  // entangled arc purple
        private static readonly Color CBgSel = new Color(0.14f, 0.17f, 0.14f, 1.00f);
        private static readonly Color CBgAct = new Color(0.10f, 0.13f, 0.10f, 0.92f);
        private static readonly Color CBgPnl = new Color(0.09f, 0.09f, 0.11f, 0.90f);
        // ─────────────────────────────────────────────────────────────────
        //  INIT
        // ─────────────────────────────────────────────────────────────────

        public ITab_Pawn_Chronicles()
        {
            var s = PawnChroniclesMod.Settings;
            size     = new Vector2(
                Mathf.Clamp(s.chroniclesWindowWidth,  SizeMin.x, SizeMax.x),
                Mathf.Clamp(s.chroniclesWindowHeight, SizeMin.y, SizeMax.y));
            labelKey = "TabChronicles";
        }

        // ─────────────────────────────────────────────────────────────────
        //  ENTRY POINT
        // ─────────────────────────────────────────────────────────────────

        protected override void FillTab()
        {
            Pawn pawn = SelPawn;
            if (pawn == null) return;
            var comp = pawn.GetComp<CompPersonalChronicles>();
            if (comp == null) return;

            // Keep size in sync with settings - picks up slider changes immediately.
            // Skip while edge-dragging so the drag isn't fighting itself.
            if (_resizeEdge == ResizeEdge.None)
                size = new Vector2(
                    Mathf.Clamp(PawnChroniclesMod.Settings.chroniclesWindowWidth,  SizeMin.x, SizeMax.x),
                    Mathf.Clamp(PawnChroniclesMod.Settings.chroniclesWindowHeight, SizeMin.y, SizeMax.y));

            // Extra inset so the engine's close button (top-right) has breathing room
            Rect outer = new Rect(0f, 0f, size.x, size.y).ContractedBy(22f);

            float lw = outer.width * LeftRatio;
            float rw = outer.width - lw - PaneSplit;

            Rect lr = new Rect(outer.x,                    outer.y, lw, outer.height);
            Rect rr = new Rect(outer.x + lw + PaneSplit,   outer.y, rw, outer.height);

            Widgets.DrawBoxSolid(lr, CBg);
            Widgets.DrawBoxSolid(rr, CBg);

            GUI.BeginGroup(lr);
            DrawLeft(new Rect(0f, 0f, lw, outer.height), pawn, comp);
            GUI.EndGroup();

            GUI.BeginGroup(rr);
            DrawRight(new Rect(0f, 0f, rw, outer.height), pawn, comp);
            GUI.EndGroup();

            // ── Resize handles ────────────────────────────────────────────────
            // Right edge: full height
            Rect rightEdge = new Rect(size.x - EdgeHit, EdgeHit,
                                      EdgeHit, size.y - EdgeHit);
            // Top edge: full width minus close-button area on the right
            Rect topEdge   = new Rect(EdgeHit, 0f,
                                      size.x - 30f - EdgeHit, EdgeHit);

            // Edge highlight feedback
            Color edgeHover = new Color(1f, 1f, 1f, 0.12f);
            if (_resizeEdge == ResizeEdge.Right || Mouse.IsOver(rightEdge))
                Widgets.DrawBoxSolid(rightEdge, edgeHover);
            if (_resizeEdge == ResizeEdge.Top || Mouse.IsOver(topEdge))
                Widgets.DrawBoxSolid(topEdge, edgeHover);

            TooltipHandler.TipRegion(rightEdge, "PC_ITab_ResizeTip".Translate());
            TooltipHandler.TipRegion(topEdge,   "PC_ITab_ResizeTip".Translate());

            // ── Interaction ───────────────────────────────────────────────────
            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0 && _resizeEdge == ResizeEdge.None)
            {
                if (rightEdge.Contains(e.mousePosition))
                {
                    _resizeEdge      = ResizeEdge.Right;
                    _resizeDragStart = GUIUtility.GUIToScreenPoint(e.mousePosition);
                    e.Use();
                }
                else if (topEdge.Contains(e.mousePosition))
                {
                    _resizeEdge      = ResizeEdge.Top;
                    _resizeDragStart = GUIUtility.GUIToScreenPoint(e.mousePosition);
                    e.Use();
                }
            }

            if (e.type == EventType.MouseDrag && _resizeEdge != ResizeEdge.None)
            {
                Vector2 screen = GUIUtility.GUIToScreenPoint(e.mousePosition);
                Vector2 delta  = screen - _resizeDragStart;
                _resizeDragStart = screen;

                switch (_resizeEdge)
                {
                    case ResizeEdge.Right:
                        size = new Vector2(
                            Mathf.Clamp(size.x + delta.x, SizeMin.x, SizeMax.x),
                            size.y);
                        break;
                    case ResizeEdge.Top:
                        // Dragging up = negative delta.y = increase height
                        size = new Vector2(
                            size.x,
                            Mathf.Clamp(size.y - delta.y, SizeMin.y, SizeMax.y));
                        break;
                }
                e.Use();
            }

            if (e.type == EventType.MouseUp && _resizeEdge != ResizeEdge.None)
            {
                _resizeEdge = ResizeEdge.None;
                var s = PawnChroniclesMod.Settings;
                s.chroniclesWindowWidth  = size.x;
                s.chroniclesWindowHeight = size.y;
                PawnChroniclesMod.Settings.Write();
                e.Use();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  LEFT PANE - chapter list + diary
        // ─────────────────────────────────────────────────────────────────

        private void DrawLeft(Rect pane, Pawn pawn, CompPersonalChronicles comp)
        {
            // Header
            const float ToggleW = 82f;
            Text.Font = GameFont.Small;
            GUI.color  = CG;
            Widgets.Label(new Rect(Pad, 10f, pane.width - Pad * 2f - ToggleW - 4f, 24f),
                "PC_ITab_ChroniclesOf".Translate(pawn.LabelShort));
            GUI.color  = Color.white;

            // Per-pawn disable toggle (top-right of header)
            Rect toggleRect = new Rect(pane.width - Pad - ToggleW, 8f, ToggleW, 22f);
            bool isDisabled = comp.chroniclesDisabled;
            GUI.color = isDisabled ? CFail : COk;
            string toggleLabel = isDisabled
                ? "PC_ITab_ChroniclesInactive".Translate()
                : "PC_ITab_ChroniclesActive".Translate();
            string toggleTip = isDisabled
                ? "PC_ITab_ChroniclesInactive_Tip".Translate()
                : "PC_ITab_ChroniclesActive_Tip".Translate();
            if (Widgets.ButtonText(toggleRect, toggleLabel))
                comp.chroniclesDisabled = !comp.chroniclesDisabled;
            TooltipHandler.TipRegion(toggleRect, toggleTip);
            GUI.color = Color.white;

            Widgets.DrawLineHorizontal(Pad, 38f, pane.width - Pad * 2f, CR);

            // Scrollable content
            Rect scrollOuter = new Rect(0f, 44f, pane.width, pane.height - 44f);

            // Short-circuit if chronicles are disabled for this pawn
            if (comp.chroniclesDisabled)
            {
                GUI.color = CD;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(Pad, scrollOuter.y + 12f, pane.width - Pad * 2f, 40f),
                    "PC_ITab_ChroniclesDisabledBody".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                return;
            }

            var arc         = comp.arcEntries ?? new List<ArcStageEntry>();
            var diary       = GetDiary(comp);
            var sharedArc   = comp.GetEntangledArc();
            var sharedEntries = sharedArc?.sharedEntries ?? new List<ArcStageEntry>();

            float contentH =
                (sharedEntries.Count > 0 ? 22f + sharedEntries.Count * ArcRowH + 6f : 0f) +
                (arc.Count   > 0 ? 22f + arc.Count   * ArcRowH   + 6f : 0f) +
                (diary.Count > 0 ? 22f + diary.Count * DiaryRowH + 4f : 0f) +
                16f;

            Rect view = new Rect(0f, 0f, scrollOuter.width - 16f,
                Mathf.Max(contentH, scrollOuter.height));

            Widgets.BeginScrollView(scrollOuter, ref _leftScroll, view);
            float y = 6f;

            // ── Shared (entangled) arc ─────────────────────────────────────
            if (sharedEntries.Count > 0 && sharedArc != null)
            {
                var other = sharedArc.OtherPawn(pawn);
                string header = other != null
                    ? "PC_ITab_SharedArcWith".Translate(other.LabelShort.ToUpper())
                    : "PC_ITab_SharedArc".Translate();
                DrawSectionLabel(ref y, view.width, header, CEnt);
                foreach (var entry in sharedEntries)
                    DrawSharedArcRow(ref y, view.width, entry, sharedArc, comp);
                y += 6f;
            }

            // ── Personal arc chapters ──────────────────────────────────────
            if (arc.Count > 0)
            {
                DrawSectionLabel(ref y, view.width, "PC_ITab_ArcChapters".Translate());
                foreach (var entry in arc)
                    DrawArcRow(ref y, view.width, entry, comp);
                y += 6f;
            }

            if (diary.Count > 0)
            {
                DrawSectionLabel(ref y, view.width, "PC_ITab_Diary".Translate(), CDy);
                foreach (var log in diary)
                    DrawDiaryRow(ref y, view.width, log);
            }

            Widgets.EndScrollView();
        }

        private void DrawArcRow(ref float y, float w,
            ArcStageEntry entry, CompPersonalChronicles comp)
        {
            bool isSel = _selEntry == entry;
            bool isCur = comp.CurrentEntry == entry;

            Rect row = new Rect(0f, y, w, ArcRowH);
            Widgets.DrawBoxSolid(row,
                isSel ? CBgSel : isCur ? CBgAct : Color.clear);
            if (Mouse.IsOver(row) && !isSel) Widgets.DrawHighlight(row);

            // Role colour strip (3 px left edge)
            Widgets.DrawBoxSolid(
                new Rect(0f, y + 2f, 3f, ArcRowH - 4f),
                RoleColor(entry.stageRole));

            // Title
            Text.Font = GameFont.Small;
            GUI.color  = isSel ? Color.white : (isCur ? CG : CB);
            Widgets.Label(new Rect(10f, y + 6f, w - 14f, 22f),
                entry.title ?? "PC_ITab_DefaultChapter".Translate());
            GUI.color = Color.white;

            // Subtitle
            Text.Font = GameFont.Tiny;
            GUI.color  = CD;
            string suffix = isCur ? "  · ACTIVE"
                          : entry.IsResolved ? "  · resolved" : "";
            Widgets.Label(new Rect(10f, y + 30f, w - 14f, 18f),
                "PC_ITab_DayStage".Translate(entry.writtenAtTick / 60000, entry.stageRole.ToUpper() + suffix));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            if (Widgets.ButtonInvisible(row))
            {
                _selEntry       = entry;
                _selSharedEntry = null;
                _selDiary       = null;
                _selTag         = null;
            }

            y += ArcRowH;
        }

        private void DrawDiaryRow(ref float y, float w, string log)
        {
            bool isSel = _selDiary == log;
            Rect row   = new Rect(0f, y, w, DiaryRowH);

            if (isSel) Widgets.DrawBoxSolid(row, CBgSel);
            if (Mouse.IsOver(row) && !isSel) Widgets.DrawHighlight(row);

            // Diary colour strip
            Widgets.DrawBoxSolid(
                new Rect(0f, y + DiaryRowH * 0.3f, 3f, DiaryRowH * 0.4f), CDy);

            Text.Font = GameFont.Tiny;
            GUI.color  = new Color(CB.r, CB.g, CB.b, 0.75f);
            Widgets.Label(
                new Rect(10f, y + 5f, w - 14f, DiaryRowH - 8f),
                TrimDiary(log));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            if (Widgets.ButtonInvisible(row))
            {
                _selDiary       = log;
                _selEntry       = null;
                _selSharedEntry = null;
                _selTag         = null;
            }

            y += DiaryRowH;
        }

        // ─────────────────────────────────────────────────────────────────
        //  SHARED ARC ROW (left pane)
        // ─────────────────────────────────────────────────────────────────

        private void DrawSharedArcRow(ref float y, float w,
            ArcStageEntry entry, EntangledArcState arc, CompPersonalChronicles comp)
        {
            bool isSel = _selSharedEntry == entry;
            bool isCur = arc.CurrentEntry == entry;

            Rect row = new Rect(0f, y, w, ArcRowH);
            Widgets.DrawBoxSolid(row, isSel ? CBgSel : isCur ? CBgAct : Color.clear);
            if (Mouse.IsOver(row) && !isSel) Widgets.DrawHighlight(row);

            // Purple strip for shared arc
            Widgets.DrawBoxSolid(new Rect(0f, y + 2f, 3f, ArcRowH - 4f), CEnt);

            Text.Font = GameFont.Small;
            GUI.color  = isSel ? Color.white : (isCur ? CEnt : CB);
            Widgets.Label(new Rect(10f, y + 6f, w - 14f, 22f), entry.title ?? "PC_ITab_DefaultSharedChapter".Translate());
            GUI.color = Color.white;

            Text.Font = GameFont.Tiny;
            GUI.color  = CD;
            string suffix = isCur ? "  · ACTIVE" : entry.IsResolved ? "  · resolved" : "";
            Widgets.Label(new Rect(10f, y + 30f, w - 14f, 18f),
                "PC_ITab_DayStage".Translate(entry.writtenAtTick / 60000, entry.stageRole.ToUpper() + suffix));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            if (Widgets.ButtonInvisible(row))
            {
                _selSharedEntry = entry;
                _selEntry       = null;
                _selDiary       = null;
                _selTag         = null;
            }

            y += ArcRowH;
        }

        // ─────────────────────────────────────────────────────────────────
        //  RIGHT PANE - router
        // ─────────────────────────────────────────────────────────────────

        private void DrawRight(Rect pane, Pawn pawn, CompPersonalChronicles comp)
        {
            if (_selSharedEntry != null)
            {
                var sharedArc = comp.GetEntangledArc();
                // If the arc no longer exists (completed and purged), fall through
                if (sharedArc != null)
                {
                    DrawSharedArcDetail(pane, pawn, sharedArc, comp);
                    return;
                }
                _selSharedEntry = null;
            }
            if (_selEntry != null) { DrawArcDetail(pane, comp);   return; }
            if (_selDiary != null) { DrawDiaryDetail(pane);       return; }
            DrawProfile(pane, pawn, comp);
        }

        // ─────────────────────────────────────────────────────────────────
        //  SHARED ARC DETAIL (right pane)
        // ─────────────────────────────────────────────────────────────────

        private void DrawSharedArcDetail(Rect pane, Pawn pawn,
            EntangledArcState arc, CompPersonalChronicles comp)
        {
            var   entry  = _selSharedEntry!;
            var   other  = arc.OtherPawn(pawn);
            var   def    = arc.ArcDef;
            float x      = Pad;
            float y      = Pad;
            float w      = pane.width - Pad * 2f;

            if (Widgets.ButtonText(new Rect(x, y, 90f, 26f), "← Back"))
            {
                _selSharedEntry = null;
                return;
            }
            y += 36f;

            // Arc type badge
            Text.Font = GameFont.Tiny;
            GUI.color  = CEnt;
            string arcLabel = def != null ? def.arcType.ToString().ToUpper() : "PC_ITab_SharedArc".Translate();
            Widgets.Label(new Rect(x, y, w, 18f),
                $"{arcLabel}  ·  {pawn.LabelShort} & {other?.LabelShort ?? "?"}");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;

            // Title
            Text.Font = GameFont.Medium;
            GUI.color  = CEnt;
            float th   = Text.CalcHeight(entry.title ?? "", w);
            Widgets.Label(new Rect(x, y, w, th), entry.title ?? "PC_ITab_DefaultSharedChapter".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += th + 4f;

            // Role + Day
            Text.Font = GameFont.Tiny;
            GUI.color  = RoleColor(entry.stageRole);
            Widgets.Label(new Rect(x, y, w, 18f),
                "PC_ITab_DayStage".Translate(entry.writtenAtTick / 60000, entry.stageRole.ToUpper()));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;

            Widgets.DrawLineHorizontal(x, y, w, CR);
            y += 12f;

            // Body
            float bh = Text.CalcHeight(entry.body ?? "", w);
            Widgets.Label(new Rect(x, y, w, bh), entry.body ?? "");
            y += bh + 16f;

            // ── MECHANICAL FAILURE REASON (shared arcs) ───────────────────────
            if (entry.stageRole == "failure" && !string.IsNullOrEmpty(entry.mechanicalFailureReason))
            {
                Widgets.DrawLineHorizontal(x, y, w, CR);
                y += 12f;

                Text.Font = GameFont.Tiny;
                GUI.color = CFail;
                Widgets.Label(new Rect(x, y, w, 20f), "PC_ITab_WhyFailed".Translate());
                y += 20f;

                GUI.color = new Color(0.95f, 0.75f, 0.75f);
                float reasonH = Text.CalcHeight(entry.mechanicalFailureReason, w);
                Widgets.Label(new Rect(x, y, w, reasonH), entry.mechanicalFailureReason);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += reasonH + 8f;
            }

            // ── MECHANICAL SUCCESS REASON ─────────────────────────────────────
            if (entry.stageRole == "success" && !string.IsNullOrEmpty(entry.mechanicalSuccessReason))
            {
                Widgets.DrawLineHorizontal(x, y, w, CR);
                y += 12f;

                Text.Font = GameFont.Tiny;
                GUI.color = COk;
                Widgets.Label(new Rect(x, y, w, 20f), "PC_ITab_WhySucceeded".Translate());
                y += 20f;

                GUI.color = new Color(0.75f, 0.95f, 0.75f);
                float reasonH = Text.CalcHeight(entry.mechanicalSuccessReason, w);
                Widgets.Label(new Rect(x, y, w, reasonH), entry.mechanicalSuccessReason);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += reasonH + 8f;
            }

            // ── WHAT'S AT STAKE (shared arc) ──────────────────────────────────
            if (arc.CurrentEntry == entry && !entry.IsResolved && arc.ArcDef != null)
            {
                bool isInit  = pawn == arc.initiator;
                var  stakeDef = arc.ArcDef;
                string? redTitle = (isInit ? stakeDef.successInitiatorBackstory : stakeDef.successPartnerBackstory)?.title;
                string? corTitle = (isInit ? stakeDef.failureInitiatorBackstory : stakeDef.failurePartnerBackstory)?.title;
                string? redDesc  = ResolveForPawn((isInit ? stakeDef.successInitiatorBackstory : stakeDef.successPartnerBackstory)?.baseDesc, pawn);
                string? corDesc  = ResolveForPawn((isInit ? stakeDef.failureInitiatorBackstory : stakeDef.failurePartnerBackstory)?.baseDesc, pawn);
                y = DrawBackstoryStakes(x, y, w, redTitle, corTitle, redDesc, corDesc, entry.isClimax);
            }

            // Wait condition / choice / advance (active stage only)
            if (arc.CurrentEntry == entry && !entry.IsResolved)
            {
                bool isInit2   = pawn == arc.initiator;
                var  def2      = arc.ArcDef;
                string? redTit = (def2 == null ? null : (isInit2 ? def2.successInitiatorBackstory : def2.successPartnerBackstory)?.title);
                string? corTit = (def2 == null ? null : (isInit2 ? def2.failureInitiatorBackstory : def2.failurePartnerBackstory)?.title);
                string? corDsc = ResolveForPawn((def2 == null ? null : (isInit2 ? def2.failureInitiatorBackstory : def2.failurePartnerBackstory)?.baseDesc), pawn);

                if (entry.AwaitingChoice)
                {
                    y = DrawChoiceButtons(x, y, w, entry.choices,
                        choiceIndex => EntangledArcManager.Instance?.PlayerMakeChoiceArc(arc, choiceIndex),
                        pawn.LabelShort, redTit, corTit, corDsc);
                }
                else
                {
                    // Show chosen path flavor
                    if (entry.chosenIndex >= 0 && entry.choices.Count > entry.chosenIndex)
                    {
                        var chosen = entry.choices[entry.chosenIndex];
                        Text.Font = GameFont.Tiny;
                        GUI.color  = TagEdgeColor(chosen.tagDefName);
                        Widgets.Label(new Rect(x, y, w, 18f),
                            "PC_ITab_ChosenPath".Translate(chosen.actionLabel));
                        GUI.color = Color.white;
                        Text.Font = GameFont.Small;
                        y += 22f;
                    }

                    bool met = entry.conditionMet;
                    Text.Font = GameFont.Tiny;
                    GUI.color  = met ? COk : CWait;
                    Widgets.Label(new Rect(x, y, w, 20f),
                        (met ? "✓ " : "▸ ") + entry.waitConditionLabel);
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    y += 22f;

                    if (!met && entry.waitConditionKey == "time" && entry.waitTargetDelta > 0)
                        y = DrawTimeProgress(x, y, w, entry);

                    y += 6f;

                    float bx = x;
                    if (met)
                    {
                        bool  isClimax  = entry.isClimax;
                        string btnLabel = isClimax ? "PC_ITab_ResolveArc".Translate() : "PC_ITab_AdvanceArc".Translate();
                        Rect  nb        = new Rect(bx, y, 150f, 30f);
                        Widgets.DrawBoxSolid(nb, isClimax
                            ? new Color(0.15f, 0.08f, 0.18f)
                            : new Color(0.10f, 0.08f, 0.16f));
                        Widgets.DrawBox(nb);
                        GUI.color = isClimax ? new Color(1.00f, 0.82f, 0.30f) : CEnt;
                        using (new TextAnchorScope(TextAnchor.MiddleCenter))
                            Widgets.Label(nb, btnLabel);
                        GUI.color = Color.white;

                        if (isClimax)
                        {
                            string tip = "PC_ITab_SharedArcFinalTip".Translate();
                            if (redTit != null) tip += $"\n\nSuccess -> {pawn.LabelShort}: {redTit}";
                            if (corTit != null) tip += $"\nFailure -> {pawn.LabelShort}: {corTit}";
                            TooltipHandler.TipRegion(nb, tip);
                        }

                        if (Widgets.ButtonInvisible(nb))
                            EntangledArcManager.Instance?.PlayerAdvanceArc(arc, devMode: false);
                        bx += 158f;
                    }

                    if (DebugSettings.godMode)
                    {
                        Rect db = new Rect(bx, y, 120f, 30f);
                        Widgets.DrawBoxSolid(db, new Color(0.20f, 0.13f, 0.06f));
                        Widgets.DrawBox(db);
                        GUI.color = new Color(0.9f, 0.5f, 0.2f);
                        using (new TextAnchorScope(TextAnchor.MiddleCenter))
                            Widgets.Label(db, "PC_ITab_DevNext".Translate());
                        GUI.color = Color.white;
                        if (Widgets.ButtonInvisible(db))
                            EntangledArcManager.Instance?.PlayerAdvanceArc(arc, devMode: true);
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  ARC DETAIL
        // ─────────────────────────────────────────────────────────────────

        private void DrawArcDetail(Rect pane, CompPersonalChronicles comp)
        {
            var   entry = _selEntry!;
            float x     = Pad;
            float y     = Pad;
            float w     = pane.width - Pad * 2f;

            // Back button
            if (Widgets.ButtonText(new Rect(x, y, 90f, 26f), "← Back"))
            {
                _selEntry = null;
                return;
            }
            y += 36f;

            // Title
            Text.Font = GameFont.Medium;
            GUI.color  = CG;
            float th   = Text.CalcHeight(entry.title ?? "", w);
            Widgets.Label(new Rect(x, y, w, th), entry.title ?? "PC_ITab_DefaultChapter".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += th + 4f;

            // Role + Day
            Text.Font = GameFont.Tiny;
            GUI.color  = RoleColor(entry.stageRole);
            Widgets.Label(new Rect(x, y, w, 18f),
                "PC_ITab_DayStage".Translate(entry.writtenAtTick / 60000, entry.stageRole.ToUpper()));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;

            Widgets.DrawLineHorizontal(x, y, w, CR);
            y += 12f;

            // Body
            float bh = Text.CalcHeight(entry.body ?? "", w);
            Widgets.Label(new Rect(x, y, w, bh), entry.body ?? "");
            y += bh + 16f;

            // ── MECHANICAL FAILURE REASON (visible stats) ─────────────────────
            if (entry.stageRole == "failure" && !string.IsNullOrEmpty(entry.mechanicalFailureReason))
            {
                Widgets.DrawLineHorizontal(x, y, w, CR);
                y += 12f;

                Text.Font = GameFont.Tiny;
                GUI.color = CFail;
                Widgets.Label(new Rect(x, y, w, 20f), "PC_ITab_WhyFailed".Translate());
                y += 20f;

                GUI.color = new Color(0.95f, 0.75f, 0.75f);
                float reasonH = Text.CalcHeight(entry.mechanicalFailureReason, w);
                Widgets.Label(new Rect(x, y, w, reasonH), entry.mechanicalFailureReason);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += reasonH + 8f;
            }

            // ── MECHANICAL SUCCESS REASON ─────────────────────────────────────
            if (entry.stageRole == "success" && !string.IsNullOrEmpty(entry.mechanicalSuccessReason))
            {
                Widgets.DrawLineHorizontal(x, y, w, CR);
                y += 12f;

                Text.Font = GameFont.Tiny;
                GUI.color = COk;
                Widgets.Label(new Rect(x, y, w, 20f), "PC_ITab_WhySucceeded".Translate());
                y += 20f;

                GUI.color = new Color(0.75f, 0.95f, 0.75f);
                float reasonH = Text.CalcHeight(entry.mechanicalSuccessReason, w);
                Widgets.Label(new Rect(x, y, w, reasonH), entry.mechanicalSuccessReason);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += reasonH + 8f;
            }

            // ── WHAT'S AT STAKE ───────────────────────────────────────────────
            // Always visible while this stage is active - player knows what's coming
            if (comp.CurrentEntry == entry && !entry.IsResolved && comp.currentEpic != null)
            {
                string? redTitle = comp.currentEpic.redeemedEpithet;
                string? corTitle = comp.currentEpic.corruptedEpithet;
                string? redDesc  = null;
                string? corDesc  = null;
                y = DrawBackstoryStakes(x, y, w, redTitle, corTitle, redDesc, corDesc, entry.isClimax);
            }

            // Wait condition / choice / advance (only for current active stage)
            if (comp.CurrentEntry == entry && !entry.IsResolved)
            {
                string? pName    = SelPawn?.LabelShort;
                string? redTitle = comp.currentEpic?.redeemedEpithet;
                string? corTitle = comp.currentEpic?.corruptedEpithet;
                string? corDesc  = null;

                if (entry.isClimax && entry.isSkillCheck)
                {
                    // Legacy climax: skill-check progress bar + deadline + resolve/abandon buttons
                    var pawnRef = SelPawn;
                    if (pawnRef != null)
                        y = DrawClimaxSkillCheck(x, y, w, entry, pawnRef, comp);
                }
                else if (entry.AwaitingChoice)
                {
                    // Two-door climax or seed/tradeoff choice: draw buttons
                    // (Hard Road / Easy Out flags handled inside DrawChoiceButtons)
                    y = DrawChoiceButtons(x, y, w, entry.choices,
                        choiceIndex => comp.PlayerMakeChoice(choiceIndex),
                        pName, redTitle, corTitle, corDesc);
                }
                else
                {
                    // Show chosen path flavor + any stat effects that were applied
                    if (entry.chosenIndex >= 0 && entry.choices != null && entry.choices.Count > entry.chosenIndex)
                    {
                        var chosen = entry.choices[entry.chosenIndex];

                        Text.Font = GameFont.Tiny;
                        GUI.color  = chosen.isHardRoad ? new Color(0.60f, 0.90f, 0.50f)
                                   : chosen.isEasyOut  ? new Color(0.80f, 0.40f, 0.40f)
                                                       : TagEdgeColor(chosen.tagDefName);
                        string pathPrefix = chosen.isHardRoad ? "⚔ Hard Road: "
                                         : chosen.isEasyOut  ? "  Easy Out: "
                                                             : "PC_ITab_PathPrefix".Translate();
                        Widgets.Label(new Rect(x, y, w, 18f), pathPrefix + chosen.actionLabel);
                        GUI.color = Color.white;
                        Text.Font = GameFont.Small;
                        y += 20f;

                        // Inline: stat effects that were applied
                        if (chosen.effects != null && chosen.effects.Count > 0)
                        {
                            Text.Font = GameFont.Tiny;
                            foreach (var fx in chosen.effects)
                            {
                                GUI.color = fx.levelDelta > 0
                                    ? new Color(0.55f, 0.90f, 0.55f)
                                    : new Color(0.90f, 0.55f, 0.55f);
                                Widgets.Label(new Rect(x + 10f, y, w - 14f, 16f),
                                    fx.DisplayLabel + " (applied)");
                                GUI.color = Color.white;
                                y += 16f;
                            }
                            Text.Font = GameFont.Small;
                            y += 4f;
                        }
                    }

                    bool met = entry.conditionMet;
                    Text.Font = GameFont.Tiny;
                    GUI.color  = met ? COk : CWait;
                    Widgets.Label(new Rect(x, y, w, 20f),
                        (met ? "✓ " : "▸ ") + entry.waitConditionLabel);
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    y += 22f;

                    // Progress bar for time-based conditions
                    if (!met && entry.waitConditionKey == "time" && entry.waitTargetDelta > 0)
                        y = DrawTimeProgress(x, y, w, entry);

                    y += 6f;

                    float bx = x;
                    if (met)
                    {
                        bool  isClimax  = entry.isClimax;
                        string btnLabel = isClimax ? "PC_ITab_ResolveArc".Translate() : "PC_ITab_NextStage".Translate();
                        Rect  nb        = new Rect(bx, y, 140f, 30f);
                        Widgets.DrawBoxSolid(nb, isClimax
                            ? new Color(0.18f, 0.14f, 0.06f)
                            : new Color(0.12f, 0.18f, 0.12f));
                        Widgets.DrawBox(nb);
                        GUI.color = isClimax ? new Color(1.00f, 0.82f, 0.30f) : COk;
                        using (new TextAnchorScope(TextAnchor.MiddleCenter))
                            Widgets.Label(nb, btnLabel);
                        GUI.color = Color.white;

                        // Climax tooltip - player knows this is permanent
                        if (isClimax)
                        {
                            string tip = "PC_ITab_ArcFinalTip".Translate();
                            if (redTitle != null) tip += $"\n\nSuccess -> {redTitle}";
                            if (corTitle != null) tip += $"\nFailure -> {corTitle}";
                            TooltipHandler.TipRegion(nb, tip);
                        }

                        if (Widgets.ButtonInvisible(nb))
                            comp.PlayerAdvanceStage(devMode: false);
                        bx += 148f;
                    }

                    if (DebugSettings.godMode)
                    {
                        Rect db = new Rect(bx, y, 120f, 30f);
                        Widgets.DrawBoxSolid(db, new Color(0.20f, 0.13f, 0.06f));
                        Widgets.DrawBox(db);
                        GUI.color = new Color(0.9f, 0.5f, 0.2f);
                        using (new TextAnchorScope(TextAnchor.MiddleCenter))
                            Widgets.Label(db, "PC_ITab_DevNext".Translate());
                        GUI.color = Color.white;
                        if (Widgets.ButtonInvisible(db))
                            comp.PlayerAdvanceStage(devMode: true);
                    }

                    y += 42f;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  DIARY DETAIL
        // ─────────────────────────────────────────────────────────────────

        private void DrawDiaryDetail(Rect pane)
        {
            float x = Pad;
            float y = Pad;
            float w = pane.width - Pad * 2f;

            if (Widgets.ButtonText(new Rect(x, y, 90f, 26f), "← Back"))
            {
                _selDiary = null;
                return;
            }
            y += 40f;

            Text.Font = GameFont.Tiny;
            GUI.color  = CDy;
            Widgets.Label(new Rect(x, y, w, 18f), "PC_ITab_DiaryEntry".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 24f;

            Widgets.DrawLineHorizontal(x, y, w, CR);
            y += 12f;

            float th = Text.CalcHeight(_selDiary ?? "", w);
            Widgets.Label(new Rect(x, y, w, th), _selDiary ?? "");
        }

        // ─────────────────────────────────────────────────────────────────
        //  PROFILE VIEW (default right-pane state)
        // ─────────────────────────────────────────────────────────────────

        private void DrawProfile(Rect pane, Pawn pawn, CompPersonalChronicles comp)
        {
            float x    = Pad;
            float y    = Pad;
            float pw   = pane.width;
            float colW = pw - Pad * 2f - PortraitW - 10f;

            // Portrait
            Rect portrait = new Rect(pw - Pad - PortraitW, y, PortraitW, PortraitH);
            RenderTexture rt = PortraitsCache.Get(
                pawn, new Vector2(PortraitW, PortraitH), Rot4.South,
                cameraOffset: new Vector3(0f, 0f, 0.2f));
            if (rt != null) GUI.DrawTexture(portrait, rt);
            else            Widgets.DrawBoxSolid(portrait, CBgPnl);

            // Active epic
            if (comp.hasActiveEpic && comp.currentEpic != null)
            {
                Text.Font = GameFont.Medium;
                GUI.color  = CG;
                string el = (comp.currentEpic.label ?? comp.currentEpic.defName).ToUpper();
                float  eh = Text.CalcHeight(el, colW);
                Widgets.Label(new Rect(x, y, colW, eh), el);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += eh + 2f;

                Text.Font = GameFont.Tiny;
                GUI.color  = CD;
                Widgets.Label(new Rect(x, y, colW, 18f),
                    $"{comp.currentEpic.modus}  ·  Stage {comp.currentStage + 1} / {comp.currentEpic.stageCount}");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += 22f;

                var cur = comp.CurrentEntry;
                if (cur != null && !cur.IsResolved)
                {
                    bool met = cur.conditionMet;
                    Text.Font = GameFont.Tiny;
                    GUI.color  = met ? COk : CWait;
                    Widgets.Label(new Rect(x, y, colW, 18f),
                        (met ? "✓ " : "▸ ") + cur.waitConditionLabel);
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    y += 20f;
                }
            }
            else
            {
                Text.Font = GameFont.Small;
                GUI.color  = CD;
                Widgets.Label(new Rect(x, y, colW, 22f), "PC_ITab_NoActiveArc".Translate());
                GUI.color = Color.white;
                y += 26f;
            }

            string storyTitle = pawn.story?.Title ?? "";
            if (!string.IsNullOrEmpty(storyTitle))
            {
                Text.Font = GameFont.Tiny;
                GUI.color  = CD;
                Widgets.Label(new Rect(x, y, colW, 18f), storyTitle);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += 20f;
            }

            y = Mathf.Max(y, Pad + PortraitH + 8f);
            Widgets.DrawLineHorizontal(x, y, pane.width - Pad * 2f, CR);
            y += 10f;

            Text.Font = GameFont.Small;
            GUI.color  = CG;
            Widgets.Label(new Rect(x, y, pane.width - Pad * 2f, 22f), "PC_ITab_NarrativeProfile".Translate());
            GUI.color = Color.white;
            y += 26f;

            var profile = comp.currentProfile ?? comp.GetOrBuildProfile();

            if (_selTag != null)
            {
                y = DrawTagBreakdown(x, y, pane.width - Pad * 2f, _selTag, profile);
                y += 8f;
            }

            float listH = pane.height - y - Pad;
            if (listH > 24f)
            {
                var sorted = profile.Scores
                    .Where(kv => kv.Value >= PawnNarrativeProfile.ActiveThreshold)
                    .OrderByDescending(kv => kv.Value)
                    .ToList();

                if (sorted.Count == 0)
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color  = CD;
                    Widgets.Label(new Rect(x, y, pane.width - Pad * 2f, 20f),
                        "PC_ITab_NoNarrativeThreads".Translate());
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }
                else
                {
                    Rect  listOuter = new Rect(x, y, pane.width - Pad * 2f, listH);
                    float viewH     = sorted.Count * (TagRowH + 2f);
                    Rect  viewRect  = new Rect(0f, 0f,
                        listOuter.width - 16f, Mathf.Max(viewH, listH));

                    Widgets.BeginScrollView(listOuter, ref _tagScroll, viewRect);
                    float ty     = 0f;
                    float barMax = viewRect.width * 0.36f;

                    foreach (var kv in sorted)
                    {
                        bool isSel = _selTag == kv.Key;
                        Rect row   = new Rect(0f, ty, viewRect.width, TagRowH);

                        if (isSel)             Widgets.DrawBoxSolid(row, CBgSel);
                        else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);

                        float barW = (kv.Value / 100f) * barMax;
                        Widgets.DrawBoxSolid(
                            new Rect(viewRect.width - barMax, ty + 5f, barW, TagRowH - 10f),
                            isSel ? new Color(0.30f, 0.58f, 0.30f, 0.55f)
                                  : new Color(0.22f, 0.40f, 0.22f, 0.28f));

                        Text.Font = GameFont.Tiny;
                        GUI.color  = isSel ? CG : CB;
                        Widgets.Label(new Rect(6f, ty + 3f, viewRect.width * 0.56f, TagRowH - 4f),
                            kv.Key.label);

                        GUI.color = isSel ? CG : CD;
                        using (new TextAnchorScope(TextAnchor.MiddleRight))
                            Widgets.Label(
                                new Rect(viewRect.width - barMax - 4f, ty, barMax - 2f, TagRowH),
                                ((int)kv.Value).ToString());
                        GUI.color = Color.white;
                        Text.Font = GameFont.Small;

                        // Tooltip - description if present, then click hint
                        string tipText = string.IsNullOrWhiteSpace(kv.Key.description)
                            ? "PC_ITab_ClickForStats".Translate()
                            : kv.Key.description + "\n\n" + "PC_ITab_ClickForStats".Translate();
                        TooltipHandler.TipRegion(row, tipText);

                        if (Widgets.ButtonInvisible(row))
                        {
                            _selTag         = isSel ? null : kv.Key;
                            _selEntry       = null;
                            _selSharedEntry = null;
                            _selDiary       = null;
                        }
                        ty += TagRowH + 2f;
                    }
                    Widgets.EndScrollView();
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  TAG BREAKDOWN PANEL
        // ─────────────────────────────────────────────────────────────────

        private float DrawTagBreakdown(float x, float y, float w,
            NarrativeTagDef tag, PawnNarrativeProfile profile)
        {
            var   contribs = profile.GetContributors(tag);
            float boxH     = Mathf.Min(
                52f + (string.IsNullOrEmpty(tag.description) ? 0f : 20f) + contribs.Count * 18f,
                180f);
            Rect box = new Rect(x, y, w, boxH);
            Widgets.DrawBoxSolid(box, CBgPnl);
            Widgets.DrawBox(box);

            float iy = y + 8f;
            float ix = x + 10f;
            float iw = w - 20f;

            Text.Font = GameFont.Small;
            GUI.color  = CG;
            Widgets.Label(new Rect(ix, iy, iw * 0.72f, 20f), tag.label);
            using (new TextAnchorScope(TextAnchor.UpperRight))
                Widgets.Label(new Rect(ix, iy, iw, 20f), ((int)profile.GetScore(tag)).ToString());
            GUI.color = Color.white;
            iy += 22f;

            if (!string.IsNullOrEmpty(tag.description))
            {
                Text.Font = GameFont.Tiny;
                GUI.color  = CD;
                Widgets.Label(new Rect(ix, iy, iw, 18f), tag.description);
                GUI.color = Color.white;
                iy += 20f;
            }

            Text.Font = GameFont.Tiny;
            foreach (var c in contribs.Take(6))
            {
                GUI.color = c.Value >= 0f ? CB : CFail;
                Widgets.Label(new Rect(ix, iy, iw * 0.72f, 18f), c.Source);
                string sign = c.Value >= 0f ? "+" : "";
                using (new TextAnchorScope(TextAnchor.UpperRight))
                    Widgets.Label(new Rect(ix, iy, iw, 18f), $"{sign}{c.Value:F0}");
                GUI.color = Color.white;
                iy += 18f;
            }
            Text.Font = GameFont.Small;
            return y + boxH;
        }

        // ─────────────────────────────────────────────────────────────────
        //  CHOICE UI HELPERS
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Renders the choice buttons for an awaiting-choice entry.
        /// Returns the new Y position below the rendered block.
        /// onChoose is called with the chosen index when a button is clicked.
        ///
        /// Handles three visual modes:
        ///   isHardRoad = true  -> gold edge, "⚔ Hard Road" header, backstory reward hint
        ///   isEasyOut  = true  -> red edge, "🚪 Easy Out" header, corrupted backstory warning
        ///   neither           -> tag-colored edge, ChoiceEffect stat lines shown below hint
        /// </summary>
        private float DrawChoiceButtons(float x, float y, float w,
            System.Collections.Generic.List<StageChoice> choices,
            System.Action<int> onChoose,
            string? pawnName       = null,
            string? redeemedTitle  = null,
            string? corruptedTitle = null,
            string? corruptedDesc  = null)
        {
            // Header varies by what kind of choices are presented
            bool hasClimaxDoors = choices.Count > 0 &&
                (choices[0].isHardRoad || choices[choices.Count - 1].isEasyOut);

            Text.Font = GameFont.Tiny;
            GUI.color  = hasClimaxDoors ? new Color(1.00f, 0.82f, 0.30f) : CG;
            Widgets.Label(new Rect(x, y, w, 18f),
                hasClimaxDoors ? "PC_ITab_ChoosePathClimax".Translate() : "PC_ITab_ChoosePath".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;

            for (int i = 0; i < choices.Count; i++)
            {
                var  choice    = choices[i];
                bool hardRoad  = choice.isHardRoad;
                bool easyOut   = choice.isEasyOut;
                bool hasEffects = choice.effects != null && choice.effects.Count > 0;

                // Button height: base 54px, +20 for each effect row, +20 for backstory warning
                float effectsH    = hasEffects ? (choice.effects!.Count * 18f + 4f) : 0f;
                float backstoryH  = (easyOut && corruptedTitle != null) ? 22f : 0f;
                float btnH        = 54f + effectsH + backstoryH;
                Rect  btn         = new Rect(x, y, w, btnH);

                Color bgCol   = hardRoad  ? new Color(0.08f, 0.10f, 0.06f)
                              : easyOut   ? new Color(0.13f, 0.07f, 0.07f)
                                          : new Color(0.10f, 0.12f, 0.16f);
                Color edgeCol = hardRoad  ? new Color(0.55f, 0.80f, 0.40f)   // green = redemption
                              : easyOut   ? new Color(0.70f, 0.25f, 0.25f)   // red = corruption
                                          : TagEdgeColor(choice.tagDefName);

                if (Mouse.IsOver(btn))
                    bgCol += new Color(0.05f, 0.05f, 0.05f, 0f);

                Widgets.DrawBoxSolid(btn, bgCol);
                Widgets.DrawBoxSolid(new Rect(x, y, 3f, btnH), edgeCol);

                // Door label prefix
                if (hardRoad || easyOut)
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color  = hardRoad ? new Color(0.60f, 0.90f, 0.50f) : new Color(0.80f, 0.40f, 0.40f);
                    Widgets.Label(new Rect(x + 10f, y + 4f, w - 14f, 16f),
                        hardRoad ? "⚔  HARD ROAD" : "  EASY OUT");
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }

                // Action label
                float labelY = (hardRoad || easyOut) ? y + 20f : y + 8f;
                Text.Font = GameFont.Small;
                GUI.color  = hardRoad ? new Color(0.80f, 1.00f, 0.70f)
                           : easyOut  ? new Color(0.85f, 0.50f, 0.50f)
                                      : Color.white;
                Widgets.Label(new Rect(x + 10f, labelY, w - 14f, 22f), choice.actionLabel);
                GUI.color = Color.white;

                // Mechanical hint
                float hintY = labelY + 22f;
                Text.Font = GameFont.Tiny;
                GUI.color  = easyOut ? new Color(0.55f, 0.35f, 0.35f) : CD;
                Widgets.Label(new Rect(x + 10f, hintY, w - 14f, 18f), choice.mechanicalHint);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                // Stat effect chips (tradeoff choices)
                if (hasEffects)
                {
                    float fy = hintY + 20f;
                    foreach (var fx in choice.effects!)
                    {
                        bool positive = fx.levelDelta > 0;
                        GUI.color = positive ? new Color(0.55f, 0.90f, 0.55f) : new Color(0.90f, 0.55f, 0.55f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(x + 10f, fy, w - 14f, 16f), fx.DisplayLabel);
                        GUI.color = Color.white;
                        Text.Font = GameFont.Small;
                        fy += 18f;
                    }
                }

                // Easy Out: inline corrupted backstory warning
                if (easyOut && corruptedTitle != null)
                {
                    float warnY = hintY + 20f + effectsH;
                    string pName = pawnName.NullOrEmpty() ? "PC_ITab_ThisPawn".Translate() : pawnName;
                    GUI.color = new Color(0.95f, 0.42f, 0.42f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(x + 10f, warnY, w - 14f, 18f),
                        $"⚠  {pName} is marked: \"{corruptedTitle}\" immediately");
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }

                // Tooltips
                if (hardRoad)
                {
                    string tip = "PC_ITab_HardRoadTip".Translate();
                    if (redeemedTitle != null) tip += $"\n\nSuccess -> {redeemedTitle}";
                    TooltipHandler.TipRegion(btn, tip);
                }
                else if (easyOut)
                {
                    string tip = "PC_ITab_EasyOutTip".Translate();
                    if (corruptedTitle != null) tip += $"\n\nPermanent outcome: {corruptedTitle}";
                    if (!corruptedDesc.NullOrEmpty()) tip += $"\n\n\"{corruptedDesc}\"";
                    TooltipHandler.TipRegion(btn, tip);
                }
                else if (hasEffects)
                {
                    string tip = "PC_ITab_EffectsNow".Translate();
                    foreach (var fx in choice.effects!)
                        tip += $"\n  {fx.DisplayLabel}";
                    if (redeemedTitle != null) tip += $"\n\nIf the arc succeeds -> {redeemedTitle}";
                    if (corruptedTitle != null) tip += $"\nIf the arc fails -> {corruptedTitle}";
                    TooltipHandler.TipRegion(btn, tip);
                }
                else if (redeemedTitle != null || corruptedTitle != null)
                {
                    string tip = "PC_ITab_EffectsDirection".Translate();
                    if (redeemedTitle != null) tip += $"\n\nIf the arc succeeds -> {redeemedTitle}";
                    if (corruptedTitle != null) tip += $"\nIf the arc fails -> {corruptedTitle}";
                    TooltipHandler.TipRegion(btn, tip);
                }

                int captured = i;
                if (Widgets.ButtonInvisible(btn))
                    onChoose(captured);

                y += btnH + 4f;
            }
            return y;
        }

        /// <summary>
        /// "WHAT'S AT STAKE" panel - shows the redeemed and corrupted backstory side by side.
        /// Hover either box to read the full backstory description.
        /// </summary>
        private float DrawBackstoryStakes(float x, float y, float w,
            string? redeemedTitle, string? corruptedTitle,
            string? redeemedDesc = null, string? corruptedDesc = null,
            bool isClimax = false)
        {
            if (redeemedTitle == null && corruptedTitle == null) return y;

            Widgets.DrawLineHorizontal(x, y, w, CR);
            y += 10f;

            Text.Font = GameFont.Tiny;
            GUI.color  = isClimax ? new Color(1.00f, 0.82f, 0.30f) : CD;
            Widgets.Label(new Rect(x, y, w, 18f),
                isClimax ? "PC_ITab_FinalStake".Translate() : "PC_ITab_WhatAtStake".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;

            float half = (w - 4f) * 0.5f;

            // Success box
            if (redeemedTitle != null)
            {
                Rect box = new Rect(x, y, half, 52f);
                Widgets.DrawBoxSolid(box, new Color(0.06f, 0.15f, 0.08f));
                Widgets.DrawBoxSolid(new Rect(x, y, half, 2f), new Color(0.35f, 0.72f, 0.42f));

                Text.Font = GameFont.Tiny;
                GUI.color  = COk;
                Widgets.Label(new Rect(x + 6f, y + 6f, half - 8f, 18f), "PC_ITab_Success".Translate());
                GUI.color  = new Color(0.85f, 0.95f, 0.85f);
                Text.Font  = GameFont.Small;
                Widgets.Label(new Rect(x + 6f, y + 22f, half - 8f, 24f), redeemedTitle);
                GUI.color = Color.white;

                if (!redeemedDesc.NullOrEmpty())
                    TooltipHandler.TipRegion(box, $"\"{redeemedDesc}\"");
            }

            // Failure/abandon box
            if (corruptedTitle != null)
            {
                float fx  = x + half + 4f;
                Rect  box = new Rect(fx, y, half, 52f);
                Widgets.DrawBoxSolid(box, new Color(0.17f, 0.06f, 0.06f));
                Widgets.DrawBoxSolid(new Rect(fx, y, half, 2f), new Color(0.75f, 0.28f, 0.28f));

                Text.Font = GameFont.Tiny;
                GUI.color  = CFail;
                Widgets.Label(new Rect(fx + 6f, y + 6f, half - 8f, 18f), "PC_ITab_Failure".Translate());
                GUI.color  = new Color(0.95f, 0.78f, 0.78f);
                Text.Font  = GameFont.Small;
                Widgets.Label(new Rect(fx + 6f, y + 22f, half - 8f, 24f), corruptedTitle);
                GUI.color = Color.white;

                if (!corruptedDesc.NullOrEmpty())
                    TooltipHandler.TipRegion(box, $"\"{corruptedDesc}\"");
            }

            Text.Font = GameFont.Small;
            y += 60f;
            return y;
        }

        // ─────────────────────────────────────────────────────────────────
        //  TIME PROGRESS BAR (between stages, wait condition = "time")
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Draws a progress bar and days-remaining countdown for a time-based
        /// wait condition. Called when the stage has not yet unlocked its Next button.
        /// Returns the new y position after the rendered block.
        /// </summary>
        private static float DrawTimeProgress(float x, float y, float w, ArcStageEntry entry)
        {
            int   now        = Find.TickManager.TicksGame;
            int   start      = entry.waitBaselineValue;
            int   total      = entry.waitTargetDelta;
            float progress   = total > 0 ? Mathf.Clamp01((float)(now - start) / total) : 1f;
            float daysLeft   = total > 0 ? Mathf.Max(0f, (start + total - now) / 60000f) : 0f;

            // ── Header ────────────────────────────────────────────────────────
            Text.Font = GameFont.Tiny;
            GUI.color  = CWait;
            Widgets.Label(new Rect(x, y, w, 18f), "PC_ITab_WaitingLabel".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;

            // ── Progress bar ──────────────────────────────────────────────────
            Rect barBg = new Rect(x, y, w, 14f);
            Rect barFg = new Rect(x, y, w * progress, 14f);
            Widgets.DrawBoxSolid(barBg, new Color(0.13f, 0.13f, 0.16f));
            Widgets.DrawBoxSolid(barFg, new Color(0.35f, 0.60f, 0.80f));
            Widgets.DrawBox(barBg);

            // Percentage label centred on bar
            Text.Font = GameFont.Tiny;
            GUI.color  = Color.white;
            using (new TextAnchorScope(TextAnchor.MiddleCenter))
                Widgets.Label(barBg, $"{(int)(progress * 100f)}%");
            Text.Font = GameFont.Small;
            y += 18f;

            // ── Countdown label ───────────────────────────────────────────────
            Text.Font = GameFont.Tiny;
            GUI.color  = daysLeft < 1f ? new Color(0.80f, 0.90f, 0.60f) : CWait;
            string countdown = daysLeft > 0f
                ? "PC_ITab_DaysRemaining".Translate(daysLeft.ToString("F1"))
                : "PC_ITab_ReadyNext".Translate();
            Widgets.Label(new Rect(x, y, w, 18f), countdown);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;

            return y;
        }

        // ─────────────────────────────────────────────────────────────────
        //  CLIMAX SKILL CHECK UI
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Renders the skill-check panel for a climax stage.
        /// Shows: skill name, current level, threshold, progress bar, deadline countdown,
        /// "Resolve Arc" button (gold, only when threshold met), and "Abandon Arc" (red, always).
        /// </summary>
        private float DrawClimaxSkillCheck(float x, float y, float w,
            ArcStageEntry entry, Pawn pawn, CompPersonalChronicles comp)
        {
            // ── Final Test header ──────────────────────────────────────────────
            Text.Font = GameFont.Tiny;
            GUI.color  = new Color(1.00f, 0.82f, 0.30f);
            Widgets.Label(new Rect(x, y, w, 18f), "⚔  FINAL TEST");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;

            // ── Condition label e.g. "Reach Shooting level 8 (currently 5)" ───
            GUI.color = entry.conditionMet ? COk : CB;
            float lh  = Text.CalcHeight(entry.waitConditionLabel, w);
            Widgets.Label(new Rect(x, y, w, lh), entry.waitConditionLabel);
            GUI.color = Color.white;
            y += lh + 6f;

            // ── Progress bar ───────────────────────────────────────────────────
            var skillDef = DefDatabase<SkillDef>.GetNamed(entry.waitConditionKey, errorOnFail: false);
            if (skillDef != null)
            {
                float current   = pawn.skills?.GetSkill(skillDef)?.Level ?? 0f;
                float threshold = entry.waitBaselineValue + entry.waitTargetDelta;
                float progress  = threshold > 0f ? Mathf.Clamp01(current / threshold) : 1f;

                Rect barBg = new Rect(x, y, w, 16f);
                Rect barFg = new Rect(x, y, w * progress, 16f);
                Widgets.DrawBoxSolid(barBg, new Color(0.15f, 0.15f, 0.17f));
                Widgets.DrawBoxSolid(barFg, entry.conditionMet
                    ? new Color(0.35f, 0.72f, 0.42f)
                    : new Color(0.30f, 0.50f, 0.80f));
                Widgets.DrawBox(barBg);

                Text.Font = GameFont.Tiny;
                GUI.color  = Color.white;
                using (new TextAnchorScope(TextAnchor.MiddleCenter))
                    Widgets.Label(barBg, $"{(int)current} / {(int)threshold}");
                Text.Font = GameFont.Small;
                y += 22f;
            }

            // ── Deadline countdown ─────────────────────────────────────────────
            if (entry.climaxDeadlineTick > 0)
            {
                int   ticksLeft = entry.climaxDeadlineTick - Find.TickManager.TicksGame;
                float daysLeft  = ticksLeft / 60000f;
                bool  urgent    = daysLeft < 5f;

                Text.Font = GameFont.Tiny;
                GUI.color  = urgent ? CFail : CWait;
                string countdown = daysLeft > 0f
                    ? "PC_ITab_DaysRemainingPrepare".Translate(daysLeft.ToString("F1"))
                    : "PC_ITab_TimeExpired".Translate();
                Widgets.Label(new Rect(x, y, w, 18f), countdown);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += 22f;
            }

            y += 4f;

            // ── Buttons ────────────────────────────────────────────────────────
            float bx = x;

            // "Resolve Arc" - gold, only when condition met
            if (entry.conditionMet)
            {
                Rect nb = new Rect(bx, y, 154f, 30f);
                Widgets.DrawBoxSolid(nb, new Color(0.18f, 0.14f, 0.06f));
                Widgets.DrawBox(nb);
                GUI.color = new Color(1.00f, 0.82f, 0.30f);
                using (new TextAnchorScope(TextAnchor.MiddleCenter))
                    Widgets.Label(nb, "PC_ITab_ResolveArc".Translate());
                GUI.color = Color.white;

                string? redTitle = comp.currentEpic?.redeemedEpithet;
                string  tip      = "PC_ITab_ResolveArcTip".Translate();
                if (redTitle != null) tip += $"\n\nSuccess -> {redTitle}";
                TooltipHandler.TipRegion(nb, tip);

                if (Widgets.ButtonInvisible(nb))
                    comp.PlayerAdvanceStage(devMode: false);
                bx += 162f;
            }

            // "Abandon Arc" - red, always visible
            {
                Rect ab = new Rect(bx, y, 142f, 30f);
                Widgets.DrawBoxSolid(ab, new Color(0.17f, 0.06f, 0.06f));
                Widgets.DrawBox(ab);
                GUI.color = CFail;
                using (new TextAnchorScope(TextAnchor.MiddleCenter))
                    Widgets.Label(ab, "PC_ITab_AbandonArc".Translate());
                GUI.color = Color.white;

                string? corTitle = comp.currentEpic?.corruptedEpithet;
                string? corDesc  = null;
                string  pName    = pawn.LabelShort;
                string  tip      = $"{pName} gives up the arc. Failure outcome applies.";
                if (corTitle != null) tip += $"\n\nFailure: {corTitle}";
                if (!corDesc.NullOrEmpty()) tip += $"\n\n\"{corDesc}\"";
                TooltipHandler.TipRegion(ab, tip);

                if (Widgets.ButtonInvisible(ab))
                    comp.PlayerAbandonClimax();
                bx += 150f;
            }

            // Dev mode forced-success skip
            if (DebugSettings.godMode)
            {
                Rect db = new Rect(bx, y, 120f, 30f);
                Widgets.DrawBoxSolid(db, new Color(0.20f, 0.13f, 0.06f));
                Widgets.DrawBox(db);
                GUI.color = new Color(0.9f, 0.5f, 0.2f);
                using (new TextAnchorScope(TextAnchor.MiddleCenter))
                    Widgets.Label(db, "PC_ITab_DevSkip".Translate());
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(db))
                    comp.PlayerAdvanceStage(devMode: true);
            }

            y += 42f;
            return y;
        }

        private static Color TagEdgeColor(string tagDefName)
        {
            string tag = tagDefName.ToLowerInvariant().Replace("pc_tag_", "");
            return tag switch
            {
                "violence" or "betrayal" or "survival"          => new Color(0.85f, 0.40f, 0.40f),
                "duty" or "leadership" or "power"               => new Color(0.55f, 0.65f, 0.90f),
                "trauma" or "decay" or "grief" or "loss"        => new Color(0.65f, 0.55f, 0.80f),
                "resilience" or "healer" or "nurture"           => new Color(0.45f, 0.80f, 0.55f),
                "craft" or "curiosity" or "scholar" or "artist" => new Color(0.85f, 0.75f, 0.40f),
                "kinship" or "refugee"                          => new Color(0.75f, 0.65f, 0.50f),
                "devotion" or "faith"                           => new Color(0.90f, 0.80f, 0.55f),
                "noble" or "augmentation"                       => new Color(0.60f, 0.80f, 0.90f),
                "isolation" or "wandering" or "pacifism"        => new Color(0.55f, 0.55f, 0.65f),
                _                                               => new Color(0.55f, 0.55f, 0.60f)
            };
        }

        // -----------------------------------------------------------------
        //  HELPERS
        // -----------------------------------------------------------------

        private static System.Collections.Generic.List<string> GetDiary(CompPersonalChronicles comp)
            => comp.ChronicleLog
                .Where(e => e.Contains("Spark:") || e.Contains("Ember:"))
                .Reverse()
                .Take(30)
                .ToList();

        private static string TrimDiary(string log)
        {
            int b = log.IndexOf(']');
            string trimmed = b >= 0 && b < log.Length - 1 ? log.Substring(b + 2) : log;
            // Clip at first newline so compact list rows show only the title line.
            int nl = trimmed.IndexOf('\n');
            return nl > 0 ? trimmed.Substring(0, nl) : trimmed;
        }

        private static void DrawSectionLabel(ref float y, float w,
            string label, Color? color = null)
        {
            Text.Font = GameFont.Tiny;
            GUI.color  = color ?? new Color(0.56f, 0.56f, 0.58f);
            Widgets.Label(new Rect(10f, y, w - 20f, 18f), label);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;
        }

        private static string ResolveForPawn(string? raw, Pawn? pawn)
        {
            if (raw.NullOrEmpty() || pawn == null) return raw ?? "";
            try
            {
                var req = new GrammarRequest();
                req.Rules.Add(new Rule_String("root", raw!));
                req.Rules.AddRange(GrammarUtility.RulesForPawn("PAWN", pawn));
                return GrammarResolver.Resolve("root", req);
            }
            catch
            {
                return raw ?? "";
            }
        }

        private static Color RoleColor(string role) => role switch
        {
            "opening" => new Color(0.70f, 0.85f, 1.00f),
            "middle"  => new Color(0.85f, 0.85f, 0.70f),
            "success" => new Color(0.60f, 0.90f, 0.60f),
            "failure" => new Color(0.90f, 0.55f, 0.55f),
            "climax"  => new Color(1.00f, 0.75f, 0.40f),
            _         => new Color(0.55f, 0.55f, 0.55f)
        };

    }
}
