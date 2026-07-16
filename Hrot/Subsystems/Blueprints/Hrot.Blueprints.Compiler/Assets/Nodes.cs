using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Hrot.Blueprints.Core.Assets;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FunctionCallNode),        "FunctionCall")]
[JsonDerivedType(typeof(BranchNode),              "Branch")]
[JsonDerivedType(typeof(SequenceNode),            "Sequence")]
[JsonDerivedType(typeof(GetVariableNode),         "GetVariable")]
[JsonDerivedType(typeof(GetParameterNode),        "GetParameter")]
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
[JsonDerivedType(typeof(GetSharedNode),          "GetShared")]
[JsonDerivedType(typeof(SetSharedNode),          "SetShared")]
[JsonDerivedType(typeof(GetComponentNode),       "GetComponent")]
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

    /// <summary>
    /// P7.1 -- baked trailing engine-context decision (see <see cref="FunctionCallContextKind"/>).
    /// When non-<see cref="FunctionCallContextKind.Unspecified"/>, <c>Stage5_Schedule</c> honors
    /// this value DIRECTLY (no CLR reflection) to decide whether to append <c>self</c>/<c>view</c>
    /// as extra trailing arguments at the FunctionCall's emitted call site. This is what makes a
    /// hand-authored/editor-baked FunctionCall survive the real MSBuild build: the Roslyn source
    /// generator runs as a netstandard2.0 analyzer that cannot load arbitrary game assemblies
    /// (e.g. <c>Hrot.AI.Behaviors.dll</c>), so the original P7 reflection-based resolution
    /// (<c>Stage5_Schedule.ResolveClrMethodForContext</c>) always returns null there, silently
    /// dropping self/view and producing uncompilable C# (CS7036).
    /// <para>
    /// Left <see cref="FunctionCallContextKind.Unspecified"/> by default so existing/legacy nodes
    /// (including all in-process-authored P7 test fixtures, which build <c>FunctionCallNode</c>
    /// programmatically with empty <c>Pins</c>) fall back unchanged to the original
    /// CLR-reflection resolution path -- which works fine when the target type IS already loaded
    /// in-process (unit tests, <c>BlueprintTestFixture</c>'s dynamic Roslyn compile-and-load).
    /// </para>
    /// </summary>
    public FunctionCallContextKind TrailingContext { get; set; } = FunctionCallContextKind.Unspecified;
}

/// <summary>
/// P7.1 -- the baked trailing-context decision for a <see cref="FunctionCallNode"/>, recorded at
/// author time (editor bake / hand-authored asset) so <c>Stage5_Schedule</c> can honor it directly
/// at generation time without CLR reflection. <see cref="Unspecified"/> is an explicit "not baked"
/// sentinel, distinct from <see cref="None"/> (baked: no trailing context) -- it tells the compiler
/// to fall back to the legacy reflection-based resolution instead of trusting an absence of context.
/// </summary>
public enum FunctionCallContextKind { Unspecified = 0, None, Self, View, SelfAndView }

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

/// <summary>
/// Reads a declared AiPrimitive/Instance <b>Parameter</b> (the host-BTree/HSM data-IN contract)
/// into the graph. Pure-data node (data-out "Value", type from the referenced Parameter). Lowers
/// in Stage5 to the pre-existing <c>IrOp_ReadParam</c> (emits <c>p.{ParamName}</c>). Closes GAP-11:
/// before this node, graphs could only read Variables/WorkingState, forcing read-only inputs to be
/// mis-stashed in WorkingState.
/// </summary>
public sealed class GetParameterNode : Node
{
    /// <summary>Guid (string) of the Parameter to read, resolved against the asset's Parameters list.</summary>
    public string ParameterId { get; set; } = "";
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
    /// (e.g. AiPrimitive <c>BlueprintCall</c>) identified by its
    /// FQN (<c>"{Namespace}.{Type}.{Method}"</c>).
    /// When null/empty the node is a channel command (ChannelType + ActionId path; unchanged).
    /// Omitted from JSON when null to preserve byte-stability of existing channel-command assets.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ActionFqn { get; set; }

    /// <summary>
    /// AN8 — FQN of the action's parameter DTO type (e.g. <c>"Hrot.AI.Behaviors.Generated.MyAction_A1B2C3D4_Bp+Params"</c>).
    /// Set by the editor when baking a non-channel action node (ActionFqn non-null).
    /// Used by the compiler to emit <c>new global::{ParamsTypeFqn} { ... }</c> initialization.
    /// Omitted from JSON when null.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ActionParamsTypeFqn { get; set; }
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

// ──────────────────────────────────────────────────────────────────────────
// GetShared / SetShared (Slice 2a-2 -- entity-scoped Blueprint shared state)
// Compile to calls into Fdp.Toolkit.Blueprints.Partitioning.BlueprintSharedState
// (Slice 2a-1). Same-entity (self) only -- no target-Entity pin, no cross-entity
// (that is Slice 2b). No Scope field -- Entity scope is implied for 2a.
//
// Slice 2b adds an OPTIONAL "Target" data-in Entity pin to GetShared ONLY (see
// NodePinSchema.GetSharedPins / Stage0_Rehydrate.EnrichGetSharedPins). When wired, the graph
// author supplies a target Entity (any Entity-valued pin) instead of self, so a member entity
// can read a coordinator entity's Entity-scoped shared slot directly (≤1-frame staleness,
// TryGetShared -> false when the target hasn't provisioned yet -- never throws). SetShared
// remains self-only by construction -- cross-entity WRITE is a separate future slice (a
// deferred-event bus), not built here.
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Reads the ENTITY-scoped shared working-state slot named <see cref="VariableId"/> off
/// <c>self</c> (or off an explicit target Entity -- Slice 2b, see "Target" pin), via
/// <c>BlueprintSharedState.TryGetShared&lt;SharedTypeId&gt;</c>. Pure-data node (no exec pins):
/// OPTIONAL data-in "Target" (<c>Fdp.Core.Entity</c> -- unwired = self, byte-identical to Slice
/// 2a-2), data-out "Value" (typed by <see cref="SharedTypeId"/>) + data-out "Found"
/// (<c>System.Boolean</c>).
/// </summary>
public sealed class GetSharedNode : Node
{
    /// <summary>Entity-scoped slot name (matches the manifest-provisioned variable name).</summary>
    public string VariableId { get; set; } = "";

    /// <summary>
    /// FQN of the standalone Category-1 shared struct (a hand-written blittable struct, NOT a
    /// generated <c>_Bp+WorkingState</c>). Used to type the "Value" pin directly and as the
    /// generic argument of <c>BlueprintSharedState.TryGetShared&lt;T&gt;</c>.
    /// </summary>
    public string SharedTypeId { get; set; } = "";
}

/// <summary>
/// Writes <c>Value</c> into the ENTITY-scoped shared working-state slot named
/// <see cref="VariableId"/> on <c>self</c>, via <c>BlueprintSharedState.TrySetShared&lt;SharedTypeId&gt;</c>.
/// Exec node: exec-In + exec-Out, data-in "Value" (typed by <see cref="SharedTypeId"/>), plus an
/// optional data-out "Written" (<c>System.Boolean</c>).
/// </summary>
public sealed class SetSharedNode : Node
{
    /// <summary>Entity-scoped slot name (matches the manifest-provisioned variable name).</summary>
    public string VariableId { get; set; } = "";

    /// <summary>
    /// FQN of the standalone Category-1 shared struct (a hand-written blittable struct, NOT a
    /// generated <c>_Bp+WorkingState</c>). Used to type the "Value" pin directly and as the
    /// generic argument of <c>BlueprintSharedState.TrySetShared&lt;T&gt;</c>.
    /// </summary>
    public string SharedTypeId { get; set; } = "";
}

// ──────────────────────────────────────────────────────────────────────────
// GetComponent (Hill-attack -> Blueprints migration P2 -- reads an ECS component field)
//
// Reflection-free by construction: ComponentTypeFqn/FieldName/FieldTypeFqn are baked strings
// authored at edit time (mirrors GetShared/SetShared's SharedTypeId and the P7.1
// FunctionCallNode.TrailingContext bake -- see that type's doc comment for why the Roslyn
// incremental generator, running as a netstandard2.0 analyzer, can never load game assemblies
// to inspect a real CLR type). Lowers in Stage5_Schedule to the SAME three existing IR ops
// WaitLowering_AiPrimitive's channel-check block already chains (IrOp_Self ->
// IrOp_GetComponentRO -> IrOp_FieldRead): no new IR op, no new StatementEmitter case.
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Reads a field off an ECS component on <c>self</c> (or an explicit target Entity -- OPTIONAL
/// "Target" data-in pin, cross-entity read, mirrors <see cref="GetSharedNode"/>'s Slice 2b
/// "Target" pin; unwired = self). Pure-data node (no exec pins): OPTIONAL data-in "Target"
/// (<c>Fdp.Core.Entity</c>), data-out "Value" (typed by <see cref="FieldTypeFqn"/> when set,
/// else the Stage4-resolved pin type). Compiles to
/// <c>{world}.GetComponentRO&lt;global::ComponentTypeFqn&gt;(entity).FieldName</c> -- see
/// <c>Stage5_Schedule</c>'s <c>GetComponentNode</c> case and <c>StatementEmitter</c>'s existing
/// <c>IrOp_GetComponentRO</c>/<c>IrOp_FieldRead</c> cases (both pre-existing, unmodified).
/// </summary>
public sealed class GetComponentNode : Node
{
    /// <summary>FQN of the ECS component struct to read (e.g. "Fdp.Toolkit.Navigation.NavigationStatus"). Emitted as GetComponentRO&lt;global::FQN&gt;. Baked string -- no reflection.</summary>
    public string ComponentTypeFqn { get; set; } = "";
    /// <summary>Name of the field/property to read off the component (e.g. "Result"). Emitted textually as .FieldName.</summary>
    public string FieldName { get; set; } = "";
    /// <summary>FQN of the read field's type (e.g. "Fdp.Toolkit.Navigation.NavigationResult"). Used only to build the result IrTypeRef locally; optional (falls back to the resolved out-pin type / UnknownType).</summary>
    public string FieldTypeFqn { get; set; } = "";
}
