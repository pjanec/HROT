using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Fdp.Presentation.Editing;

/// <summary>One row of the entity picker's candidate list: <c>glyph · name · side · distance</c>. <c>Id</c> is opaque to the field (the host's id: a network id, a reference key…).</summary>
public readonly record struct EntityCandidate(string Label, Vector4 SideColor, IconGlyph Kind, string Side, string Distance, long Id);

public enum EntityFieldOpKind { None, Pick, PickOnMap, ByKey, Clear }

/// <summary>What the user chose in an <see cref="EntityField"/> this frame (the host writes the value).</summary>
public readonly record struct EntityFieldOp(EntityFieldOpKind Kind, long Id = 0);

/// <summary>
/// ⭐ K1 (docs/DESIGN_Struct_Editor.md §8, struct-editor-visual.svg panel D/E) — THE entity / place field: the kit's <see cref="Chip"/> (▾ end) and ONE picker popup — a type-ahead <c>InputText</c> filtering the
/// caller's candidate rows as Selectables <i>glyph · name · side · distance</i>, then <i>Pick on the map</i> · <i>By key (@name)</i> · <i>Clear</i>. A thing that no longer exists draws its chip RED with the text the caller gives
/// (<c>gone · #42</c>). Lives in <c>Fdp.Presentation</c> because the candidates come from the reference scope (it knows no scope itself — it is handed rows). The filtered list is rebuilt only when the filter text or the
/// candidates' <c>version</c> changes; ⭐ R-2084: zero allocation per steady frame. N-5 wires hosts to the live reference scope.
/// </summary>
public static class EntityField
{
    /// <summary>At most this many rows are listed; the rest is said in a hint (type to narrow).</summary>
    public const int MaxRows = 50;

    private sealed class State
    {
        public readonly TextBuffer Query = new(96);
        public int[] Filtered = new int[16];
        public int FilteredCount, LastVersion = int.MinValue, LastCount = -1;
        // ⭐ CE-4349 — the columns, MEASURED over every candidate (when the candidates or the font change): the glyph, the widest name / side / distance
        public float GlyphW, LabelW, SideW, DistW, MeasuredFont = -1f;
        public int MeasuredVersion = int.MinValue, MeasuredCount = -1;
        public bool Dirty = true, JustOpened;
        public string MoreText = "";
        public int MoreFor = -1;
    }

    private static readonly Dictionary<uint, State> States = new();
    private static readonly IconGlyph GoneIcon = new(KitGlyphWarning, IconPalette.Error);
    private const string KitGlyphWarning = "";

    /// <param name="offerKey">⭐ N-5 — <i>By key (@name)</i> is offered (an <c>EntityLink</c>); false for a value that cannot hold a key (an <c>EntityRef</c>).</param>
    /// <param name="offerMap">⭐ N-5 — <i>Pick on the map</i> is offered (the host has a pick context).</param>
    public static EntityFieldOp Draw(string id, string chipLabel, IconGlyph? kind, float maxWidth, IReadOnlyList<EntityCandidate> candidates, int version = 0, string? goneText = null,
                                     bool offerKey = true, bool offerMap = true)
    {
        uint sid = ImGui.GetID(id);
        ImGui.PushID(id);
        if (!States.TryGetValue(sid, out var st)) States[sid] = st = new State();
        var op = default(EntityFieldOp);

        bool gone = goneText is not null;
        var hit = Chip.Draw("##chip", gone ? goneText! : chipLabel, gone ? GoneIcon : kind, maxWidth, ChipEnd.Dropdown, tint: gone ? IconPalette.Error : null);
        KitProbe.Set(KitProbe.EntityChip, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (hit == ChipHit.Body)
        {
            ImGui.OpenPopup("##pick");
            st.Query.Clear(); st.Dirty = true; st.JustOpened = true;
        }

        Measure(st, candidates, version);
        ImGui.SetNextWindowSize(new Vector2(PopupWidth(st), 0f));   // ⭐ CE-4349 — wide enough for the widest row, at any font
        if (ImGui.BeginPopup("##pick"))
        {
            DrawPopup(st, candidates, version, ref op, offerKey, offerMap);
            ImGui.EndPopup();
        }
        ImGui.PopID();
        return op;
    }

    private static void DrawPopup(State st, IReadOnlyList<EntityCandidate> candidates, int version, ref EntityFieldOp op, bool offerKey, bool offerMap)
    {
        float width = ImGui.GetContentRegionAvail().X;
        ImGui.TextDisabled(KitGlyphSearch);
        ImGui.SameLine(0f, 6f);
        if (st.JustOpened) { ImGui.SetKeyboardFocusHere(); st.JustOpened = false; }
        if (st.Query.Draw("##q", "search by name", -1f)) st.Dirty = true;
        KitProbe.Set(KitProbe.Entry + 3, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (st.Dirty || st.LastVersion != version || st.LastCount != candidates.Count) Rebuild(st, candidates, version);

        var dl = ImGui.GetWindowDrawList();
        LastColumns = (LabelX(st), SideX(st), st.LabelW, st.GlyphW, ImGui.GetWindowWidth());
        int shown = Math.Min(st.FilteredCount, MaxRows);
        float rowH = ImGui.GetTextLineHeight() + 6f;
        for (int r = 0; r < shown; r++)
        {
            var c = candidates[st.Filtered[r]];
            ImGui.PushID(r);
            var pos = ImGui.GetCursorScreenPos();
            bool clicked = ImGui.Selectable("##c", false, ImGuiSelectableFlags.None, new Vector2(width, rowH));
            if (r < 8) KitProbe.Set(KitProbe.CandidateRow + r, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            float ty = pos.Y + 3f;
            // ⭐ CE-4349 — every column from the measured text: the name starts after the glyph (never under it), the side column after the widest name
            float labelX = pos.X + LabelX(st), sideX = pos.X + SideX(st);
            dl.AddText(new Vector2(pos.X + Pad, ty), ImGui.GetColorU32(c.Kind.Color), c.Kind.Glyph);
            dl.PushClipRect(new Vector2(labelX, pos.Y), new Vector2(sideX - Gap * 0.5f, pos.Y + rowH), true);
            dl.AddText(new Vector2(labelX, ty), ImGui.GetColorU32(ImGuiCol.Text), c.Label);
            dl.PopClipRect();
            float dot = ImGui.GetTextLineHeight() * 0.3f;
            dl.AddCircleFilled(new Vector2(sideX + dot, ty + ImGui.GetTextLineHeight() * 0.5f), dot, ImGui.GetColorU32(c.SideColor));
            dl.AddText(new Vector2(sideX + dot * 2f + Pad, ty), ImGui.GetColorU32(ImGuiCol.TextDisabled), c.Side);
            dl.AddText(new Vector2(pos.X + width - ImGui.CalcTextSize(c.Distance).X - 8f, ty), ImGui.GetColorU32(ImGuiCol.TextDisabled), c.Distance);
            if (clicked && op.Kind == EntityFieldOpKind.None) { op = new EntityFieldOp(EntityFieldOpKind.Pick, c.Id); ImGui.CloseCurrentPopup(); }
            ImGui.PopID();
        }
        if (st.FilteredCount > shown)
        {
            if (st.MoreFor != st.FilteredCount - shown) { st.MoreFor = st.FilteredCount - shown; st.MoreText = "… " + st.MoreFor.ToString() + " more — type to narrow"; }
            ImGui.TextDisabled(st.MoreText);
        }
        else if (st.FilteredCount == 0) ImGui.TextDisabled("no match");

        ImGui.Separator();
        if (offerMap && IconRow(0, "##map", KitGlyphCrosshairs, IconPalette.Grey, "Pick on the map", width) && op.Kind == EntityFieldOpKind.None) { op = new EntityFieldOp(EntityFieldOpKind.PickOnMap); ImGui.CloseCurrentPopup(); }
        if (offerKey && IconRow(1, "##key", KitGlyphBookmark, IconPalette.Amber, "By key (@name) — follows whoever holds the key", width) && op.Kind == EntityFieldOpKind.None) { op = new EntityFieldOp(EntityFieldOpKind.ByKey); ImGui.CloseCurrentPopup(); }
        if (IconRow(2, "##clr", KitGlyphXmark, IconPalette.Grey, "Clear", width) && op.Kind == EntityFieldOpKind.None) { op = new EntityFieldOp(EntityFieldOpKind.Clear); ImGui.CloseCurrentPopup(); }
    }

    private const string KitGlyphSearch = "", KitGlyphCrosshairs = "", KitGlyphBookmark = "", KitGlyphXmark = "";

    private static bool IconRow(int slot, string id, string glyph, Vector4 color, string text, float width)
    {
        var pos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.Selectable(id, false, ImGuiSelectableFlags.None, new Vector2(width, ImGui.GetTextLineHeight() + 4f));
        KitProbe.Set(KitProbe.Entry + slot, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var dl = ImGui.GetWindowDrawList();
        dl.AddText(new Vector2(pos.X + 6f, pos.Y + 2f), ImGui.GetColorU32(color), glyph);
        dl.AddText(new Vector2(pos.X + Pad + Math.Max(ImGui.CalcTextSize(glyph).X, ImGui.GetFontSize()) + Pad, pos.Y + 2f), ImGui.GetColorU32(ImGuiCol.Text), text);   // ⭐ CE-4349 — after the glyph, at any font
        return clicked;
    }

    private const float Pad = 6f, Gap = 16f;
    /// <summary>The widest a name column may grow before it is clipped (a very long label does not push the popup off the screen).</summary>
    private const float MaxLabelEm = 28f;

    private static float LabelX(State st) => Pad + st.GlyphW + Pad;
    private static float SideX(State st) => LabelX(st) + st.LabelW + Gap;

    /// <summary>⭐ CE-4349 — the popup's width: the glyph, the widest name, the side column, the distance and the padding — never less than the 380 px the mockup draws at scale 1.</summary>
    private static float PopupWidth(State st)
    {
        float dot = ImGui.GetTextLineHeight() * 0.6f;
        float rows = SideX(st) + dot + Pad + st.SideW + Gap + st.DistW + Pad * 2f + ImGui.GetStyle().WindowPadding.X * 2f;
        return Math.Max(380f * ImGui.GetFontSize() / Fdp.Presentation.Fonts.EditorFontService.BaseUiPx, rows);
    }

    /// <summary>⭐ CE-4349 — measures the columns once per candidates version / count / font (a click's work, not a frame's: 0 B in a steady frame).</summary>
    private static void Measure(State st, IReadOnlyList<EntityCandidate> candidates, int version)
    {
        float font = ImGui.GetFontSize();
        if (st.MeasuredVersion == version && st.MeasuredCount == candidates.Count && st.MeasuredFont == font) return;
        st.MeasuredVersion = version; st.MeasuredCount = candidates.Count; st.MeasuredFont = font;
        float g = ImGui.CalcTextSize(KitGlyphSearch).X, l = 0f, sd = 0f, d = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            g = Math.Max(g, ImGui.CalcTextSize(c.Kind.Glyph).X);
            l = Math.Max(l, ImGui.CalcTextSize(c.Label).X);
            sd = Math.Max(sd, ImGui.CalcTextSize(c.Side).X);
            d = Math.Max(d, ImGui.CalcTextSize(c.Distance).X);
        }
        st.GlyphW = g; st.LabelW = Math.Min(l, MaxLabelEm * font); st.SideW = sd; st.DistW = d;
    }

    /// <summary>For rails: the last drawn popup's columns, relative to a row's left (the name's x, the side column's x, the widest name, the glyph's width) and its width.</summary>
    public static (float LabelX, float SideX, float LabelW, float GlyphW, float PopupW) LastColumns { get; private set; }

    /// <summary>Filters the candidates by the typed text (case-insensitive, on the label) into <see cref="State.Filtered"/> — only when the text / version / count moved.</summary>
    private static void Rebuild(State st, IReadOnlyList<EntityCandidate> candidates, int version)
    {
        string q = st.Query.Text;
        int n = candidates.Count, count = 0;
        if (st.Filtered.Length < n) st.Filtered = new int[Math.Max(n, st.Filtered.Length * 2)];
        for (int i = 0; i < n; i++)
            if (q.Length == 0 || candidates[i].Label.Contains(q, StringComparison.OrdinalIgnoreCase)) st.Filtered[count++] = i;
        st.FilteredCount = count;
        st.LastVersion = version; st.LastCount = n; st.Dirty = false;
    }

    /// <summary>Pure filter (for rails and for hosts that pre-filter): the indices of <paramref name="candidates"/> whose label contains <paramref name="query"/>.</summary>
    public static int Filter(IReadOnlyList<EntityCandidate> candidates, string query, int[] into)
    {
        int count = 0;
        for (int i = 0; i < candidates.Count && count < into.Length; i++)
            if (query.Length == 0 || candidates[i].Label.Contains(query, StringComparison.OrdinalIgnoreCase)) into[count++] = i;
        return count;
    }
}
