using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Hrot.Blueprints.Core.Assets;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FunctionCallNode),        "FunctionCall")]
[JsonDerivedType(typeof(BranchNode),              "Branch")]
[JsonDerivedType(typeof(SequenceNode),            "Sequence")]
[JsonDerivedType(typeof(GetVariableNode),         "GetVariable")]
[JsonDerivedType(typeof(SetVariableNode),         "SetVariable")]
[JsonDerivedType(typeof(LiteralNode),             "Literal")]
[JsonDerivedType(typeof(EventEntryNode),          "EventEntry")]
[JsonDerivedType(typeof(ReturnNode),              "Return")]
[JsonDerivedType(typeof(CastNode),                "Cast")]
[JsonDerivedType(typeof(ArrayMakeNode),           "ArrayMake")]
[JsonDerivedType(typeof(ArrayGetNode),            "ArrayGet")]
[JsonDerivedType(typeof(LatentDelayNode),         "Delay")]
[JsonDerivedType(typeof(CallEventDispatcherNode), "CallDispatcher")]
[JsonDerivedType(typeof(BindEventDispatcherNode), "BindDispatcher")]
[JsonDerivedType(typeof(CallCustomEventNode),     "CallCustomEvent")]
[JsonDerivedType(typeof(CallPeerBlueprintNode),   "CallPeerBlueprint")]
[JsonDerivedType(typeof(ChannelCommandNode),      "ChannelCommand")]
[JsonDerivedType(typeof(WaitForChannelNode),      "WaitForChannel")]
[JsonDerivedType(typeof(WaitForEventNode),        "WaitForEvent")]
[JsonDerivedType(typeof(WhenNode),           "When")]           // NEW
[JsonDerivedType(typeof(ReadEqsResultNode),  "ReadEqsResult")]  // NEW
[JsonDerivedType(typeof(SpawnEqsSensorNode), "SpawnEqsSensor")] // NEW
[JsonDerivedType(typeof(ScoreDecisionNode),    "ScoreDecision")]
[JsonDerivedType(typeof(ReadRankedResultNode), "ReadRankedResult")]
[JsonDerivedType(typeof(PartitionElementsNode), "PartitionElements")]
[JsonDerivedType(typeof(AssignRolesNode),        "AssignRoles")]
[JsonDerivedType(typeof(AdvancePhaseNode),       "AdvancePhase")]
[JsonDerivedType(typeof(AcquireSlotNode),        "AcquireSlot")]
public abstract class Node
{
    public Guid Id { get; set; }
    public List<Pin> Pins { get; set; } = new();
    public NodeMetadata EditorMetadata { get; set; } = new();

    /// <summary>
    /// Persisted map of pin-name → default-value-string for input data pins.
    /// Survives save/load even when <see cref="Pins"/> is serialized as <c>[]</c>
    /// (projection-only).  Null (and omitted from JSON) when no defaults have been set.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? PinDefaults { get; set; }
}

public sealed class FunctionCallNode : Node
{
    public string TargetTypeId { get; set; } = "";
    public string MethodName { get; set; } = "";
    public bool IsPure { get; set; }
    /// <summary>
    /// When non-empty, this is the GUID string of an in-blueprint Function graph to call.
    /// Empty (default) = existing CLR library call (unchanged behaviour).
    /// </summary>
    public string TargetGraphId { get; set; } = "";
}

public sealed class BranchNode : Node { }

public sealed class SequenceNode : Node { }

public sealed class GetVariableNode : Node
{
    public string VariableId { get; set; } = "";
}

public sealed class SetVariableNode : Node
{
    public string VariableId { get; set; } = "";
}

public sealed class LiteralNode : Node
{
    public string TypeId { get; set; } = "";
    public string ValueJson { get; set; } = "";
}

public sealed class EventEntryNode : Node
{
    public string EventTypeId { get; set; } = "";
}

public sealed class ReturnNode : Node
{
    public NodeStatus Status { get; set; } = NodeStatus.Success;
}

public sealed class CastNode : Node
{
    public string TargetTypeId { get; set; } = "";
}

public sealed class ArrayMakeNode : Node
{
    public string ElementTypeId { get; set; } = "";
}

public sealed class ArrayGetNode : Node { }

public sealed class LatentDelayNode : Node { }

public sealed class CallEventDispatcherNode : Node
{
    public string DispatcherId { get; set; } = "";
}

public sealed class BindEventDispatcherNode : Node
{
    public string DispatcherId { get; set; } = "";
}

public sealed class CallCustomEventNode : Node
{
    public string EventId { get; set; } = "";
}

public sealed class CallPeerBlueprintNode : Node
{
    public string PeerBlueprintId { get; set; } = "";
    public string FunctionRef { get; set; } = "";
}

public sealed class ChannelCommandNode : Node
{
    public string ChannelType { get; set; } = "";
    public string ActionId { get; set; } = "";

    /// <summary>
    /// AN7 — non-channel action identity.
    /// When non-null and non-empty, this node represents a non-channel behavior action
    /// (e.g. <c>[SharedAiAction]</c> / AiPrimitive <c>BlueprintCall</c>) identified by its
    /// FQN (<c>"{Namespace}.{Type}.{Method}"</c>).
    /// When null/empty the node is a channel command (ChannelType + ActionId path; unchanged).
    /// Omitted from JSON when null to preserve byte-stability of existing channel-command assets.
    /// Compiler lowering of non-channel nodes is deferred to AN8.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ActionFqn { get; set; }
}

public sealed class WaitForChannelNode : Node
{
    public string ChannelType { get; set; } = "";
}

public sealed class WaitForEventNode : Node
{
    public string EventTypeId { get; set; } = "";
    public string? FilterByField { get; set; }
    public string? CorrelationField { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────
// WhenNode and supporting types (DESIGN §2.3)
// ──────────────────────────────────────────────────────────────────────────

public sealed class WhenNode : Node
{
    public WhenMode Mode { get; set; }
    public WhenEdge Edges { get; set; } = WhenEdge.RisingEdge;

    public ValueChangedPayload? ValueChanged { get; set; }
    public EventFiredPayload? EventFired { get; set; }
    public ConditionMetPayload? ConditionMet { get; set; }
    public EqsResultPayload? EqsResult { get; set; }
}

public enum WhenMode { ValueChanged, EventFired, ConditionMet, EqsResult }

[Flags]
public enum WhenEdge { None = 0, RisingEdge = 1, FallingEdge = 2 }

public sealed class ValueChangedPayload
{
    public string ComponentTypeId { get; set; } = "";
    public string PropertyPath { get; set; } = "";
    public double Epsilon { get; set; }
    public ValueChangedSource Source { get; set; }
    public Guid? PeerBlueprintAssetId { get; set; }
    public string? PeerVariableName { get; set; }
    public string? WorkingStateFieldId { get; set; }
}

public enum ValueChangedSource { SelfComponent, PeerBlueprintVariable, WorkingStateField }

public sealed class EventFiredPayload
{
    public string EventTypeId { get; set; } = "";
    public EventTargetFilter TargetFilter { get; set; } = EventTargetFilter.Self;
    public string? TargetFieldName { get; set; }
    public PayloadCondition? PayloadCheck { get; set; }
}

public enum EventTargetFilter { None, Self }

public sealed class PayloadCondition
{
    public string PropertyPath { get; set; } = "";
    public ComparisonOperator Operator { get; set; }
    public string TargetValueText { get; set; } = "";
}

/// <summary>Comparison operator for payload condition checks in WhenNode.EventFired mode.</summary>
public enum ComparisonOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

public sealed class ConditionMetPayload
{
    /// <summary>
    /// The predicate tree serialized as a raw JSON node.
    /// Stored as <see cref="JsonNode"/> so deserialization never requires
    /// Fdp.Toolkits to be loaded (e.g. in the netstandard2.0 analyzer host).
    /// The net8 editor converts to/from SearchPredicateDto at its own boundary.
    /// </summary>
    public JsonNode? Condition { get; set; }
}

public sealed class EqsResultPayload
{
    public string SensorVariableName { get; set; } = "";
    public EqsTrigger Trigger { get; set; }
    public float ScoreThreshold { get; set; }
    public float MaxAgeSeconds { get; set; }
}

public enum EqsTrigger { FirstReady, TopChanged, ScoreCrossed, BecomesStale }

// ──────────────────────────────────────────────────────────────────────────
// ReadEqsResultNode (DESIGN §2.4)
// ──────────────────────────────────────────────────────────────────────────

public sealed class ReadEqsResultNode : Node
{
    public string SensorVariableName { get; set; } = "";
}

// ──────────────────────────────────────────────────────────────────────────
// SpawnEqsSensorNode (DESIGN §2.5)
// ──────────────────────────────────────────────────────────────────────────

public sealed class SpawnEqsSensorNode : Node
{
    /// <summary>
    /// The chosen EQS template's stable identifier (the AssetId from the template's
    /// [EqsTemplate(AssetId = "...")] declaration). At lowering time this resolves
    /// to the BlueprintId stored in the spawned EqsSensor component.
    /// </summary>
    public Guid TemplateAssetId { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────
// ScoreDecisionNode (DESIGN §7.3 -- runs a UtilityDecisionDef, outputs WinningOptionId)
// ──────────────────────────────────────────────────────────────────────────

public sealed class ScoreDecisionNode : Node
{
    /// <summary>
    /// The GUID string of the UtilityDecisionDef asset to evaluate (e.g.
    /// "3c6f9e42-5d10-6f3a-ac23-posture0000001" for CombatPostureDecision).
    /// </summary>
    public string AssetId { get; set; } = string.Empty;
}

// ──────────────────────────────────────────────────────────────────────────
// ReadRankedResultNode (DESIGN §7.3 -- reads rank-i entry from UtilityResultBuffer)
// ──────────────────────────────────────────────────────────────────────────

public sealed class ReadRankedResultNode : Node
{
    /// <summary>0-based rank index (0 = top-ranked).</summary>
    public int Rank { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────
// Squad Primitive Nodes (TASK-SQD-P6-02 -- Blueprint host for squad logic)
// These nodes wrap the five squad coordination primitives (Phase 1 library).
// The node carries only authoring-time configuration; execution is delegated
// to the corresponding FDP primitive at IR stage.
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Partition squad members into N elements (wraps ElementPartitionPrimitive.Partition).
/// </summary>
public sealed class PartitionElementsNode : Node
{
    /// <summary>Number of elements to partition into (e.g. 2 for Lead/Overwatch).</summary>
    public int ElementCount { get; set; } = 2;
}

/// <summary>
/// Assign roles to squad members via greedy matrix (wraps RoleSlotAssignmentPrimitive.AssignRoles).
/// </summary>
public sealed class AssignRolesNode : Node
{
    /// <summary>The ManeuverKind whose StandardCandidates table to use (e.g. 2 for BoundingOverwatch).</summary>
    public ushort ManeuverKind { get; set; }
}

/// <summary>
/// Advance the phase sequencer one step (wraps PhaseSequencer.Advance).
/// </summary>
public sealed class AdvancePhaseNode : Node
{
    /// <summary>Phase ID to jump to if dwell timeout elapses. Use the terminal Aborted phase.</summary>
    public ushort AbortPhaseId { get; set; }
    /// <summary>Dwell timeout in simulation ticks (0 = never timeout).</summary>
    public uint DwellTimeoutTicks { get; set; }
}

/// <summary>
/// Acquire the next available slot from the slot rotation ring (wraps SlotRotation.AcquireSlot).
/// </summary>
public sealed class AcquireSlotNode : Node
{
    /// <summary>Total number of slots in the ring.</summary>
    public int TotalSlots { get; set; } = 1;
}
