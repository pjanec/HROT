using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Hrot.AiEditor.Persistence;

namespace Hrot.AiEditor.Persistence.BTree;

// ── Design §5.2/§5.3: BTree persisted DTO ────────────────────────────────────
// Runtime-only fields EXCLUDED per §5.2:
//   Blob / Metadata, KernelBlobIndex, derived *PinId, _syncNodeMeta,
//   _aliases runtime hydration, LoadDiagnosticMessage, IsDirty, Changed, IsBreakpoint.

// ── Blackboard block (§5.4) ───────────────────────────────────────────────────

/// <summary>Array- and default-capable type reference (§5.4).</summary>
public sealed class BlackboardTypeRefDto
{
    /// <summary>CLR type full name, e.g. "System.Int32".</summary>
    public string TypeId { get; set; } = string.Empty;
    /// <summary>True when this variable is a fixed-length inline array.</summary>
    public bool IsArray { get; set; }
    /// <summary>Array element count when IsArray is true; null otherwise.</summary>
    public int? FixedLength { get; set; }
}

/// <summary>One blackboard variable entry (§5.4).</summary>
public sealed class BlackboardVariableDto
{
    public string Name { get; set; } = string.Empty;
    public BlackboardTypeRefDto Type { get; set; } = new();
    /// <summary>JSON-encoded default value; null = no default authored (omitted from JSON for byte-stability).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultValueJson { get; set; }
    public string? Comment { get; set; }
    /// <summary>
    /// True when this variable was auto-created by the "Promote to new variable" feature.
    /// Omitted from JSON when false (default) for backwards compatibility.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAutoManaged { get; set; }
    /// <summary>
    /// Authoring role: Input (default) or State. Omitted from JSON when Input (default).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public BlackboardVariableRole Role { get; set; }
    /// <summary>
    /// Working-state scope (only meaningful when Role == State). Omitted from JSON when Node (default).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WorkingStateScope Scope { get; set; }
}

/// <summary>Forward-compatible blackboard block (§5.4).</summary>
public sealed class BlackboardBlockDto
{
    /// <summary>False = Category-1 (reflect hand-written struct, read-only); true = Category-2 (editor-owned).</summary>
    public bool Managed { get; set; }
    /// <summary>Category-1: referenced struct type name; Category-2: generated struct name.</summary>
    public string TypeName { get; set; } = string.Empty;
    /// <summary>Non-null when a Blackboard1024 heavy tier is used.</summary>
    public string? HeavyDtoType { get; set; }
    /// <summary>Variable schema (round-trip only in this thread; authoring in Slice 1.5).</summary>
    public List<BlackboardVariableDto> Variables { get; set; } = new();
}

// ── Canvas layout ─────────────────────────────────────────────────────────────

/// <summary>Canvas pan and zoom state (§5.3).</summary>
public sealed class CanvasDto
{
    public float PanX { get; set; }
    public float PanY { get; set; }
    public float Zoom { get; set; } = 1.0f;
}

// ── Suppression sets (§5.2: "alias relationships, conflict/unused suppressions") ──

public sealed class ConflictSuppressionDto
{
    public string VariableName { get; set; } = string.Empty;
    public string WriterPairKey { get; set; } = string.Empty;
}

public sealed class SuppressionsDto
{
    public List<ConflictSuppressionDto> Conflict { get; set; } = new();
    public List<string> Unused { get; set; } = new();
}

// ── Subtree sync bindings (§5.2, §5.3) ───────────────────────────────────────

public sealed class SubtreeSyncBindingDto
{
    public string FieldName { get; set; } = string.Empty;
    public string? MasterVariableName { get; set; }
    public bool SyncIn { get; set; }
    public bool SyncOut { get; set; }
}

// ── EditorMetadata (§5.1 recommendation: Blueprint-style X/Y) ────────────────

/// <summary>
/// A single waypoint coordinate for a BTree link. Uses properties (not fields) so
/// System.Text.Json round-trips correctly without IncludeFields.
/// Named BTreeWaypointDto to avoid collision with Hrot.AiEditor.Persistence.Hsm.WaypointDto.
/// </summary>
public sealed class BTreeWaypointDto
{
    public float X { get; set; }
    public float Y { get; set; }
}

/// <summary>Per-node layout metadata. Uses X/Y floats per §5.1 recommendation.</summary>
public sealed class NodeEditorMetadataDto
{
    public float X { get; set; }
    public float Y { get; set; }
    public string? Comment { get; set; }
    public bool Collapsed { get; set; }
    public string? Color { get; set; }

    /// <summary>
    /// Waypoints for the edge from this node up to its parent.
    /// Null/empty when no reroute points exist — omitted from JSON when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BTreeWaypointDto>? Waypoints { get; set; }
}

// ── Payload types ─────────────────────────────────────────────────────────────

/// <summary>Delegate shape for Action/Condition nodes. Matches BTreeActionDelegateShape in the editor.</summary>
public enum BTreeDelegateShapeDto
{
    ThreeParamReusable,
    FourParamFull,
    /// <summary>
    /// S2-1: stateful 4-param shape: (ref TParams, ref TWorkingState, ref BehaviorTreeState, ref BTreeContext).
    /// The WorkingState is projected from the entity's active BlueprintBlackboard* partition slot,
    /// keyed by FNV-1a-32(assetGuid, nodeVisualId).
    /// </summary>
    ThreeParamReusableStateful = 2,
}

public sealed class BTreeActionPayloadDto
{
    public string MethodFqn { get; set; } = string.Empty;
    public string? ExpressionTargetField { get; set; }
    public BTreeDelegateShapeDto DelegateShape { get; set; }
    /// <summary>
    /// S2-1: for <see cref="BTreeDelegateShapeDto.ThreeParamReusableStateful"/> bindings,
    /// the CLR FQN of the WorkingState struct (second ref param after TParams).
    /// E.g. "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorState".
    /// Null/omitted for stateless shapes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingStateTypeId { get; set; }

    /// <summary>
    /// S3-G: for stateful bindings whose <b>working-state</b> variable is distinct from the
    /// param variable (<see cref="ExpressionTargetField"/>), the Name of the authored
    /// working-state blackboard variable. Its declared <c>Role</c>/<c>Scope</c> drive the
    /// slot key + provisioning scope (a shared Behavior/Entity variable lives here, not in
    /// the param variable). When null/omitted, scope resolution falls back to
    /// <see cref="ExpressionTargetField"/> (back-compat: Slice-2 assets and tests where the
    /// bound variable IS the stateful one stay byte-identical).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingStateTargetField { get; set; }
}

public sealed class BTreeConditionPayloadDto
{
    public string MethodFqn { get; set; } = string.Empty;
    public string? ExpressionTargetField { get; set; }
    public BTreeDelegateShapeDto DelegateShape { get; set; }
}

public sealed class BTreeWaitPayloadDto
{
    public float Duration { get; set; }
}

public sealed class BTreeSubtreePayloadDto
{
    public Guid SubtreeAssetId { get; set; }
    public string SubtreeName { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
}

// ── Polymorphic node types (§5.3 "[JsonPolymorphic kind]") ───────────────────

/// <summary>Base class for all persisted BTree node DTOs. Discriminated by "kind".</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BTreeRootNodeDto),        "Root")]
[JsonDerivedType(typeof(BTreeSequenceNodeDto),    "Sequence")]
[JsonDerivedType(typeof(BTreeSelectorNodeDto),    "Selector")]
[JsonDerivedType(typeof(BTreeParallelNodeDto),    "Parallel")]
[JsonDerivedType(typeof(BTreeActionNodeDto),      "Action")]
[JsonDerivedType(typeof(BTreeConditionNodeDto),   "Condition")]
[JsonDerivedType(typeof(BTreeWaitNodeDto),        "Wait")]
[JsonDerivedType(typeof(BTreeSubtreeNodeDto),     "Subtree")]
[JsonDerivedType(typeof(BTreeInverterNodeDto),    "Inverter")]
[JsonDerivedType(typeof(BTreeRepeaterNodeDto),    "Repeater")]
[JsonDerivedType(typeof(BTreeCooldownNodeDto),    "Cooldown")]
[JsonDerivedType(typeof(BTreeForceSuccessNodeDto),"ForceSuccess")]
[JsonDerivedType(typeof(BTreeForceFailureNodeDto),"ForceFailure")]
[JsonDerivedType(typeof(BTreeUntilSuccessNodeDto),     "UntilSuccess")]
[JsonDerivedType(typeof(BTreeUntilFailureNodeDto),     "UntilFailure")]
[JsonDerivedType(typeof(BTreeObserverSelectorNodeDto), "ObserverSelector")]
[JsonDerivedType(typeof(BTreeServiceNodeDto),          "Service")]
[JsonDerivedType(typeof(BTreeObserverNodeDto),         "Observer")]
public abstract class BTreeNodeDto
{
    public Guid VisualId { get; set; }
    public List<Guid> ChildVisualIds { get; set; } = new();
    public string DisplayLabel { get; set; } = string.Empty;
    public NodeEditorMetadataDto EditorMetadata { get; set; } = new();
}

public sealed class BTreeRootNodeDto        : BTreeNodeDto { }
public sealed class BTreeSequenceNodeDto    : BTreeNodeDto { }
public sealed class BTreeSelectorNodeDto    : BTreeNodeDto { }
public sealed class BTreeParallelNodeDto    : BTreeNodeDto { }
public sealed class BTreeInverterNodeDto    : BTreeNodeDto { }
public sealed class BTreeForceSuccessNodeDto: BTreeNodeDto { }
public sealed class BTreeForceFailureNodeDto: BTreeNodeDto { }
public sealed class BTreeUntilSuccessNodeDto     : BTreeNodeDto { }
public sealed class BTreeUntilFailureNodeDto     : BTreeNodeDto { }
public sealed class BTreeObserverSelectorNodeDto : BTreeNodeDto { }
public sealed class BTreeServiceNodeDto          : BTreeNodeDto { }
public sealed class BTreeObserverNodeDto         : BTreeNodeDto { }

public sealed class BTreeRepeaterNodeDto : BTreeNodeDto
{
    /// <summary>Repeat count (from the Repeater kernel node's IntParam).</summary>
    public int? IntParam { get; set; }
}

public sealed class BTreeCooldownNodeDto : BTreeNodeDto
{
    /// <summary>Cooldown duration in seconds.</summary>
    public float? FloatParam { get; set; }
}

public sealed class BTreeActionNodeDto : BTreeNodeDto
{
    public BTreeActionPayloadDto? Action { get; set; }
}

public sealed class BTreeConditionNodeDto : BTreeNodeDto
{
    public BTreeConditionPayloadDto? Condition { get; set; }
}

public sealed class BTreeWaitNodeDto : BTreeNodeDto
{
    public BTreeWaitPayloadDto? Wait { get; set; }
}

public sealed class BTreeSubtreeNodeDto : BTreeNodeDto
{
    public BTreeSubtreePayloadDto? Subtree { get; set; }
}

// ── Decorator pill (§5.3 "Pills") ────────────────────────────────────────────

/// <summary>Decorator pill attached to a host node.</summary>
public sealed class BTreePillDto
{
    public Guid VisualId { get; set; }
    public Guid HostNodeVisualId { get; set; }
    /// <summary>Decorator kind name matching Fbt.NodeType enum names.</summary>
    public string DecoratorType { get; set; } = string.Empty;
    public int? IntParam { get; set; }
    public float? FloatParam { get; set; }
    public string? Comment { get; set; }
    public int StackIndex { get; set; }
}

// ── Root DTO ──────────────────────────────────────────────────────────────────

/// <summary>
/// Persisted representation of a BTree asset. Serialized to *.btree.json.
/// Design §5.2/§5.3.
/// Runtime-only fields excluded: Blob/Metadata, KernelBlobIndex, *PinId,
/// _syncNodeMeta, LoadDiagnosticMessage, IsDirty, Changed, IsBreakpoint.
/// </summary>
public sealed class BehaviorTreeAssetDto
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public Guid AssetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TargetNamespace { get; set; } = string.Empty;
    public string BlackboardTypeName { get; set; } = string.Empty;
    public string ContextTypeName { get; set; } = string.Empty;

    // ── Topology ──────────────────────────────────────────────────────────────
    public List<BTreeNodeDto> Nodes { get; set; } = new();
    public List<BTreePillDto> Pills { get; set; } = new();

    // ── Canvas layout ─────────────────────────────────────────────────────────
    public CanvasDto Canvas { get; set; } = new();

    // ── Subtree sync bindings (§5.2) ──────────────────────────────────────────
    /// <summary>Key = SubtreeNode VisualId (as string), value = bindings list.</summary>
    public Dictionary<string, List<SubtreeSyncBindingDto>> SubtreeSyncBindings { get; set; } = new();

    // ── Suppressions (§5.2) ───────────────────────────────────────────────────
    public SuppressionsDto Suppressions { get; set; } = new();

    // ── Blackboard (§5.4) ─────────────────────────────────────────────────────
    public BlackboardBlockDto Blackboard { get; set; } = new();
}
