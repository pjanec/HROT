using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Blueprints.Editor.Variables;

/// <summary>
/// BP-57 — the <see cref="IVariablesSchemaSource"/> over one graph's <see cref="Graph.LocalVariables"/>.
///
/// <para>
/// ⭐⭐ <b>Written to the SHARED interface on purpose, and that is the whole point of the file.</b> The
/// unification's <c>U-6</c> moves every variable surface onto <c>IVariablesSchemaSource</c>; a locals
/// model written to it is <b>absorbed</b> by that work, whereas a bespoke one would have to be undone
/// first. Nothing here is blueprint-private that could not have been.
/// </para>
///
/// <para>
/// ⛔ <b>It implements the interface; it does NOT change it.</b> Adding a capability member is
/// <c>U-5</c>'s <c>V2</c> — a <c>Hrot.Editor.AiShared</c> change that moves that gate and touches
/// <c>BTreeHsmSchemaSource</c> and the HSM source. ⭐ The AiShared suite staying at <b>1213</b> is the
/// assertion that this file stayed on its own side of the line.
/// </para>
///
/// <para>
/// ⛔ <b><c>UpdateVariableRole</c>/<c>UpdateVariableScope</c> are deliberately not implemented.</b>
/// <c>Q-k</c> ruled <c>Role</c>/<c>Scope</c> read-only for blueprints — changing either is a MOVE with
/// reference consequences, not a toggle. They are default-bodied members, so leaving them is the
/// contract's intended shape rather than an omission.
/// </para>
///
/// <para>
/// ⚠ <b>The graph is read through a delegate, never captured.</b> The section this feeds follows the
/// canvas (<c>BP-72</c>: a panel editing the graph you are not looking at is a defect), so the source
/// must resolve the current graph at call time, not at construction.
/// </para>
/// </summary>
public sealed class BlueprintLocalVariableSchemaSource : IVariablesSchemaSource
{
    private readonly BlueprintAsset _asset;
    private readonly Func<Graph?> _currentGraph;
    private readonly Action _onChanged;

    public BlueprintLocalVariableSchemaSource(
        BlueprintAsset asset, Func<Graph?> currentGraph, Action onChanged)
    {
        _asset        = asset        ?? throw new ArgumentNullException(nameof(asset));
        _currentGraph = currentGraph ?? throw new ArgumentNullException(nameof(currentGraph));
        _onChanged    = onChanged    ?? throw new ArgumentNullException(nameof(onChanged));
    }

    /// <summary>
    /// ⭐ <b>A <see cref="GraphKind.Macro"/> graph is read-only here, and that is <c>BP1664</c> speaking.</b>
    /// A macro is spliced into its call sites, so after expansion it is not a graph and its nodes belong
    /// to the host — there is nothing for a macro-local to be scoped to. ⚠ Read-only rather than absent:
    /// a surface that vanishes teaches nothing (the standing <c>Q26-B2</c> ruling).
    /// </summary>
    public bool IsReadOnly => _currentGraph() is not { } g || g.Kind == GraphKind.Macro;

    /// <summary>Null when no graph is open, or when the open graph cannot own locals.</summary>
    private Graph? EditableGraph
    {
        get
        {
            var g = _currentGraph();
            return g is null || g.Kind == GraphKind.Macro ? null : g;
        }
    }

    private List<VariableDecl> Locals => _currentGraph()?.LocalVariables ?? new List<VariableDecl>();

    /// <summary>
    /// ⚠ <b>Graph-qualified, unlike the asset-variable key.</b> Two graphs may each declare a
    /// <c>Scratch</c> (<c>Q27-C1</c> even lets one shadow an asset variable of that name), so a key
    /// that named only the asset and the variable would collide across graphs — and a refactor keyed
    /// on it would rename the wrong declaration.
    /// </summary>
    public string? GetRefactorKey(string variableName)
    {
        var g = _currentGraph();
        return g is null ? null : $"{_asset.AssetId:D}::{g.Id:D}::local::{variableName}";
    }

    public IReadOnlyList<VariableViewModel> Variables
    {
        get
        {
            var locals = Locals;
            var result = new List<VariableViewModel>(locals.Count);
            foreach (var v in locals)
            {
                var clrType = ResolveClrType(v.Type?.TypeId);
                result.Add(new VariableViewModel(
                    Name:       v.Name,
                    TypeName:   BlackboardTypeHelper.GetDisplayName(clrType),
                    ByteSize:   PayloadByteSize(clrType),
                    FieldType:  clrType,
                    Comment:    v.Comment,
                    AliasedBy:  Array.Empty<(string, Guid, Guid)>(),
                    // ⭐ Honestly computed, not hardcoded false. The existing asset-variable source
                    // passes `false` unconditionally; this one can afford the truth because it already
                    // has the reference walk below.
                    IsUnused:   CountNodesReferencingVariable(v.Name) == 0,
                    IsReadOnly: IsReadOnly));
            }
            return result;
        }
    }

    /// <summary>
    /// ⭐⭐ <b>A REAL count. <c>BP-230</c> is the reason this doc comment exists.</b>
    ///
    /// <para>
    /// <c>BlueprintVariableSchemaSource.CountNodesReferencingVariable</c> returns a hardcoded <c>0</c> —
    /// trap #5, a member that reports success while doing nothing — and the delete gesture in §4 is
    /// built on this number. A source that inherited that stub would report *"0 references"* for every
    /// local and delete anyway.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Counted by ID across the WHOLE asset, not by name and not only in the owning graph.</b>
    /// By id because that is how <c>Stage5.FindLocalIndex</c> resolves — id-only, no name fallback, so a
    /// node carrying the NAME is not a reference to this local at all. Across the asset because a node
    /// in another graph carrying this id is exactly the <b>dangling</b> case <c>BP1670</c> refuses, and
    /// a delete gesture that could not see it would leave the asset uncompilable while reporting itself
    /// clean.
    /// </para>
    /// </summary>
    public int CountNodesReferencingVariable(string name)
    {
        var decl = Locals.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));
        return decl is null ? 0 : CountReferencesTo(decl.Id);
    }

    /// <summary>Every <c>Get</c>/<c>SetVariable</c> node in the asset whose id resolves to <paramref name="id"/>.</summary>
    internal int CountReferencesTo(Guid id) => ReferencesTo(id).Count;

    /// <summary>
    /// The referencing nodes themselves, with their graphs — §4's delete path needs the nodes, not
    /// just the count, so it can take them along and hand them back for the undo.
    /// </summary>
    internal List<(Graph Graph, Node Node)> ReferencesTo(Guid id)
    {
        var hits = new List<(Graph, Node)>();
        foreach (var graph in _asset.Graphs)
            foreach (var node in graph.Nodes)
            {
                var raw = node switch
                {
                    GetVariableNode gv => gv.VariableId,
                    SetVariableNode sv => sv.VariableId,
                    _                  => null,
                };
                if (raw is not null && TryParseVariableId(raw, out var parsed) && parsed == id)
                    hits.Add((graph, node));
            }
        return hits;
    }

    /// <summary>Mirrors <c>Stage5.FindLocalIndex</c>'s <c>var:</c> prefix handling exactly.</summary>
    internal static bool TryParseVariableId(string raw, out Guid id)
    {
        var s = raw.StartsWith("var:", StringComparison.OrdinalIgnoreCase) ? raw.Substring(4) : raw;
        return Guid.TryParse(s, out id);
    }

    // ── mutation ────────────────────────────────────────────────────────────

    public void AddVariable(BlackboardVariableEntry entry)
    {
        var g = EditableGraph;
        if (g is null) return;

        g.LocalVariables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = entry.Name,
            Type = new BlueprintTypeRef { TypeId = entry.FieldType.FullName ?? entry.FieldType.Name },
            DefaultValueJson = entry.DefaultValueJson ?? "",
            Comment = entry.Comment,
        });
        _onChanged();
    }

    public void RemoveVariable(string name) => RemoveVariables(new[] { name });

    public void RemoveVariables(IReadOnlyList<string> names)
    {
        var g = EditableGraph;
        if (g is null) return;

        var set = new HashSet<string>(names, StringComparer.Ordinal);
        if (g.LocalVariables.RemoveAll(v => set.Contains(v.Name)) > 0)
            _onChanged();
    }

    /// <summary>
    /// ⭐ <b>Safe, and the compiler is why.</b> A local resolves by <b>id</b>
    /// (<c>Stage5.FindLocalIndex</c>, id-only by design), so a rename cannot re-target a reference —
    /// every <c>Get</c>/<c>Set</c> keeps pointing at the same declaration.
    ///
    /// <para>
    /// ⚠ <b>This is the OPPOSITE of <c>BP-225</c>'s exec pins</b>, where identity IS the name and a
    /// rename destroys one pin and creates another. Carrying that fear across would mean refusing a
    /// gesture that is provably harmless here.
    /// </para>
    /// </summary>
    public void RenameVariable(string oldName, string newName)
    {
        var g = EditableGraph;
        if (g is null) return;
        if (string.IsNullOrWhiteSpace(newName)) return;

        var trimmed = newName.Trim();
        var match = g.LocalVariables.FirstOrDefault(v => string.Equals(v.Name, oldName, StringComparison.Ordinal));
        if (match is null || string.Equals(match.Name, trimmed, StringComparison.Ordinal)) return;

        match.Name = trimmed;
        _onChanged();
    }

    /// <summary>
    /// ⚠ <b>Reordering locals is not cosmetic for a suspending graph.</b> Their slots are laid out in
    /// declaration order, so the order feeds <c>FieldLayout</c>'s offsets and therefore
    /// <c>StructureHash</c> — a reorder re-initialises the blackboard on next run. That is correct
    /// (the layout genuinely changed), and it is why there is no separate order list to keep in sync:
    /// the declaration list IS the order, so <c>BP-231</c>'s stale-id problem cannot arise here.
    /// </summary>
    public void MoveVariable(int sourceIndex, int destIndex)
    {
        var g = EditableGraph;
        if (g is null) return;

        var list = g.LocalVariables;
        if (sourceIndex < 0 || sourceIndex >= list.Count) return;
        if (destIndex   < 0 || destIndex   >= list.Count) return;
        if (sourceIndex == destIndex) return;

        var item = list[sourceIndex];
        list.RemoveAt(sourceIndex);
        list.Insert(destIndex, item);
        _onChanged();
    }

    // ── aliasing: not a blueprint concept ───────────────────────────────────

    public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements
        => Array.Empty<UnboundRequirementViewModel>();
    public void AddAlias(string name, BlackboardAliasBinding binding) { }
    public void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId) { }
    public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;

    // ── helpers ─────────────────────────────────────────────────────────────

    private static Type ResolveClrType(string? typeId)
        => (typeId is null ? null : Type.GetType(typeId)) ?? typeof(int);

    /// <summary>Mirrors <c>BlueprintVariableSchemaSource.GetPayloadByteSize</c> — same table, same fallback.</summary>
    private static int PayloadByteSize(Type type)
    {
        if (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)) return 1;
        if (type == typeof(short) || type == typeof(ushort)) return 2;
        if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 4;
        if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return 8;
        if (type == typeof(System.Numerics.Vector2)) return 8;
        if (type == typeof(System.Numerics.Vector3)) return 12;
        if (type == typeof(System.Numerics.Vector4) || type == typeof(System.Numerics.Quaternion)) return 16;
        return 8;
    }
}
