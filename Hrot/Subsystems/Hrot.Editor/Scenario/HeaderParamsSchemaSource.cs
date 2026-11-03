using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.Scenario;

/// <summary>
/// ⭐⭐ CE-4210 P2 (MA-5; docs/DESIGN_Macro_Authoring_Editor.md §3) — the scenario / macro PARAMETERS as one more <see cref="IVariablesSchemaSource"/>, so the Variables table the blackboard
/// already uses edits them: Add · Remove · Rename · Move · default JSON · comment. The rows are the resident header's <c>Params</c> (<see cref="ScenarioDescription.HeaderOf"/>); every edit
/// REPLACES the resident via <see cref="ScenarioDescription.Stamp"/> (a new string, never a field mutated in place — the file's own rule).
/// <para>The reference count of a row = its uses in the world across the three binding homes (<see cref="ParamUsage"/>). ⛔ Remove is REFUSED while bound, naming the bindings.
/// <b>Rename</b> rewrites every use in ONE undoable step (<see cref="Hrot.Presentation.Edits.EditHistory"/>). ⛔ The <c>.params.json</c> source is NOT built here.</para>
/// </summary>
public sealed class HeaderParamsSchemaSource : IVariablesSchemaSource
{
    private readonly Func<EntityRepository?> _world;
    private readonly Hrot.Presentation.Edits.EditHistory _history;
    private readonly Action<string>? _refuse;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IReadOnlyDictionary<string, int>? _counts;
    private long _countsAt = long.MinValue;

    public HeaderParamsSchemaSource(Func<EntityRepository?> world, Hrot.Presentation.Edits.EditHistory history, Action<string>? refuse = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _refuse = refuse;
    }

    public Hrot.Presentation.Edits.EditHistory History => _history;
    /// <summary>The last refusal's text (what the window shows).</summary>
    public string? LastRefusal { get; private set; }

    private void Refuse(string why) { LastRefusal = why; _refuse?.Invoke(why); }

    public bool IsReadOnly => _world() is null;

    // ── the resident header ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The declared params now.</summary>
    public IReadOnlyList<ScenarioParamDecl> Decls() => ScenarioDescription.HeaderOf(_world())?.Params ?? Array.Empty<ScenarioParamDecl>();

    private void Set(IReadOnlyList<ScenarioParamDecl> decls)
    {
        if (_world() is not { } w) return;
        var header = ScenarioDescription.HeaderOf(w) ?? new ScenarioHeader("");
        var next = header with { Params = decls.Count == 0 ? null : decls.ToArray() };
        var held = w.HasSingletonManaged<ScenarioDescriptionResident>() ? w.GetSingletonManaged<ScenarioDescriptionResident>() : null;
        ScenarioDescription.Stamp(w, held?.Text, held?.Name, held?.Hash, held?.Args, ScenarioDescription.HeaderJsonOf(next));
        _counts = null;
    }

    private IInspectableSession? Session => _world() is { } w ? new RepositoryAdapter(w) : null;

    private IReadOnlyDictionary<string, int> Counts()
    {
        long now = _clock.ElapsedMilliseconds;
        if (_counts is null || now - _countsAt > 1000)
        {
            _counts = Session is { } s ? ParamUsage.CountAll(s) : new Dictionary<string, int>();
            _countsAt = now;
        }
        return _counts;
    }

    // ── the table ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<VariableViewModel> Variables
    {
        get
        {
            var counts = Counts();
            return Decls().Select(d =>
            {
                ParamTypes.TryResolve(d.TypeId, out var clr);
                return new VariableViewModel(d.Name, d.TypeId, 0, clr ?? typeof(object), d.Comment, Array.Empty<(string, Guid, Guid)>(),
                    IsUnused: !counts.ContainsKey(d.Name), IsReadOnly: IsReadOnly, DefaultValueJson: d.Default?.ToJsonString());
            }).ToList();
        }
    }

    public string? GetRefactorKey(string variableName) => null;

    public int CountNodesReferencingVariable(string name) => Counts().TryGetValue(name, out var c) ? c : 0;

    // ── edits ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private void Edit(string label, IReadOnlyList<ScenarioParamDecl> before, IReadOnlyList<ScenarioParamDecl> after)
        => _history.Do(label, () => Set(after), () => Set(before));

    public void AddVariable(BlackboardVariableEntry entry)
    {
        if (IsReadOnly) return;
        var before = Decls();
        string name = entry.Name?.Trim() ?? "";
        if (name.Length == 0) { Refuse("A parameter needs a name."); return; }
        if (System.Text.Encoding.UTF8.GetByteCount(name) > FixedString32.MaxLength) { Refuse($"'{name}' is too long; a parameter name is at most {FixedString32.MaxLength} bytes (a link binds by name)."); return; }
        if (before.Any(d => d.Name == name)) { Refuse($"A parameter '{name}' is already declared."); return; }
        if (ParamTypes.IdOf(entry.FieldType) is not { } typeId) { Refuse($"{entry.FieldType.Name} is not a parameter type."); return; }
        JsonNode? def = null;
        if (!string.IsNullOrWhiteSpace(entry.DefaultValueJson)) { try { def = JsonNode.Parse(entry.DefaultValueJson!); } catch (JsonException) { } }
        var after = before.Append(new ScenarioParamDecl(name, typeId, def, string.IsNullOrEmpty(entry.Comment) ? null : entry.Comment)).ToList();
        Edit($"Add Parameter '{name}'", before, after);
    }

    public void RemoveVariable(string name) => RemoveVariables(new[] { name });

    public void RemoveVariables(IReadOnlyList<string> names)
    {
        if (IsReadOnly) return;
        var before = Decls();
        var doomed = before.Where(d => names.Contains(d.Name)).ToList();
        if (doomed.Count == 0) return;
        foreach (var d in doomed)
        {
            var (n, where) = Session is { } s ? ParamUsage.Find(s, d.Name) : (0, Array.Empty<string>());
            if (n > 0) { Refuse($"'{d.Name}' is still bound by {n} field(s): {string.Join(", ", where)}{(n > where.Count ? ", …" : "")}. Unbind them first."); return; }
        }
        Edit(doomed.Count == 1 ? $"Delete Parameter '{doomed[0].Name}'" : $"Delete {doomed.Count} Parameters", before, before.Except(doomed).ToList());
    }

    public void RenameVariable(string oldName, string newName)
    {
        if (IsReadOnly || string.IsNullOrWhiteSpace(newName)) return;
        string to = newName.Trim();
        var before = Decls();
        if (before.FirstOrDefault(d => d.Name == oldName) is not { } decl || to == oldName) return;
        if (before.Any(d => d.Name == to)) { Refuse($"A parameter '{to}' is already declared."); return; }
        if (System.Text.Encoding.UTF8.GetByteCount(to) > FixedString32.MaxLength) { Refuse($"'{to}' is too long (at most {FixedString32.MaxLength} bytes)."); return; }
        var after = before.Select(d => d == decl ? d with { Name = to } : d).ToList();
        // ONE step: the declaration and every binding of it (all three homes) move together; undo moves them back together.
        _history.Do($"Rename Parameter '{oldName}' to '{to}'",
            () => { Set(after); if (Session is { } s) ParamUsage.Rename(s, oldName, to); },
            () => { Set(before); if (Session is { } s) ParamUsage.Rename(s, to, oldName); });
    }

    public void MoveVariable(int sourceIndex, int destIndex)
    {
        if (IsReadOnly) return;
        var before = Decls();
        if (sourceIndex < 0 || sourceIndex >= before.Count || destIndex < 0 || destIndex >= before.Count || sourceIndex == destIndex) return;
        var list = before.ToList(); var item = list[sourceIndex]; list.RemoveAt(sourceIndex); list.Insert(destIndex, item);
        Edit($"Reorder Parameter '{item.Name}'", before, list);
    }

    public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)
        => UpdateVariableProperties(name, new VariablePropertyValues(DefaultValueJson: defaultValueJson ?? ""));

    public void UpdateVariableProperties(string name, VariablePropertyValues values)
    {
        if (IsReadOnly || values is null) return;
        var before = Decls();
        if (before.FirstOrDefault(d => d.Name == name) is not { } decl) return;
        var next = decl;
        if (values.DefaultValueJson is { } json)
        {
            JsonNode? def = null;
            if (!string.IsNullOrWhiteSpace(json)) { try { def = JsonNode.Parse(json); } catch (JsonException) { Refuse($"'{name}': the default is not valid JSON."); return; } }
            next = next with { Default = def };
        }
        if (values.Comment is { } c) next = next with { Comment = c.Length == 0 ? null : c };
        if (next == decl) return;
        Edit($"Edit Parameter '{name}'", before, before.Select(d => d == decl ? next : d).ToList());
    }

    public DeclarationPropertySnapshot? ReadVariableProperties(string name)
        => Decls().FirstOrDefault(d => d.Name == name) is { } d
            ? new DeclarationPropertySnapshot(VariableDeclarationKind.BlackboardEntry,
                new VariablePropertyValues(DefaultValueJson: d.Default?.ToJsonString() ?? "", Comment: d.Comment ?? ""), d.TypeId)
            : null;

    // ── no aliasing for parameters ──────────────────────────────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements => Array.Empty<UnboundRequirementViewModel>();
    public void AddAlias(string name, BlackboardAliasBinding binding) { }
    public void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId) { }
    public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;

    // ── declaring from the bind popup (IParamBindContext.NewParam) ──────────────────────────────────────────────────────────────────

    /// <summary>Declares a param of <paramref name="fieldType"/> named after <paramref name="suggestedName"/> (made unique), default = the field's current value; null when the type is not a param type or the world is gone.</summary>
    public ParamInScope? Declare(Type fieldType, string suggestedName, object? currentValue)
    {
        if (IsReadOnly || ParamTypes.IdOf(fieldType) is not { } typeId) return null;
        var before = Decls();
        string name = ParamBindModel.SuggestParamName(suggestedName, before.Select(d => d.Name));
        string? json = null;
        try { if (currentValue is not null) json = JsonSerializer.Serialize(currentValue, fieldType, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed); } catch (Exception) { json = null; }
        AddVariable(new BlackboardVariableEntry(name, fieldType, null, DefaultValueJson: json));
        return Decls().Any(d => d.Name == name) ? new ParamInScope(name, typeId) : null;
    }
}
