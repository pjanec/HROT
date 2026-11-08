using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>One row of a typed args form: the header's param, its default, and the override (if any).</summary>
public sealed record ParamArgRow(string Name, string TypeId, Type? Type, string? Comment, string? DefaultJson, string? OverrideJson)
{
    public bool IsOverridden => OverrideJson is not null;

    /// <summary>What the form shows and a run will use: the override, else the default (<c>null</c> literal when neither).</summary>
    public string EffectiveJson => OverrideJson ?? DefaultJson ?? "null";

    /// <summary>One row per declared param of <paramref name="decls"/>, in declaration order, each carrying the matching entry of <paramref name="overrides"/> (a name → value object) if there is one.</summary>
    public static IReadOnlyList<ParamArgRow> From(IReadOnlyList<ScenarioParamDecl>? decls, JsonObject overrides)
    {
        if (decls is not { Count: > 0 }) return Array.Empty<ParamArgRow>();
        return decls.Select(d =>
        {
            ParamTypes.TryResolve(d.TypeId, out var clr);
            return new ParamArgRow(d.Name, d.TypeId, clr, d.Comment, d.Default?.ToJsonString(),
                                   overrides.TryGetPropertyValue(d.Name, out var v) ? v?.ToJsonString() ?? "null" : null);
        }).ToList();
    }
}

/// <summary>
/// ⭐⭐ CE-4141 / CE-4210 MA-8 → ⭐ CE-3384 (presets §5d) — THE typed args form, shared by the macro instance's Details section: one row per declared param, drawn by the Define table (<see cref="FieldTable"/>) in FILL mode — the names and
/// types are fixed (no Add / ⋮ / drag), the value column is the struct table's own cell with the declared default as its default: grey while it is the default, an amber bar + ↺ once overridden (↺ writes the default back, which the caller's
/// <c>onReset</c> turns into "no override"). The caller owns what an edit MEANS: <c>onSet</c> gets the row and the committed JSON, <c>onReset</c> the row.
/// ⛔ The old form's own table, its <i>reset</i> column, its alpha-greyed default and its one-field holder sessions are gone — there is ONE value form.
/// </summary>
public sealed class ParamArgsTable : IDisposable
{
    /// <summary>The rows as the Define table's declarations: fixed names / types, the value = the effective JSON, the default = the declaration's. A write is the caller's onSet / onReset.</summary>
    private sealed class ArgsDeclarations : IFieldDeclarations
    {
        private IReadOnlyList<ParamArgRow> _rows = Array.Empty<ParamArgRow>();
        private string[] _effective = Array.Empty<string>();   // what the table shows per row (an own write updates it at once, so the host echoing it back is no change)
        private FieldRow[] _fieldRows = Array.Empty<FieldRow>();
        private int _version;
        private Action<ParamArgRow, string> _onSet = (_, _) => { };
        private Action<ParamArgRow> _onReset = _ => { };

        public IReadOnlyList<FieldRow> Rows => _fieldRows;
        public int Version => _version;
        public bool IsReadOnly => false;
        public string? LastRefusal => null;

        /// <summary>Takes this frame's rows; the table rebuilds only when they differ from what it shows (an own write is already shown).</summary>
        public void Update(IReadOnlyList<ParamArgRow> rows, Action<ParamArgRow, string> onSet, Action<ParamArgRow> onReset)
        {
            _onSet = onSet; _onReset = onReset;
            bool same = rows.Count == _rows.Count;
            for (int i = 0; same && i < rows.Count; i++)
            {
                var a = rows[i]; var b = _rows[i];
                same = string.Equals(a.Name, b.Name, StringComparison.Ordinal) && string.Equals(a.TypeId, b.TypeId, StringComparison.Ordinal)
                    && string.Equals(a.DefaultJson, b.DefaultJson, StringComparison.Ordinal) && string.Equals(a.EffectiveJson, _effective[i], StringComparison.Ordinal);
            }
            if (same) { _rows = rows; return; }
            Rebuild(rows);
        }

        public void Invalidate() => Rebuild(_rows);

        private void Rebuild(IReadOnlyList<ParamArgRow> rows)
        {
            _rows = rows;
            _effective = new string[rows.Count];
            _fieldRows = new FieldRow[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                _effective[i] = r.EffectiveJson;
                _fieldRows[i] = new FieldRow(r.Name, r.TypeId, r.Type ?? typeof(object), r.EffectiveJson, r.Comment, false, r.DefaultJson);
            }
            _version++;
        }

        public bool Add(string name, string typeId, string? valueJson) => false;
        public bool Rename(string name, string newName) => false;
        public bool Retype(string name, string typeId) => false;
        public bool Move(int from, int to) => false;
        public bool Remove(string name) => false;
        public int UsedBy(string name) => 0;

        public void SetValueJson(string name, string? valueJson)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!string.Equals(_rows[i].Name, name, StringComparison.Ordinal)) continue;
                var row = _rows[i];
                if (valueJson is null || Same(valueJson, row.DefaultJson)) { _onReset(row); _effective[i] = row.DefaultJson ?? "null"; }   // a value equal to the default is NO override
                else { _onSet(row, valueJson); _effective[i] = valueJson; }
                return;
            }
        }

        private static bool Same(string a, string? b)
        {
            if (b is null) return false;
            try { return JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b)); }
            catch (System.Text.Json.JsonException) { return false; }
        }
    }

    private readonly Func<IMapPickContext?> _picks;
    private readonly string _pickScope;
    private readonly ArgsDeclarations _decls = new();
    private readonly FieldTable _table = new();
    private IMapPickContext? _scopedFor;
    private IMapPickContext? _scoped;
    private static readonly FieldTableSettings Settings = new() { Fill = true, ShowDefaults = true, ValueHeader = "Value" };

    /// <summary>What the last <see cref="Draw"/> showed (read by the frame rails): the rows drawn and the overridden ones (each carries a ↺).</summary>
    public int RowsDrawn { get; private set; }
    public int ResetButtons { get; private set; }

    /// <summary>The screen centre of the last ↺ drawn (the frame rails click it); null when none was drawn.</summary>
    public System.Numerics.Vector2? LastResetButtonCenter { get; private set; }

    /// <param name="pickScope">The map-pick scope of the form's fields (<c>"$.macroarg"</c>): a row's field is <c>scope.name</c>.</param>
    public ParamArgsTable(Func<IMapPickContext?>? picks = null, string pickScope = "$.arg")
    {
        _picks = picks ?? (() => null);
        _pickScope = pickScope.TrimEnd('.');
    }

    /// <summary>Zeroes the counters (a view that returns before drawing the table starts each frame from nothing).</summary>
    public void ResetCounts() { RowsDrawn = ResetButtons = 0; LastResetButtonCenter = null; }

    /// <summary>Draws the table. <paramref name="id"/> keeps two tables on one window apart.</summary>
    public bool Draw(string id, IReadOnlyList<ParamArgRow> rows, Action<ParamArgRow, string> onSet, Action<ParamArgRow> onReset)
    {
        ResetCounts();
        _decls.Update(rows, onSet, onReset);
        IMapPickContext? scoped = null;
        if (_picks() is { } p)
        {
            if (!ReferenceEquals(p, _scopedFor)) { _scopedFor = p; _scoped = new ScopedMapPickContext(p, _pickScope); }
            scoped = _scoped;
        }
        ImGuiNET.ImGui.PushID(id);
        _table.Draw(_decls, Settings, scoped);
        ImGuiNET.ImGui.PopID();
        RowsDrawn = rows.Count;
        foreach (var r in rows) if (r.IsOverridden) ResetButtons++;
        if (ResetButtons > 0) LastResetButtonCenter = NodeEditor.Core.Widgets.KitProbe.Center(NodeEditor.Core.Widgets.KitProbe.ResetMark);
        return true;
    }

    /// <summary>The value node of row <paramref name="name"/> in the table's current document (null before it was drawn) — the frame rails read what it shows.</summary>
    internal StructEdit.Core.EditNode? NodeOf(string name) => _table.ValueNode(name);

    /// <summary>Forces a rebuild of the table's document from the next frame's rows (the macro or the entity changed).</summary>
    public void Clear() => _decls.Invalidate();

    public void Dispose() => Clear();
}
