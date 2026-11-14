using System;
using System.Collections.Generic;
using NodeEditor.Core.Widgets;
using StructEdit.Core;
using StructEdit.Core.Defaults;
using StructEdit.Core.Text;
using ImGuiNET;
using Fdp.Presentation.Editing.Types;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// ⭐⭐ N-3 / N-4 (CE-3380, CE-3381, docs/DESIGN_Struct_Editor.md §3 Parse, §6 S6–S9, §10.1) — the leaf CELLS of the table: a text / number / nullable / flags cell parses what the user types through the catalogue's TypeEditor
/// (else <see cref="ValueText"/>); a REFUSAL keeps the typed text in the cell, keeps the old value in the binding and draws <see cref="RowMarks.ErrorLine"/> under the row — never a silent ignore. A field at its default draws grey, a changed one the
/// amber bar and a ↺ (see <c>DrawLeafNode</c>); a read-only row draws disabled. ⭐ R-2084: parse / format text is cached per node and rebuilt only when the table's edit epoch moves (the typed text itself lives in a pinned <see cref="TextBuffer"/>).
/// </summary>
public sealed partial class ComponentEditDrawer
{
    /// <summary>One text cell's state: the typed text, the refusal (or validation) line, the epoch its text was formatted at, and — for a flags cell — its mask.</summary>
    private sealed class CellState
    {
        public TextBuffer? Buf;
        public string? Error;
        public int Epoch = -1;
        public bool Active, IsSet, OpenBound;
        public ulong Mask;
        /// <summary>⭐ CE-3403 — the boxed value of an enum / bool row, read when the table\'s epoch moves (a boxed byte-sized enum is 24 B: reading it every frame allocated, R-2084).</summary>
        public object? Boxed;
    }

    /// <summary>⭐ CE-3403 — the row\'s value for the plain-primitive path: boxed once per epoch (like the number cells\' text), never per frame.</summary>
    private object? BoxedOf(EditNode node)
    {
        var st = CellOf(node);
        if (st.Epoch != _epoch || st.Boxed == null)
        {
            st.Boxed = node.Binding?.GetBoxed() ?? GetDefaultForType(node.ClrType);
            st.Epoch = _epoch;
        }
        return st.Boxed;
    }

    private readonly Dictionary<int, CellState> _cells = new();
    private readonly Dictionary<int, (int Epoch, bool Changed)> _changed = new();

    private CellState CellOf(EditNode node)
    {
        if (!_cells.TryGetValue(node.Id.Value, out var c)) _cells[node.Id.Value] = c = new CellState();
        return c;
    }

    /// <summary>For rails: the refusal / validation line under a leaf row, or null.</summary>
    public string? CellErrorOf(EditNode node) => _cells.TryGetValue(node.Id.Value, out var c) ? c.Error : null;

    // ── defaults (N-4 / S7) ──────────────────────────────────────────────────────────────────────────────────────────────

    private bool IsChangedFromDefault(EditNode node)
    {
        if (_changed.TryGetValue(node.Id.Value, out var e) && e.Epoch == _epoch) return e.Changed;
        bool changed = FieldValues.IsChanged(node, _defaults!);
        _changed[node.Id.Value] = (_epoch, changed);
        return changed;
    }

    /// <summary>For rails: the text in the cell of <paramref name="node"/> (typed, possibly refused), or null.</summary>
    public string? CellTextOf(EditNode node) => _cells.TryGetValue(node.Id.Value, out var c) ? c.Buf?.Text : null;

    /// <summary>For rails: whether the table draws <paramref name="node"/> as changed from its default.</summary>
    public bool IsChanged(EditNode node) => _defaults != null && IsChangedFromDefault(node);

    private void ResetToDefault(EditNode node)
    {
        if (_defaults == null || !_defaults.TryGetDefault(node, out var d)) return;
        node.Binding?.SetBoxed(d);
        if (_cells.TryGetValue(node.Id.Value, out var c)) { c.Error = null; c.Epoch = -1; }
        _epoch++;
    }

    // ── the cells ────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static bool IsNumber(Type t) => t.IsPrimitive && t != typeof(bool) && t != typeof(char) || t == typeof(decimal);

    /// <summary>Draws the cell when <paramref name="node"/> is one this class owns (a number / string / nullable / flags field that the catalogue does not draw and that is not a slider); false ⇒ the old path draws it.</summary>
    private bool DrawCellNode(EditNode node, float width, out bool changed)
    {
        changed = false;
        var type = node.ClrType;
        var under = Nullable.GetUnderlyingType(type);
        var core = under ?? type;
        if (_types.TryGetFor(node, out var typeEditor) && typeEditor.CanDraw) return false;   // a registered cell draws itself
        bool ranged = node.Metadata.Min.HasValue && node.Metadata.Max.HasValue && (core == typeof(float) || core == typeof(int));
        if (core.IsEnum && Attribute.IsDefined(core, typeof(FlagsAttribute))) { changed = DrawFlagChips(node, core, width); return true; }
        if (!(core == typeof(string) || IsNumber(core)) || ranged) return false;
        if (under != null) { changed = DrawNullableCell(node, under, width); return true; }
        changed = DrawTextCell(node, core, width, typeEditor);
        return true;
    }

    private static bool IsOpenBound(Type t, object? v) => v switch
    {
        float f => f == float.MaxValue,
        double d => d == double.MaxValue,
        int i => i == int.MaxValue,
        _ => false,
    };

    private string TextOf(Type type, object? v, ITypeEditor? editor, CellState st)
    {
        st.OpenBound = IsOpenBound(type, v);
        if (st.OpenBound) return "";   // ⭐ U-7 ⑤: an open bound reads as "no limit" (the hint), never as 3.4E+38
        return editor != null ? editor.Format(v) : ValueText.Format(v);
    }

    /// <summary>A text or number cell: the text lives in a pinned buffer; every edit is parsed — a good value is written (and validated: a problem shows its line but the value stands), a refusal changes nothing but the line.</summary>
    private bool DrawTextCell(EditNode node, Type type, float width, ITypeEditor? editor, bool disabledValue = false)
    {
        var st = CellOf(node);
        st.Buf ??= new TextBuffer(type == typeof(string) ? 512 : 64);
        editor ??= _types.TryGet(type, out var te) ? te : null;
        if (!st.Active && st.Error == null && st.Epoch != _epoch)
        {
            st.Buf.Set(TextOf(type, node.Binding?.GetBoxed(), editor, st));
            st.Epoch = _epoch;
        }
        string? unit = type == typeof(string) ? null : UnitOf(node);
        float w = unit == null ? width : Math.Max(width - ImGuiApi.CalcTextSize(unit).X - ImGuiApi.GetStyle().ItemSpacing.X, 40f);
        bool refused = st.Error != null;
        if (refused) { ImGuiApi.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f); ImGuiApi.PushStyleColor(ImGuiCol.Border, IconPalette.Error); }   // the red frame of the mockup's error row
        bool edited = st.Buf.Draw("##v", st.OpenBound ? NumberInput.NoLimit : "", w);
        if (refused) { ImGuiApi.PopStyleColor(); ImGuiApi.PopStyleVar(); }
        st.Active = ImGuiApi.IsItemActive();
        KitProbe.Set(KitProbe.TextCell, ImGuiApi.GetItemRectMin(), ImGuiApi.GetItemRectMax());
        if (unit != null) { ImGuiApi.SameLine(); ImGuiApi.TextDisabled(unit); }

        bool wrote = false;
        if (edited)
        {
            string text = st.Buf.Text;
            if (text.Length == 0 && st.OpenBound) { st.Error = null; }
            else if (TryParseText(editor, type, text, out var value, out var problem))
            {
                node.Binding?.SetBoxed(value);
                st.Error = editor?.Validate(value, new Types.TypeDrawContext(node, _session, _pickerCtx, _services));   // a problem with a parsed value: shown, the value stands
                wrote = true;
            }
            else st.Error = problem + " — the value stays " + (node.Binding?.GetBoxed() is { } kept ? ValueText.Format(kept) : "empty");
        }
        if (st.Error != null) RowMarks.ErrorLine(st.Error);
        return wrote;
    }

    private static bool TryParseText(ITypeEditor? editor, Type type, string text, out object? value, out string? problem)
        => editor != null ? editor.TryParse(text, out value, out problem) : ValueText.TryParse(type, text, out value, out problem);

    /// <summary>A <c>Nullable&lt;T&gt;</c>: a "set" box and the value (disabled while unset); unchecking writes null, checking writes the type's default.</summary>
    private bool DrawNullableCell(EditNode node, Type under, float width)
    {
        var st = CellOf(node);
        if (st.Epoch != _epoch && !st.Active) { st.IsSet = node.Binding?.GetBoxed() != null; }
        bool isSet = st.IsSet;
        bool changed = false;
        if (RowMarks.SetBox("##set", ref isSet))
        {
            st.IsSet = isSet;
            node.Binding?.SetBoxed(isSet ? ContainerOps.DefaultOf(under) : null);
            st.Epoch = -1; st.Error = null;
            changed = true;
        }
        ImGuiApi.SameLine();
        if (!st.IsSet) ImGuiApi.BeginDisabled();
        float w = Math.Max(width - ImGuiApi.GetFrameHeight() - ImGuiApi.GetStyle().ItemSpacing.X, 40f);
        ImGuiApi.SetNextItemWidth(w);
        if (st.IsSet) { if (DrawTextCell(node, under, w, null)) changed = true; }
        else ImGuiApi.TextDisabled("not set");
        if (!st.IsSet) ImGuiApi.EndDisabled();
        return changed;
    }

    // ── [Flags] enums: toggle chips, written as joined names ─────────────────────────────────────────────────────────────────

    private sealed class FlagInfo { public string[] Names = Array.Empty<string>(); public ulong[] Values = Array.Empty<ulong>(); public string[] Ids = Array.Empty<string>(); }
    private static readonly Dictionary<Type, FlagInfo> Flags = new();

    private static FlagInfo FlagsOf(Type type)
    {
        if (Flags.TryGetValue(type, out var f)) return f;
        var names = new List<string>(); var values = new List<ulong>();
        foreach (var n in Enum.GetNames(type))
        {
            ulong v = Convert.ToUInt64(Enum.Parse(type, n));
            if (v == 0) continue;
            names.Add(n); values.Add(v);
        }
        f = new FlagInfo { Names = names.ToArray(), Values = values.ToArray(), Ids = new string[names.Count] };
        for (int i = 0; i < f.Ids.Length; i++) f.Ids[i] = "##f" + i.ToString();
        Flags[type] = f;
        return f;
    }

    private bool DrawFlagChips(EditNode node, Type type, float width)
    {
        var st = CellOf(node);
        var info = FlagsOf(type);
        if (st.Epoch != _epoch) { st.Mask = node.Binding?.GetBoxed() is { } v ? Convert.ToUInt64(v) : 0UL; st.Epoch = _epoch; }
        float spacing = ImGuiApi.GetStyle().ItemSpacing.X, avail = Math.Max(width, 60f), x = 0f;
        bool changed = false;
        for (int i = 0; i < info.Names.Length; i++)
        {
            float cw = Chip.WidthOf(info.Names[i], null, avail, ChipEnd.Toggle);
            if (i > 0) { if (x + spacing + cw <= avail) { ImGuiApi.SameLine(); x += spacing + cw; } else x = cw; } else x = cw;
            bool on = (st.Mask & info.Values[i]) == info.Values[i];
            var hit = Chip.Draw(info.Ids[i], info.Names[i], null, avail, ChipEnd.Toggle, on);
            if (i < 8) KitProbe.Set(KitProbe.FlagChip + i, ImGuiApi.GetItemRectMin(), ImGuiApi.GetItemRectMax());
            if (hit == ChipHit.Body)
            {
                st.Mask ^= info.Values[i];
                changed = true;
            }
        }
        if (changed) node.Binding?.SetBoxed(Enum.ToObject(type, st.Mask));   // the value's own names are what the file and the cell text read as (ValueText.Format joins them)
        return changed;
    }
}
