using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

// Editor-side model of an HSM asset.
// Implements IEditableAsset so the shared asset catalog can hold it.
// Mutable; tracks layout, editor-specific identity, and a reference to the kernel blob.
public sealed class HsmAsset : IEditableAsset, IBlackboardManagedAsset, IStitchableAsset, IStatefulScopeAsset, ISubtreeHostingAsset
{
    // Identity
    public Guid AssetId { get; }
    public string Name { get; set; }
    public AssetKind Kind => AssetKind.Hsm;
    public string SourceFilePath { get; }
    public bool IsDirty { get; internal set; }
    public bool IsEditorOwned { get; }
    public string TargetNamespace { get; }

    /// <summary>
    /// Name of the blackboard struct type generated for this HSM.
    /// Defaults to <c>&lt;SanitizedName&gt;_Blackboard</c> on construction.
    /// Can be overridden by the editor when the user renames the asset.
    /// </summary>
    public string BlackboardTypeName { get; set; }

    /// <summary>
    /// ⭐ <c>CE-503</c> — the bound blueprint RESOLVER asset (<c>Q76</c> §12.20/§12.27g), or <c>null</c>: the record a BTree binds
    /// too. ⚠ Must round-trip through <c>HsmAssetMapper</c>.
    /// </summary>
    public BehaviorResolverRef? Resolver { get; set; }

    // Kernel-side data (mutable via UpdateBlob for PU-302 stitch; read-only otherwise)
    private HsmDefinitionBlob _blob;
    private MachineMetadata _metadata;

    public HsmDefinitionBlob Blob => _blob;
    public MachineMetadata Metadata => _metadata;

    // Editor-side state hierarchy
    // RootState is the synthetic root (never rendered; parent of top-level states)
    public StateNode RootState { get; }

    public IReadOnlyList<StateNode> AllStates { get; }
    public IReadOnlyList<TransitionNode> AllTransitions { get; }
    public IReadOnlyList<GlobalTransitionNode> AllGlobalTransitions { get; }
    public IReadOnlyList<RegionNode> AllRegions { get; }
    public IReadOnlyList<EventDefinition> AllEvents { get; }

    // Canvas layout
    public Vector2 CanvasPanOffset { get; set; }
    public float CanvasZoomLevel { get; set; } = 1.0f;

    // ---- IBlackboardManagedAsset ----
    private readonly List<BlackboardVariableEntry> _blackboardVariables = new();
    private readonly Dictionary<string, List<BlackboardAliasBinding>> _aliases = new();
    // Variables explicitly permitted for concurrent writes from parallel regions (TASK-BB-1f-02).
    // Session-only: persistence to the layout method is deferred to TASK-BB-1f-05.
    private readonly HashSet<(string VariableName, string WriterPairKey)> _conflictSuppressions = new();
    private readonly HashSet<string> _unusedSuppressions = new();
    public bool IsBlackboardEditorManaged { get; set; }
    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _blackboardVariables;

    /// <summary>Enables or disables editor-managed blackboard mode and marks the asset dirty.</summary>
    public void SetBlackboardEditorManaged(bool managed)
    {
        IsBlackboardEditorManaged = managed;
        MarkDirty();
    }

    /// <summary>Load-time health of the companion blackboard file. Defaults to Clean.</summary>
    public BlackboardLoadState LoadState { get; private set; }

    /// <summary>Diagnostic message when LoadState is non-Clean; null otherwise.</summary>
    public string? LoadDiagnosticMessage { get; private set; }

    /// <summary>Sets the load diagnostic. Called by the projector after parsing the companion file.</summary>
    internal void SetLoadDiagnostic(BlackboardLoadState state, string? message)
    {
        LoadState = state;
        LoadDiagnosticMessage = message;
    }

    /// <summary>
    /// Replaces the current variable list and fires Changed.
    /// Call this when the editor commits an updated set of variables.
    /// </summary>
    /// <summary>
    /// ⭐⭐⭐ <b><c>E4</c> — the HSM equivalent of <c>BehaviorTreeAsset.HasAnyStatefulNode()</c>,</b>
    /// which <c>DEBT-AIB-028</c>'s activation recipe asks for by name.
    ///
    /// <para>
    /// ⚠ <b>An HSM has no per-node delegate SHAPE to inspect</b> — there is no
    /// <c>Stateful</c> on a state. ⇒ the honest equivalent is the one Batch 67's
    /// <c>E1</c> already established: an HSM asset maintains per-instance working state exactly when it
    /// declares a <c>Role = State</c> variable scoped <c>Behavior</c> or <c>Entity</c>, because that is
    /// precisely the set <c>HsmBridgeEmitCore</c> emits <c>StatefulSlotInfo</c> entries for.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Same predicate, one definition.</b> ⛔ Inventing a different notion of "stateful" here
    /// would let the validator and the emitter disagree about which assets own a partition slot —
    /// which is the disagreement the whole slot-key discipline exists to prevent. ⚠ <c>Node</c> scope
    /// is excluded for the same reason it is skipped at emission: it keys off a node id the HSM has
    /// nothing to supply.
    /// </para>
    /// </summary>
    public bool HasAnyStatefulNode() => GetSharedScopeKeys().Count > 0;

    /// <summary>
    /// ⭐⭐⭐ <b><c>E4</c> completion (Batch 69) — the SHARED (<c>Behavior</c>/<c>Entity</c>) scope keys
    /// this asset's stateful variables resolve to,</b> which is what rule 8b compares across parallel
    /// regions.
    ///
    /// <para>
    /// ⭐⭐ <b>ONE definition of "the shared state set", and three callers now share it:</b> this
    /// method, <see cref="HasAnyStatefulNode"/> above (which is now literally "is that set non-empty"),
    /// and <c>HsmBridgeEmitCore</c>'s slot emission, which filters identically. ⛔ Three copies of a
    /// filter that decides which variables own a partition slot is exactly the drift the slot-key
    /// discipline exists to prevent.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>The key is <c>BTreeBridgeEmitCore.ComputeStatefulSlotKey</c>, CALLED — not
    /// reimplemented.</b> A second key algorithm would make the validator's idea of "these two regions
    /// touch the same shared slot" disagree with the allocator's, which is the one thing rule 8b must
    /// not get wrong.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<int> GetSharedScopeKeys()
    {
        HashSet<int>? keys = null;
        foreach (var v in _blackboardVariables)
        {
            if (v.Role != Hrot.AiEditor.Persistence.BlackboardVariableRole.State) continue;
            if (v.Scope != Hrot.AiEditor.Persistence.WorkingStateScope.Behavior) continue;

            (keys ??= new HashSet<int>()).Add(
                Hrot.AiEditor.Persistence.Emit.BTreeBridgeEmitCore.ComputeStatefulSlotKey(
                    AssetId, v.Scope, System.Guid.Empty, v.Name));
        }
        return (IReadOnlyCollection<int>?)keys ?? System.Array.Empty<int>();
    }

    /// <summary>
    /// ⭐⭐ <b><c>E5</c> item 7 — the forward asset edge: every sub-tree asset any STATE hosts.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.16.
    ///
    /// <para>⭐ Walks <see cref="AllStates"/>, which is the FLATTENED state list — ⛔ deliberately not
    /// the parent/child recursion <c>HsmValidator.SubtreeHostsUnder</c> does, because that one exists
    /// to answer a per-COMPOSITE question (which region hosts what) and this one is per-ASSET.</para>
    ///
    /// <para>⚠ <b>Returns the SET, so a child hosted from two states appears once</b> — that is a
    /// legitimate diamond, and the cycle walk cares only whether the edge exists.</para>
    /// </summary>
    public IReadOnlyCollection<System.Guid> GetHostedSubtreeAssetIds()
    {
        HashSet<System.Guid>? ids = null;
        foreach (var s in AllStates)
        {
            if (s.SubtreeAssetId == System.Guid.Empty) continue;
            (ids ??= new HashSet<System.Guid>()).Add(s.SubtreeAssetId);
        }
        return (IReadOnlyCollection<System.Guid>?)ids ?? System.Array.Empty<System.Guid>();
    }

    public void SetBlackboardVariables(IEnumerable<BlackboardVariableEntry> vars)
    {
        _blackboardVariables.Clear();
        _blackboardVariables.AddRange(vars);
        MarkDirty();
    }

    /// <summary>Appends a new variable at the end of the canonical order. Fires Changed.</summary>
    public void AddVariable(BlackboardVariableEntry entry)
    {
        _blackboardVariables.Add(entry);
        MarkDirty();
    }

    /// <summary>Removes the variable with the given name. No-op if not found. Fires Changed.</summary>
    public void RemoveVariable(string name)
    {
        int idx = _blackboardVariables.FindIndex(v => v.Name == name);
        if (idx < 0) return;
        _blackboardVariables.RemoveAt(idx);
        _aliases.Remove(name);
        MarkDirty();
    }

    /// <summary>
    /// Removes all variables whose names appear in <paramref name="names"/>.
    /// Fires Changed exactly once if any variables were removed; no-op (no Changed) when none match.
    /// </summary>
    public void RemoveVariables(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return;
        bool removed = false;
        foreach (var name in names)
        {
            int idx = _blackboardVariables.FindIndex(v => v.Name == name);
            if (idx < 0) continue;
            _blackboardVariables.RemoveAt(idx);
            _aliases.Remove(name);
            removed = true;
        }
        if (removed) MarkDirty();
    }

    /// <summary>Replaces the comment on an existing variable. No-op if not found. Fires Changed.</summary>
    public void UpdateVariableComment(string name, string? comment)
    {
        int idx = _blackboardVariables.FindIndex(v => v.Name == name);
        if (idx < 0) return;
        _blackboardVariables[idx] = _blackboardVariables[idx] with { Comment = comment };
        MarkDirty();
    }

    /// <summary>
    /// Sets (or clears) the authored default-value JSON for an existing variable (B-3).
    /// No-op if the variable is not found. Fires Changed (marks asset dirty).
    /// Passing <c>null</c> clears any previously authored default.
    /// </summary>
    public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)
    {
        int idx = _blackboardVariables.FindIndex(v => v.Name == name);
        if (idx < 0) return;
        _blackboardVariables[idx] = _blackboardVariables[idx] with { DefaultValueJson = defaultValueJson };
        MarkDirty();
    }

    /// <summary>Sets the authoring role on an existing variable (S3-1). No-op if not found. Fires Changed.</summary>
    public void UpdateVariableRole(string name, Hrot.AiEditor.Persistence.BlackboardVariableRole role)
    {
        int idx = _blackboardVariables.FindIndex(v => v.Name == name);
        if (idx < 0) return;

        // ⭐⭐⭐ CE-435 — A `State` VARIABLE IS ALWAYS `Behavior`-SCOPED, AND THE MODEL ENFORCES IT.
        //
        // 🔴 Before this, flipping Role to State left Scope at its default `Node`, and a STANDALONE
        //    Node-scoped State variable is SILENTLY SKIPPED by both bridge emitters — no slot, no
        //    diagnostic (CE-423). ⇒ the defect was reachable through the Variables panel in two
        //    clicks, and nothing told the author their variable had no storage.
        // 📐 Measured 2026-09-29 across every .btree.json/.hsm.json: of the authored State
        //    variables, ZERO were at Node and (after CE-436) two at Entity, both re-homed by this
        //    slice. `Behavior` is the only scope an author ever chose deliberately.
        // ⛔ This does NOT touch OccurrenceSlotKey.Compute's Node arm — that keys node-BOUND working
        //    state for hosted AiPrimitives, which is the common case and is not authored here.
        var scope = role == Hrot.AiEditor.Persistence.BlackboardVariableRole.State
            ? Hrot.AiEditor.Persistence.WorkingStateScope.Behavior
            : _blackboardVariables[idx].Scope;

        _blackboardVariables[idx] = _blackboardVariables[idx] with { Role = role, Scope = scope };
        MarkDirty();
    }

    /// <summary>Sets the working-state scope on an existing variable (S3-1). No-op if not found. Fires Changed.</summary>
    public void UpdateVariableScope(string name, Hrot.AiEditor.Persistence.WorkingStateScope scope)
    {
        int idx = _blackboardVariables.FindIndex(v => v.Name == name);
        if (idx < 0) return;
        _blackboardVariables[idx] = _blackboardVariables[idx] with { Scope = scope };
        MarkDirty();
    }

    /// <summary>Moves a variable from sourceIndex to destIndex in canonical order. Fires Changed.</summary>
    public void MoveVariable(int sourceIndex, int destIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= _blackboardVariables.Count) return;
        if (destIndex   < 0 || destIndex   >= _blackboardVariables.Count) return;
        if (sourceIndex == destIndex) return;
        var entry = _blackboardVariables[sourceIndex];
        _blackboardVariables.RemoveAt(sourceIndex);
        _blackboardVariables.Insert(destIndex, entry);
        MarkDirty();
    }

    /// <summary>Renames a variable. No-op if not found. Fires Changed.</summary>
    public void RenameVariable(string oldName, string newName)
    {
        int idx = _blackboardVariables.FindIndex(v => v.Name == oldName);
        if (idx < 0) return;
        _blackboardVariables[idx] = _blackboardVariables[idx] with { Name = newName };
        if (_aliases.TryGetValue(oldName, out var list))
        {
            _aliases.Remove(oldName);
            _aliases[newName] = list;
        }
        MarkDirty();
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>E7b</c> — a REAL count over <c>ExpressionTargetField</c> bindings.</b>
    ///
    /// <para>
    /// 🔴 <b>It returned a hardcoded <c>0</c>, with the comment <i>"HSM does not use
    /// ExpressionTargetField in this phase"</i> — and that was false when it was written.</b>
    /// <c>ExpressionTargetField</c> is an <b>OUTPUT binding</b> (<i>"blackboard field that receives
    /// the expression result of <c>ActionFunction</c>"</i>): <c>HsmAssetMapper</c> round-trips it,
    /// <c>HsmCommandSink</c> maintains it, and <c>HsmValidator</c> already reads it as a writer style.
    /// ⚠ <c>FIX-01-REPORT:43</c>'s <i>"no per-node"</i> meant per-<b>NODE</b>, not <i>absent</i>.
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>The consequence, and it is trap #5 again:</b>
    /// <c>BlueprintLocalVariableSchemaSource</c> computes <c>IsUnused: Count… == 0</c>, so a variable
    /// written through <c>ExpressionTargetField</c> read as <b>UNUSED</b> — offered for deletion with
    /// a clean conscience. A member reporting success while doing nothing.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Both transition kinds count.</b> A global transition is excluded from the validator's
    /// cross-region conflict rule because it belongs to no region — ⛔ but it is still a <b>writer</b>,
    /// so excluding it here would resurrect the same wrong answer for a narrower case.
    /// </para>
    ///
    /// <para>
    /// ⚠ Comparison is <see cref="StringComparison.OrdinalIgnoreCase"/> via
    /// <see cref="IsExpressionTargetOf"/> — ⭐ ONE predicate, shared with <c>HsmValidator</c>, so the
    /// count and the conflict rule cannot disagree about what "bound to this variable" means.
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <b><c>CE-387</c> — STATES count too, and for the same reason the rest of this method
    /// exists.</b> A state's <c>ExpressionTargetField</c> is a <b>READ</b> (its occurrence seeds its
    /// params from that variable) rather than a write, ⛔ but the caller's question is
    /// <c>IsUnused</c>, and a variable that something seeds from is plainly USED. ⚠ Omitting them
    /// would resurrect the exact defect this method was written to fix — a bound variable offered
    /// for deletion with a clean conscience — for the state case.
    /// </remarks>
    public int CountNodesReferencingVariable(string name)
    {
        int count = 0;
        // ⭐ CE-417: a NODE counts once if any of its bindings targets the variable.
        foreach (var t in AllTransitions)
            if (AnyTargets(name, t.Guard, t.Action)) count++;
        foreach (var g in AllGlobalTransitions)
            if (AnyTargets(name, g.Guard, g.Action)) count++;
        foreach (var s in AllStates)
            if (AnyTargets(name, s.OnEntry, s.OnExit, s.Activity, s.Timer)) count++;
        return count;
    }

    /// <summary>
    /// ⭐ <b>The ONE definition of "this <c>ExpressionTargetField</c> names that variable".</b>
    /// <c>HsmValidator.IsLocallyBoundTo</c> delegates here rather than repeating the comparison —
    /// two spellings of one predicate is how the count and the conflict rule drift apart.
    /// </summary>
    private static bool AnyTargets(string name, params BehaviorActionBinding?[] bindings)
    {
        foreach (var b in bindings)
            if (b is not null && IsExpressionTargetOf(b.ExpressionTargetField, name)) return true;
        return false;
    }

    public static bool IsExpressionTargetOf(string? expressionTargetField, string variableName)
        => !string.IsNullOrEmpty(expressionTargetField)
        && string.Equals(expressionTargetField, variableName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns all alias bindings recorded against the named variable. Empty list if none.</summary>
    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) =>
        _aliases.TryGetValue(variableName, out var list)
            ? list.AsReadOnly()
            : Array.Empty<BlackboardAliasBinding>();

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 91 (<c>91b</c>) — the whole alias map, for the MAPPER.</b> ⛔ Deliberately shaped
    /// like <c>GetAllSyncBindings</c> above: 📌 the persistence design lists aliases and sync bindings
    /// in the same breath, and a different shape here would be a third mechanism for one idea.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<BlackboardAliasBinding>> GetAllAliases() =>
        _aliases.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<BlackboardAliasBinding>)kv.Value.AsReadOnly());

    /// <summary>
    /// ⭐⭐ <b>Rehydrates the alias map on LOAD — the call that never existed.</b>
    /// 🔴 Measured Batch 91: the only writers to <c>_aliases</c> were rename, <c>AddAlias</c> and
    /// prune ⇒ <b>nothing on the load path touched them</b>, so every authored alias died with the
    /// editor session. ⚠ The DTO header even filed <c>_aliases</c> under <i>"runtime hydration"</i> —
    /// ⭐ a hydration that was never written.
    /// </summary>
    public void LoadAliases(IReadOnlyDictionary<string, IReadOnlyList<BlackboardAliasBinding>>? aliases)
    {
        _aliases.Clear();
        if (aliases is null) return;
        foreach (var kv in aliases)
            _aliases[kv.Key] = new List<BlackboardAliasBinding>(kv.Value);
    }


    /// <summary>Binds an unbound sub-tree requirement to a defined variable. Fires Changed.</summary>
    public void AddAlias(string variableName, BlackboardAliasBinding binding)
    {
        if (!_aliases.TryGetValue(variableName, out var list))
        {
            list = new List<BlackboardAliasBinding>();
            _aliases[variableName] = list;
        }
        // Prevent duplicate by (RequiringAssetId, RequiringElementId) pair.
        if (list.Exists(a => a.RequiringAssetId == binding.RequiringAssetId
                          && a.RequiringElementId == binding.RequiringElementId))
            return;
        list.Add(binding);
        MarkDirty();
    }

    /// <summary>
    /// Removes an alias binding from the named variable. No-op if not found. Fires Changed.
    /// </summary>
    public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId)
    {
        if (!_aliases.TryGetValue(variableName, out var list)) return;
        int idx = list.FindIndex(a => a.RequiringAssetId == requiringAssetId
                                   && a.RequiringElementId == requiringElementId);
        if (idx < 0) return;
        list.RemoveAt(idx);
        MarkDirty();
    }

    /// <summary>
    /// Removes alias bindings whose RequiringAssetId is not present in knownAssetIds.
    /// Fires Changed once if any bindings were removed.
    /// </summary>
    public void PruneStaleAliasBindings(IReadOnlyCollection<Guid> knownAssetIds)
    {
        bool removed = false;
        foreach (var varName in new List<string>(_aliases.Keys))
        {
            var list = _aliases[varName];
            int before = list.Count;
            list.RemoveAll(a => !knownAssetIds.Contains(a.RequiringAssetId));
            if (list.Count < before) removed = true;
            if (list.Count == 0) _aliases.Remove(varName);
        }
        if (removed) MarkDirty();
    }

    /// <summary>
    /// Returns the set of all distinct RequiringAssetId GUIDs currently referenced
    /// across all alias binding lists.
    /// </summary>
    public IReadOnlyCollection<Guid> GetKnownSubAssetIds()
    {
        var ids = new HashSet<Guid>();
        foreach (var list in _aliases.Values)
            foreach (var a in list)
                ids.Add(a.RequiringAssetId);
        return ids;
    }

    /// <summary>
    /// Returns true if the variable conflict is suppressed for the given writer pair.
    /// </summary>
    public bool IsConflictSuppressed(string variableName, string writerPairKey) =>
        _conflictSuppressions.Contains((variableName, writerPairKey));

    /// <summary>
    /// </summary>
    public void SetConflictSuppressed(string variableName, string writerPairKey, bool suppressed)
    {
        bool wasSuppressed = _conflictSuppressions.Contains((variableName, writerPairKey));
        if (wasSuppressed == suppressed) return;
        if (suppressed) _conflictSuppressions.Add((variableName, writerPairKey));
        else            _conflictSuppressions.Remove((variableName, writerPairKey));
        MarkDirty();
    }

    // ── W7b (§9.4) — "Allow concurrent writes", PER VARIABLE ────────────────────
    //
    // ⛔⛔ A SEPARATE SET from _conflictSuppressions on purpose. §9.3's suppression is per
    // (variable, writer-PAIR) so that "a new aliasing relationship on the same variable would
    // surface a fresh diagnostic"; §9.4's allowance is per VARIABLE, so it covers pairs that do
    // not exist yet. ⇒ merging the two would silence future writers the designer never reviewed.
    private readonly HashSet<string> _concurrentWritesAllowed = new();

    public bool IsConcurrentWritesAllowed(string variableName) =>
        _concurrentWritesAllowed.Contains(variableName);

    public IEnumerable<string> GetConcurrentWritesAllowed() => _concurrentWritesAllowed;

    public void SetConcurrentWritesAllowed(string variableName, bool allowed)
    {
        bool was = _concurrentWritesAllowed.Contains(variableName);
        if (was == allowed) return;
        if (allowed) _concurrentWritesAllowed.Add(variableName);
        else         _concurrentWritesAllowed.Remove(variableName);
        MarkDirty();
    }

    public bool IsUnusedWarningSuppressed(string variableName) =>
        _unusedSuppressions.Contains(variableName);

    public IEnumerable<(string VariableName, string WriterPairKey)> GetConflictSuppressions() => _conflictSuppressions;
    public IEnumerable<string> GetUnusedSuppressions() => _unusedSuppressions;

    public void SetUnusedWarningSuppressed(string variableName, bool suppressed)
    {
        bool wasSuppressed = _unusedSuppressions.Contains(variableName);
        if (wasSuppressed == suppressed) return;
        if (suppressed) _unusedSuppressions.Add(variableName);
        else            _unusedSuppressions.Remove(variableName);
        MarkDirty();
    }

    /// <summary>
    /// Returns a map of StateId -> RegionIndex for all states that are direct children
    /// of a parallel composite, enabling the shared window to check cross-region conflicts
    /// without a circular project reference.
    /// </summary>
    public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap()
    {
        var map = new Dictionary<Guid, int>();
        foreach (var s in AllStates)
        {
            if (s.Parent?.IsParallel == true)
                map[s.StableId] = s.RegionIndex;
        }
        return map.Count > 0 ? map : null;
    }

    // Identity bridges (built once on projection; rebuilt on reload)
    private readonly Dictionary<Guid, StateNode> _stableIdToState;
    private readonly Dictionary<Guid, TransitionNode> _visualIdToTransition;
    private readonly Dictionary<Guid, RegionNode> _stableIdToRegion;
    private readonly Dictionary<ushort, StateNode> _flatIndexToState;
    private readonly Dictionary<ushort, TransitionNode> _flatIndexToTransition;
    private readonly Dictionary<ushort, EventDefinition> _eventIdToEvent;

    // Mutable backing lists for states, transitions, regions, global transitions, and attachments.
    private readonly List<StateNode>            _allStatesList;
    private readonly List<TransitionNode>       _allTransitionsList;
    private readonly List<RegionNode>           _allRegionsList;
    private readonly List<GlobalTransitionNode> _allGlobalTransitionsList;
    private readonly Dictionary<AttachmentId, HsmAttachment> _attachments = new();

    public event Action? Changed;

    internal HsmAsset(
        Guid assetId,
        string name,
        string sourceFilePath,
        bool isEditorOwned,
        string targetNamespace,
        HsmDefinitionBlob blob,
        MachineMetadata metadata,
        StateNode rootState,
        List<StateNode> allStates,
        List<TransitionNode> allTransitions,
        List<GlobalTransitionNode> allGlobalTransitions,
        List<RegionNode> allRegions,
        List<EventDefinition> allEvents)
    {
        AssetId = assetId;
        Name = name;
        SourceFilePath = sourceFilePath;
        IsEditorOwned = isEditorOwned;
        TargetNamespace = targetNamespace;
        BlackboardTypeName = SanitizeIdentifier(name) + "_Blackboard";
        _blob = blob;
        _metadata = metadata;
        RootState = rootState;
        AllStates = allStates.AsReadOnly();
        AllTransitions = allTransitions.AsReadOnly();
        AllGlobalTransitions = allGlobalTransitions.AsReadOnly();
        AllRegions = allRegions.AsReadOnly();
        AllEvents = allEvents.AsReadOnly();
        _allStatesList            = allStates;
        _allTransitionsList       = allTransitions;
        _allRegionsList           = allRegions;
        _allGlobalTransitionsList = allGlobalTransitions;

        _stableIdToState = new Dictionary<Guid, StateNode>(allStates.Count);
        _visualIdToTransition = new Dictionary<Guid, TransitionNode>(allTransitions.Count);
        _stableIdToRegion = new Dictionary<Guid, RegionNode>(allRegions.Count);
        _flatIndexToState = new Dictionary<ushort, StateNode>(allStates.Count);
        _flatIndexToTransition = new Dictionary<ushort, TransitionNode>(allTransitions.Count);
        _eventIdToEvent = new Dictionary<ushort, EventDefinition>(allEvents.Count);

        foreach (var s in allStates)
        {
            _stableIdToState[s.StableId] = s;
            _flatIndexToState[s.FlatIndex] = s;
        }
        foreach (var t in allTransitions)
        {
            _visualIdToTransition[t.VisualId] = t;
            _flatIndexToTransition[t.FlatIndex] = t;
        }
        foreach (var r in allRegions)
            _stableIdToRegion[r.StableId] = r;
        foreach (var e in allEvents)
            _eventIdToEvent[e.EventId] = e;
    }

    // Identity bridge lookups
    public StateNode? FindStateByStableId(Guid stableId) =>
        _stableIdToState.GetValueOrDefault(stableId);

    public TransitionNode? FindTransitionByVisualId(Guid visualId) =>
        _visualIdToTransition.GetValueOrDefault(visualId);

    public RegionNode? FindRegionByStableId(Guid stableId) =>
        _stableIdToRegion.GetValueOrDefault(stableId);

    public StateNode? FindStateByFlatIndex(ushort flatIndex) =>
        _flatIndexToState.GetValueOrDefault(flatIndex);

    public TransitionNode? FindTransitionByFlatIndex(ushort flatIndex) =>
        _flatIndexToTransition.GetValueOrDefault(flatIndex);

    public EventDefinition? FindEventById(ushort eventId) =>
        _eventIdToEvent.GetValueOrDefault(eventId);

    /// <summary>Resolve the state whose hidden OUTPUT pin matches <paramref name="pinId"/> (transition source side).</summary>
    public StateNode? FindStateByOutputPin(Guid pinId)
    {
        foreach (var s in AllStates)
            if (s.HiddenOutputPinId == pinId) return s;
        return null;
    }

    /// <summary>Resolve the state whose hidden INPUT pin matches <paramref name="pinId"/> (transition target side).</summary>
    public StateNode? FindStateByInputPin(Guid pinId)
    {
        foreach (var s in AllStates)
            if (s.HiddenInputPinId == pinId) return s;
        return null;
    }

    // ── PU-302: Post-reload stitch ───────────────────────────────────────────

    /// <summary>
    /// Replaces the runtime blob and metadata references after a hot reload.
    /// Used by <see cref="StitchKernelIndices"/> to point the asset at the freshly
    /// recompiled blob without replacing the JSON-authoritative topology/layout.
    /// Must NOT call <see cref="MarkDirty"/> (PU-602 constraint).
    /// </summary>
    internal void UpdateBlob(HsmDefinitionBlob blob, MachineMetadata metadata)
    {
        _blob     = blob;
        _metadata = metadata;
    }

    /// <summary>
    /// Stitches runtime indices from a freshly assembly-projected asset onto this
    /// JSON-loaded editor model (design §6.6 / D13).
    /// <para>
    /// For each <see cref="StateNode"/> in <see cref="AllStates"/>, the matching state
    /// in <paramref name="fresh"/> is found by <c>StableId</c> via
    /// <see cref="MachineMetadata.StateStableIds"/> and its <c>FlatIndex</c> is copied
    /// across.  For each <see cref="TransitionNode"/>, the match is by <c>VisualId</c>
    /// via <see cref="MachineMetadata.TransitionVisualIds"/>.
    /// </para>
    /// <para>
    /// Unmatched nodes remain visible with <c>FlatIndex = 0</c> (sentinel) and a
    /// <see cref="BlackboardLoadState.Warning"/> diagnostic is set on the asset.
    /// </para>
    /// <para>
    /// <b>Must NOT call <see cref="MarkDirty"/></b> (PU-602 constraint).
    /// </para>
    /// </summary>
    public void StitchKernelIndices(HsmAsset? fresh)
    {
        if (fresh is null || fresh.AssetId != AssetId)
        {
            SetLoadDiagnostic(BlackboardLoadState.AssemblyFailed,
                "Assembly blob unavailable; runtime indices unset (debug overlay inert).");
            return;
        }

        var freshMeta = fresh.Metadata;

        // Build reverse maps: StableId → FlatIndex, VisualId string → FlatIndex
        var stableIdToFlatIndex    = new Dictionary<Guid, ushort>(freshMeta.StateStableIds.Count);
        var visualIdToTransFlat    = new Dictionary<Guid, ushort>(freshMeta.TransitionVisualIds.Count);

        foreach (var kv in freshMeta.StateStableIds)
            stableIdToFlatIndex[kv.Value] = kv.Key;

        foreach (var kv in freshMeta.TransitionVisualIds)
            visualIdToTransFlat[kv.Value] = kv.Key;

        bool anyUnmatched = false;

        foreach (var state in AllStates)
        {
            if (stableIdToFlatIndex.TryGetValue(state.StableId, out var flatIdx))
            {
                state.FlatIndex = flatIdx;
            }
            else
            {
                state.FlatIndex = 0; // sentinel
                anyUnmatched = true;
            }
        }

        foreach (var transition in AllTransitions)
        {
            if (visualIdToTransFlat.TryGetValue(transition.VisualId, out var flatIdx))
            {
                transition.FlatIndex = flatIdx;
            }
            else
            {
                transition.FlatIndex = 0; // sentinel
                anyUnmatched = true;
            }
        }

        // Update blob/metadata references to the recompiled versions
        UpdateBlob(fresh.Blob, fresh.Metadata);

        if (anyUnmatched)
        {
            SetLoadDiagnostic(BlackboardLoadState.StructParseFailed,
                "One or more states/transitions have no blob match; debug overlay partially inert.");
        }
        else
        {
            SetLoadDiagnostic(BlackboardLoadState.Clean, null);
        }
        // Do NOT call MarkDirty — stitch is a reload-only operation (PU-602 constraint).
    }

    // ---- IStitchableAsset ----

    /// <inheritdoc/>
    void IStitchableAsset.StitchRuntimeIndices(IEditableAsset? fresh)
        => StitchKernelIndices(fresh as HsmAsset);

    internal void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// Clears the in-memory dirty flag after a successful save/emit.
    /// Called by the <c>RegenerationScheduler</c> flush action in <c>EditorSubsystem</c>.
    /// </summary>
    public void ClearDirty() => IsDirty = false;

    // Converts a name into a valid C# identifier (strips non-alphanumeric chars,
    // prepends '_' when the first char is a digit, falls back to "HsmAsset").
    private static string SanitizeIdentifier(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in name)
            if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
        if (sb.Length == 0) return "HsmAsset";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    // ---- Region mutation helpers (called by HsmCommandSink) ----

    // Registers a new region in the asset-level lookup dictionary and backing list.
    // The caller is responsible for inserting the region into the parent StateNode.RegionNodes.
    internal void RegisterRegion(RegionNode region)
    {
        _allRegionsList.Add(region);
        _stableIdToRegion[region.StableId] = region;
    }

    // Unregisters a region from the asset-level lookup dictionary and backing list.
    // The caller is responsible for removing the region from the parent StateNode.RegionNodes.
    internal void UnregisterRegion(RegionNode region)
    {
        _allRegionsList.Remove(region);
        _stableIdToRegion.Remove(region.StableId);
    }

    // ---- State mutation helpers (called by HsmCommandSink) ----

    // Registers a newly-created (editor-authored) state under `parent`. Updates the
    // backing list + identity maps. FlatIndex is assigned to the next free value so the
    // flat-index map stays collision-free in-session; it is authoritatively re-derived
    // from the blob on save->reload.
    internal void RegisterState(StateNode state, StateNode parent)
    {
        state.Parent = parent;
        if (!parent.Children.Contains(state)) parent.Children.Add(state);
        if (state.FlatIndex == 0 || _flatIndexToState.ContainsKey(state.FlatIndex))
            state.FlatIndex = NextFreeStateFlatIndex();
        _allStatesList.Add(state);
        _stableIdToState[state.StableId] = state;
        _flatIndexToState[state.FlatIndex] = state;
    }

    internal void UnregisterState(StateNode state)
    {
        state.Parent?.Children.Remove(state);
        _allStatesList.Remove(state);
        _stableIdToState.Remove(state.StableId);
        _flatIndexToState.Remove(state.FlatIndex);
    }

    // ---- Transition mutation helpers ----

    internal void RegisterTransition(TransitionNode t)
    {
        if (t.FlatIndex == 0 || _flatIndexToTransition.ContainsKey(t.FlatIndex))
            t.FlatIndex = NextFreeTransitionFlatIndex();
        if (!t.Source.OutgoingTransitions.Contains(t)) t.Source.OutgoingTransitions.Add(t);
        _allTransitionsList.Add(t);
        _visualIdToTransition[t.VisualId] = t;
        _flatIndexToTransition[t.FlatIndex] = t;
    }

    internal void UnregisterTransition(Guid visualId)
    {
        if (!_visualIdToTransition.TryGetValue(visualId, out var t)) return;
        t.Source?.OutgoingTransitions.Remove(t);
        _allTransitionsList.Remove(t);
        _visualIdToTransition.Remove(visualId);
        _flatIndexToTransition.Remove(t.FlatIndex);
    }

    private ushort NextFreeStateFlatIndex()
    {
        ushort max = 0;
        foreach (var key in _flatIndexToState.Keys)
            if (key > max) max = key;
        return (ushort)(max + 1);
    }

    private ushort NextFreeTransitionFlatIndex()
    {
        ushort max = 0;
        foreach (var key in _flatIndexToTransition.Keys)
            if (key > max) max = key;
        return (ushort)(max + 1);
    }

    // ---- Global transition mutation helpers ----

    /// <summary>Removes the global transition with the given VisualId, if it exists.</summary>
    internal bool RemoveGlobalTransition(Guid visualId)
    {
        int idx = _allGlobalTransitionsList.FindIndex(g => g.VisualId == visualId);
        if (idx < 0) return false;
        _allGlobalTransitionsList.RemoveAt(idx);
        return true;
    }

    // ---- Attachment mutation helpers (called by HsmCommandSink) ----

    internal void AddAttachment(HsmAttachment attachment)
    {
        _attachments[attachment.Id] = attachment;
    }

    internal void RemoveAttachments(IReadOnlyList<AttachmentId> ids)
    {
        foreach (var id in ids)
            _attachments.Remove(id);
    }

    internal HsmAttachment? FindAttachmentById(AttachmentId id) =>
        _attachments.GetValueOrDefault(id);

    internal IEnumerable<HsmAttachment> AllAttachments => _attachments.Values;

    internal IReadOnlyList<HsmAttachment> GetAttachmentsForNode(NodeId hostId)
    {
        var result = new List<HsmAttachment>();
        foreach (var att in _attachments.Values)
            if (att.HostNodeId == hostId) result.Add(att);
        return result;
    }
}

// Editor-side representation of a single state.
// Augments the kernel-side StateDef with editor-only fields (layout, comments, etc.).
public sealed class StateNode : IContainerNodeModel
{
    // Primary editor identity (stable across hot reloads if layout method is present)
    public Guid StableId;
    // Re-derived on each reload; index into HsmDefinitionBlob.States
    public ushort FlatIndex;

    public string Name;
    public StateNode? Parent;
    public List<StateNode> Children { get; } = new();
    public List<TransitionNode> OutgoingTransitions { get; } = new();
    public List<RegionNode> RegionNodes { get; } = new();

    // State configuration (from StateDef.Flags)
    public bool IsInitial;
    public bool IsHistory;
    public bool IsDeepHistory;
    public bool IsParallel;
    public bool IsFinal;

    // True when this state is a pseudo-state (History, Deep-History, or Final).
    // Pseudo-states are rendered exclusively via HsmHistoryGlyphsRenderer;
    // the node body background is drawn transparent.
    public bool IsPseudostate => IsHistory || IsDeepHistory || IsFinal;

    // ⭐⭐⭐ CE-417 (slice 4a) — the state's four action slots, each ONE BehaviorActionBinding (null = slot unbound).
    // 📄 docs/blueprints/DESIGN_Behavior_Action_Binding.md §3, §5.4. Each binding carries its OWN target field (B-2).
    public BehaviorActionBinding? OnEntry;
    public BehaviorActionBinding? OnExit;

    /// <summary>
    /// The per-tick activity: a C# method (<see cref="BehaviorActionBinding.MethodFqn"/>) OR a blueprint
    /// (<see cref="BehaviorActionBinding.BlueprintAssetId"/> + name).
    /// <para>⭐⭐⭐ CE-385 — the two are MUTUALLY EXCLUSIVE: a NAMED activity resolves through FNV1a16(FQN); a
    /// blueprint-hosted thunk registers under its BlueprintId = FNV-1a32 of the Guid — two id spaces no authorable string
    /// bridges (CE-383/CE-384). The validator says so (design §9 ③). ⭐ The Guid is the identity and the RENAME SURVIVOR;
    /// the name is display + re-resolution only. 📄 DESIGN_Hsm_Blueprint_Behaviour_Authoring.md §3.2, §7.</para>
    /// </summary>
    public BehaviorActionBinding? Activity;

    public BehaviorActionBinding? Timer;

    /// <summary>The four slots, unbound ones skipped.</summary>
    public IEnumerable<BehaviorActionBinding> Bindings
    {
        get
        {
            if (OnEntry  is not null) yield return OnEntry;
            if (OnExit   is not null) yield return OnExit;
            if (Activity is not null) yield return Activity;
            if (Timer    is not null) yield return Timer;
        }
    }

    // Inferred from action declarations; read-only in the editor
    public byte OutputLaneMask;

    // Event IDs deferred while in this state (to be populated from blob deferred-event table
    // in a later task; empty for now)
    public List<ushort> DeferredEventIds { get; } = new();

    // Zero-based index of the orthogonal region this state belongs to within its parent parallel composite.
    // 0 for states that are not children of a parallel state.
    public int RegionIndex;

    // When non-empty, this state acts as a "Subtree host" that runs an external behavior asset
    // (BTree or nested HSM) identified by this GUID. Used by the cross-region stateful-Subtree
    // validator (S2-4) to detect concurrent execution of the same stateful asset in orthogonal
    // parallel regions. Guid.Empty means no sub-behavior reference.
    public Guid SubtreeAssetId;

    // ⭐⭐ E5 / Q36-B = A — the hosted child's REGISTRY NAME, beside the Guid.
    //
    // ⛔ The Guid alone cannot host anything: a host resolves its child through BehaviorRegistry,
    //    which is keyed by NAME (TryGetId/TryGetDefinition) — there is no asset-id index, and
    //    Q36-B ruled against adding one (one mechanism with the shipped BTree path, whose
    //    BTreeSubtreePayload has carried the same {Guid, Name} pair since PU).
    // ⭐ The Guid stays as the RENAME SURVIVOR: it is what the editor re-resolves the name from.
    // 📄 DESIGN_Occurrence_Scoped_Storage.md §32.8 item 1.
    public string? SubtreeName;

    // ⭐⭐ CE-439 — the HOST variable (Role=Input, typed as the child's Inputs struct) that seeds the hosted child on each
    //   start; set by the subtree pick's compose step. Null = unbound. Mirrors BTreeSubtreePayload.ParamsVariable.
    public string? SubtreeParamsVariable;

    // ⭐⭐ DERIVED, NOT PERSISTED — recomputed by HsmSubtreeResolver against the asset catalogue on
    //    load and after a hot reload. 📄 HSM_Editor_NodeEditor_Host_Design.md §11.1a.
    // ⛔ Deliberately absent from StateNodeDto, mirroring BTree: a persisted `true` would outlive
    //    the asset it describes and claim a dangling reference is fine.
    public bool IsSubtreeResolved;

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-387</c> — which blackboard variable THIS STATE's hosted occurrence seeds its params from.</b>
    /// 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.4; <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.6.
    ///
    /// <para>⭐ <c>CE-417</c>: DERIVED from the slots — Activity's target, else OnEntry's, OnExit's, Timer's — the SAME rule
    /// as <c>HsmBridgeEmitCore.StateWideField</c>, so the editor and the emitter cannot disagree about the seed. ⛔ Read-only
    /// since slice 5: each slot authors its own variable (B-2), and the v1 "one field to every slot" setter had only test
    /// writers left after 4b — a second write path for a value the slots own. A variable typed before a method is picked
    /// lives on an Activity binding that names nothing (CE-401).</para>
    ///
    /// <para>⛔⛔ <b>A state's target field is an INPUT</b> (the variable its occurrence SEEDS FROM); a transition ACTION's is
    /// an OUTPUT (the field that RECEIVES its result), which is why only the latter is in the cross-region WRITER-conflict
    /// rule. ⇒ a state binding must NEVER be added to that rule — concurrent readers are legal.</para>
    /// </summary>
    public string? StateWideTargetField
        => Field(Activity) ?? Field(OnEntry) ?? Field(OnExit) ?? Field(Timer);

    private static string? Field(BehaviorActionBinding? b)
        => b is null || string.IsNullOrEmpty(b.ExpressionTargetField) ? null : b.ExpressionTargetField;


    // Editor-only (persisted in layout method)
    public Vector2 Position { get; set; }
    public Vector2? SizeOverride { get; set; }
    public string? Comment;
    public bool IsCollapsed { get; set; }
    public string? ColorOverride;

    // Editor-only ephemeral (not persisted)
    public bool IsBreakpoint;

    // Editor-only ephemeral (not persisted) — set by HsmGraphModel from validation each rebuild.
    public NodeState? DiagnosticState;
    public string?    DiagnosticTooltip;

    // Hidden pin IDs used by HsmTransitionLink to connect this state in the node graph.
    // Derived deterministically from StableId so they are stable across reloads.
    // Output pin = source side of a transition FROM this state.
    // Input pin  = target side of a transition TO this state.
    public Guid HiddenOutputPinId => DeriveOutputPinId(StableId);
    public Guid HiddenInputPinId  => DeriveInputPinId(StableId);

    public StateNode(string name)
    {
        Name = name;
        StableId = Guid.NewGuid();  // replaced by projector if layout provides one
    }

    // Derives a deterministic output pin GUID from a state's StableId.
    // Flips bit 0 of the last byte to ensure distinct from input pin and state ID.
    internal static Guid DeriveOutputPinId(Guid stableId)
    {
        var bytes = stableId.ToByteArray();
        bytes[15] = (byte)(bytes[15] ^ 0x01);
        return new Guid(bytes);
    }

    // Derives a deterministic input pin GUID from a state's StableId.
    // Flips bit 1 of the last byte to ensure distinct from output pin and state ID.
    internal static Guid DeriveInputPinId(Guid stableId)
    {
        var bytes = stableId.ToByteArray();
        bytes[15] = (byte)(bytes[15] ^ 0x02);
        return new Guid(bytes);
    }

    // ---- INodeModel ----

    public NodeId Id => new NodeId(StableId);

    // Resolve the catalog kind key from state flags.
    public NodeKindKey Kind
    {
        get
        {
            if (IsFinal)       return new NodeKindKey(HsmKinds.Final);
            if (IsDeepHistory) return new NodeKindKey(HsmKinds.DeepHistory);
            if (IsHistory)     return new NodeKindKey(HsmKinds.History);
            if (IsParallel)    return new NodeKindKey(HsmKinds.Parallel);
            if (Children.Count > 0) return new NodeKindKey(HsmKinds.Composite);
            return new NodeKindKey(HsmKinds.Simple);
        }
    }

    public string Title => Name;
    public string? Subtitle => null;
    public NodeCategory Category
    {
        get
        {
            // Pseudo-states keep Custom → HsmEditorTheme maps Custom to transparent so the
            // glyph renderer (H / H* / F) owns their visual. (RHS-04)
            if (IsHistory || IsDeepHistory || IsFinal) return NodeCategory.Custom;
            if (IsParallel)         return NodeCategory.Event;    // parallel composite
            if (Children.Count > 0) return NodeCategory.Macro;   // composite
            return NodeCategory.Function;                         // simple state
        }
    }
    public NodeState State => DiagnosticState ?? (IsBreakpoint ? NodeState.Warning : NodeState.Normal);
    public string?   StatusTooltip => DiagnosticTooltip;
    public bool ShowAdvancedPins => false;

    // Two hidden pins: output (source of transitions FROM this state) and input (target TO this state).
    // Lazy-initialized to avoid allocation on non-pinned code paths.
    private IReadOnlyList<IPinModel>? _pins;
    public IReadOnlyList<IPinModel> Pins => _pins ??= BuildPins();

    private IReadOnlyList<IPinModel> BuildPins()
    {
        return new IPinModel[]
        {
            new HsmPinModel(new PinId(HiddenOutputPinId), new NodeId(StableId), PinDirection.Output),
            new HsmPinModel(new PinId(HiddenInputPinId),  new NodeId(StableId), PinDirection.Input),
        };
    }

    // ParentContainerId is null for top-level states (Parent is RootState which has no parent).
    public NodeId? ParentContainerId =>
        Parent?.Parent != null ? new NodeId(Parent!.StableId) : (NodeId?)null;

    // ---- IContainerNodeModel ----

    public bool IsContainer => Children.Count > 0 || IsParallel;

    public IReadOnlyList<NodeId> ChildNodeIds =>
        Children.Select(c => new NodeId(c.StableId)).ToList();

    // For parallel composites, expose region descriptors.
    // For non-parallel composites, return empty.
    public IReadOnlyList<RegionDescriptor> Regions
    {
        get
        {
            if (!IsParallel || RegionNodes.Count == 0)
                return Array.Empty<RegionDescriptor>();
            return RegionNodes
                .Select(r => new RegionDescriptor(r.RegionIndex, r.Name, r.Priority, null))
                .ToList();
        }
    }

    public int GetRegionIndexForChild(NodeId childId)
    {
        // Find the child StateNode with matching StableId
        var child = Children.FirstOrDefault(c => c.StableId == childId.Value);
        if (child == null) return -1;
        return child.RegionIndex;
    }

    public ContainerPadding Padding => ContainerPadding.Default;

    public Vector2 MinimumInteriorSize =>
        IsParallel ? new Vector2(280f, 120f) : new Vector2(200f, 80f);

	public RegionLayoutOrientation RegionOrientation => RegionLayoutOrientation.VerticalStack;
}

// Editor-side representation of a transition between two states.
public sealed class TransitionNode
{
    // Primary editor identity (stable if layout method is present)
    public Guid VisualId;
    // Re-derived on each reload; index into HsmDefinitionBlob.Transitions
    public ushort FlatIndex;

    public StateNode Source = null!;
    public StateNode Target = null!;
    public ushort EventId;
    public string? EventName;    // symbolicated from MachineMetadata; for display

    /// <summary>
    /// ⭐ CE-417 — the guard: a C# method OR (CE-385) a blueprint, MUTUALLY EXCLUSIVE for the same two-id-space reason as
    /// <see cref="StateNode.Activity"/>. Its target field is an INPUT: the variable the guard blueprint seeds from (CE-413).
    /// </summary>
    public BehaviorActionBinding? Guard;

    /// <summary>
    /// ⭐ CE-417 — the transition action. Its target field is an OUTPUT: the blackboard field that receives the action's
    /// result — the one binding in this node that the cross-region WRITER-conflict rule considers.
    /// </summary>
    public BehaviorActionBinding? Action;

    public byte Priority;
    public TransitionKind Kind;
    public ushort SyncGroupId;

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-381</c>/<c>CE-385</c> — POLLED: this transition's guard is evaluated on every
    /// QUIESCENT tick, with no event posted.</b>
    ///
    /// <para>⛔⛔ <b>Not the same as "a transition with no event".</b> An eventless transition is
    /// selected by the RTC loop's COMPLETION pass and fires once, as a consequence of another
    /// transition firing; a polled one fires whenever its guard passes while the machine is idle.
    /// 🔒 The two were deliberately NOT collapsed onto one encoding — 📄 §2.3, §3.1, §10 ③.</para>
    ///
    /// <para>⚠ <b>Editing this on a LIVE cluster appears to do nothing</b> until the behaviour is
    /// re-assigned: <c>IsPolled</c> moves no layout, so it does not always move
    /// <c>StructureHash</c>. 🔒 Accepted and documented — §8b.</para>
    /// </summary>
    public bool IsPolled;

    // Editor-only (persisted in layout method)
    public List<Vector2> Waypoints { get; } = new();
    public string? Comment;
    public bool IsBreakpoint;
}

public enum TransitionKind { External, Internal, Local }

// Editor-side representation of a global (unconditional-source) transition.
public sealed class GlobalTransitionNode
{
    public Guid VisualId;
    public ushort FlatIndex;
    public StateNode Target = null!;
    public ushort EventId;
    public string? EventName;

    /// <summary>⭐ CE-417 (B-3) — the same two bindings as a <see cref="TransitionNode"/>.</summary>
    public BehaviorActionBinding? Guard;

    /// <summary>⭐ CE-417 (B-3) — its target field is an OUTPUT, as on a <see cref="TransitionNode"/>.</summary>
    public BehaviorActionBinding? Action;

    public byte Priority;
    public string? Comment;
    public bool IsBreakpoint;
}

// Editor-side representation of an orthogonal region within a parallel state.
public sealed class RegionNode
{
    // Separate identity from the parent state
    public Guid StableId;
    // 0..(RegionCount-1) within parent state
    public byte RegionIndex;
    // Editor-only label; not stored in the kernel
    public string Name;
    public byte Priority;
    public StateNode? InitialChild;

    // Editor-only
    public string? Comment;
    public string? ColorOverride;

    public RegionNode(string name)
    {
        Name = name;
        StableId = Guid.NewGuid();
    }
}

// Editor-side representation of an event declared in the state machine.
public sealed class EventDefinition
{
    public ushort EventId;
    public string Name;
    public int PayloadSize;
    public bool IsIndirect;
    public bool IsDeferrable;     // whether some state defers this event
    public bool HasGlobalTransition;  // derived from the GlobalTransitions list

    public EventDefinition(string name, ushort eventId)
    {
        Name = name;
        EventId = eventId;
    }
}
