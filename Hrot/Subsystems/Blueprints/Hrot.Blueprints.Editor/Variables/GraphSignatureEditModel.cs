using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Variables;

/// <summary>
/// Headless edit model that wraps a single <see cref="Graph"/>'s
/// <see cref="Graph.Inputs"/> or <see cref="Graph.Outputs"/> list and exposes
/// typed mutations (Add / Remove / Rename / Retype / Move).
///
/// <para>
/// Every mutation invokes the injected <paramref name="onChanged"/> delegate so
/// callers (e.g. <see cref="GraphSignatureWindow"/>) can mark the owning
/// <see cref="BlueprintAsset"/> dirty via <see cref="DirtyTracker.MarkDirty"/>.
/// </para>
///
/// <para>
/// Deliberately free of ImGui so the class is fully unit-testable without a
/// display context.  All rendering lives in <see cref="ParameterRowsView"/>.
/// </para>
///
/// <para>
/// BP-89: an optional <c>record</c> seam gives callers with an undo stack (e.g.
/// <c>ReturnNodeDrawer</c>'s Outputs table) one undo entry per edit. Every mutation is made
/// undoable uniformly by whole-list snapshot — <see cref="ParameterDecl"/> is a mutable class and
/// <see cref="RenameParameter"/>/<see cref="RetypeParameter"/> mutate elements in place, so a
/// shallow list copy is not enough to capture "before". When <c>record</c> is null the model
/// behaves exactly as it did before BP-89: it mutates directly and skips the snapshot entirely.
/// </para>
/// </summary>
public sealed class GraphSignatureEditModel
{
    private readonly Graph  _graph;
    private readonly bool   _isOutputs;
    private readonly Action _onChanged;

    /// <summary>
    /// BP-89: optional undo-recorder seam. When non-null, invoked as
    /// <c>record(label, apply, undo)</c> instead of applying the mutation directly — the caller
    /// (typically routing to <c>IEditService.RecordPropertyEdit</c>) is expected to invoke
    /// <c>apply</c> as part of recording, matching that interface's contract.
    /// </summary>
    private readonly Action<string, Action, Action>? _record;

    // ── ctor ─────────────────────────────────────────────────────────────────

    /// <param name="graph">The graph whose signature list is being edited.</param>
    /// <param name="isOutputs">
    ///   <c>true</c> → edits <see cref="Graph.Outputs"/>;
    ///   <c>false</c> → edits <see cref="Graph.Inputs"/>.
    /// </param>
    /// <param name="onChanged">
    ///   Invoked exactly once per successful mutation (once by <c>apply</c>, once by <c>undo</c>
    ///   when a recorder is wired up).  Typically wires to <c>dirtyTracker.MarkDirty(asset.AssetId)</c>.
    /// </param>
    /// <param name="record">
    ///   BP-89: optional undo recorder. Null (the default) preserves this model's pre-BP-89
    ///   behaviour exactly — every existing caller/test keeps working unchanged.
    /// </param>
    public GraphSignatureEditModel(
        Graph graph, bool isOutputs, Action onChanged,
        Action<string, Action, Action>? record = null)
    {
        _graph     = graph     ?? throw new ArgumentNullException(nameof(graph));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _isOutputs = isOutputs;
        _record    = record;
    }

    // ── Public read-only view ─────────────────────────────────────────────────

    /// <summary>Live read-only view of the list being edited (Inputs or Outputs).</summary>
    public IReadOnlyList<ParameterDecl> Parameters
        => _isOutputs ? _graph.Outputs : _graph.Inputs;

    // ── Mutations ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a new <see cref="ParameterDecl"/> with a fresh <see cref="Guid"/> id.
    /// </summary>
    /// <param name="name">Parameter name (must be non-null).</param>
    /// <param name="typeId">
    ///   CLR type id string (e.g. <c>"System.Single"</c> or <c>"float"</c>).
    ///   Stored verbatim in <see cref="BlueprintTypeRef.TypeId"/>.
    /// </param>
    public void AddParameter(string name, string typeId)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        if (typeId == null) throw new ArgumentNullException(nameof(typeId));

        var decl = new ParameterDecl
        {
            Id   = Guid.NewGuid(),
            Name = name,
            Type = new BlueprintTypeRef { TypeId = typeId },
        };

        if (_record == null)
        {
            List().Add(decl);
            _onChanged();
            return;
        }

        var before = Snapshot(List());
        void Apply() { List().Add(decl); _onChanged(); }
        void Undo()  { RestoreInto(List(), before); _onChanged(); }
        _record(Label("Add", name), Apply, Undo);
    }

    /// <summary>
    /// Removes the first <see cref="ParameterDecl"/> whose
    /// <see cref="ParameterDecl.Name"/> matches <paramref name="name"/> (ordinal).
    /// No-op (no event fired) when the name is not found.
    /// </summary>
    public void RemoveParameter(string name)
    {
        var list = List();
        var idx  = list.FindIndex(p => p.Name == name);
        if (idx < 0) return;

        if (_record == null)
        {
            list.RemoveAt(idx);
            _onChanged();
            return;
        }

        var before = Snapshot(list);
        void Apply()
        {
            var l = List();
            var i = l.FindIndex(p => p.Name == name);
            if (i >= 0) l.RemoveAt(i);
            _onChanged();
        }
        void Undo() { RestoreInto(List(), before); _onChanged(); }
        _record(Label("Remove", name), Apply, Undo);
    }

    /// <summary>
    /// Renames the first parameter matching <paramref name="oldName"/> to
    /// <paramref name="newName"/>.  No-op (no event) when <paramref name="oldName"/>
    /// is not found.
    /// </summary>
    public void RenameParameter(string oldName, string newName)
    {
        var match = List().FirstOrDefault(p => p.Name == oldName);
        if (match == null) return;
        if (newName == null) throw new ArgumentNullException(nameof(newName));

        if (_record == null)
        {
            match.Name = newName;
            _onChanged();
            return;
        }

        var before = Snapshot(List());
        void Apply()
        {
            var m = List().FirstOrDefault(p => p.Name == oldName);
            if (m != null) m.Name = newName;
            _onChanged();
        }
        void Undo() { RestoreInto(List(), before); _onChanged(); }
        _record(LabelRename(oldName, newName), Apply, Undo);
    }

    /// <summary>
    /// Changes the type of the first parameter matching <paramref name="name"/> to
    /// <paramref name="newTypeId"/>.  No-op (no event) when not found.
    /// </summary>
    public void RetypeParameter(string name, string newTypeId)
    {
        var match = List().FirstOrDefault(p => p.Name == name);
        if (match == null) return;
        if (newTypeId == null) throw new ArgumentNullException(nameof(newTypeId));

        if (_record == null)
        {
            match.Type = new BlueprintTypeRef { TypeId = newTypeId };
            _onChanged();
            return;
        }

        var before = Snapshot(List());
        void Apply()
        {
            var m = List().FirstOrDefault(p => p.Name == name);
            if (m != null) m.Type = new BlueprintTypeRef { TypeId = newTypeId };
            _onChanged();
        }
        void Undo() { RestoreInto(List(), before); _onChanged(); }
        _record(Label("Retype", name), Apply, Undo);
    }

    /// <summary>
    /// Moves the parameter at <paramref name="fromIndex"/> to <paramref name="toIndex"/>.
    /// Silently ignored when either index is out of range.
    /// </summary>
    public void MoveParameter(int fromIndex, int toIndex)
    {
        var list = List();
        if (fromIndex < 0 || fromIndex >= list.Count) return;
        if (toIndex   < 0 || toIndex   >= list.Count) return;
        if (fromIndex == toIndex) return;

        if (_record == null)
        {
            var item = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, item);
            _onChanged();
            return;
        }

        var before = Snapshot(list);
        void Apply()
        {
            var l = List();
            if (fromIndex < 0 || fromIndex >= l.Count) return;
            var item = l[fromIndex];
            l.RemoveAt(fromIndex);
            var insertAt = Math.Min(toIndex, l.Count);
            l.Insert(insertAt, item);
            _onChanged();
        }
        void Undo() { RestoreInto(List(), before); _onChanged(); }
        _record($"Reorder {NounPlural}", Apply, Undo);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private List<ParameterDecl> List()
        => _isOutputs ? _graph.Outputs : _graph.Inputs;

    private string Noun       => _isOutputs ? "output"  : "input";
    private string NounPlural => _isOutputs ? "outputs" : "inputs";

    private string Label(string verb, string name) => $"{verb} {Noun} '{name}'";

    private string LabelRename(string oldName, string newName)
        => $"Rename {Noun} '{oldName}' → '{newName}'";

    /// <summary>
    /// BP-89: deep-copies <paramref name="list"/> for a snapshot-based undo. <see cref="ParameterDecl"/>
    /// is a mutable class and <see cref="RenameParameter"/>/<see cref="RetypeParameter"/> mutate
    /// elements in place, so a shallow <c>ToList()</c> would still point at the live (already-mutated)
    /// objects — a deep copy per element (including a fresh <see cref="BlueprintTypeRef"/>) is required
    /// for the snapshot to actually capture "before".
    /// </summary>
    private static List<ParameterDecl> Snapshot(List<ParameterDecl> list)
        => list.Select(p => new ParameterDecl
        {
            Id               = p.Id,
            Name             = p.Name,
            Type             = new BlueprintTypeRef
            {
                TypeId        = p.Type?.TypeId ?? "",
                IsArray       = p.Type?.IsArray ?? false,
                GenericArgs   = new List<BlueprintTypeRef>(p.Type?.GenericArgs ?? new()),
                Capacity      = p.Type?.Capacity ?? 0,
                InitialLength = p.Type?.InitialLength ?? 0,
            },
            DefaultValueJson = p.DefaultValueJson,
            Tooltip          = p.Tooltip,
            Comment          = p.Comment,
        }).ToList();

    /// <summary>
    /// Restores <paramref name="target"/> to the contents of <paramref name="snapshot"/> IN PLACE
    /// (clear + refill the same <see cref="List{ParameterDecl}"/> instance) — other code (e.g. a
    /// live <see cref="Parameters"/> view, or the owning <see cref="Graph"/>) may hold the list
    /// reference, so reassigning <c>Graph.Outputs</c>/<c>Graph.Inputs</c> instead would leave those
    /// references stale.
    ///
    /// <para>
    /// The snapshot is re-copied on the way back out, never handed over directly: an undo entry can
    /// be replayed (undo → redo → undo), and publishing the snapshot's own <see cref="ParameterDecl"/>
    /// instances into the live list would let a later in-place edit — <see cref="RenameParameter"/>
    /// and <see cref="RetypeParameter"/> both mutate elements in place — rewrite this entry's
    /// captured "before" state, so undoing it a second time would restore the *newer* value.
    /// </para>
    /// </summary>
    private static void RestoreInto(List<ParameterDecl> target, List<ParameterDecl> snapshot)
    {
        target.Clear();
        target.AddRange(Snapshot(snapshot));
    }
}
