using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;   // 99a
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
    private readonly Action<string, Func<bool>> _record;
    private readonly Action<string>? _refuse;

    /// <param name="record">
    /// ⭐⭐ <b>The undo seam.</b> Runs one gesture's mutation inside one undo entry —
    /// <c>record(label, mutate)</c>, where <c>mutate</c> returns false when it changed nothing so no
    /// empty entry is pushed. ⚠ <b>One entry per GESTURE, not per keystroke</b> (<c>BP-204</c>).
    /// <para>
    /// Defaults to running the mutation bare so a headless caller with no canvas still works — but a
    /// host that HAS a view must supply one, or every locals gesture is unundoable.
    /// <c>BlueprintDocumentFactory.LocalVariableUndoRecorder</c> is that host implementation.
    /// </para>
    /// </param>
    /// <param name="refuse">
    /// How a refusal reaches the designer. ⛔ <b>Never silence</b> — the standing <c>Q26-B2</c> ruling
    /// is that a gesture which cannot proceed must SAY so; <c>BP-76</c>/<c>BP-77</c> were both filed
    /// because something was greyed out with no explanation.
    /// </param>
    public BlueprintLocalVariableSchemaSource(
        BlueprintAsset asset,
        Func<Graph?> currentGraph,
        Action onChanged,
        Action<string, Func<bool>>? record = null,
        Action<string>? refuse = null)
    {
        _asset        = asset        ?? throw new ArgumentNullException(nameof(asset));
        _currentGraph = currentGraph ?? throw new ArgumentNullException(nameof(currentGraph));
        _onChanged    = onChanged    ?? throw new ArgumentNullException(nameof(onChanged));
        _record       = record ?? ((_, mutate) => mutate());
        _refuse       = refuse;
    }

    /// <summary>
    /// ⭐ <b>A <see cref="GraphKind.Macro"/> graph is read-only here, and that is <c>BP1664</c> speaking.</b>
    /// A macro is spliced into its call sites, so after expansion it is not a graph and its nodes belong
    /// to the host — there is nothing for a macro-local to be scoped to. ⚠ Read-only rather than absent:
    /// a surface that vanishes teaches nothing (the standing <c>Q26-B2</c> ruling).
    /// </summary>
    public bool IsReadOnly => _currentGraph() is not { } g || g.Kind == GraphKind.Macro;

    /// <summary>
    /// U-5 / <c>Q-k</c> — a blueprint local has no <c>Role</c>/<c>Scope</c> to edit. ⭐ Answering
    /// <c>false</c> is what stops the panel drawing a live combo whose result would be discarded; the
    /// interface's setters now throw rather than silently accepting the call (<c>BP-230</c>).
    /// </summary>
    public bool SupportsRoleScopeEditing => false;

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
                    IsReadOnly: IsReadOnly,
                    // ⭐ Row 58 — the INITIAL arm's source for a graph local.
                    DefaultValueJson: v.DefaultValueJson));
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
        if (g is null)
        {
            _refuse?.Invoke(
                "A macro graph cannot declare a local variable. A macro is spliced into every call "
                + "site, so after expansion it is not a graph and its nodes belong to the host — "
                + "there is nothing for a macro-local to be scoped to. Declare it on the graph that "
                + "CALLS this macro, or use an asset variable.");
            return;
        }

        _record($"Add Local Variable '{entry.Name}'", () =>
        {
            g.LocalVariables.Add(new VariableDecl
            {
                Id   = Guid.NewGuid(),
                Name = entry.Name,
                Type = new BlueprintTypeRef { TypeId = entry.FieldType.FullName ?? entry.FieldType.Name },
                DefaultValueJson = entry.DefaultValueJson ?? "",
                Comment = entry.Comment,
            });
            _onChanged();
            return true;
        });
    }

    public void RemoveVariable(string name) => RemoveVariables(new[] { name });

    /// <summary>
    /// ⭐⭐ <b>Refuses while referenced, naming the count and where.</b>
    ///
    /// <para>
    /// ⛔ <b>What it replaces was a naive <c>RemoveAll</c></b> that dropped the declaration and left
    /// every <c>Get</c>/<c>Set</c> pointing at nothing. Not merely untidy: <c>BP1670</c> then refuses
    /// the asset at Stage 2, so a one-click gesture reliably made the blueprint uncompilable.
    /// </para>
    ///
    /// <para>
    /// ⚖️ <b>Why refusing beats taking the nodes along.</b> A delete that silently removes the
    /// designer's wired-up nodes is the bigger surprise — the repo already ruled that way for asset
    /// variables (<c>BlueprintDocumentFactory.DeleteItem</c>: <i>"silently deleting a designer's
    /// wired-up nodes because a declaration went away is not [recoverable]"</i>).
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>But it diverges from that policy in one direction, deliberately.</b> Asset variables are
    /// deleted and their nodes left dangling for the compiler to name; a LOCAL's references can sit in
    /// <b>another graph</b> — the cross-graph case <c>CountNodesReferencingVariable</c> counts — which
    /// the designer cannot see from the current canvas. ⭐ Refusing with a count tells them something
    /// they could not otherwise learn; leaving it to <c>BP1670</c> tells them only after a build.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>It also keeps the undo honest for free</b> (<c>BP-225</c>): because no nodes are ever
    /// removed, the undo entry has only declarations to restore — there is no way for it to restore a
    /// declaration and forget its references, which is exactly the trap <c>BP-225</c> recorded.
    /// </para>
    /// </summary>
    public void RemoveVariables(IReadOnlyList<string> names)
    {
        var g = EditableGraph;
        if (g is null) return;

        var doomed = g.LocalVariables
            .Where(v => names.Contains(v.Name, StringComparer.Ordinal))
            .ToList();
        if (doomed.Count == 0) return;

        // ⭐ Gather refusals BEFORE mutating anything — a partial delete followed by a refusal would
        // be the worst of both.
        var blocked = doomed
            .Select(v => (Decl: v, Refs: ReferencesTo(v.Id)))
            .Where(x => x.Refs.Count > 0)
            .ToList();

        if (blocked.Count > 0)
        {
            foreach (var (decl, refs) in blocked)
            {
                var graphs = refs.Select(r => r.Graph.Name).Distinct(StringComparer.Ordinal).ToList();
                _refuse?.Invoke(
                    $"'{decl.Name}' is still used by {refs.Count} node(s) in "
                    + $"{string.Join(", ", graphs.Select(n => $"'{n}'"))}. "
                    + "Delete or retarget them first — removing the declaration on its own would "
                    + "leave those nodes pointing at nothing, and the blueprint would stop compiling.");
            }
            return;
        }

        _record(
            doomed.Count == 1
                ? $"Delete Local Variable '{doomed[0].Name}'"
                : $"Delete {doomed.Count} Local Variables",
            () =>
            {
                foreach (var v in doomed) g.LocalVariables.Remove(v);
                _onChanged();
                return true;
            });
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

        _record($"Rename Local Variable '{oldName}' to '{trimmed}'", () =>
        {
            match.Name = trimmed;
            _onChanged();
            return true;
        });
    }

    /// <summary>
    /// ⭐⭐ <b>Batch 98 (<c>98a</c>) — a local's initial value, through the SAME undo recorder every
    /// other local mutation uses.</b>
    ///
    /// <para>⭐ <c>_record</c> is not decoration here: 📌 <c>B41 §4</c> made local rename/delete
    /// undoable through it, and an initial-value edit that skipped it would be the one local mutation
    /// <b>Ctrl+Z could not take back</b> — a worse outcome than refusing.</para>
    ///
    /// <para>⚠ <b>Refuses on a read-only graph</b> *(<c>BP1664</c> — a macro's locals belong to the
    /// host after splicing)*, via <see cref="EditableGraph"/>, exactly as the rename does. ⛔ Not a
    /// second rule.</para>
    /// </summary>
    public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)
    {
        var g = EditableGraph;
        if (g is null) return;

        var match = g.LocalVariables.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));
        if (match is null) return;

        _record($"Set Local Variable '{name}' default", () =>
        {
            match.DefaultValueJson = defaultValueJson;
            _onChanged();
            return true;
        });
    }

    /// <summary>
    /// ⭐⭐ <b>Batch 99 (<c>99a</c>) — a local's properties, through the same undo recorder.</b>
    /// ⭐ <c>_record</c> is not decoration: 📌 <c>B41 §4</c> made local rename/delete undoable through
    /// it, and a properties edit that skipped it would be the one local mutation Ctrl+Z could not take
    /// back. ⚠ Refuses on a read-only graph *(<c>BP1664</c>)* via <see cref="EditableGraph"/>.
    /// </summary>
    public void UpdateVariableProperties(string name, VariablePropertyValues values)
    {
        if (values is null) return;

        var g = EditableGraph;
        if (g is null) return;

        var match = g.LocalVariables.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));
        if (match is null) return;

        _record($"Edit Local Variable '{name}' properties", () =>
        {
            if (values.DefaultValueJson is not null) match.DefaultValueJson = values.DefaultValueJson;
            if (values.Tooltip          is not null) match.Tooltip          = values.Tooltip;
            if (values.Comment          is not null) match.Comment          = values.Comment;
            if (values.Category         is not null) match.Category         = values.Category;
            if (values.IsEditable       is { } e)    match.IsEditable       = e;
            if (values.IsExposedOnSpawn is { } x)    match.IsExposedOnSpawn = x;
            _onChanged();
            return true;
        });
    }

    /// <summary>⭐⭐ <b>Batch 99 (<c>99a</c>)</b> — a local IS a <c>VariableDecl</c>, so it carries the
    /// full set. ⚠ Read from the CURRENT graph, never a captured one.</summary>
    public DeclarationPropertySnapshot? ReadVariableProperties(string name)
    {
        var match = Locals.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));
        if (match is null) return null;

        return new DeclarationPropertySnapshot(
            VariableDeclarationKind.BlueprintVariable,
            new VariablePropertyValues(
                DefaultValueJson: match.DefaultValueJson ?? "",
                Tooltip:          match.Tooltip ?? "",
                Comment:          match.Comment ?? "",
                Category:         match.Category ?? "",
                IsEditable:       match.IsEditable,
                IsExposedOnSpawn: match.IsExposedOnSpawn),
            match.Type?.TypeId ?? "");
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

        _record($"Reorder Local Variable '{list[sourceIndex].Name}'", () =>
        {
            var item = list[sourceIndex];
            list.RemoveAt(sourceIndex);
            list.Insert(destIndex, item);
            _onChanged();
            return true;
        });
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
