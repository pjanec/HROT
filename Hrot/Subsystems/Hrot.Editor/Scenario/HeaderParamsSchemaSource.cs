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
public sealed class HeaderParamsSchemaSource : IVariablesSchemaSource, IRevisionedSource, IRefusingSource, IRetypableSource
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

    private string? _seenHeader;
    private int _revision;
    /// <summary>⭐ N-6 — moves whenever the resident header was replaced (an edit, an undo, a scenario load): the Define table compares this one int per frame. The resident is REPLACED whole on every change, so its string's identity is the signal.</summary>
    public int Revision
    {
        get
        {
            string? cur = _world() is { } w && w.HasSingletonManaged<ScenarioDescriptionResident>() ? w.GetSingletonManaged<ScenarioDescriptionResident>()?.HeaderJson : null;
            if (!ReferenceEquals(cur, _seenHeader)) { _seenHeader = cur; _revision++; }
            return _revision;
        }
    }

    public bool IsReadOnly => _world() is null;
    public DeclarationFeatures Features => DeclarationFeatures.Parameters;

    // ── the resident header ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The declared params now.</summary>
    public IReadOnlyList<ScenarioParamDecl> Decls() => ScenarioDescription.HeaderOf(_world())?.Params ?? Array.Empty<ScenarioParamDecl>();

    /// <summary>⭐ CE-3391 — the stored presets (Default is implicit and not in this list), in order.</summary>
    public IReadOnlyList<ScenarioPreset> Presets() => ScenarioDescription.HeaderOf(_world())?.Presets ?? Array.Empty<ScenarioPreset>();

    private void Set(IReadOnlyList<ScenarioParamDecl> decls) => SetState(decls, Presets());

    /// <summary>The ONE writer of the resident header's params and presets: a NEW header, ONE <see cref="ScenarioDescription.Stamp"/> (never a field mutated in place).</summary>
    private void SetState(IReadOnlyList<ScenarioParamDecl> decls, IReadOnlyList<ScenarioPreset> presets)
    {
        if (_world() is not { } w) return;
        var header = ScenarioDescription.HeaderOf(w) ?? new ScenarioHeader("");
        var next = header with { Params = decls.Count == 0 ? null : decls.ToArray(), Presets = presets.Count == 0 ? null : presets.ToArray() };
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

    /// <summary>One undo step for a declaration edit; <paramref name="mapPresets"/> (a remove / rename / retype of a param) updates every preset's Args in the SAME step (one Stamp each way).</summary>
    private void Edit(string label, IReadOnlyList<ScenarioParamDecl> before, IReadOnlyList<ScenarioParamDecl> after, Func<IReadOnlyList<ScenarioPreset>, IReadOnlyList<ScenarioPreset>>? mapPresets = null)
    {
        var beforeP = Presets();
        var afterP = mapPresets is null ? beforeP : mapPresets(beforeP);
        _history.Do(label, () => SetState(after, afterP), () => SetState(before, beforeP));
    }

    /// <summary>Every preset with its Args rewritten by <paramref name="map"/> (a CLONE of the Args: null ⇒ the key set is unchanged; an empty result ⇒ no Args).</summary>
    private static IReadOnlyList<ScenarioPreset> MapArgs(IReadOnlyList<ScenarioPreset> presets, Action<JsonObject> map)
    {
        var list = new List<ScenarioPreset>(presets.Count);
        foreach (var p in presets)
        {
            if (p.Args is null) { list.Add(p); continue; }
            var args = (JsonObject)p.Args.DeepClone();
            map(args);
            list.Add(p with { Args = args.Count == 0 ? null : args });
        }
        return list;
    }

    public void AddVariable(BlackboardVariableEntry entry)
    {
        LastRefusal = null;
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
        LastRefusal = null;
        if (IsReadOnly) return;
        var before = Decls();
        var doomed = before.Where(d => names.Contains(d.Name)).ToList();
        if (doomed.Count == 0) return;
        foreach (var d in doomed)
        {
            var (n, where) = Session is { } s ? ParamUsage.Find(s, d.Name) : (0, Array.Empty<string>());
            if (n > 0) { Refuse($"'{d.Name}' is still bound by {n} field(s): {string.Join(", ", where)}{(n > where.Count ? ", …" : "")}. Unbind them first."); return; }
        }
        var gone = doomed.Select(d => d.Name).ToList();
        Edit(doomed.Count == 1 ? $"Delete Parameter '{doomed[0].Name}'" : $"Delete {doomed.Count} Parameters", before, before.Except(doomed).ToList(),
            ps => MapArgs(ps, a => { foreach (var n in gone) a.Remove(n); }));
    }

    public void RenameVariable(string oldName, string newName)
    {
        LastRefusal = null;
        if (IsReadOnly || string.IsNullOrWhiteSpace(newName)) return;
        string to = newName.Trim();
        var before = Decls();
        if (before.FirstOrDefault(d => d.Name == oldName) is not { } decl || to == oldName) return;
        if (before.Any(d => d.Name == to)) { Refuse($"A parameter '{to}' is already declared."); return; }
        if (System.Text.Encoding.UTF8.GetByteCount(to) > FixedString32.MaxLength) { Refuse($"'{to}' is too long (at most {FixedString32.MaxLength} bytes)."); return; }
        var after = before.Select(d => d == decl ? d with { Name = to } : d).ToList();
        // ONE step: the declaration, every preset's override of it and every binding of it (all three homes) move together; undo moves them back together.
        var beforeP = Presets();
        var afterP = MapArgs(beforeP, a =>
        {
            if (!a.ContainsKey(oldName)) return;
            var rebuilt = new JsonObject();   // order kept: the key keeps its place
            foreach (var kv in a.ToList()) { a.Remove(kv.Key); rebuilt[kv.Key == oldName ? to : kv.Key] = kv.Value; }
            foreach (var kv in rebuilt.ToList()) { rebuilt.Remove(kv.Key); a[kv.Key] = kv.Value; }
        });
        _history.Do($"Rename Parameter '{oldName}' to '{to}'",
            () => { SetState(after, afterP); if (Session is { } s) ParamUsage.Rename(s, oldName, to); },
            () => { SetState(before, beforeP); if (Session is { } s) ParamUsage.Rename(s, to, oldName); });
    }

    public void MoveVariable(int sourceIndex, int destIndex)
    {
        LastRefusal = null;
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
        LastRefusal = null;
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

    /// <summary>⭐ N-6 — changes a parameter's TYPE (the picker's Retype): the default is dropped (it was a value of the old type); refused while a field is bound to it, naming the bindings.</summary>
    public void RetypeVariable(string name, string typeId)
    {
        LastRefusal = null;
        if (IsReadOnly) return;
        var before = Decls();
        if (before.FirstOrDefault(d => d.Name == name) is not { } decl) return;
        if (!ParamTypes.TryResolve(typeId, out var clr) || ParamTypes.IdOf(clr) is not { } id) { Refuse($"'{typeId}' is not a parameter type."); return; }
        if (id == decl.TypeId) return;
        var (n, where) = Session is { } s ? ParamUsage.Find(s, name) : (0, Array.Empty<string>());
        if (n > 0) { Refuse($"'{name}' is still bound by {n} field(s): {string.Join(", ", where)}{(n > where.Count ? ", …" : "")}. Unbind them first."); return; }
        Edit($"Retype Parameter '{name}'", before, before.Select(d => d == decl ? d with { TypeId = id, Default = null } : d).ToList(),
            ps => MapArgs(ps, a => a.Remove(name)));
    }

    public DeclarationPropertySnapshot? ReadVariableProperties(string name)
        => Decls().FirstOrDefault(d => d.Name == name) is { } d
            ? new DeclarationPropertySnapshot(VariableDeclarationKind.BlackboardEntry,
                new VariablePropertyValues(DefaultValueJson: d.Default?.ToJsonString() ?? "", Comment: d.Comment ?? ""), d.TypeId)
            : null;

    // ── ⭐ CE-3391 (docs/UX/DESIGN_Scenario_Presets.md §5a) — the presets: header edits, each ONE history step, never a field mutated in place ────────────────────

    private bool EditPresets(string label, Func<IReadOnlyList<ScenarioPreset>, IReadOnlyList<ScenarioPreset>?> change)
    {
        LastRefusal = null;
        if (IsReadOnly) return false;
        var before = Presets();
        var after = change(before);
        if (after is null) return false;
        _history.Do(label, () => SetPresetsOnly(after), () => SetPresetsOnly(before));
        return true;
    }

    private void SetPresetsOnly(IReadOnlyList<ScenarioPreset> presets) => SetState(Decls(), presets);

    private static bool IsDefault(string name) => string.Equals(name, ScenarioPreset.DefaultName, StringComparison.OrdinalIgnoreCase);

    private static string FreeName(IReadOnlyList<ScenarioPreset> presets, string stem)
    {
        for (int n = 1; ; n++)
        {
            string name = stem + " " + n;
            if (!IsDefault(name) && !presets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return name;
        }
    }

    /// <summary>[+] — a new preset that is a COPY of <paramref name="shown"/> (Default ⇒ as authored, no overrides), named <i>Preset N</i> (unique). Returns its name, null when refused.</summary>
    public string? AddPresetCopyOf(string shown)
    {
        string? made = null;
        EditPresets("Add Preset", ps =>
        {
            var src = ps.FirstOrDefault(p => string.Equals(p.Name, shown, StringComparison.OrdinalIgnoreCase));
            made = FreeName(ps, "Preset");
            return ps.Append(new ScenarioPreset(made, src?.Variant ?? PresetVariant.AsAuthored, src?.Args is { } a ? (JsonObject)a.DeepClone() : null)).ToList();
        });
        return made;
    }

    /// <summary>Duplicate — a copy right after <paramref name="name"/>, named <i>"name 2"</i>… (unique). Null for Default / unknown.</summary>
    public string? DuplicatePreset(string name)
    {
        string? made = null;
        EditPresets($"Duplicate Preset '{name}'", ps =>
        {
            int i = IndexOfPreset(ps, name);
            if (i < 0) { Refuse(IsDefault(name) ? "Default is the declared values; add a preset to copy them." : $"No preset '{name}'."); return null; }
            made = FreeName(ps, ps[i].Name);
            var list = ps.ToList();
            list.Insert(i + 1, new ScenarioPreset(made, ps[i].Variant, ps[i].Args is { } a ? (JsonObject)a.DeepClone() : null));
            return list;
        });
        return made;
    }

    public bool RenamePreset(string name, string newName)
    {
        newName = newName?.Trim() ?? "";
        return EditPresets($"Rename Preset '{name}'", ps =>
        {
            int i = IndexOfPreset(ps, name);
            if (i < 0) { Refuse(IsDefault(name) ? "Default cannot be renamed." : $"No preset '{name}'."); return null; }
            if (newName.Length == 0) { Refuse("A preset needs a name."); return null; }
            if (IsDefault(newName) || ps.Where((p, k) => k != i).Any(p => string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase))) { Refuse($"A preset '{newName}' already exists."); return null; }
            if (newName == ps[i].Name) return null;
            var list = ps.ToList(); list[i] = list[i] with { Name = newName }; return list;
        });
    }

    public bool DeletePreset(string name) => EditPresets($"Delete Preset '{name}'", ps =>
    {
        int i = IndexOfPreset(ps, name);
        if (i < 0) { Refuse(IsDefault(name) ? "Default cannot be deleted." : $"No preset '{name}'."); return null; }
        var list = ps.ToList(); list.RemoveAt(i); return list;
    });

    /// <summary>Moves a stored preset by <paramref name="delta"/> places (Default stays first).</summary>
    public bool MovePreset(string name, int delta) => EditPresets($"Move Preset '{name}'", ps =>
    {
        int i = IndexOfPreset(ps, name), to = i + delta;
        if (i < 0 || to < 0 || to >= ps.Count) return null;
        var list = ps.ToList(); var p = list[i]; list.RemoveAt(i); list.Insert(to, p); return list;
    });

    /// <summary>Reset all to default — the preset keeps its variant, loses every override.</summary>
    public bool ResetPreset(string name) => EditPresets($"Reset Preset '{name}'", ps =>
    {
        int i = IndexOfPreset(ps, name);
        if (i < 0 || ps[i].Args is null) return null;
        var list = ps.ToList(); list[i] = list[i] with { Args = null }; return list;
    });

    public bool SetPresetVariant(string name, PresetVariant variant) => EditPresets($"Variant of Preset '{name}'", ps =>
    {
        int i = IndexOfPreset(ps, name);
        if (i < 0 || ps[i].Variant == variant) return null;
        var list = ps.ToList(); list[i] = list[i] with { Variant = variant }; return list;
    });

    /// <summary>
    /// A value of a preset: <paramref name="valueJson"/> becomes the override of <paramref name="param"/>; null, or a value EQUAL to the declared default, REMOVES the key (an override is a difference).
    /// </summary>
    public bool SetPresetValue(string name, string param, string? valueJson)
    {
        var decl = Decls().FirstOrDefault(d => d.Name == param);
        if (decl is null) return false;
        JsonNode? value = null;
        if (!string.IsNullOrWhiteSpace(valueJson)) { try { value = JsonNode.Parse(valueJson!); } catch (JsonException) { LastRefusal = null; Refuse($"'{param}': the value is not valid JSON."); return false; } }
        bool isDefault = value is null || JsonNode.DeepEquals(value, decl.Default) || (decl.Default is null && value is JsonValue v0 && IsTypeDefault(v0, decl.TypeId));
        return EditPresets($"Edit '{param}' in Preset '{name}'", ps =>
        {
            int i = IndexOfPreset(ps, name);
            if (i < 0) { Refuse(IsDefault(name) ? "Default's values are the declared defaults." : $"No preset '{name}'."); return null; }
            var args = ps[i].Args is { } a ? (JsonObject)a.DeepClone() : new JsonObject();
            if (isDefault) { if (!args.Remove(param)) return null; }
            else args[param] = value!.DeepClone();
            var list = ps.ToList(); list[i] = list[i] with { Args = args.Count == 0 ? null : args }; return list;
        });
    }

    /// <summary>A declared param with no default holds the TYPE's default (0 · false · "" · the first enum member): a value equal to it is "not overridden".</summary>
    private static bool IsTypeDefault(JsonValue v, string typeId)
    {
        if (!ParamTypes.TryResolve(typeId, out var t)) return false;
        try { var d = Activator.CreateInstance(t) ?? (t == typeof(string) ? "" : null); return d is not null && JsonNode.DeepEquals(v, JsonSerializer.SerializeToNode(d, t, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)); }
        catch (Exception e) when (e is JsonException or NotSupportedException or MissingMethodException) { return false; }
    }

    private static int IndexOfPreset(IReadOnlyList<ScenarioPreset> ps, string name)
    {
        for (int i = 0; i < ps.Count; i++) if (string.Equals(ps[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

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
