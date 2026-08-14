using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;   // VariableKind (U-3/U-4: one vocabulary for the three lists)
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.AiShared;

namespace Hrot.Blueprints.Editor.Variables;

public sealed class BlueprintEditableAssetAdapter : IEditableAsset
{
    private readonly BlueprintAsset _asset;
    public BlueprintEditableAssetAdapter(BlueprintAsset asset) { _asset = asset; }
    public Guid AssetId => _asset.AssetId;
    public string Name => _asset.Name;
    public AssetKind Kind => AssetKind.Blueprint;
    public string SourceFilePath => string.Empty;
    public bool IsDirty => false;
    public bool IsEditorOwned => true;
    public event Action? Changed { add { } remove { } }
    public BlueprintAsset Asset => _asset;
}

/// <summary>
/// U-4 — the editor's projection of ONE of the asset's three declaration lists.
///
/// <para>
/// ⛔ <b>This took a <c>bool isParams</c> over a THREE-list model.</b> A flag with two values cannot
/// name three lists, so <c>Variables</c> — the <c>State</c> struct at offset 16, the list every
/// Instance blueprint actually uses — was <b>not representable at all</b>, and ten
/// <c>if (_isParams)</c> branches rode the flag.
/// </para>
///
/// <para>
/// ⭐⭐ <b>Same defect shape as <c>U-3</c>, opposite end of the pipeline</b> — there an untagged
/// <c>int</c>, here an untagged <c>bool</c>, both over these same three lists. ⭐ So it takes the
/// same carrier: <see cref="VariableKind"/>, <b>the compiler's own enum</b>. One vocabulary for one
/// model; a reader who has seen <c>Stage5.FindVariableRef</c> already knows this type.
/// </para>
/// </summary>
public sealed class BlueprintVariableSchemaSource : IVariablesSchemaSource
{
    private readonly BlueprintAsset _asset;
    private readonly Action _onChanged;
    private readonly VariableKind _kind;

    /// <param name="kind">
    /// Which list this source projects. ⛔ <see cref="VariableKind.Unresolved"/> is rejected: it is
    /// the enum's default precisely so a forgotten assignment is loud rather than silently meaning
    /// the first list (<c>U-3</c>'s reasoning, applied at the other end).
    /// </param>
    public BlueprintVariableSchemaSource(BlueprintAsset asset, VariableKind kind, Action onChanged)
    {
        if (kind == VariableKind.Unresolved)
            throw new ArgumentOutOfRangeException(
                nameof(kind), "A schema source must project a specific declaration list.");
        _asset = asset;
        _kind = kind;
        _onChanged = onChanged;
    }

    public bool IsReadOnly => false;

    /// <summary>
    /// ⭐ <c>Q-k</c>: <c>Role</c>/<c>Scope</c> are <b>read-only for blueprints</b> — changing either is
    /// a MOVE between storage classes with reference consequences, not a toggle. ⛔ Saying so is what
    /// stops the panel drawing a live combo whose result is discarded (<c>BP-230</c>).
    /// </summary>
    public bool SupportsRoleScopeEditing => false;

    /// <summary>
    /// ⚠ <b>Parameters get a real C# symbol path</b> — they become fields of the generated
    /// <c>Params</c> struct, so a rename is a genuine cross-language refactor. The other two are
    /// asset-scoped.
    /// <para>
    /// 📌 <b><c>WorkingState</c> and <c>Variables</c> deliberately share the key shape</b>, exactly as
    /// before this task. They cannot collide: <c>BP1024</c>/<c>BP1031</c> mean no asset populates both
    /// — the same fact that made <c>BP-226</c> invisible for so long. ⭐ <c>U-9</c>'s tagged
    /// declaration is where the three become one key; widening it here would change refactor
    /// correlation for a case that cannot arise.
    /// </para>
    /// </summary>
    public string? GetRefactorKey(string variableName)
    {
        if (_kind == VariableKind.Parameter)
        {
            var sanitizedName = Hrot.Blueprints.Core.Compiler.Emit.Sanitizer.SanitizeName(_asset.Name);
            var bpId = Fdp.Toolkit.Blueprints.BlueprintIdHash.Compute(_asset.AssetId);
            return $"Hrot.AI.Behaviors.Generated.{sanitizedName}_{bpId:X8}_Bp+Params::{variableName}";
        }
        return $"{_asset.AssetId:D}::{variableName}";
    }

    /// <summary>The declaration list this source projects, in display order.</summary>
    private IEnumerable<BlackboardVariableEntry> Entries => _kind switch
    {
        VariableKind.Parameter => GetOrdered(_asset.Parameters, _asset.ParameterOrder)
            .Select(p => new BlackboardVariableEntry(p.Name, Type.GetType(p.Type.TypeId) ?? typeof(int), p.Comment ?? "")),
        VariableKind.WorkingState => GetOrdered(_asset.WorkingState, _asset.WorkingStateOrder)
            .Select(v => new BlackboardVariableEntry(v.Name, Type.GetType(v.Type.TypeId) ?? typeof(int), v.Comment ?? "")),
        _ => GetOrdered(_asset.Variables, _asset.VariableOrder)
            .Select(v => new BlackboardVariableEntry(v.Name, Type.GetType(v.Type.TypeId) ?? typeof(int), v.Comment ?? "")),
    };

    /// <summary>The <c>VariableDecl</c> list behind this source, or null for Parameters.</summary>
    private List<VariableDecl>? Decls => _kind switch
    {
        VariableKind.WorkingState => _asset.WorkingState,
        VariableKind.Variable     => _asset.Variables,
        _                         => null,
    };

    /// <summary>The order list behind this source. ⚠ Assigned back, so it is a ref-returning property.</summary>
    private List<Guid>? Order
    {
        get => _kind switch
        {
            VariableKind.Parameter    => _asset.ParameterOrder,
            VariableKind.WorkingState => _asset.WorkingStateOrder,
            _                         => _asset.VariableOrder,
        };
        set
        {
            switch (_kind)
            {
                case VariableKind.Parameter:    _asset.ParameterOrder    = value; break;
                case VariableKind.WorkingState: _asset.WorkingStateOrder = value; break;
                default:                        _asset.VariableOrder     = value; break;
            }
        }
    }

    private IEnumerable<T> GetOrdered<T>(List<T> items, List<Guid>? order)
    {
        if (order == null || order.Count == 0) return items;
        var dict = items.ToDictionary(GetId);
        var result = new List<T>();
        foreach (var id in order)
        {
            if (dict.TryGetValue(id, out var item))
            {
                result.Add(item);
                dict.Remove(id);
            }
        }
        result.AddRange(dict.Values);
        return result;

        Guid GetId(T item)
        {
            if (item is ParameterDecl p) return p.Id;
            if (item is VariableDecl v) return v.Id;
            return Guid.Empty;
        }
    }

    public IReadOnlyList<VariableViewModel> Variables
    {
        get
        {
            var list = new List<VariableViewModel>();
            foreach (var e in Entries)
            {
                list.Add(new VariableViewModel(
                    e.Name,
                    BlackboardTypeHelper.GetDisplayName(e.FieldType),
                    GetPayloadByteSize(e.FieldType),
                    e.FieldType,
                    e.Comment,
                    Array.Empty<(string, Guid, Guid)>(),
                    false // Not dynamically tracking unused in Blueprint right now
                ));
            }
            return list;
        }
    }

    public void AddVariable(BlackboardVariableEntry entry)
    {
        var typeRef = new BlueprintTypeRef { TypeId = entry.FieldType.FullName ?? entry.FieldType.Name };
        Guid id;
        if (_kind == VariableKind.Parameter)
        {
            var p = new ParameterDecl { Id = Guid.NewGuid(), Name = entry.Name, Type = typeRef, Comment = entry.Comment };
            _asset.Parameters.Add(p);
            id = p.Id;
            Order ??= _asset.Parameters.Where(x => x != p).Select(x => x.Id).ToList();
        }
        else
        {
            var list = Decls!;
            var v = new VariableDecl { Id = Guid.NewGuid(), Name = entry.Name, Type = typeRef, Comment = entry.Comment };
            list.Add(v);
            id = v.Id;
            Order ??= list.Where(x => x != v).Select(x => x.Id).ToList();
        }
        Order!.Add(id);
        _onChanged?.Invoke();
    }

    public void MoveVariable(int sourceIndex, int destIndex)
    {
        Order ??= CurrentIds().ToList();
        var order = Order!;
        if (sourceIndex < 0 || sourceIndex >= order.Count) return;
        if (destIndex   < 0 || destIndex   >= order.Count) return;

        var item = order[sourceIndex];
        order.RemoveAt(sourceIndex);
        order.Insert(destIndex, item);
        _onChanged?.Invoke();
    }

    public void RemoveVariable(string name) => RemoveVariables(new[] { name });

    /// <summary>
    /// U-5 / <c>BP-231</c> — removes the declarations <b>and their ids from the order list</b>.
    ///
    /// <para>
    /// ⛔ <b>The order list used to leak.</b> <c>AddVariable</c> and <c>MoveVariable</c> maintained it;
    /// remove and rename did not, so a deleted variable's id stayed in <c>*Order</c> forever.
    /// ✅ Benign today — <c>Stage5.GetOrdered</c> skips unknown ids and appends unlisted fields —
    /// ⚠ <b>but not benign after <c>U-9</c></b>, which turns the order lists into projections of a
    /// tagged declaration. Cheap to fix now, load-bearing later.
    /// </para>
    ///
    /// <para>
    /// 📌 Rename needs nothing: the order list holds <b>ids</b>, and a rename does not change one.
    /// ⭐ That is worth a test rather than a comment, so a future "fix" cannot add a name-keyed
    /// rewrite that would corrupt it.
    /// </para>
    /// </summary>
    public void RemoveVariables(IReadOnlyList<string> names)
    {
        var set = new HashSet<string>(names, StringComparer.Ordinal);

        var doomed = _kind == VariableKind.Parameter
            ? _asset.Parameters.Where(x => set.Contains(x.Name)).Select(x => x.Id).ToHashSet()
            : Decls!.Where(x => set.Contains(x.Name)).Select(x => x.Id).ToHashSet();
        if (doomed.Count == 0) return;

        if (_kind == VariableKind.Parameter) _asset.Parameters.RemoveAll(x => doomed.Contains(x.Id));
        else                                 Decls!.RemoveAll(x => doomed.Contains(x.Id));

        Order?.RemoveAll(doomed.Contains);
        _onChanged?.Invoke();
    }

    public void RenameVariable(string oldName, string newName)
    {
        if (_kind == VariableKind.Parameter)
        {
            var match = _asset.Parameters.FirstOrDefault(x => x.Name == oldName);
            if (match != null) match.Name = newName;
        }
        else
        {
            var match = Decls!.FirstOrDefault(x => x.Name == oldName);
            if (match != null) match.Name = newName;
        }
        // ⚠ The order list is keyed by ID, so a rename must NOT touch it. See RemoveVariables.
        _onChanged?.Invoke();
    }

    private IEnumerable<Guid> CurrentIds()
        => _kind == VariableKind.Parameter
            ? _asset.Parameters.Select(p => p.Id)
            : Decls!.Select(v => v.Id);

    /// <summary>
    /// U-5 / <c>BP-230</c> — ⭐⭐ <b>a REAL count.</b>
    ///
    /// <para>
    /// ⛔ <b>This returned a hardcoded <c>0</c></b> — trap #5, a member that reports success while
    /// doing nothing — and the panel's delete confirmation is built on it, so every variable reported
    /// *"0 references"* and deleted anyway.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>It resolves references exactly as <c>Stage5.FindVariableRef</c> does</b> — id first (with
    /// the <c>var:</c> prefix stripped), then the <b>name fallback</b>, both in
    /// <c>Variables → WorkingState → Parameters</c> priority order — and counts a node only when that
    /// resolution lands on <b>this</b> source's list and entry.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>The name fallback is why this cannot simply copy the locals source.</b>
    /// <c>BlueprintLocalVariableSchemaSource</c> counts by id ONLY, correctly, because
    /// <c>FindLocalIndex</c> has no name fallback — a node carrying a local's NAME is not a reference
    /// to it. For asset variables the compiler <b>does</b> match by name, so a count that ignored it
    /// would under-report exactly the hand-authored references <c>BP1670</c> was scoped around.
    /// </para>
    /// </summary>
    public int CountNodesReferencingVariable(string name)
    {
        int index = _kind == VariableKind.Parameter
            ? _asset.Parameters.FindIndex(x => string.Equals(x.Name, name, StringComparison.Ordinal))
            : Decls!.FindIndex(x => string.Equals(x.Name, name, StringComparison.Ordinal));
        if (index < 0) return 0;

        var self = new VariableRef(_kind, index);
        int count = 0;
        foreach (var graph in _asset.Graphs)
            foreach (var node in graph.Nodes)
            {
                var raw = node switch
                {
                    GetVariableNode gv => gv.VariableId,
                    SetVariableNode sv => sv.VariableId,
                    _                  => null,
                };
                if (raw is null) continue;
                // ⚠ A graph-local shadows an asset variable inside its own graph (Q27-C1), so a
                // reference the compiler resolves to a LOCAL is not a reference to this declaration.
                if (graph.LocalVariables.Count > 0 && ResolvesToLocal(graph, raw)) continue;
                if (Resolve(raw) == self) count++;
            }
        return count;
    }

    private static string StripPrefix(string raw)
        => raw.StartsWith("var:", StringComparison.OrdinalIgnoreCase) ? raw.Substring(4) : raw;

    private static bool ResolvesToLocal(Graph graph, string raw)
        => Guid.TryParse(StripPrefix(raw), out var g) && graph.LocalVariables.Any(v => v.Id == g);

    /// <summary>Mirrors <c>Stage5.FindVariableRef</c> — id first, then name, both in list priority order.</summary>
    private VariableRef Resolve(string raw)
    {
        var idStr = StripPrefix(raw);
        if (Guid.TryParse(idStr, out var guid))
        {
            int i = _asset.Variables.FindIndex(x => x.Id == guid);
            if (i >= 0) return new(VariableKind.Variable, i);
            i = _asset.WorkingState.FindIndex(x => x.Id == guid);
            if (i >= 0) return new(VariableKind.WorkingState, i);
            i = _asset.Parameters.FindIndex(x => x.Id == guid);
            if (i >= 0) return new(VariableKind.Parameter, i);
        }
        {
            int i = _asset.Variables.FindIndex(x => x.Name == idStr);
            if (i >= 0) return new(VariableKind.Variable, i);
            i = _asset.WorkingState.FindIndex(x => x.Name == idStr);
            if (i >= 0) return new(VariableKind.WorkingState, i);
            i = _asset.Parameters.FindIndex(x => x.Name == idStr);
            if (i >= 0) return new(VariableKind.Parameter, i);
        }
        return VariableRef.Unresolved;
    }

    // U-5 / Q-k: Role/Scope are read-only for blueprints — see SupportsRoleScopeEditing. The setters
    // are NOT overridden: the interface's defaults now throw, which is the point. A source that says
    // it cannot edit them is never asked, and one that lies is loud.

    public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements => Array.Empty<UnboundRequirementViewModel>();
    public void AddAlias(string name, BlackboardAliasBinding binding) { }
    public void RemoveAlias(string name, Guid reqAssetId, Guid reqElemId) { }
    public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;

    private static int GetPayloadByteSize(Type type)
    {
        if (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)) return 1;
        if (type == typeof(short) || type == typeof(ushort)) return 2;
        if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 4;
        if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return 8;
        if (type == typeof(System.Numerics.Vector2)) return 8;
        if (type == typeof(System.Numerics.Vector3)) return 12;
        if (type == typeof(System.Numerics.Vector4) || type == typeof(System.Numerics.Quaternion)) return 16;
        return 8; // fallback
    }
}

public sealed class BlueprintVariablesWindow : BlueprintEditorWindowBase
{
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly IRefactorService _refactorService;
    private VariablesPanelControl? _variablesControl;
    private IEditableAsset? _lastAsset;

    public BlueprintVariablesWindow(EditorSelectionStore selectionStore, DirtyTracker dirtyTracker, IRefactorService refactorService)
    {
        _selectionStore = selectionStore;
        _dirtyTracker = dirtyTracker;
        _refactorService = refactorService;
    }

    public override string Title => "Variables";

    public override void DrawUI()
    {
        if (_selectionStore.SelectedAsset == null)
        {
            ImGui.TextDisabled("No blueprint selected.");
            return;
        }

        var asset = _selectionStore.SelectedAsset;
        var adapter = new BlueprintEditableAssetAdapter(asset);

        if (_variablesControl == null || _lastAsset?.AssetId != asset.AssetId)
        {
            // No IActionSchemaExporter is in scope here -- the Blueprint Variables window is
            // constructed without one, so the choice list is primitives + [BlackboardDtoStruct]
            // types only (no action-schema DTO types). See BlackboardTypeChoiceBuilder.
            _variablesControl = new VariablesPanelControl(_refactorService, adapter, BlackboardTypeChoiceBuilder.BuildDefault());
            _lastAsset = adapter;
        }

        var paramsSchema = new BlueprintVariableSchemaSource(asset, VariableKind.Parameter, () => _dirtyTracker.MarkDirty(asset.AssetId));
        var stateSchema = new BlueprintVariableSchemaSource(asset, VariableKind.WorkingState, () => _dirtyTracker.MarkDirty(asset.AssetId));

        var paramsVars = paramsSchema.Variables;
        int paramsInline = paramsVars.Sum(v => v.ByteSize);
        var paramsSection = new VariablesPanelSection(
            "Parameters (Sync In)",
            "##bp_params",
            paramsSchema,
            paramsInline,
            100, // 100B per budget spec 1g-03
            0,
            128, // MaxHeavyBytes not used in BP, just give a number
            false,
            paramsInline > 100 ? (PackWarning)1 : (PackWarning)0,
            false // aliasing-off
        );

        var stateVars = stateSchema.Variables;
        int stateInline = stateVars.Sum(v => v.ByteSize);
        int stateBudget = asset.TierHint switch
        {
            BlackboardTierHint.Auto => 1024,
            BlackboardTierHint.Force1024 => 1024,
            BlackboardTierHint.Force4096 => 4096,
            BlackboardTierHint.Force16384 => 16384,
            _ => 1024
        };

        var stateSection = new VariablesPanelSection(
            "Working State",
            "##bp_state",
            stateSchema,
            stateInline,
            stateBudget,
            0,
            128, // arbitrary max heavy
            false,
            stateInline > stateBudget ? (PackWarning)2 : (PackWarning)0,
            false // aliasing-off
        );

        _variablesControl.DrawDual(paramsSection, stateSection);
    }
}
