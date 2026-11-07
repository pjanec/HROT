using System;
using System.Collections.Generic;
using Fdp.Presentation.Editing;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>A schema source whose rows can change without any call through the table (an undo, a scenario load): a counter that moves when they do. ⭐ N-6 — it is what lets the Define table compare ONE int per frame instead of reading the rows.</summary>
public interface IRevisionedSource { int Revision { get; } }

/// <summary>A schema source that says why it refused the last call (cleared at the start of every mutating call).</summary>
public interface IRefusingSource { string? LastRefusal { get; } }

/// <summary>A schema source that can change a declaration's TYPE (a migration — refused while something uses the declaration).</summary>
public interface IRetypableSource { void RetypeVariable(string name, string typeId); }

/// <summary>
/// ⭐ N-6 (CE-3383, docs/DESIGN_Struct_Editor.md §11a) — the ONE adapter between the Define table (<see cref="IFieldDeclarations"/>) and the declaration sources that already exist
/// (<see cref="IVariablesSchemaSource"/>): every edit is a forwarded call, so the source's own validation, refusals and undo history stay the single owner. ⛔ No schema logic lives here beyond the shared
/// name check (<see cref="BlackboardNameValidator.ValidateRename"/> — moved out of the old table, not copied).
/// </summary>
public sealed class SchemaSourceDeclarations : IFieldDeclarations
{
    private readonly IVariablesSchemaSource _src;
    private readonly Func<string, Type?> _resolve;
    private int _own;
    private string? _refusal;
    private IReadOnlyList<VariableViewModel>? _lastList;
    private int _listMoves;

    /// <param name="source">The declarations.</param>
    /// <param name="resolveType">The host's type id → CLR type (null ⇒ unknown); used to turn a picked id into the entry a source's <c>AddVariable</c> takes.</param>
    public SchemaSourceDeclarations(IVariablesSchemaSource source, Func<string, Type?> resolveType)
    {
        _src = source ?? throw new ArgumentNullException(nameof(source));
        _resolve = resolveType ?? throw new ArgumentNullException(nameof(resolveType));
    }

    public IReadOnlyList<FieldRow> Rows
    {
        get
        {
            var vars = _src.Variables;
            var rows = new List<FieldRow>(vars.Count);
            foreach (var v in vars) rows.Add(new FieldRow(v.Name, v.TypeName, v.FieldType, v.DefaultValueJson, v.Comment, v.IsReadOnly));
            return rows;
        }
    }

    public int Version
    {
        get
        {
            if (_src is IRevisionedSource r) return unchecked(_own + r.Revision);
            var cur = _src.Variables;   // a source without a revision: the list's identity is the only signal
            if (!ReferenceEquals(cur, _lastList)) { _lastList = cur; _listMoves++; }
            return unchecked(_own + _listMoves);
        }
    }

    public bool IsReadOnly => _src.IsReadOnly;

    public string? LastRefusal => _refusal ?? (_src as IRefusingSource)?.LastRefusal;

    private bool Begin() { _refusal = null; _own++; return true; }
    private bool Ok => LastRefusal == null;

    public bool Add(string name, string typeId, string? valueJson)
    {
        Begin();
        var type = _resolve(typeId);
        if (type is null) { _refusal = $"'{typeId}' is not a type this table can declare."; return false; }
        if (BlackboardNameValidator.Validate(name) is { } why) { _refusal = why; return false; }
        _src.AddVariable(new BlackboardVariableEntry(name, type, null, DefaultValueJson: valueJson));
        return Ok;
    }

    public bool Rename(string name, string newName)
    {
        Begin();
        var names = new List<string>();
        foreach (var v in _src.Variables) names.Add(v.Name);
        if (BlackboardNameValidator.ValidateRename(newName, names) is { } why) { _refusal = $"Rename refused: {why}"; return false; }
        _src.RenameVariable(name, newName);
        return Ok;
    }

    public bool Retype(string name, string typeId)
    {
        Begin();
        if (_src is not IRetypableSource r) { _refusal = "This table cannot change a type."; return false; }
        r.RetypeVariable(name, typeId);
        return Ok;
    }

    public bool Move(int from, int to) { Begin(); _src.MoveVariable(from, to); return Ok; }

    public bool Remove(string name) { Begin(); _src.RemoveVariable(name); return Ok; }

    public int UsedBy(string name) => _src.CountNodesReferencingVariable(name);

    public void SetValueJson(string name, string? valueJson) { Begin(); _src.UpdateVariableDefaultValueJson(name, valueJson); }
}
