using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Scenario;

namespace Hrot.Editor.Scenario;

/// <summary>
/// ⭐ CE-3391 (📄 docs/UX/DESIGN_Scenario_Presets.md §5a, D2 / D3) — the Define table's rows as the SHOWN PRESET sees them: under <c>Default</c> the rows are the params source's own (the value is the declared
/// default); under another preset each row's value is the preset's override, else the declared default — and <see cref="FieldRow.DefaultJson"/> is always the declared default, so the table marks a difference
/// (amber bar + ↺) and a value equal to it GREY. A value write goes to the preset's Args (an equal-to-default value REMOVES the key); every declaration edit goes to the params source under ANY preset
/// (the source updates every preset's Args in the same undo step). ⛔ Showing a preset never loads anything: it only changes these rows.
/// </summary>
internal sealed class PresetDeclarations : IFieldDeclarations
{
    private readonly IFieldDeclarations _inner;
    private readonly HeaderParamsSchemaSource _source;
    private readonly Func<bool> _readOnly;
    private string _shown = ScenarioPreset.DefaultName;
    private int _shownMoves;
    private string? _refusal;

    public PresetDeclarations(IFieldDeclarations inner, HeaderParamsSchemaSource source, Func<bool>? readOnly = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _readOnly = readOnly ?? (() => false);
    }

    /// <summary>The preset the table shows (Default = the declared values).</summary>
    public string Shown
    {
        get => _shown;
        set { if (_shown != value) { _shown = value; _shownMoves++; } }
    }

    public bool IsDefault => string.Equals(_shown, ScenarioPreset.DefaultName, StringComparison.OrdinalIgnoreCase);

    private ScenarioPreset? ShownPreset()
    {
        if (IsDefault) return null;
        foreach (var p in _source.Presets()) if (string.Equals(p.Name, _shown, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    public IReadOnlyList<FieldRow> Rows
    {
        get
        {
            var rows = _inner.Rows;
            if (IsDefault || ShownPreset() is not { } preset) return rows;
            var list = new List<FieldRow>(rows.Count);
            foreach (var r in rows)
            {
                string? over = preset.Args is { } a && a.TryGetPropertyValue(r.Name, out var v) && v is not null ? v.ToJsonString() : null;
                list.Add(r with { ValueJson = over ?? r.DefaultJson });
            }
            return list;
        }
    }

    public int Version => unchecked(_inner.Version + _shownMoves * 7919);
    public bool IsReadOnly => _inner.IsReadOnly || _readOnly();
    public string? LastRefusal => _refusal ?? _inner.LastRefusal;

    private void Clear() => _refusal = null;

    public bool Add(string name, string typeId, string? valueJson)
    {
        Clear();
        // a new row is declared with its VALUE as the declared default (under a preset: the shown value is a default only when it is the preset's own override — declare it plain, then override)
        if (IsDefault || ShownPreset() is null) return _inner.Add(name, typeId, valueJson);
        return _inner.Add(name, typeId, null);
    }

    public bool Rename(string name, string newName) { Clear(); return _inner.Rename(name, newName); }
    public bool Retype(string name, string typeId) { Clear(); return _inner.Retype(name, typeId); }
    public bool Move(int from, int to) { Clear(); return _inner.Move(from, to); }
    public bool Remove(string name) { Clear(); return _inner.Remove(name); }
    public int UsedBy(string name) => _inner.UsedBy(name);

    public void SetValueJson(string name, string? valueJson)
    {
        Clear();
        if (IsDefault || ShownPreset() is null) { _inner.SetValueJson(name, valueJson); return; }
        if (!_source.SetPresetValue(_shown, name, valueJson)) _refusal = _source.LastRefusal;
    }
}
