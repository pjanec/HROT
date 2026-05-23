using System;
using System.Collections.Generic;
using System.Numerics;
using Fbt;
using Hrot.Editor.AiShared;

namespace Hrot.BTree.Editor.Model;

// ── Payload types ─────────────────────────────────────────────────────────────

/// <summary>Describes which delegate overload an Action or Condition node uses.</summary>
public enum BTreeActionDelegateShape
{
    /// <summary>Three-parameter reusable delegate with an expression-target field selector.</summary>
    ThreeParamReusable,
    /// <summary>Four-parameter delegate with full blackboard access.</summary>
    FourParamFull,
}

/// <summary>Payload for Action leaf nodes.</summary>
public sealed class BTreeActionPayload
{
    /// <summary>Fully-qualified method name, e.g. "Hrot.Game.Combat.CombatActions.AimAndFire".</summary>
    public string MethodFqn = string.Empty;
    /// <summary>Blackboard field referenced by the expression target (null when not using ThreeParamReusable).</summary>
    public string? ExpressionTargetField;
    public BTreeActionDelegateShape DelegateShape;
}

/// <summary>Payload for Condition leaf nodes.</summary>
public sealed class BTreeConditionPayload
{
    public string MethodFqn = string.Empty;
    public string? ExpressionTargetField;
    public BTreeActionDelegateShape DelegateShape;
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
public sealed class BehaviorTreeAsset : IEditableAsset
{
    private bool _isDirty;
    private readonly List<BTreeEditorNode> _nodes = new();
    private readonly List<BTreeEditorPill> _pills  = new();

    // ---- BT-S1-03: lookup tables ----
    private readonly Dictionary<Guid, int>              _visualIdToBlobIndex = new();
    private readonly Dictionary<Guid, BTreeEditorNode>  _visualIdToNode      = new();
    private readonly Dictionary<Guid, BTreeEditorPill>  _visualIdToPill      = new();

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
    public BehaviorTreeBlob Blob { get; private set; }

    // ---- Editor collections (read-only views) ----
    public IReadOnlyList<BTreeEditorNode> Nodes => _nodes;
    public IReadOnlyList<BTreeEditorPill> Pills => _pills;

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
        BehaviorTreeBlob blob)
    {
        AssetId              = assetId;
        Name                 = name;
        SourceFilePath       = sourceFilePath;
        IsEditorOwned        = isEditorOwned;
        BlackboardTypeName   = blackboardTypeName;
        ContextTypeName      = contextTypeName;
        Blob                 = blob;
    }

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
}
