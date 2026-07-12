using System;
using System.Collections.Generic;
using Hrot.AiEditor.Persistence;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Implemented by assets that carry an editor-managed blackboard variable list.
/// Allows <see cref="Hrot.Editor.AiShared.Windows.BlackboardAuthoringWindow"/> to access
/// variable data without a circular project reference to subsystem editor assemblies.
/// </summary>
public interface IBlackboardManagedAsset
{
    /// <summary>True when this asset's blackboard companion file is editor-owned.</summary>
    bool IsBlackboardEditorManaged { get; }

    /// <summary>Enables or disables editor-managed blackboard mode and marks the asset dirty.</summary>
    void SetBlackboardEditorManaged(bool managed);

    /// <summary>All editor-managed variables in canonical declaration order.</summary>
    IReadOnlyList<BlackboardVariableEntry> BlackboardVariables { get; }

    /// <summary>Appends a new variable at the end of the canonical order. Fires Changed.</summary>
    void AddVariable(BlackboardVariableEntry entry);

    /// <summary>Removes the variable with the given name. No-op if not found. Fires Changed.</summary>
    void RemoveVariable(string name);

    /// <summary>Replaces the comment on an existing variable. No-op if not found. Fires Changed.</summary>
    void UpdateVariableComment(string name, string? comment);

    /// <summary>
    /// Sets (or clears) the authored default-value JSON for an existing variable (B-3).
    /// No-op if the variable is not found. Fires Changed (marks asset dirty).
    /// Passing <c>null</c> clears any previously authored default (byte-stable: null is omitted
    /// from the persisted JSON).
    /// </summary>
    void UpdateVariableDefaultValueJson(string name, string? defaultValueJson);

    /// <summary>
    /// Sets the authoring role on an existing variable (S3-1). No-op if not found. Fires Changed.
    /// </summary>
    void UpdateVariableRole(string name, BlackboardVariableRole role) { }

    /// <summary>
    /// Sets the working-state scope on an existing variable (S3-1). No-op if not found. Fires Changed.
    /// </summary>
    void UpdateVariableScope(string name, WorkingStateScope scope) { }

    /// <summary>Moves a variable from sourceIndex to destIndex in canonical order. Fires Changed.</summary>
    void MoveVariable(int sourceIndex, int destIndex);

    /// <summary>Renames a variable. No-op if not found. Fires Changed.</summary>
    void RenameVariable(string oldName, string newName);

    /// <summary>Returns the number of nodes/bindings in this asset that reference variableName.</summary>
    int CountNodesReferencingVariable(string name);

    /// <summary>Returns all alias bindings recorded against the named variable. Empty list if none.</summary>
    IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName);

    /// <summary>Binds an unbound sub-tree requirement to a defined variable. Fires Changed.</summary>
    void AddAlias(string variableName, BlackboardAliasBinding binding);

    /// <summary>
    /// Removes an alias binding from the named variable. No-op if not found.
    /// Returns the removed requirement back to the "unbound" pool implicitly (the aggregation result
    /// re-surfaces it on the next BuildViewModel call). Fires Changed.
    /// </summary>
    void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId);

    /// <summary>
    /// Removes all variables whose names appear in <paramref name="names"/>.
    /// No-op for names not found. Fires Changed exactly once after all removals (or not at all
    /// when the list is empty or no names match).
    /// </summary>
    void RemoveVariables(IReadOnlyList<string> names);

    // ---- Load-state (TASK-1f-07) -------------------------------------------

    /// <summary>
    /// Describes the load-time health of the companion blackboard file.
    /// Defaults to <see cref="BlackboardLoadState.Clean"/> for assets that do not
    /// track load diagnostics.
    /// </summary>
    BlackboardLoadState LoadState => BlackboardLoadState.Clean;

    /// <summary>
    /// Human-readable diagnostic message associated with a non-Clean
    /// <see cref="LoadState"/>, or null when the state is Clean.
    /// </summary>
    string? LoadDiagnosticMessage => null;

    // ---- Stale-alias pruning (DEBT-06) -------------------------------------

    /// <summary>
    /// Removes alias bindings whose <c>RequiringAssetId</c> is not present in
    /// <paramref name="knownAssetIds"/>.  Fires Changed once if any bindings were removed.
    /// Default implementation is a no-op for assets that do not maintain alias bindings.
    /// </summary>
    void PruneStaleAliasBindings(IReadOnlyCollection<Guid> knownAssetIds) { }

    /// <summary>
    /// Returns the set of all distinct <c>RequiringAssetId</c> GUIDs currently referenced
    /// across all alias binding lists in this asset.
    /// Default implementation returns an empty collection for assets that do not maintain
    /// alias bindings.
    /// </summary>
    IReadOnlyCollection<Guid> GetKnownSubAssetIds() => Array.Empty<Guid>();

    // ---- Suppressions (TASK-BB-1f-05) ---------------------

    /// <summary>
    /// Returns true if the variable conflict is suppressed for the given writer pair.
    /// </summary>
    bool IsConflictSuppressed(string variableName, string writerPairKey) => false;
    
    void SetConflictSuppressed(string variableName, string writerPairKey, bool suppressed) { }

    bool IsUnusedWarningSuppressed(string variableName) => false;
    void SetUnusedWarningSuppressed(string variableName, bool suppressed) { }

    /// <summary>
    /// Returns a map of StateId -> RegionIndex for all states that are direct children
    /// of a parallel composite state, or null when this asset has no parallel regions.
    /// Used by <see cref="BlackboardAliasDropValidator"/> to check cross-region conflicts
    /// without creating a circular project reference from the shared window to subsystem editors.
    /// Default: null (no parallel regions -- safe for BTree assets).
    /// </summary>
    IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;

    // ---- Identity helpers (default implementations) -----------------------

    /// <summary>
    /// The name of this asset. Default implementation returns the implementing type name.
    /// Overridden by concrete assets that have a proper Name property.
    /// </summary>
    string Name => GetType().Name;

    /// <summary>
    /// The fully-qualified type name of the blackboard companion struct.
    /// Default implementation derives a name from the implementing type.
    /// Overridden by concrete assets that have a proper BlackboardTypeName property.
    /// </summary>
    string BlackboardTypeName => GetType().Name + "_Blackboard";
}
