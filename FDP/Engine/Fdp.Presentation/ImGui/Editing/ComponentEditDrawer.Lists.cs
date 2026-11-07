using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core.Serialization;
using Fdp.Presentation.Input;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using StructEdit.Core;
using StructEdit.Core.Attributes;
using StructEdit.Core.Text;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// ⭐⭐ N-2 (CE-3379, docs/DESIGN_Struct_Editor.md §4 B1–B6, §10.1 second sequence, §10.3) — a DynamicArray / InlineArray / FixedBuffer node draws through the widget KIT instead of the old expand-all + "Add" + trash:
/// short leaves as <see cref="ChipRow"/> (B1), structures as <see cref="ListBlock"/> summary rows (B2 / B5 kinds / B6 filter + pages), <c>[EditListForm(Grid)]</c> as <see cref="GridBlock"/> (B3), fixed buffers as a fixed-size
/// <see cref="ListBlock"/> (B4). Every op goes to the node's <see cref="IContainerOps"/> (a container that is not one resizes only, as before); a move is in place, every other op marks a structural change. ⭐ R-2084: labels and
/// summaries are cached per list and rebuilt only when the table's edit epoch or the count moved.
/// </summary>
public sealed partial class ComponentEditDrawer
{
    private enum ListShape { Chips, List, Grid, FixedLeaves }

    /// <summary>One list node's drawing state (kept by node id): its form, cached text, kit options and the key target.</summary>
    private sealed class ListForm : IStructTableTarget
    {
        public ListShape Shape;
        public Type Element = typeof(object);
        public bool Fixed;
        public readonly List<string> Labels = new();
        public readonly List<IconGlyph?> Kinds = new();
        public int Epoch = -1, Count = -1, Version;
        public ListOptions Options = new();
        public string[] GridHeaders = Array.Empty<string>();
        public Type[] Derived = Array.Empty<Type>();
        public bool HasOps;
        public readonly List<string> RowLabels = new();   // FixedLeaves: "Slot 1" … (the field name's singular + the 1-based index; "[i]" when it cannot be singularised)
        public string Footer = "";
        public int SelectedChip = -1;
        public string? Error;
        public ListOp Pending;
        public int KeySel = -1, KeyCount;

        public bool CanRun(StructTableCommand c)
        {
            if (KeySel < 0 || KeySel >= KeyCount) return false;
            return c switch
            {
                StructTableCommand.Duplicate => !Fixed && HasOps,
                StructTableCommand.Delete => !Fixed,
                StructTableCommand.MoveUp => KeySel > 0,
                StructTableCommand.MoveDown => KeySel < KeyCount - 1,
                StructTableCommand.Paste => ClipboardGet() is { Length: > 0 },
                StructTableCommand.Rename => false,   // a name belongs to the Define table
                _ => true,
            };
        }

        public void Run(StructTableCommand c) => Pending = c switch
        {
            StructTableCommand.Duplicate => new ListOp(ListOpKind.Duplicate, KeySel),
            StructTableCommand.Delete => new ListOp(ListOpKind.Delete, KeySel),
            StructTableCommand.MoveUp => new ListOp(ListOpKind.MoveUp, KeySel, KeySel - 1),
            StructTableCommand.MoveDown => new ListOp(ListOpKind.MoveDown, KeySel, KeySel + 1),
            StructTableCommand.Copy => new ListOp(ListOpKind.Copy, KeySel),
            _ => new ListOp(ListOpKind.Paste, KeySel),
        };
    }

    private readonly Dictionary<string, ListForm> _lists = new(StringComparer.Ordinal);   // by JSON path: it survives the document rebuild an insert / remove causes (node ids do not)
    private int _epoch;   // bumped by every value write and list op this table makes — cached list text is rebuilt when it moves

    /// <summary>The clipboard seam (rails replace it; production is ImGui's).</summary>
    public static Func<string?> ClipboardGet { get; set; } = () => ImGuiApi.GetClipboardText();
    public static Action<string> ClipboardSet { get; set; } = t => ImGuiApi.SetClipboardText(t);

    /// <summary>For rails: the bytes the last list form drawn this frame allocated (R-2084: 0 in a steady frame).</summary>
    public long ListFormBytes { get; private set; }

    /// <summary>For rails: how many element rows the last fixed leaf buffer drew.</summary>
    public int LeafRowsDrawn { get; private set; }
    private string? _rowLabel;   // a fixed buffer names its element rows ("Slot 1"); DrawLeafNode reads it once

    /// <summary>For rails: which form a list node draws as — <c>chips</c> · <c>list</c> · <c>grid</c>, <c>fixed</c> appended for a fixed-size one; null before it was drawn.</summary>
    public string? ListFormOf(EditNode node)
        => _lists.TryGetValue(node.JsonPath, out var f) ? (f.Shape == ListShape.Chips ? "chips" : f.Shape == ListShape.Grid ? "grid" : f.Shape == ListShape.FixedLeaves ? "leaves" : "list") + (f.Fixed ? " fixed" : "") : null;

    /// <summary>For rails: the refused-paste message under a list, or null.</summary>
    public string? ListErrorOf(EditNode node) => _lists.TryGetValue(node.JsonPath, out var f) ? f.Error : null;

    // ── the form of a list node ─────────────────────────────────────────────────────────────────────────────────────────

    private static bool IsShortLeaf(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(Guid);

    private static Type ElementOf(EditNode node)
        => node.Children.Count > 0 ? node.Children[0].ClrType : ElementTypeOf(node.ClrType) ?? typeof(object);

    private static bool WantsGrid(EditNode node, Type element)
    {
        var attrs = node.Metadata.CustomAttributes;
        for (int i = 0; i < attrs.Count; i++) if (attrs[i] is EditListFormAttribute a) return a.Form == EditListForm.Grid;
        return element.GetCustomAttributes(typeof(EditListFormAttribute), true) is { Length: > 0 } found && ((EditListFormAttribute)found[0]).Form == EditListForm.Grid;
    }

    private ListForm FormOf(EditNode node)
    {
        if (_lists.TryGetValue(node.JsonPath, out var f)) return f;
        f = new ListForm { Element = ElementOf(node) };
        var container = node.Binding as IContainerBinding;
        var ops = container as IContainerOps;
        f.HasOps = ops != null;
        f.Fixed = node.Kind != EditNodeKind.DynamicArray || container == null || !(ops?.CanInsertRemove ?? container.CanResize);
        f.Shape = f.Fixed ? (IsShortLeaf(f.Element) ? ListShape.FixedLeaves : ListShape.List) : IsShortLeaf(f.Element) ? ListShape.Chips : WantsGrid(node, f.Element) ? ListShape.Grid : ListShape.List;

        if (f.Element.IsAbstract || f.Element.IsInterface)
        {
            var derived = new List<Type>();
            foreach (var a in f.Element.GetCustomAttributes(typeof(JsonDerivedTypeAttribute), true))
                if (a is JsonDerivedTypeAttribute d) derived.Add(d.DerivedType);
            f.Derived = derived.ToArray();
        }
        var o = f.Options;
        o.FixedSize = f.Fixed;
        o.AddLabel = "Add " + (f.Element == typeof(object) ? "item" : DisplayNames.Of(f.Element).ToLowerInvariant());
        if (f.Derived.Length > 0)
        {
            var kinds = new ListKind[f.Derived.Length];
            for (int k = 0; k < kinds.Length; k++)
            {
                var t = f.Derived[k];
                var icon = DisplayNames.IconOf(t) is { } key && NodeEditor.Core.Widgets.Icons.TryGet(key, out var g) ? g : new IconGlyph("", IconPalette.Grey);
                kinds[k] = new ListKind(icon, DisplayNames.Of(t));
            }
            o.Kinds = kinds;
        }
        if (!f.HasOps) o.Disabled = ListCmd.InsertAbove | ListCmd.InsertBelow | ListCmd.Duplicate;
        o.Shortcut(ListCmd.Duplicate, StructTableFocus.ChordLabel(StructTableCommand.Duplicate))
         .Shortcut(ListCmd.MoveUp, StructTableFocus.ChordLabel(StructTableCommand.MoveUp))
         .Shortcut(ListCmd.MoveDown, StructTableFocus.ChordLabel(StructTableCommand.MoveDown))
         .Shortcut(ListCmd.Delete, StructTableFocus.ChordLabel(StructTableCommand.Delete))
         .Shortcut(ListCmd.Copy, StructTableFocus.ChordLabel(StructTableCommand.Copy))
         .Shortcut(ListCmd.Paste, StructTableFocus.ChordLabel(StructTableCommand.Paste));
        _lists[node.JsonPath] = f;
        return f;
    }

    // ── cached text: chip labels / item summaries / kind glyphs ───────────────────────────────────────────────────────────────

    private void Refresh(ListForm f, EditNode node)
    {
        int n = node.Children.Count;
        if (f.Epoch == _epoch && f.Count == n) return;
        f.Epoch = _epoch; f.Count = n; f.Version++;
        f.Labels.Clear(); f.Kinds.Clear();
        bool poly = f.Derived.Length > 0 || f.Element.IsAbstract || f.Element.IsInterface;
        for (int i = 0; i < n; i++)
        {
            var child = node.Children[i];
            object? v = child.Binding?.GetBoxed();
            f.Labels.Add(f.Shape == ListShape.Chips ? ChipLabel(v) : SummaryOf(child, v, poly));
            f.Kinds.Add(poly && v != null ? KindIconOf(child) : null);
        }
        if (f.Shape == ListShape.Grid) f.GridHeaders = GridHeadersOf(f, node);
        if (f.Shape == ListShape.FixedLeaves)
        {
            f.RowLabels.Clear();
            string? singular = SingularOf(LabelOf(node));
            for (int i = 0; i < n; i++) f.RowLabels.Add(singular != null ? singular + " " + (i + 1).ToString() : node.Children[i].Name);
            f.Footer = n.ToString() + " / " + n.ToString() + " — fixed size";
        }
    }

    /// <summary>The singular of a field label for the row names of a fixed buffer (<c>Slots</c> → <c>Slot</c>, <c>Entries</c> → <c>Entry</c>); null when it does not end in a plural s — the rows keep <c>[i]</c>.</summary>
    private static string? SingularOf(string label)
    {
        if (label.Length > 3 && label.EndsWith("ies", StringComparison.Ordinal)) return label.Substring(0, label.Length - 3) + "y";
        if (label.Length > 1 && label.EndsWith("s", StringComparison.Ordinal) && !label.EndsWith("ss", StringComparison.Ordinal)) return label.Substring(0, label.Length - 1);
        return null;
    }

    private string ChipLabel(object? v)
    {
        if (v == null) return "(none)";
        return _types.TryGet(v.GetType(), out var e) ? e.Format(v) : ValueText.Format(v);
    }

    /// <summary>The summary row text of an item: the type's registered summary, else its catalogue text, else its first two fields' values (<c>Alpha · 10</c>); a polymorphic item leads with its kind's name.</summary>
    private string SummaryOf(EditNode child, object? v, bool poly)
    {
        if (v == null) return "(none)";
        var t = v.GetType();
        string? text = EditSummaries.Of(v);
        if (text == null && ValueText.Handles(t)) text = _types.TryGet(t, out var he) ? he.Format(v) : ValueText.Format(v);
        if (text == null && _types.TryGet(t, out var e) && e.CanDraw)
        {
            string s = e.Format(v);
            if (s.Length > 0 && s != t.ToString() && s != v.ToString()) text = s;
        }
        if (text == null)
        {
            int taken = 0;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < child.Children.Count && taken < 2; i++)
            {
                var g = child.Children[i];
                if (g.Children.Count > 0 || g.Binding == null) continue;
                string part = ValueText.Format(g.Binding.GetBoxed());
                if (part.Length == 0) continue;
                if (taken++ > 0) sb.Append(" · ");
                sb.Append(part);
            }
            text = sb.ToString();
        }
        if (poly) text = DisplayNames.Of(t) + (text.Length > 0 ? "  " + text : "");
        return text;
    }

    private static string[] GridHeadersOf(ListForm f, EditNode node)
    {
        if (node.Children.Count > 0)
        {
            var fields = node.Children[0].Children;
            var h = new string[fields.Count];
            for (int i = 0; i < h.Length; i++) h[i] = LabelOf(fields[i]);
            return h;
        }
        var names = new List<string>();
        foreach (var m in f.Element.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) names.Add(DisplayNames.Humanize(m.Name));
        return names.ToArray();
    }

    // ── drawing ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private void DrawListNode(EditNode node)
    {
        var container = node.Binding as IContainerBinding;
        if (container == null) { DrawContainerNode(node, null, -1); return; }   // no binding to operate on: the plain tree
        var f = FormOf(node);
        Refresh(f, node);

        ImGuiApi.TableNextRow();
        ImGuiApi.TableSetColumnIndex(0);
        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        ImGuiApi.PushID(node.JsonPath);   // ⭐ the kit's per-list state (open flags, selection) is keyed by this: the path survives a rebuild
        ImGuiApi.AlignTextToFramePadding();
        ImGuiApi.TextUnformatted(LabelOf(node));
        ImGuiApi.TableSetColumnIndex(1);

        ListOp op = default;
        switch (f.Shape)
        {
            case ListShape.FixedLeaves: ImGuiApi.TextDisabled(f.Footer); break;
            case ListShape.Chips: op = DrawChips(f, node); break;
            case ListShape.Grid: op = DrawGrid(f, node); break;
            default: op = DrawListBlock(f, node); break;
        }
        if (f.Error != null) RowMarks.ErrorLine(f.Error);
        ImGuiApi.PopID();
        ListFormBytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;

        if (op.Kind != ListOpKind.None) ApplyListOp(f, node, container, op);

        // B4: a fixed buffer of LEAVES — one row per element, "Slot 1" in the label column and the element's own cell in the value column (no summary rows, no arrows)
        LeafRowsDrawn = 0;
        if (f.Shape == ListShape.FixedLeaves)
            for (int i = 0; i < node.Children.Count && i < f.RowLabels.Count; i++)
            {
                _rowLabel = f.RowLabels[i];
                DrawEditNode(node.Children[i]);
                _rowLabel = null;
                LeafRowsDrawn++;
            }

        // B1: the clicked chip's element, edited through its normal leaf cell on the row below
        if (f.Shape == ListShape.Chips && f.SelectedChip >= 0 && f.SelectedChip < node.Children.Count)
            DrawEditNode(node.Children[f.SelectedChip]);
    }

    private ListOp DrawChips(ListForm f, EditNode node)
    {
        var o = ChipRow.Draw("chips", f.Labels, canRemove: !f.Fixed, canAdd: !f.Fixed);
        return o.Kind switch
        {
            ChipRowOpKind.Click => ToggleChip(f, o.Index),
            ChipRowOpKind.Remove => new ListOp(ListOpKind.Delete, o.Index),
            ChipRowOpKind.Add => new ListOp(ListOpKind.Add, o.Index, 0),
            ChipRowOpKind.Move => new ListOp(ListOpKind.Move, o.Index, o.Target),
            _ => default,
        };
    }

    private static ListOp ToggleChip(ListForm f, int i) { f.SelectedChip = f.SelectedChip == i ? -1 : i; return default; }

    private ListOp DrawListBlock(ListForm f, EditNode node)
    {
        ListBlock.Begin("list", f.Labels, f.Options, f.Version);
        if (f.Pending.Kind != ListOpKind.None) { ListBlock.Inject(f.Pending); f.Pending = default; }
        int sel = ListBlock.SelectedIndex;
        bool owns = ListBlock.OwnsSelection;
        for (int v = 0; v < ListBlock.VisibleCount; v++)
        {
            int i = ListBlock.VisibleIndex(v);
            if (i >= node.Children.Count) break;
            if (ListBlock.ItemBegin(i, f.Kinds[i])) DrawItemBody(node.Children[i]);
            ListBlock.ItemEnd();
        }
        var op = ListBlock.End();
        // §10.3 — this list's selected item has the keys while its window is focused (the router runs the StructTable scope; nothing here reads a key)
        if (sel >= 0 && owns && ImGuiApi.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) { f.KeySel = sel; f.KeyCount = node.Children.Count; StructTableFocus.Set(f); }
        return op;
    }

    /// <summary>An open item: its fields (or its one leaf cell) as a nested two-column table indented under its row.</summary>
    private void DrawItemBody(EditNode element)
    {
        if (!ImGuiApi.BeginTable("##item", 2, ImGuiTableFlags.SizingStretchProp)) return;
        ImGuiApi.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthStretch, 0.4f);
        ImGuiApi.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.6f);
        if (element.Children.Count > 0 && element.Kind is EditNodeKind.Struct or EditNodeKind.Class or EditNodeKind.Record)
        {
            _containers.Push(_bindCtx is null ? null : element.Binding?.GetBoxed());
            try
            {
                object? parentValue = element.Binding?.GetBoxed();
                for (int i = 0; i < element.Children.Count; i++)
                {
                    if (HidesChild(parentValue, element.Children[i])) continue;
                    DrawEditNode(element.Children[i]);
                }
            }
            finally { _containers.Pop(); }
        }
        else if (element.Binding?.GetBoxed() == null && (element.ClrType.IsAbstract || element.ClrType.IsInterface))
        {
            ImGuiApi.TableNextRow(); ImGuiApi.TableSetColumnIndex(1); ImGuiApi.TextDisabled("(none)");
        }
        else DrawEditNode(element);
        ImGuiApi.EndTable();
    }

    private ListOp DrawGrid(ListForm f, EditNode node)
    {
        ListOp op = default;
        if (GridBlock.Begin("grid", f.GridHeaders, node.Children.Count, f.Options))
        {
            for (int r = 0; r < node.Children.Count; r++)
            {
                GridBlock.Row(r);
                var fields = node.Children[r].Children;
                for (int c = 0; c < fields.Count && c < f.GridHeaders.Length; c++)
                {
                    GridBlock.Cell(c);
                    DrawCell(fields[c]);
                }
            }
        }
        op = GridBlock.End();
        return op;
    }

    /// <summary>A grid cell: the field's value control only (no label, no row of its own).</summary>
    private void DrawCell(EditNode field)
    {
        ImGuiApi.PushID(field.Id.Value);
        bool changed;
        if (ReferenceLeafDrawers.Handles(field.ClrType)) changed = ReferenceLeafDrawers.Draw(field, 0f, _pickerCtx, _refState);
        else
        {
            object value = field.Binding?.GetBoxed() ?? GetDefaultForType(field.ClrType);
            changed = DrawPrimitiveInput(field.ClrType, ref value, field);
            if (changed) field.Binding?.SetBoxed(value);
        }
        if (changed) _epoch++;
        ImGuiApi.PopID();
    }

    // ── ops ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private object? NewValueFor(ListForm f, IContainerBinding container, int kind)
    {
        if (f.Derived.Length > 0 && (uint)kind < (uint)f.Derived.Length) return Activator.CreateInstance(f.Derived[kind]);
        if (_types.TryGet(f.Element, out var e) && e.NewValue() is { } v) return v;
        return container is IContainerOps ops ? ops.NewElement() : ContainerOps.DefaultOf(f.Element);
    }

    private void Structural() { _epoch++; _session.MarkStructuralChange(); }

    private void ApplyListOp(ListForm f, EditNode node, IContainerBinding container, ListOp op)
    {
        f.Error = null;
        var ops = container as IContainerOps;
        int count = container.Count;
        switch (op.Kind)
        {
            case ListOpKind.Add:
                if (ops != null) ops.Insert(count, NewValueFor(f, container, op.Target)); else container.Resize(count + 1);
                Structural();
                break;
            case ListOpKind.InsertAbove:
                ops?.Insert(op.Index, NewValueFor(f, container, -1)); if (ops != null) Structural();
                break;
            case ListOpKind.InsertBelow:
                ops?.Insert(op.Index + 1, NewValueFor(f, container, -1)); if (ops != null) Structural();
                break;
            case ListOpKind.Duplicate:
                ops?.Duplicate(op.Index); if (ops != null) Structural();
                break;
            case ListOpKind.Delete:
                if (f.Fixed) break;
                if (ops != null) ops.RemoveAt(op.Index); else RemoveElementAtIndex(container, op.Index);
                if (f.SelectedChip == op.Index) f.SelectedChip = -1; else if (f.SelectedChip > op.Index) f.SelectedChip--;
                Structural();
                break;
            case ListOpKind.Move:
            case ListOpKind.MoveUp:
            case ListOpKind.MoveDown:
                if ((uint)op.Target < (uint)count) { ContainerOps.Move(container, op.Index, op.Target); _epoch++; f.SelectedChip = -1; }   // in place: no rebuild
                break;
            case ListOpKind.Reset:
                container.GetElementBinding(op.Index).SetBoxed(NewValueFor(f, container, -1)); _epoch++;
                break;
            case ListOpKind.Copy:
                CopyElement(container, op.Index);
                break;
            case ListOpKind.Paste:
                PasteElement(f, container, op.Index);
                break;
        }
    }

    private static void CopyElement(IContainerBinding container, int index)
    {
        object? v = container.GetElementBinding(index).GetBoxed();
        if (v == null) return;
        ClipboardSet(IsShortLeaf(v.GetType()) ? ValueText.Format(v) : JsonSerializer.Serialize(v, v.GetType(), FdpJsonOptionsRegistry.DefaultRelaxed));
    }

    private void PasteElement(ListForm f, IContainerBinding container, int index)
    {
        string text = ClipboardGet() ?? "";
        if (text.Length == 0) { f.Error = "nothing to paste — copy an item first"; return; }
        var binding = container.GetElementBinding(index);
        var type = binding.ValueType;
        try
        {
            object? value;
            if (ValueText.Handles(type))
            {
                if (!ValueText.TryParse(type, text, out value, out var problem)) { f.Error = "can't paste: " + problem; return; }
            }
            else value = JsonSerializer.Deserialize(text, type, FdpJsonOptionsRegistry.DefaultRelaxed);
            if (value == null) { f.Error = "can't paste: the clipboard holds no " + type.Name; return; }
            binding.SetBoxed(value);
            _epoch++;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            f.Error = "can't paste: " + e.Message;
        }
    }
}
