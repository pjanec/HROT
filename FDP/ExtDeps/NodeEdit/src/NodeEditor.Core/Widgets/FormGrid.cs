using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ C1 (docs/UX/DESIGN_UI_Conventions.md §3) — THE form layout: a table of <b>label | control</b> (| status slot). Column 1 is
/// <c>min(widest label + 14, max(25 % of the form's width, 90 px × UI scale))</c> (§3.3, Q-C6): the widest label is measured once per form (cached by form id) and the width is
/// recomputed only when the form's width changes. A label wider than the column is cut with … and shows its full text as a tooltip. <see cref="Row"/> leaves the cursor in column 2
/// with <c>SetNextItemWidth(-1)</c>. ⭐ R-2084: after the first frame nothing is allocated.
/// <para>Usage: <c>if (FormGrid.Begin("scn.form", Labels)) { FormGrid.Row("Name"); ImGui.InputText("##n", …); … FormGrid.End(); }</c> with <c>Labels</c> a static array;
/// <c>Begin(id)</c> with no labels learns the widest label from the rows (one frame behind).</para>
/// </summary>
public static class FormGrid
{
    /// <summary>The font size the 90 px floor and the 14 px padding are measured at; the UI scale is <c>FontSize / ReferenceFontSize</c>.</summary>
    public const float ReferenceFontSize = 13f;
    public const float LabelPadding = 14f;
    public const float MinLabelColumn = 90f;
    public const float MaxLabelFraction = 0.25f;
    /// <summary>The status slot's width at the reference font size (DESIGN_Details_Forms §6.2: fixed 18 px).</summary>
    public const float StatusSlotWidth = 18f;

    private sealed class State
    {
        public float Widest, FontSize, FormWidth, Column, LastWidest;
        public bool Fixed;           // measured from the labels given to Begin
        public bool StatusSlot;
    }

    private static readonly Dictionary<string, State> States = new();
    private static readonly State?[] Stack = new State?[8];
    private static int _depth;

    /// <summary>The label column width the last (or current) form of this id used — for rails and for aligning things drawn outside the table.</summary>
    public static float ColumnWidthOf(string id) => States.TryGetValue(id, out var s) ? s.Column : 0f;

    /// <summary>The §3.3 rule as a pure function: the column width for a widest label, a form width and a UI scale.</summary>
    public static float LabelColumn(float widestLabel, float formWidth, float uiScale)
        => MathF.Min(widestLabel + LabelPadding * uiScale, MathF.Max(MaxLabelFraction * formWidth, MinLabelColumn * uiScale));

    /// <summary>Begins a form. <paramref name="labels"/> (a caller-owned, static array) fixes the widest label up front; null learns it from <see cref="Row"/>.</summary>
    /// <returns>false when the table is not visible (do not call Row/End then).</returns>
    public static bool Begin(string id, string[]? labels = null, bool statusSlot = false)
        => Begin(id, labels, statusSlot, ImGuiTableFlags.None, tree: false);

    /// <summary>
    /// A TREE / property table — rows the caller lays out itself (tree nodes in column 1, values in column 2: <c>ImGuiPropertyTree</c>, the raw component editor) on the SAME label-column rule (§3.3):
    /// <c>min(widest + 14, max(25 % of the width, 90 px × scale))</c>, the widest learned from <see cref="Note"/> (nothing noted ⇒ the 25 % cap). ⛔ Not user-resizable — one rule, no per-table drag.
    /// </summary>
    public static bool BeginTree(string id, ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit)
        => Begin(id, null, false, flags, tree: true);

    /// <summary>A tree row tells the current form how wide its column-1 content is (text + indent), so the column learns the widest.</summary>
    public static void Note(float contentWidth)
    {
        var st = _depth > 0 && _depth <= Stack.Length ? Stack[_depth - 1] : null;
        if (st is not null && !st.Fixed && contentWidth > st.Widest) st.Widest = contentWidth;
    }

    private static bool Begin(string id, string[]? labels, bool statusSlot, ImGuiTableFlags flags, bool tree)
    {
        if (!States.TryGetValue(id, out var st)) States[id] = st = new State();
        float fs = ImGui.GetFontSize();
        float scale = fs / ReferenceFontSize;
        if (st.FontSize != fs) { st.FontSize = fs; st.Widest = 0f; st.Fixed = false; st.FormWidth = -1f; st.LastWidest = -1f; }
        if (labels is not null && !st.Fixed)
        {
            float w = 0f;
            for (int i = 0; i < labels.Length; i++) w = MathF.Max(w, ImGui.CalcTextSize(labels[i]).X);
            st.Widest = w; st.Fixed = true; st.FormWidth = -1f;
        }
        float avail = ImGui.GetContentRegionAvail().X;
        // a form that learns its labels (or a tree that Notes its rows) starts at the cap; a wider/narrower learned width recomputes the column
        float widest = !st.Fixed && st.Widest <= 0f ? 1e6f : st.Widest;   // nothing learned yet: the cap, not a 14 px sliver
        if (avail != st.FormWidth || widest != st.LastWidest) { st.FormWidth = avail; st.LastWidest = widest; st.Column = LabelColumn(widest, avail, scale); }
        st.StatusSlot = statusSlot;

        if (!ImGui.BeginTable(id, statusSlot ? 3 : 2, flags)) return false;
        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed, st.Column);
        ImGui.TableSetupColumn("##control", ImGuiTableColumnFlags.WidthStretch);
        if (statusSlot) ImGui.TableSetupColumn("##status", ImGuiTableColumnFlags.WidthFixed, StatusSlotWidth * scale);   // §6.2: a fixed 18 px slot, always there
        if (_depth < Stack.Length) Stack[_depth] = st;
        _depth++;
        return true;
    }

    /// <summary>Starts a row: the label in column 1 (cut with … + tooltip when too wide), the cursor in column 2 with the next item stretched to the cell.</summary>
    public static void Row(string label)
    {
        var st = _depth > 0 && _depth <= Stack.Length ? Stack[_depth - 1] : null;
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        float textW = ImGui.CalcTextSize(label).X;
        if (st is not null && !st.Fixed && textW > st.Widest) st.Widest = textW;     // learning mode: the column catches up next frame
        float col = st?.Column ?? textW;
        float avail = col - ImGui.GetStyle().CellPadding.X * 2f;
        ImGui.AlignTextToFramePadding();
        if (textW <= avail)
            ImGui.TextUnformatted(label);
        else
            CutLabel(label, avail);
        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(-1f);
    }

    private static readonly bool[] FieldOpen = new bool[8];
    private static int _fieldDepth;

    /// <summary>
    /// ONE labelled row as its own one-row form: <c>BeginField("bp.node", "Mode"); ImGui.Combo("##mode", …); EndField();</c>. ⭐ Every field of one <paramref name="form"/> id shares the
    /// learned widest label and the cached column, so rows drawn by separate calls (a drawer's methods, a panel's sections) still line up on ONE label column — the helper for code
    /// whose rows are interleaved with other widgets. ⛔ Never <c>return</c> / <c>break</c> between the pair (the table would stay open); a field that must exit early uses <c>Begin</c>/<see cref="End"/>.
    /// </summary>
    public static void BeginField(string form, string label)
    {
        bool open = Begin(form);
        if (_fieldDepth < FieldOpen.Length) FieldOpen[_fieldDepth] = open;
        _fieldDepth++;
        if (open) Row(label);
    }

    /// <summary>Ends the field begun by <see cref="BeginField"/>.</summary>
    public static void EndField()
    {
        if (_fieldDepth == 0) return;
        _fieldDepth--;
        if (_fieldDepth < FieldOpen.Length && FieldOpen[_fieldDepth]) End();
    }

    /// <summary>Moves to the status slot (column 3) of the current row — only for a form begun with a status slot.</summary>
    public static void Status() => ImGui.TableSetColumnIndex(2);

    public static void End()
    {
        if (_depth > 0) _depth--;
        ImGui.EndTable();
    }

    private static void CutLabel(string label, float avail)
    {
        string ell = EllipsisText();
        float ellW = ImGui.CalcTextSize(ell).X;
        // the longest prefix that fits with the ellipsis (binary search on the span — no substring)
        int lo = 0, hi = label.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (ImGui.CalcTextSize(label.AsSpan(0, mid)).X + ellW <= avail) lo = mid; else hi = mid - 1;
        }
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        uint col = ImGui.GetColorU32(ImGuiCol.Text);
        dl.AddText(pos, col, label.AsSpan(0, lo));
        dl.AddText(new Vector2(pos.X + ImGui.CalcTextSize(label.AsSpan(0, lo)).X, pos.Y), col, ell);
        ImGui.Dummy(new Vector2(avail, ImGui.GetTextLineHeight()));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(label);
    }

    /// <summary>"…" when the font has it, else "...". A font without the glyph draws its fallback "?" — so a width equal to the "?" width means the glyph is missing (no pointer access).</summary>
    private static string EllipsisText()
        => ImGui.CalcTextSize("\u2026").X == ImGui.CalcTextSize("?").X ? "..." : "\u2026";
}
