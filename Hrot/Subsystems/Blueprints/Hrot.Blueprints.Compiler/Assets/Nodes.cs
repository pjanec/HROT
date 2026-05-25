using System.Text.Json.Serialization;
#if NET8_0_OR_GREATER
using Fdp.Toolkit.ReplayBrowser.Search;
#endif

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
public abstract class Node
{
    public Guid Id { get; set; }
    public List<Pin> Pins { get; set; } = new();
    public NodeMetadata EditorMetadata { get; set; } = new();
}

public sealed class FunctionCallNode : Node
{
    public string TargetTypeId { get; set; } = "";
    public string MethodName { get; set; } = "";
    public bool IsPure { get; set; }
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
#if NET8_0_OR_GREATER
    public SearchPredicateDto? Condition { get; set; }
#else
    public object? Condition { get; set; }
#endif
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
