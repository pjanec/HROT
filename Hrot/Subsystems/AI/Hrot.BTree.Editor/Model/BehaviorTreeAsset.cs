using System;
using System.Collections.Generic;
using System.Numerics;
using Fbt;
using Hrot.BTree.Editor.Debug;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.BTree.Editor.Model;

// ── Payload types ─────────────────────────────────────────────────────────────

/// <summary>Describes which delegate overload an Action or Condition node uses.</summary>
public enum BTreeActionDelegateShape
{
    /// <summary>Three-parameter reusable delegate with an expression-target field selector.</summary>
    ThreeParamReusable,
    /// <summary>Four-parameter delegate with full blackboard access.</summary>
    FourParamFull,

    // NOTE: value 2 (ThreeParamReusableStateful in BTreeDelegateShapeDto, the persisted DTO enum)
    // has no named member here yet; the numeric value still round-trips correctly through the
    // (BTreeActionDelegateShape)/(BTreeDelegateShapeDto) casts in BehaviorTreeAssetMapper.

    /// <summary>
    /// I2/I3/E2: a blueprint-authored AiPrimitive action composed as a host-BTree node. The host
    /// owns the Params layout (bin-packed into the blackboard at a baked offset, like
    /// ThreeParamReusable) plus a partition slot for the blueprint's WorkingState; the node
    /// dispatches to the blueprint's generated
    /// <c>TickCore(ref Params, ref WorkingState, Entity self, EntityRepository world, float time)</c>.
    /// Explicit value 3 to match BTreeDelegateShapeDto.AiPrimitiveTickCore (the persisted DTO enum).
    /// </summary>
    AiPrimitiveTickCore = 3,
}

/// <summary>Payload for Action leaf nodes.</summary>
public sealed class BTreeActionPayload
{
    /// <summary>Fully-qualified method name, e.g. "Hrot.Game.Combat.CombatActions.AimAndFire".</summary>
    public string MethodFqn = string.Empty;
    /// <summary>Blackboard field referenced by the expression target (null when not using ThreeParamReusable).</summary>
    public string? ExpressionTargetField;
    public BTreeActionDelegateShape DelegateShape;
    /// <summary>
    /// E2: for <see cref="BTreeActionDelegateShape.AiPrimitiveTickCore"/> bindings, the CLR FQN of
    /// the blueprint's generated WorkingState struct (second ref param after Params), e.g.
    /// "Hrot.AI.Behaviors.Brains.DemoAiPrimitiveNodes+WorkingState". Null for other shapes.
    /// </summary>
    public string? WorkingStateTypeId;
    /// <summary>
    /// Slice 1 (shared working-state): for <see cref="BTreeActionDelegateShape.AiPrimitiveTickCore"/>
    /// bindings, the Name of the authored working-state blackboard variable (Role=State), distinct
    /// from <see cref="ExpressionTargetField"/> (the Params variable). Its declared Scope governs the
    /// partition slot key — Behavior-scoped nodes bound to the same variable share one slot. Null
    /// falls back to <see cref="ExpressionTargetField"/> for scope resolution (back-compat).
    /// </summary>
    public string? WorkingStateTargetField;
}

/// <summary>Payload for Condition leaf nodes.</summary>
public sealed class BTreeConditionPayload
{
    public string MethodFqn = string.Empty;
    public string? ExpressionTargetField;
    public BTreeActionDelegateShape DelegateShape;
    /// <summary>
    /// E2: for <see cref="BTreeActionDelegateShape.AiPrimitiveTickCore"/> bindings, the CLR FQN
    /// of the blueprint's generated WorkingState struct (second ref param after Params), e.g.
    /// "Hrot.AI.Behaviors.Brains.DemoAiPrimitiveNodes+WorkingState". Null for other shapes.
    /// </summary>
    public string? WorkingStateTypeId;
    /// <summary>
    /// Slice 1 (shared working-state): for <see cref="BTreeActionDelegateShape.AiPrimitiveTickCore"/>
    /// bindings, the Name of the authored working-state blackboard variable (Role=State), distinct
    /// from <see cref="ExpressionTargetField"/> (the Params variable). Its declared Scope governs the
    /// partition slot key — Behavior-scoped nodes bound to the same variable share one slot. Null
    /// falls back to <see cref="ExpressionTargetField"/> for scope resolution (back-compat).
    /// </summary>
    public string? WorkingStateTargetField;
}

/// <summary>Payload for Wait leaf nodes.</summary>
public sealed class BTreeWaitPayload
{
    /// <summary>Duration in seconds; sourced from BehaviorTreeBlob.FloatParams.</summary>
    public float Duration;
}

/// <summary>Payload for Subtree leaf nodes.</summary>
public sealed class BTreeSubtreePayload
{
    /// <summary>Resolved asset GUID; may be Guid.Empty if unresolved.</summary>
    public Guid SubtreeAssetId;
    public string SubtreeName = string.Empty;
    /// <summary>False if the referenced asset is absent from the catalog.</summary>
    public bool IsResolved;
}

// ── BTreeEditorPill ───────────────────────────────────────────────────────────

/// <summary>
/// Represents a decorator wrapper collapsed into an attachment pill in the editor.
/// Corresponds to one decorator-type kernel node whose child is the decorated host node.
/// </summary>
public sealed class BTreeEditorPill
{
    /// <summary>Stable visual identity of this pill (minted or sourced from NodeDebugMetadata.VisualId).</summary>
    public Guid VisualId;
    /// <summary>Visual ID of the host node that this pill decorates.</summary>
    public Guid HostNodeVisualId;
    /// <summary>Decorator kind (Inverter, Repeater, Cooldown, …).</summary>
    public NodeType DecoratorType;
    /// <summary>Integer parameter (e.g. Repeater's count). Null when not applicable.</summary>
    public int? IntParam;
    /// <summary>Float parameter (e.g. Cooldown's duration). Null when not applicable.</summary>
    public float? FloatParam;
    public string? Comment;
    /// <summary>Zero-based ordering within the host node's pill stack (top = 0).</summary>
    public int StackIndex;
}

// ── BTreeEditorNode ───────────────────────────────────────────────────────────

/// <summary>
/// Editor-side representation of one node in a behavior tree.
/// Mutable; position / layout data are updated by the canvas.
/// </summary>
public sealed class BTreeEditorNode
{
    /// <summary>Primary editor identity; stable across reloads when sourced from NodeDebugMetadata.VisualId.</summary>
    public Guid VisualId;
    /// <summary>Runtime node type (Root, Sequence, Action, etc.).</summary>
    public NodeType KernelType;
    /// <summary>Index into BehaviorTreeBlob.Nodes[]; re-derived on every projection.</summary>
    public int KernelBlobIndex;
    /// <summary>Canvas position in graph-space units.</summary>
    public Vector2 Position;
    /// <summary>Human-readable label sourced from NodeDebugMetadata.Label.</summary>
    public string DisplayLabel = string.Empty;
    /// <summary>Editor-only comment sourced from NodeDebugMetadata.CustomComment.</summary>
    public string? Comment;
    /// <summary>Ordered child visual IDs (BTree composites are order-sensitive).</summary>
    public List<Guid> ChildVisualIds = new();

    // Per-node-type payloads (mutually exclusive; at most one is non-null).
    public BTreeActionPayload?    Action;
    public BTreeConditionPayload? Condition;
    public BTreeWaitPayload?      Wait;
    public BTreeSubtreePayload?   Subtree;

    /// <summary>Session-local breakpoint flag; not persisted in the layout method.</summary>
    public bool IsBreakpoint;

    /// <summary>
    /// Waypoints for the edge from this node UP to its parent.
    /// Empty when no reroute points have been added. Persisted in the layout method.
    /// </summary>
    public List<Vector2> Waypoints { get; } = new();

    // ── Stable pin IDs derived from VisualId ─────────────────────────────────
    // Deterministically derived so they survive reload and are not persisted.
    // XOR with fixed constants avoids collisions when VisualId is the same Guid.
    //   OutputPinId: child's "up-link" (reversed-pin convention)
    //   InputPinId:  parent's "down-link" (receives children's output pins)

    private static Guid XorGuid(Guid g, ulong hi, ulong lo)
    {
        var bytes = g.ToByteArray();
        var hiBytes = BitConverter.GetBytes(hi);
        var loBytes = BitConverter.GetBytes(lo);
        for (int i = 0; i < 8; i++) bytes[i]     ^= hiBytes[i];
        for (int i = 0; i < 8; i++) bytes[i + 8] ^= loBytes[i];
        return new Guid(bytes);
    }

    // Session-local pin-id overrides. Set when a node is created via the canvas
    // drag-to-create flow, which pre-generates pin IDs (for stable Undo/Redo) and
    // forms the auto-wire link against them. Not persisted — on reload the derived
    // IDs are used and links are re-projected from ChildVisualIds, so this only
    // needs to be consistent within a session.
    private Guid? _outputPinIdOverride;
    private Guid? _inputPinIdOverride;

    /// <summary>
    /// Adopt externally-supplied pin IDs (from the canvas drag-create flow). A null
    /// argument leaves that pin on its derived ID. Lets <see cref="OutputPinId"/> /
    /// <see cref="InputPinId"/> match the IDs the canvas baked into the auto-wire link.
    /// </summary>
    public void SetExplicitPinIds(Guid? output, Guid? input)
    {
        if (output.HasValue) _outputPinIdOverride = output;
        if (input.HasValue)  _inputPinIdOverride  = input;
    }

    /// <summary>
    /// Stable output-pin ID for this node (child's upward exec link).
    /// Session override if one was supplied, else derived deterministically from
    /// <see cref="VisualId"/> — never null.
    /// </summary>
    public Guid OutputPinId => _outputPinIdOverride ?? XorGuid(VisualId, 0xBB_00_00_00_00_00_00_01UL, 0x00_00_00_00_00_00_00_02UL);

    /// <summary>
    /// Stable input-pin ID for this node (parent's downward exec link).
    /// Session override if one was supplied, else derived deterministically from
    /// <see cref="VisualId"/> — never null.
    /// </summary>
    public Guid InputPinId  => _inputPinIdOverride ?? XorGuid(VisualId, 0xBB_00_00_00_00_00_00_03UL, 0x00_00_00_00_00_00_00_04UL);

    /// <summary>Returns true when this node kind cannot have children in a BTree.</summary>
    public bool IsLeaf =>
        KernelType == NodeType.Action    ||
        KernelType == NodeType.Condition ||
        KernelType == NodeType.Wait      ||
        KernelType == NodeType.Subtree;

    /// <summary>Returns true when this node kind is a decorator wrapper.</summary>
    public bool IsDecorator =>
        KernelType == NodeType.Inverter     ||
        KernelType == NodeType.Repeater     ||
        KernelType == NodeType.Cooldown     ||
        KernelType == NodeType.ForceSuccess ||
        KernelType == NodeType.ForceFailure ||
        KernelType == NodeType.UntilSuccess ||
        KernelType == NodeType.UntilFailure;
}

// ── BehaviorTreeAsset ─────────────────────────────────────────────────────────

/// <summary>
/// Editor-side model of a BTree asset.
/// Implements <see cref="IEditableAsset"/> so it participates in the shared
/// AI editor selection store and asset browser.
/// </summary>
public sealed class BehaviorTreeAsset : IEditableAsset, IBlackboardManagedAsset, IBTreeSyncableAsset, IStitchableAsset
{
    private bool _isDirty;
    private readonly List<BTreeEditorNode> _nodes = new();
    private readonly List<BTreeEditorPill> _pills  = new();
    private readonly List<BlackboardVariableEntry> _blackboardVariables = new();
    private readonly Dictionary<string, List<BlackboardAliasBinding>> _aliases = new();
    private readonly Dictionary<Guid, List<SubtreeSyncBinding>> _syncBindings = new();
    // Sub-tree identity metadata per subtree-node visual ID. Populated by Inspector callbacks.
    private readonly Dictionary<Guid, (string SubtreeName, string SubDtoTypeName, string? SubDtoTypeNs)> _syncNodeMeta = new();
    private readonly HashSet<(string VariableName, string WriterPairKey)> _conflictSuppressions = new();
    private readonly HashSet<string> _unusedSuppressions = new();

    // ---- BT-S1-03: lookup tables ----
    private readonly Dictionary<Guid, int>              _visualIdToBlobIndex = new();
    private readonly Dictionary<Guid, BTreeEditorNode>  _visualIdToNode      = new();
    private readonly Dictionary<Guid, BTreeEditorPill>  _visualIdToPill      = new();

    // PU-302: debug session reference set by the JSON contributor so StitchKernelIndices
    // can re-wire symbolication without needing external injection at stitch time.
    private BTreeDebugSession? _debugSession;

    // ---- IEditableAsset ----
    public Guid AssetId { get; }
    public string Name { get; set; }
    public AssetKind Kind => AssetKind.BTree;
    public string SourceFilePath { get; }
    public bool IsDirty => _isDirty;
    public bool IsEditorOwned { get; }
    public event Action? Changed;

    // ---- Kernel data ----
    public string BlackboardTypeName { get; }
    public string ContextTypeName    { get; }
    /// <summary>
    /// Target C# namespace for the emitted file, e.g. "Hrot.AI.Behaviors.Trees".
    /// </summary>
    public string TargetNamespace    { get; set; }
    public BehaviorTreeBlob Blob { get; private set; }

    // ---- Editor collections (read-only views) ----
    public IReadOnlyList<BTreeEditorNode> Nodes => _nodes;
    public IReadOnlyList<BTreeEditorPill> Pills => _pills;

    // ---- IBlackboardManagedAsset ----
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

    /// <summary>Replaces the current variable list and fires Changed.
    /// Call this when the editor commits an updated set of variables.
    /// </summary>
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
        _blackboardVariables[idx] = _blackboardVariables[idx] with { Role = role };
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

    /// <summary>Returns the count of action/condition nodes that reference variableName via ExpressionTargetField.</summary>
    public int CountNodesReferencingVariable(string name)
    {
        int count = 0;
        foreach (var node in _nodes)
        {
            if (node.Action?.ExpressionTargetField == name) count++;
            else if (node.Condition?.ExpressionTargetField == name) count++;
        }
        return count;
    }

    /// <summary>Returns all alias bindings recorded against the named variable. Empty list if none.</summary>
    /// <summary>
    /// ⭐⭐⭐ <b><c>E4</c> — "does this asset maintain per-instance working state?"</b>
    ///
    /// <para>
    /// 📄 <c>DEBT-AIB-028</c>'s activation recipe names this method by name: <i>"a
    /// <c>BehaviorTreeAsset.HasAnyStatefulNode()</c> (any <c>ThreeParamReusableStateful</c> action) +
    /// HSM equivalent, wire <c>id => catalog.TryFind(id, out a) &amp;&amp; a.HasAnyStatefulNode()</c>
    /// through the production validator ctor."</i> ⛔ Not re-derived here — the recipe was already
    /// filed, and Batch 67's <c>W7c</c> boundary is what found it.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>TWO shapes count, not one.</b> The recipe names <c>ThreeParamReusableStateful</c>, but
    /// <c>AiPrimitiveTickCore</c> also carries a WorkingState — <c>BTreeBridgeEmitCore</c> emits a
    /// partition slot for <b>both</b> (<i>"both ride the partition-slot rail"</i>, I2/I3/E2). ⇒
    /// checking only the named one would call a composed blueprint action stateless and let two of
    /// them run concurrently unreported, which is the defect this predicate exists to prevent.
    /// </para>
    ///
    /// <para>
    /// 📌 <b><c>ThreeParamReusableStateful</c> has no NAMED member on the editor enum</b> — the DTO
    /// enum pins it to <c>2</c> and the editor casts numerically (see the note at the top of this
    /// file). Hence the explicit cast rather than a member reference: an invented member would be a
    /// second spelling of a value that already round-trips.
    /// </para>
    /// </summary>
    public bool HasAnyStatefulNode()
    {
        const BTreeActionDelegateShape ThreeParamReusableStateful = (BTreeActionDelegateShape)2;

        foreach (var node in _nodes)
        {
            var shape = node.Action?.DelegateShape ?? node.Condition?.DelegateShape;
            if (shape is null) continue;
            if (shape == ThreeParamReusableStateful
             || shape == BTreeActionDelegateShape.AiPrimitiveTickCore) return true;
        }
        return false;
    }

    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) =>
        _aliases.TryGetValue(variableName, out var list)
            ? list.AsReadOnly()
            : Array.Empty<BlackboardAliasBinding>();

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

    public bool IsConflictSuppressed(string variableName, string writerPairKey) =>
        _conflictSuppressions.Contains((variableName, writerPairKey));

    public void SetConflictSuppressed(string variableName, string writerPairKey, bool suppressed)
    {
        bool wasSuppressed = _conflictSuppressions.Contains((variableName, writerPairKey));
        if (wasSuppressed == suppressed) return;
        if (suppressed) _conflictSuppressions.Add((variableName, writerPairKey));
        else            _conflictSuppressions.Remove((variableName, writerPairKey));
        MarkDirty();
    }

    public bool IsUnusedWarningSuppressed(string variableName) =>
        _unusedSuppressions.Contains(variableName);

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

    // ---- IBTreeSyncableAsset -----------------------------------------------

    /// <summary>
    /// Returns subtree info for the node with the given visual ID, or null when the node
    /// does not exist or is not a Subtree node.
    /// </summary>
    public SubtreeNodeInfo? GetSubtreeNodeInfo(Guid nodeVisualId)
    {
        var node = FindNode(nodeVisualId);
        if (node is null || node.KernelType != NodeType.Subtree) return null;
        var payload = node.Subtree;
        if (payload is null) return null;
        return new SubtreeNodeInfo(payload.IsResolved, payload.SubtreeAssetId);
    }

    /// <summary>Returns the sync bindings for the given Subtree node. Empty list when none exist.</summary>
    public IReadOnlyList<SubtreeSyncBinding> GetSyncBindings(Guid nodeVisualId) =>
        _syncBindings.TryGetValue(nodeVisualId, out var list)
            ? list.AsReadOnly()
            : Array.Empty<SubtreeSyncBinding>();

    /// <summary>
    /// Upserts a sync binding for the given Subtree node.
    /// An existing binding with the same FieldName is replaced. Fires Changed.
    /// </summary>
    public void SetSyncBinding(Guid nodeVisualId, SubtreeSyncBinding binding)
    {
        if (!_syncBindings.TryGetValue(nodeVisualId, out var list))
        {
            list = new List<SubtreeSyncBinding>();
            _syncBindings[nodeVisualId] = list;
        }
        int idx = list.FindIndex(b => b.FieldName == binding.FieldName);
        if (idx >= 0)
            list[idx] = binding;
        else
            list.Add(binding);
        MarkDirty();
    }

    /// <summary>
    /// Removes all sync bindings for the given Subtree node.
    /// No-op when none exist. Fires Changed only when bindings were actually removed.
    /// </summary>
    public void ClearSyncBindings(Guid nodeVisualId)
    {
        if (!_syncBindings.TryGetValue(nodeVisualId, out var list) || list.Count == 0) return;
        _syncBindings.Remove(nodeVisualId);
        MarkDirty();
    }

    /// <summary>
    /// Returns all blackboard variables whose display type name equals typeName
    /// (exact match, case-sensitive via BlackboardTypeHelper.GetDisplayName).
    /// </summary>
    public IReadOnlyList<BlackboardVariableEntry> GetVariablesOfType(string typeName)
    {
        var result = new List<BlackboardVariableEntry>();
        foreach (var v in _blackboardVariables)
        {
            if (BlackboardTypeHelper.GetDisplayName(v.FieldType) == typeName)
                result.Add(v);
        }
        return result;
    }

    /// <summary>Records sub-tree identity metadata for a Subtree node.</summary>
    public void RecordSubtreeNodeMeta(Guid nodeVisualId, string subTreeName, string subDtoTypeName, string? subDtoTypeNs)
        => _syncNodeMeta[nodeVisualId] = (subTreeName, subDtoTypeName, subDtoTypeNs);

    /// <summary>
    /// Returns Approach B sync groups: subtree nodes with at least one active sync binding
    /// whose sub-tree identity has been recorded via RecordSubtreeNodeMeta.
    /// </summary>
    public IReadOnlyList<ApproachBSyncGroup> GetApproachBSyncGroups()
    {
        var result = new List<ApproachBSyncGroup>();
        foreach (var kv in _syncBindings)
        {
            var nodeId = kv.Key;
            var bindings = kv.Value;
            // Only include if at least one binding has active sync direction.
            bool anyActive = bindings.Any(b =>
                (b.SyncIn || b.SyncOut) && b.MasterVariableName != null);
            if (!anyActive) continue;
            // Only include if identity metadata has been recorded.
            if (!_syncNodeMeta.TryGetValue(nodeId, out var meta)) continue;
            result.Add(new ApproachBSyncGroup(
                nodeId,
                meta.SubtreeName,
                meta.SubDtoTypeName,
                meta.SubDtoTypeNs,
                bindings.AsReadOnly()));
        }
        return result;
    }

    /// <summary>
    /// Exposes _syncBindings for the emitter (read-only view).
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<SubtreeSyncBinding>> GetAllSyncBindings() =>
        _syncBindings.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<SubtreeSyncBinding>)kv.Value.AsReadOnly());

    /// <summary>
    /// Called by BehaviorTreeAssetProjector after applying node layout.
    /// </summary>
    public void LoadSyncBindings(IReadOnlyDictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>? bindings)
    {
        _syncBindings.Clear();
        if (bindings is null) return;
        foreach (var kv in bindings)
            _syncBindings[kv.Key] = new List<SubtreeSyncBinding>(kv.Value);
    }

    /// <summary>
    /// Returns auto-allocated blackboard variable entries for Approach B subtree nodes
    /// that are not covered by an Approach A alias.
    /// The caller adds these to the bin-packer's aggregated variable list.
    /// </summary>
    public IReadOnlyList<BlackboardVariableEntry> GetAutoAllocatedVariables()
    {
        var groups = GetApproachBSyncGroups();
        if (groups.Count == 0) return Array.Empty<BlackboardVariableEntry>();

        var result = new List<BlackboardVariableEntry>(groups.Count);
        foreach (var group in groups)
        {
            // Check if covered by Approach A: any master variable has an alias binding
            // whose RequiringElementId == group.NodeVisualId.
            bool coveredByA = _blackboardVariables.Any(v =>
                GetAliasesFor(v.Name).Any(a => a.RequiringElementId == group.NodeVisualId));
            if (coveredByA) continue;

            string fieldName = $"{group.SubtreeName}_{group.SubtreeDtoTypeName}";
            // Use object as a placeholder type -- the real type is known only at runtime.
            // DEBT: real type resolution requires catalog integration (deferred to a future batch).
            result.Add(new BlackboardVariableEntry(fieldName, typeof(object), Comment: null));
        }
        return result;
    }

    // ---- Canvas state ----
    public Vector2 CanvasPanOffset  { get; set; }
    public float   CanvasZoomLevel  { get; set; } = 1f;

    public BehaviorTreeAsset(
        Guid assetId,
        string name,
        string sourceFilePath,
        bool isEditorOwned,
        string blackboardTypeName,
        string contextTypeName,
        BehaviorTreeBlob blob,
        string targetNamespace = "")
    {
        AssetId              = assetId;
        Name                 = name;
        SourceFilePath       = sourceFilePath;
        IsEditorOwned        = isEditorOwned;
        BlackboardTypeName   = blackboardTypeName;
        ContextTypeName      = contextTypeName;
        Blob                 = blob;
        TargetNamespace      = targetNamespace;
    }

    /// <summary>
    /// Attaches a debug session so that <see cref="StitchKernelIndices"/> can re-wire
    /// symbolication without external injection at stitch time.
    /// Called by <see cref="BTree.Editor.Catalog.BTreeJsonAssetContributor"/> during load.
    /// </summary>
    internal void SetDebugSession(BTreeDebugSession? session)
        => _debugSession = session;

    // ---- IStitchableAsset ----

    /// <inheritdoc/>
    void IStitchableAsset.StitchRuntimeIndices(IEditableAsset? fresh)
        => StitchKernelIndices(fresh as BehaviorTreeAsset, _debugSession);

    // ---- Mutation helpers ----

    /// <summary>Marks the asset as dirty and fires the Changed event.</summary>
    public void MarkDirty()
    {
        _isDirty = true;
        Changed?.Invoke();
    }

    /// <summary>Clears the dirty flag after a successful save.</summary>
    public void ClearDirty() => _isDirty = false;

    // ---- BT-S1-03 lookups ----

    /// <summary>Returns the node with the given visual ID, or null if not found.</summary>
    public BTreeEditorNode? FindNode(Guid visualId) =>
        _visualIdToNode.TryGetValue(visualId, out var n) ? n : null;

    /// <summary>Returns the blob index for the given visual ID, or -1 if not found.</summary>
    public int FindBlobIndex(Guid visualId) =>
        _visualIdToBlobIndex.TryGetValue(visualId, out var i) ? i : -1;

    /// <summary>Returns the pill with the given visual ID, or null if not found.</summary>
    public BTreeEditorPill? FindPill(Guid visualId) =>
        _visualIdToPill.TryGetValue(visualId, out var p) ? p : null;

    // ---- Projection helpers (called by the projector; not public API) ----

    /// <summary>Replaces the full node+pill list and rebuilds all lookup tables.</summary>
    internal void ReplaceAll(
        List<BTreeEditorNode> nodes,
        List<BTreeEditorPill> pills,
        BehaviorTreeBlob newBlob)
    {
        Blob = newBlob;
        _nodes.Clear();
        _pills.Clear();
        _visualIdToBlobIndex.Clear();
        _visualIdToNode.Clear();
        _visualIdToPill.Clear();

        foreach (var node in nodes)
        {
            _nodes.Add(node);
            _visualIdToNode[node.VisualId] = node;
            if (node.KernelBlobIndex >= 0)
                _visualIdToBlobIndex[node.VisualId] = node.KernelBlobIndex;
        }
        foreach (var pill in pills)
        {
            _pills.Add(pill);
            _visualIdToPill[pill.VisualId] = pill;
        }
    }

    /// <summary>Adds a single node and updates lookup tables (used during authoring).</summary>
    internal void AddNode(BTreeEditorNode node)
    {
        _nodes.Add(node);
        _visualIdToNode[node.VisualId] = node;
        if (node.KernelBlobIndex >= 0)
            _visualIdToBlobIndex[node.VisualId] = node.KernelBlobIndex;
    }

    /// <summary>Adds a single pill and updates lookup tables.</summary>
    internal void AddPill(BTreeEditorPill pill)
    {
        _pills.Add(pill);
        _visualIdToPill[pill.VisualId] = pill;
    }

    /// <summary>Removes a node by visual ID. Returns false if not found.</summary>
    internal bool RemoveNode(Guid visualId)
    {
        if (!_visualIdToNode.TryGetValue(visualId, out var node)) return false;
        _nodes.Remove(node);
        _visualIdToNode.Remove(visualId);
        _visualIdToBlobIndex.Remove(visualId);
        return true;
    }

    /// <summary>Removes a pill by visual ID. Returns false if not found.</summary>
    internal bool RemovePill(Guid visualId)
    {
        if (!_visualIdToPill.TryGetValue(visualId, out var pill)) return false;
        _pills.Remove(pill);
        _visualIdToPill.Remove(visualId);
        return true;
    }

    // ── PU-302: Post-reload stitch ───────────────────────────────────────────

    /// <summary>
    /// Stitches runtime indices from a freshly assembly-projected asset (<paramref name="fresh"/>)
    /// onto this JSON-loaded editor model (design §6.6 / D13).
    /// <para>
    /// The JSON model is the authoritative source for topology and layout.
    /// For each editor node, the matching blob node is found in <paramref name="fresh"/> by
    /// <c>VisualId</c> via <see cref="NodeDebugMetadata.VisualId"/>, and its
    /// <c>KernelBlobIndex</c> is copied across.  The blob reference is updated to the
    /// recompiled blob from <paramref name="fresh"/>.
    /// </para>
    /// <para>
    /// Unmatched nodes (e.g. the assembly did not compile yet) are left visible with
    /// <c>KernelBlobIndex = -1</c> and a load diagnostic is set on the asset —
    /// the debug overlay is inert for those nodes until the blob catches up.
    /// </para>
    /// <para>
    /// <b>Must NOT call <see cref="MarkDirty"/> anywhere</b> — that would enqueue a stale
    /// emit/write until PU-602.
    /// </para>
    /// </summary>
    /// <param name="fresh">
    ///   The freshly assembly-projected <see cref="BehaviorTreeAsset"/>.
    ///   Must have the same <see cref="AssetId"/>.  May be null (compile failed) — in
    ///   that case all indices stay at sentinel and diagnostics are set.
    /// </param>
    /// <param name="debugSession">
    ///   Optional debug session to re-wire after index assignment.
    /// </param>
    public void StitchKernelIndices(BehaviorTreeAsset? fresh, BTreeDebugSession? debugSession = null)
    {
        if (fresh is null || fresh.AssetId != AssetId)
        {
            // Nothing to match against — reset all indices to sentinel and record diagnostic
            foreach (var node in _nodes)
                node.KernelBlobIndex = -1;
            _visualIdToBlobIndex.Clear();
            SetLoadDiagnostic(BlackboardLoadState.AssemblyFailed,
                "Assembly blob unavailable; runtime indices unset (debug overlay inert).");
            return;
        }

        // Build a map from VisualId string → blob index from the fresh blob's DebugMetadata
        var freshBlob = fresh.Blob;
        var visualIdToFreshIndex = new Dictionary<string, int>(
            freshBlob.DebugMetadata?.Length ?? 0,
            StringComparer.OrdinalIgnoreCase);

        if (freshBlob.DebugMetadata != null)
        {
            for (int i = 0; i < freshBlob.DebugMetadata.Length; i++)
            {
                var meta = freshBlob.DebugMetadata[i];
                if (!string.IsNullOrEmpty(meta.VisualId))
                    visualIdToFreshIndex[meta.VisualId] = i;
            }
        }

        bool anyUnmatched = false;
        foreach (var node in _nodes)
        {
            var visualIdStr = node.VisualId.ToString();
            if (visualIdToFreshIndex.TryGetValue(visualIdStr, out var idx))
            {
                node.KernelBlobIndex = idx;
                _visualIdToBlobIndex[node.VisualId] = idx;
            }
            else
            {
                node.KernelBlobIndex = -1;
                _visualIdToBlobIndex.Remove(node.VisualId);
                anyUnmatched = true;
            }
        }

        // Update the blob reference to the recompiled one
        Blob = freshBlob;

        if (anyUnmatched)
        {
            SetLoadDiagnostic(BlackboardLoadState.StructParseFailed,
                "One or more nodes have no blob match (compile incomplete); overlay partially inert.");
        }
        else
        {
            // Clear any stale diagnostic
            SetLoadDiagnostic(BlackboardLoadState.Clean, null);
        }

        // Re-wire the debug session so the overlay symbolication is up-to-date.
        debugSession?.SetDebugMetadata(freshBlob.DebugMetadata, AssetId);
        // Do NOT call MarkDirty — stitch is a reload-only operation (PU-602 constraint).
    }
}
