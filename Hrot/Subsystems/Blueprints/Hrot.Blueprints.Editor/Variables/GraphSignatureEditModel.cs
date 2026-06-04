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
/// display context.  All rendering lives in <see cref="GraphSignatureWindow"/>.
/// </para>
/// </summary>
public sealed class GraphSignatureEditModel
{
    private readonly Graph  _graph;
    private readonly bool   _isOutputs;
    private readonly Action _onChanged;

    // ── ctor ─────────────────────────────────────────────────────────────────

    /// <param name="graph">The graph whose signature list is being edited.</param>
    /// <param name="isOutputs">
    ///   <c>true</c> → edits <see cref="Graph.Outputs"/>;
    ///   <c>false</c> → edits <see cref="Graph.Inputs"/>.
    /// </param>
    /// <param name="onChanged">
    ///   Invoked exactly once per successful mutation.  Typically wires to
    ///   <c>dirtyTracker.MarkDirty(asset.AssetId)</c>.
    /// </param>
    public GraphSignatureEditModel(Graph graph, bool isOutputs, Action onChanged)
    {
        _graph     = graph     ?? throw new ArgumentNullException(nameof(graph));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _isOutputs = isOutputs;
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
        var decl = new ParameterDecl
        {
            Id   = Guid.NewGuid(),
            Name = name ?? throw new ArgumentNullException(nameof(name)),
            Type = new BlueprintTypeRef { TypeId = typeId ?? throw new ArgumentNullException(nameof(typeId)) },
        };
        List().Add(decl);
        _onChanged();
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
        list.RemoveAt(idx);
        _onChanged();
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
        match.Name = newName ?? throw new ArgumentNullException(nameof(newName));
        _onChanged();
    }

    /// <summary>
    /// Changes the type of the first parameter matching <paramref name="name"/> to
    /// <paramref name="newTypeId"/>.  No-op (no event) when not found.
    /// </summary>
    public void RetypeParameter(string name, string newTypeId)
    {
        var match = List().FirstOrDefault(p => p.Name == name);
        if (match == null) return;
        match.Type = new BlueprintTypeRef { TypeId = newTypeId ?? throw new ArgumentNullException(nameof(newTypeId)) };
        _onChanged();
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

        var item = list[fromIndex];
        list.RemoveAt(fromIndex);
        list.Insert(toIndex, item);
        _onChanged();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private List<ParameterDecl> List()
        => _isOutputs ? _graph.Outputs : _graph.Inputs;
}
