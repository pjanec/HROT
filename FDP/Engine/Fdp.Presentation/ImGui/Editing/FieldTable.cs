using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Fdp.Presentation.Input;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using StructEdit.Core;
using StructEdit.Core.Declared;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>One declared field as the Define table shows it.</summary>
/// <param name="Name">The declared name (the key every binding uses).</param>
/// <param name="TypeId">The host's id of the type (what the type picker returns and <see cref="IFieldDeclarations.Retype"/> takes).</param>
/// <param name="ClrType">The value's CLR type (a leaf, a structure or a list) — <c>typeof(object)</c> when the host cannot resolve the id.</param>
/// <param name="ValueJson">The value (a default, a preset's value, …) as JSON; null ⇒ not set (the type's default shows).</param>
/// <param name="Comment">The declaration's comment, or null.</param>
/// <param name="ReadOnly">The row is greyed.</param>
public readonly record struct FieldRow(string Name, string TypeId, Type ClrType, string? ValueJson, string? Comment, bool ReadOnly);

/// <summary>The groups the ONE type picker lists (docs/DESIGN_Struct_Editor.md §11a): a group the host's settings forbid is greyed with the reason.</summary>
public enum TypeGroup { Values, Enums, References, Structures, Lists }

/// <summary>One entry of the type picker: the host's id, its CLR type and the group it is listed under (also the colour of the type text).</summary>
public readonly record struct TypeChoice(string Id, Type Type, TypeGroup Group);

/// <summary>
/// ⭐ N-6 (CE-3383, docs/DESIGN_Struct_Editor.md §11a) — the ONLY things that make one Define table differ from another: the words, the columns, which type groups the picker offers. ⛔ A host name is never tested in the table.
/// </summary>
public sealed class FieldTableSettings
{
    /// <summary>The Add button's words (<i>Add parameter</i> / <i>Add field</i>).</summary>
    public string AddLabel { get; init; } = "Add field";
    /// <summary>The stem of a new row's name — a free <c>stem1</c>, <c>stem2</c>… is made.</summary>
    public string NewNameStem { get; init; } = "field";
    /// <summary>The value column's header (<i>Default</i> · <i>Value</i> · <i>Live</i>).</summary>
    public string ValueHeader { get; init; } = "Value";
    /// <summary>Show the read-only <i>Used by</i> column.</summary>
    public bool UsedBy { get; init; }
    /// <summary>False ⇒ the picker's Structures group is greyed (<see cref="StructsOffReason"/>).</summary>
    public bool AllowStructs { get; init; } = true;
    /// <summary>False ⇒ the picker's Lists group is greyed (<see cref="ListsOffReason"/>).</summary>
    public bool AllowLists { get; init; } = true;
    public string StructsOffReason { get; init; } = "structures are off here";
    public string ListsOffReason { get; init; } = "lists are off here";
    /// <summary>What the picker offers, in order (the first is the type of a NEW row).</summary>
    public IReadOnlyList<TypeChoice> TypeChoices { get; init; } = Array.Empty<TypeChoice>();

    /// <summary>The group a type belongs to when the host did not say: enum · list · primitive/string/decimal · other.</summary>
    public static TypeGroup GroupOf(Type t)
    {
        if (t.IsEnum) return TypeGroup.Enums;
        if (t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))) return TypeGroup.Lists;
        if (t.IsPrimitive || t == typeof(string) || t == typeof(decimal) || t == typeof(Guid) || t == typeof(DateTime)) return TypeGroup.Values;
        return TypeGroup.Structures;
    }
}

/// <summary>
/// ⭐ N-6 (CE-3383) — what the Define table edits: the declared rows and the operations on them. Every edit is one call to the source that owns the declarations (with its one undo history);
/// a refusal answers false and says why in <see cref="LastRefusal"/>. <see cref="Version"/> moves whenever the rows may have changed (an own edit, an undo, a load) — the table rebuilds its document only then.
/// </summary>
public interface IFieldDeclarations
{
    IReadOnlyList<FieldRow> Rows { get; }
    /// <summary>Moves whenever <see cref="Rows"/> may differ from the last read; a steady frame compares it and does nothing else (R-2084).</summary>
    int Version { get; }
    bool IsReadOnly { get; }
    /// <summary>Why the last edit was refused (the one that returned false); meaningful right after it.</summary>
    string? LastRefusal { get; }
    bool Add(string name, string typeId, string? valueJson);
    bool Rename(string name, string newName);
    bool Retype(string name, string typeId);
    bool Move(int from, int to);
    bool Remove(string name);
    int UsedBy(string name);
    /// <summary>Writes the value of one row (its member JSON); null clears it.</summary>
    void SetValueJson(string name, string? valueJson);
}

/// <summary>
/// ⭐⭐ N-6 (CE-3383, docs/DESIGN_Struct_Editor.md §11a) — the DEFINE table: Name · Type · Value · [Used by] · ⋮ over any <see cref="IFieldDeclarations"/>. It owns no value drawing and no schema logic: the value column is the struct table's own cell
/// (<see cref="ComponentEditDrawer.DrawValueCell"/>) over a declared document built ONCE per <see cref="IFieldDeclarations.Version"/>, and every declaration edit is a call back to the source.
/// Inline rename (double-click / F2: a refusal draws its reason under the row and keeps the typed text), ONE type picker (groups; a forbidden group greyed with its reason), the ⋮ menu, ⋮⋮ drag reorder, the
/// keys through the keymap (<see cref="StructTableFocus"/>: F2 · Ctrl+D · Alt+↑/↓ · Del). One instance per table (it keeps the rename box, the selection and the document).
/// </summary>
public sealed class FieldTable : IStructTableTarget
{
    private readonly Types.TypeCatalogue _types;
    private readonly IServiceProvider? _services;
    private DeclaredEditDocumentBuilder _builder;

    private IFieldDeclarations? _decls;
    private IMapPickContext? _picks;
    private int _builtVersion = -1;
    private FieldRow[] _rows = Array.Empty<FieldRow>();
    private TypeGroup[] _groups = Array.Empty<TypeGroup>();
    private DeclaredEditSession? _session;
    private ComponentEditDrawer? _drawer;
    private FieldTableSettings _settings = new();
    private bool _ro;

    private int _sel = -1;
    private string? _errRow, _errText;                    // the error line: under the row of that name ("" = under the Add button)

    // rename
    private readonly TextBuffer _rename = new(64);
    private int _renameIdx = -1;
    private string _renameOld = "";
    private bool _renameFocus, _renameSeen;
    private int _renameFrames;

    // type picker
    private readonly TextBuffer _search = new(64);
    private int _pickRow = -1;
    private bool _openPicker;

    // pending operation (UI gestures and key commands both end here; applied after the table so the rows do not move mid-draw)
    private enum Op { None, Rename, Retype, Move, Remove, Duplicate, Add, StartRename }
    private Op _op; private int _opIndex, _opTo; private string? _opArg;
    private StructTableCommand? _key;

    // probes (rails)
    private Vector2[] _nameMin = new Vector2[8], _nameMax = new Vector2[8], _typeMin = new Vector2[8], _typeMax = new Vector2[8], _menuMin = new Vector2[8], _menuMax = new Vector2[8];
    private int[] _usedShown = new int[8];
    private Vector2 _addMin, _addMax;

    private string? _addLabelFor, _addText;
    private static readonly string[] Numbers = BuildNumbers(256);
    private static string[] BuildNumbers(int n) { var a = new string[n]; for (int i = 0; i < n; i++) a[i] = i.ToString(); return a; }
    private static string Num(int n) => (uint)n < (uint)Numbers.Length ? Numbers[n] : n.ToString();

    private static readonly Vector4 GreyText = new(0.78f, 0.78f, 0.82f, 1f);

    /// <param name="types">The catalogue the value cells draw from (null ⇒ <see cref="Types.TypeCatalogue.Standard"/>).</param>
    /// <param name="services">What a domain type's cell reads (see <see cref="Types.TypeDrawContext.Get{T}"/>).</param>
    public FieldTable(Types.TypeCatalogue? types = null, IServiceProvider? services = null)
    {
        _types = types ?? Types.TypeCatalogue.Standard;
        _services = services;
        _builder = new DeclaredEditDocumentBuilder(Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed, _types.FieldEditors);
    }

    // ── probes (frame rails read them; nothing in the table does) ───────────────────────────────────────────────────────────────────

    /// <summary>Where each row's name cell was drawn last frame.</summary>
    public IReadOnlyList<(string Name, Vector2 Min, Vector2 Max)> NameCells
    {
        get { var l = new List<(string, Vector2, Vector2)>(_rows.Length); for (int i = 0; i < _rows.Length; i++) l.Add((_rows[i].Name, _nameMin[i], _nameMax[i])); return l; }
    }
    /// <summary>Where each row's type text was drawn.</summary>
    public (Vector2 Min, Vector2 Max) TypeCell(string name) { int i = IndexOf(name); return i < 0 ? default : (_typeMin[i], _typeMax[i]); }
    /// <summary>Where each row's ⋮ button was drawn.</summary>
    public (Vector2 Min, Vector2 Max) MenuButton(string name) { int i = IndexOf(name); return i < 0 ? default : (_menuMin[i], _menuMax[i]); }
    /// <summary>Where the Add button was drawn.</summary>
    public (Vector2 Min, Vector2 Max) AddButton => (_addMin, _addMax);
    /// <summary>The column headers last drawn.</summary>
    public IReadOnlyList<string> ColumnHeaders => _settings.UsedBy ? new[] { "Name", "Type", _settings.ValueHeader, "Used by" } : new[] { "Name", "Type", _settings.ValueHeader };
    /// <summary>The <i>Used by</i> counts last drawn, by row name.</summary>
    public IReadOnlyDictionary<string, int> UsedByDrawn { get { var d = new Dictionary<string, int>(); for (int i = 0; i < _rows.Length; i++) d[_rows[i].Name] = _usedShown[i]; return d; } }
    /// <summary>The error line now drawn (the row it sits under — "" for the Add button — and its text), or null.</summary>
    public (string Row, string Text)? Error => _errText is null ? null : (_errRow ?? "", _errText);
    /// <summary>The name being renamed (null when none), and what the box holds.</summary>
    public string? RenamingName => _renameIdx >= 0 && _renameIdx < _rows.Length ? _renameOld : null;
    public string RenameText => _rename.Text;
    /// <summary>The selected row's name, or null.</summary>
    public string? SelectedName => _sel >= 0 && _sel < _rows.Length ? _rows[_sel].Name : null;
    /// <summary>The value node of a row in the table's current declared document (what its cell draws).</summary>
    public EditNode? ValueNode(string name) { int i = IndexOf(name); return i < 0 || _session is null ? null : _session.Document.Root.Children[i]; }
    /// <summary>The table's type picker is open for this row (null when closed).</summary>
    public string? PickerRow => _pickRow >= 0 && _pickRow < _rows.Length && _picking ? _rows[_pickRow].Name : null;
    private bool _picking;
    private readonly Dictionary<string, (Vector2 Min, Vector2 Max)> _pickCells = new();
    private readonly (Vector2 Min, Vector2 Max)[] _pickGroupCells = new (Vector2, Vector2)[5];
    private readonly string?[] _pickGroupReason = new string?[5];
    /// <summary>Where a type choice of the OPEN picker was drawn (default when it is not drawn).</summary>
    public (Vector2 Min, Vector2 Max) PickerChoice(string id) => _pickCells.TryGetValue(id, out var r) ? r : default;
    /// <summary>The greyed placeholder of a forbidden group of the open picker: where it was drawn and the reason its tooltip gives (null reason ⇒ the group is allowed).</summary>
    public ((Vector2 Min, Vector2 Max) Cell, string? Reason) PickerGroup(TypeGroup g) => (_pickGroupCells[(int)g], _pickGroupReason[(int)g]);
    /// <summary>The value cell drawer (the forwarding rails assert what it was constructed with).</summary>
    public ComponentEditDrawer? Drawer => _drawer;

    private int IndexOf(string name) { for (int i = 0; i < _rows.Length; i++) if (_rows[i].Name == name) return i; return -1; }

    // ── IStructTableTarget: the keys act on the selected row ────────────────────────────────────────────────────────────────────────

    bool IStructTableTarget.CanRun(StructTableCommand c)
    {
        if (_ro || _sel < 0 || _sel >= _rows.Length) return false;
        return c switch
        {
            StructTableCommand.Rename or StructTableCommand.Delete or StructTableCommand.Duplicate => true,
            StructTableCommand.MoveUp => _sel > 0,
            StructTableCommand.MoveDown => _sel < _rows.Length - 1,
            _ => false,
        };
    }

    void IStructTableTarget.Run(StructTableCommand c) => _key = c;

    // ── the frame ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Draws the table at the cursor (it brings its own <c>BeginTable</c>). <paramref name="picks"/> is the map-pick context the value cells use (null ⇒ none).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]  // R-2084: tier-0 JIT boxes struct arguments (40 B per call) — the steady frame must not depend on tiering
    public void Draw(IFieldDeclarations decls, FieldTableSettings settings, IMapPickContext? picks)
    {
        ArgumentNullException.ThrowIfNull(decls);
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        if (_key is { } k) { _key = null; ApplyKey(k); }
        if (_op != Op.None) Exec(decls);
        if (!ReferenceEquals(decls, _decls) || decls.Version != _builtVersion || !ReferenceEquals(picks, _picks)) Rebuild(decls, picks);
        _ro = decls.IsReadOnly;
        _drawer!.ReadOnly = _ro;

        float frameH = ImGuiApi.GetFrameHeight();
        float menuW = frameH;
        var tableMin = ImGuiApi.GetCursorScreenPos();
        float tableW = ImGuiApi.GetContentRegionAvail().X;
        int cols = _settings.UsedBy ? 5 : 4;
        bool ownWrite = false;
        if (ImGuiApi.BeginTable("##fields", cols, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGuiApi.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 0.30f);
            ImGuiApi.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthStretch, 0.18f);
            ImGuiApi.TableSetupColumn(_settings.ValueHeader, ImGuiTableColumnFlags.WidthStretch, 0.40f);
            if (_settings.UsedBy) ImGuiApi.TableSetupColumn("Used by", ImGuiTableColumnFlags.WidthFixed, 64f);
            ImGuiApi.TableSetupColumn("##menu", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHeaderLabel, menuW + 6f);
            ImGuiApi.TableHeadersRow();

            uint owner = ImGuiApi.GetID("##fieldrows");
            int n = _rows.Length;
            var dl = ImGuiApi.GetWindowDrawList();
            for (int i = 0; i < n; i++)
            {
                ref readonly var r = ref _rows[i];
                ImGuiApi.PushID(i);
                ImGuiApi.TableNextRow();

                // name: ⋮⋮ handle, then the cell (or the rename box)
                ImGuiApi.TableSetColumnIndex(0);
                var gpos = ImGuiApi.GetCursorScreenPos();
                float gw = frameH * 0.6f;
                ImGuiApi.InvisibleButton("##g", new Vector2(gw, frameH));
                bool gripHot = ImGuiApi.IsItemHovered();
                if (!_ro) KitDrag.Reorder(owner, i, r.Name, asSource: true, asTarget: false, out _);
                CenterGlyph(dl, KitGlyph.Grip, gpos, gw, frameH, ImGuiApi.GetColorU32(gripHot ? ImGuiCol.Text : ImGuiCol.TextDisabled));
                ImGuiApi.SameLine(0f, 2f);
                if (_renameIdx == i) DrawRenameBox(i);
                else
                {
                    var npos = ImGuiApi.GetCursorScreenPos();
                    bool clicked = ImGuiApi.Selectable("##n", _sel == i, ImGuiSelectableFlags.AllowDoubleClick, new Vector2(0f, frameH));
                    _nameMin[i] = ImGuiApi.GetItemRectMin(); _nameMax[i] = ImGuiApi.GetItemRectMax();
                    if (!_ro && KitDrag.Reorder(owner, i, r.Name, asSource: false, asTarget: true, out int from)) Queue(Op.Move, from, i);
                    if (clicked) { _sel = i; if (!_ro && ImGuiApi.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Queue(Op.StartRename, i); }
                    if (ImGuiApi.IsItemClicked(ImGuiMouseButton.Right)) { _sel = i; ImGuiApi.OpenPopup("##rowmenu"); }
                    dl.AddText(new Vector2(npos.X + 4f, npos.Y + ImGuiApi.GetStyle().FramePadding.Y), ImGuiApi.GetColorU32(r.ReadOnly || _ro ? ImGuiCol.TextDisabled : ImGuiCol.Text), r.Name);
                }
                if (_renameIdx == i) { _nameMin[i] = ImGuiApi.GetItemRectMin(); _nameMax[i] = ImGuiApi.GetItemRectMax(); }

                // type: coloured text, one click = the picker
                ImGuiApi.TableSetColumnIndex(1);
                var tpos = ImGuiApi.GetCursorScreenPos();
                bool tclick = ImGuiApi.Selectable("##t", false, ImGuiSelectableFlags.None, new Vector2(0f, frameH));
                _typeMin[i] = ImGuiApi.GetItemRectMin(); _typeMax[i] = ImGuiApi.GetItemRectMax();
                dl.AddText(new Vector2(tpos.X, tpos.Y + ImGuiApi.GetStyle().FramePadding.Y), ImGuiApi.GetColorU32(ColorOf(_groups[i])), r.TypeId);
                if (tclick && !_ro && !r.ReadOnly) { _pickRow = i; _openPicker = true; }

                // value: the struct table's own cell over the declared document
                ImGuiApi.TableSetColumnIndex(2);
                if (r.ClrType == typeof(object)) { ImGuiApi.AlignTextToFramePadding(); ImGuiApi.TextDisabled("unknown type"); }
                else if (_drawer.DrawValueCell(_session!.Document.Root.Children[i]))
                {
                    decls.SetValueJson(r.Name, _session.Values[r.Name]?.ToJsonString());
                    ownWrite = true;
                    _errText = null;
                }

                int col = 3;
                if (_settings.UsedBy)
                {
                    ImGuiApi.TableSetColumnIndex(col++);
                    int u = decls.UsedBy(r.Name);
                    _usedShown[i] = u;
                    ImGuiApi.AlignTextToFramePadding();
                    ImGuiApi.TextDisabled(Num(u));
                }

                // ⋮ menu
                ImGuiApi.TableSetColumnIndex(col);
                DrawMenu(dl, i, menuW, frameH, n);

                ImGuiApi.PopID();

                if (_errText != null && _errRow == r.Name)
                {
                    ImGuiApi.TableNextRow();
                    ImGuiApi.TableSetColumnIndex(0);
                    var p = ImGuiApi.GetCursorScreenPos();
                    ImGuiApi.PushClipRect(new Vector2(tableMin.X, p.Y), new Vector2(tableMin.X + tableW, p.Y + ImGuiApi.GetTextLineHeightWithSpacing()), false);
                    RowMarks.ErrorLine(_errText);
                    ImGuiApi.PopClipRect();
                }
            }
            ImGuiApi.EndTable();
        }
        if (ownWrite) _builtVersion = decls.Version;   // our own value write: the cells already show it — rebuilding would drop the focused text box

        DrawAdd(frameH);
        if (_errText != null && _errRow == "") RowMarks.ErrorLine(_errText);

        DrawPicker(decls);

        // the keys act on the selected row while this window has the focus (nothing here reads a key — the router runs the scope)
        if (_sel >= 0 && _renameIdx < 0 && ImGuiApi.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) StructTableFocus.Set(this);
        if (_op != Op.None) Exec(decls);
    }

    private static Vector4 ColorOf(TypeGroup g) => g switch
    {
        TypeGroup.Enums => IconPalette.Teal,
        TypeGroup.References => IconPalette.Blue,
        TypeGroup.Structures => IconPalette.Violet,
        TypeGroup.Lists => IconPalette.Orange,
        _ => GreyText,
    };

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]  // R-2084: tier-0 JIT boxes struct arguments (40 B per call) — the steady frame must not depend on tiering
    private static void CenterGlyph(ImDrawListPtr dl, string glyph, Vector2 pos, float w, float h, uint col)
    {
        var sz = ImGuiApi.CalcTextSize(glyph);
        dl.AddText(new Vector2(pos.X + (w - sz.X) * 0.5f, pos.Y + (h - sz.Y) * 0.5f), col, glyph);
    }

    // ── the rename box ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]  // R-2084: tier-0 JIT boxes struct arguments (40 B per call) — the steady frame must not depend on tiering
    private void DrawRenameBox(int i)
    {
        if (_renameFocus) { ImGuiApi.SetKeyboardFocusHere(); _renameFocus = false; }
        _rename.Draw("##rn", "", ImGuiApi.GetContentRegionAvail().X);
        bool active = ImGuiApi.IsItemActive();
        if (active) _renameSeen = true;
        _renameFrames++;
        if (_renameSeen && ImGuiApi.IsItemDeactivated()) { _rename.Sync(); Queue(Op.Rename, i, 0, _rename.Text.Trim()); }
        else if (!_renameSeen && _renameFrames > 3) { _renameIdx = -1; }
    }

    private void StartRename(int i)
    {
        _renameIdx = i; _renameOld = _rows[i].Name; _rename.Set(_renameOld);
        _renameFocus = true; _renameSeen = false; _renameFrames = 0;
        _sel = i;
    }

    // ── the ⋮ menu ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]  // R-2084: tier-0 JIT boxes struct arguments (40 B per call) — the steady frame must not depend on tiering
    private void DrawMenu(ImDrawListPtr dl, int i, float w, float h, int count)
    {
        var pos = ImGuiApi.GetCursorScreenPos();
        bool open = ImGuiApi.InvisibleButton("##m", new Vector2(w, h));
        _menuMin[i] = ImGuiApi.GetItemRectMin(); _menuMax[i] = ImGuiApi.GetItemRectMax();
        bool hot = ImGuiApi.IsItemHovered();
        if (hot) dl.AddRectFilled(pos, pos + new Vector2(w, h), ImGuiApi.GetColorU32(ImGuiCol.HeaderHovered, 0.5f), ImGuiApi.GetStyle().FrameRounding);
        CenterGlyph(dl, KitGlyph.Ellipsis, pos, w, h, ImGuiApi.GetColorU32(hot ? ImGuiCol.Text : ImGuiCol.TextDisabled));
        if (open && !_ro) { _sel = i; ImGuiApi.OpenPopup("##rowmenu"); }
        if (!ImGuiApi.BeginPopup("##rowmenu")) return;
        if (ImGuiApi.MenuItem("Rename", StructTableFocus.ChordLabel(StructTableCommand.Rename), false, !_ro)) Queue(Op.StartRename, i);
        if (ImGuiApi.MenuItem("Duplicate", StructTableFocus.ChordLabel(StructTableCommand.Duplicate), false, !_ro)) Queue(Op.Duplicate, i);
        ImGuiApi.Separator();
        if (ImGuiApi.MenuItem("Move up", StructTableFocus.ChordLabel(StructTableCommand.MoveUp), false, !_ro && i > 0)) Queue(Op.Move, i, i - 1);
        if (ImGuiApi.MenuItem("Move down", StructTableFocus.ChordLabel(StructTableCommand.MoveDown), false, !_ro && i < count - 1)) Queue(Op.Move, i, i + 1);
        ImGuiApi.Separator();
        if (ImGuiApi.MenuItem("Delete", StructTableFocus.ChordLabel(StructTableCommand.Delete), false, !_ro)) Queue(Op.Remove, i);
        ImGuiApi.EndPopup();
    }

    // ── Add ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]  // R-2084: tier-0 JIT boxes struct arguments (40 B per call) — the steady frame must not depend on tiering
    private void DrawAdd(float frameH)
    {
        if (!ReferenceEquals(_addLabelFor, _settings.AddLabel)) { _addLabelFor = _settings.AddLabel; _addText = "        " + _settings.AddLabel; }
        ImGuiApi.Spacing();
        if (_ro) ImGuiApi.BeginDisabled();
        if (ImGuiApi.Button(_addText!)) Queue(Op.Add, 0);
        _addMin = ImGuiApi.GetItemRectMin(); _addMax = ImGuiApi.GetItemRectMax();
        ImGuiApi.GetWindowDrawList().AddText(new Vector2(_addMin.X + 8f, _addMin.Y + ImGuiApi.GetStyle().FramePadding.Y), ImGuiApi.GetColorU32(IconPalette.Green), KitGlyph.Plus);   // the green ⊕ of the mockup
        if (_ro) ImGuiApi.EndDisabled();
    }

    // ── the ONE type picker ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly string[] GroupNames = { "Values", "Enums", "References / places", "Structures", "Lists" };

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]  // R-2084: tier-0 JIT boxes struct arguments (40 B per call) — the steady frame must not depend on tiering
    private void DrawPicker(IFieldDeclarations decls)
    {
        if (_openPicker) { _openPicker = false; _search.Clear(); ImGuiApi.OpenPopup("##typepick"); }
        _picking = false;
        if (!ImGuiApi.BeginPopup("##typepick")) { if (_pickCells.Count > 0) { _pickCells.Clear(); Array.Clear(_pickGroupReason); } return; }
        _picking = true;
        if (_pickRow < 0 || _pickRow >= _rows.Length) { ImGuiApi.EndPopup(); return; }
        if (ImGuiApi.IsWindowAppearing()) ImGuiApi.SetKeyboardFocusHere();
        _search.Draw("##search", "Search types", 260f);
        string filter = _search.Text;
        string current = _rows[_pickRow].TypeId;
        var choices = _settings.TypeChoices;
        for (int g = 0; g < GroupNames.Length; g++)
        {
            var group = (TypeGroup)g;
            bool allowed = group switch { TypeGroup.Structures => _settings.AllowStructs, TypeGroup.Lists => _settings.AllowLists, _ => true };
            string? reason = group == TypeGroup.Structures ? _settings.StructsOffReason : group == TypeGroup.Lists ? _settings.ListsOffReason : null;
            int shown = 0;
            for (int c = 0; c < choices.Count; c++) if (choices[c].Group == group && (filter.Length == 0 || choices[c].Id.Contains(filter, StringComparison.OrdinalIgnoreCase))) shown++;
            if (shown == 0 && allowed) continue;
            ImGuiApi.Spacing();
            ImGuiApi.TextDisabled(GroupNames[g]);
            if (!allowed)
            {
                ImGuiApi.BeginDisabled();
                ImGuiApi.Selectable(group == TypeGroup.Lists ? "list of …" : "any structure …");
                _pickGroupCells[g] = (ImGuiApi.GetItemRectMin(), ImGuiApi.GetItemRectMax()); _pickGroupReason[g] = reason;
                ImGuiApi.EndDisabled();
                if (ImGuiApi.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGuiApi.SetTooltip(reason ?? "");
                continue;
            }
            for (int c = 0; c < choices.Count; c++)
            {
                var ch = choices[c];
                if (ch.Group != group || (filter.Length != 0 && !ch.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))) continue;
                ImGuiApi.PushStyleColor(ImGuiCol.Text, ColorOf(group));
                bool pick = ImGuiApi.Selectable(ch.Id, ch.Id == current);
                ImGuiApi.PopStyleColor();
                _pickCells[ch.Id] = (ImGuiApi.GetItemRectMin(), ImGuiApi.GetItemRectMax());
                if (pick) { Queue(Op.Retype, _pickRow, 0, ch.Id); ImGuiApi.CloseCurrentPopup(); }
            }
        }
        ImGuiApi.EndPopup();
    }

    // ── the document ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private void Rebuild(IFieldDeclarations decls, IMapPickContext? picks)
    {
        _decls = decls; _picks = picks;
        var rows = decls.Rows;
        _rows = new FieldRow[rows.Count];
        if (_nameMin.Length < rows.Count)
        {
            int cap = Math.Max(rows.Count, _nameMin.Length * 2);
            _nameMin = new Vector2[cap]; _nameMax = new Vector2[cap]; _typeMin = new Vector2[cap]; _typeMax = new Vector2[cap]; _menuMin = new Vector2[cap]; _menuMax = new Vector2[cap]; _usedShown = new int[cap];
        }
        _groups = new TypeGroup[rows.Count];
        var fields = new List<FieldDeclaration>(rows.Count);
        var values = new JsonObject();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            _rows[i] = r;
            fields.Add(new FieldDeclaration(r.Name, r.ClrType, null, null, null, r.ReadOnly));
            if (r.ValueJson is { Length: > 0 } json) { try { values[r.Name] = JsonNode.Parse(json); } catch (System.Text.Json.JsonException) { } }
            _groups[i] = GroupFor(r);
        }
        _session = new DeclaredEditSession(_builder, fields, values);
        _drawer = new ComponentEditDrawer(_session, picks, null, _types, _services, null, decls.IsReadOnly);
        _builtVersion = decls.Version;
        if (_sel >= _rows.Length) _sel = -1;
        if (_renameIdx >= 0 && (_renameIdx >= _rows.Length || _rows[_renameIdx].Name != _renameOld)) _renameIdx = -1;
        if (_errRow is { Length: > 0 } er && IndexOf(er) < 0) { _errText = null; _errRow = null; }
    }

    private TypeGroup GroupFor(in FieldRow r)
    {
        var choices = _settings.TypeChoices;
        for (int c = 0; c < choices.Count; c++) if (choices[c].Id == r.TypeId) return choices[c].Group;
        return FieldTableSettings.GroupOf(r.ClrType);
    }

    // ── operations ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private void Queue(Op op, int index, int to = 0, string? arg = null) { if (_op == Op.None) { _op = op; _opIndex = index; _opTo = to; _opArg = arg; } }

    private void ApplyKey(StructTableCommand k)
    {
        if (_sel < 0 || _sel >= _rows.Length) return;
        switch (k)
        {
            case StructTableCommand.Rename: Queue(Op.StartRename, _sel); break;
            case StructTableCommand.Delete: Queue(Op.Remove, _sel); break;
            case StructTableCommand.Duplicate: Queue(Op.Duplicate, _sel); break;
            case StructTableCommand.MoveUp: Queue(Op.Move, _sel, _sel - 1); break;
            case StructTableCommand.MoveDown: Queue(Op.Move, _sel, _sel + 1); break;
        }
    }

    private void Exec(IFieldDeclarations decls)
    {
        var op = _op; int i = _opIndex, to = _opTo; string? arg = _opArg;
        _op = Op.None; _opArg = null;
        if (_ro || (op != Op.Add && (i < 0 || i >= _rows.Length))) return;
        switch (op)
        {
            case Op.StartRename: StartRename(i); break;
            case Op.Rename:
            {
                string old = _renameOld;
                if (string.IsNullOrEmpty(arg) || arg == old) { _renameIdx = -1; if (_errRow == old) _errText = null; break; }
                if (decls.Rename(old, arg)) { _renameIdx = -1; _errText = null; }
                else { Fail(old, decls); _renameFocus = true; _renameSeen = false; _renameFrames = 0; }   // the typed text stays; the box stays open
                break;
            }
            case Op.Retype:
                if (decls.Retype(_rows[i].Name, arg!)) _errText = null; else Fail(_rows[i].Name, decls);
                break;
            case Op.Move:
                if (to >= 0 && to < _rows.Length && decls.Move(i, to)) { _errText = null; _sel = to; } else if (to >= 0 && to < _rows.Length) Fail(_rows[i].Name, decls);
                break;
            case Op.Remove:
            {
                string name = _rows[i].Name; int before = _rows.Length;
                if (decls.Remove(name)) { _errText = null; _sel = Math.Min(i, before - 2); } else Fail(name, decls);
                break;
            }
            case Op.Duplicate:
            {
                var r = _rows[i];
                string name = FreeName(r.Name);
                if (decls.Add(name, r.TypeId, r.ValueJson)) { _errText = null; _sel = _rows.Length; } else Fail(r.Name, decls);
                break;
            }
            case Op.Add:
            {
                var choices = _settings.TypeChoices;
                if (choices.Count == 0) break;
                string name = FreeName(_settings.NewNameStem);
                if (decls.Add(name, choices[0].Id, null)) { _errText = null; _sel = _rows.Length; } else { _errRow = ""; _errText = decls.LastRefusal ?? "refused"; }
                break;
            }
        }
    }

    private void Fail(string row, IFieldDeclarations decls) { _errRow = row; _errText = decls.LastRefusal ?? "refused"; }

    private string FreeName(string stem)
    {
        for (int n = 1; ; n++)
        {
            string name = stem + n.ToString();
            if (IndexOf(name) < 0) return name;
        }
    }
}
