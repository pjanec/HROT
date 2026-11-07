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
        if (hit == ChipHit.Body)
        {
            ImGui.OpenPopup("##pick");
            st.Query.Clear(); st.Dirty = true; st.JustOpened = true;
        }

        ImGui.SetNextWindowSize(new Vector2(380f, 0f));
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
            dl.AddText(new Vector2(pos.X + 6f, ty), ImGui.GetColorU32(c.Kind.Color), c.Kind.Glyph);
            dl.AddText(new Vector2(pos.X + 28f, ty), ImGui.GetColorU32(ImGuiCol.Text), c.Label);
            float sx = pos.X + width * 0.50f;
            dl.AddCircleFilled(new Vector2(sx, ty + ImGui.GetTextLineHeight() * 0.5f), 4f, ImGui.GetColorU32(c.SideColor));
            dl.AddText(new Vector2(sx + 10f, ty), ImGui.GetColorU32(ImGuiCol.TextDisabled), c.Side);
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
        dl.AddText(new Vector2(pos.X + 28f, pos.Y + 2f), ImGui.GetColorU32(ImGuiCol.Text), text);
        return clicked;
    }

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
