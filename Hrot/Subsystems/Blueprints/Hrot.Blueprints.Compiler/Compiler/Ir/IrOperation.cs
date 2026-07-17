namespace Hrot.Blueprints.Core.Compiler.Ir;

public abstract record IrOperation;

// Constants and references
public sealed record IrOp_Const(string CSharpLiteral, IrTypeRef Type) : IrOperation;
public sealed record IrOp_ReadParam(int ParamIndex) : IrOperation;
public sealed record IrOp_ReadVariable(int VariableIndex) : IrOperation;
public sealed record IrOp_WriteVariable(int VariableIndex, IrValue Value) : IrOperation;
public sealed record IrOp_ReadInputArg(int ArgIndex) : IrOperation;
public sealed record IrOp_Self : IrOperation;
public sealed record IrOp_Time : IrOperation;
public sealed record IrOp_DeltaTime : IrOperation;

// Read instance version (Q-18.1 addition)
public sealed record IrOp_ReadInstanceVersion : IrOperation;

// Pure-function calls (math, logical, type coercion)
/// <param name="AppendSelfArg">
/// P7 -- true when the target CLR method's parameter list ends with a recognized trailing
/// `Entity self` context parameter that was OMITTED from the node's data-IN pins (and therefore
/// from <paramref name="Args"/>). Stage 7 (<see cref="Emit.StatementEmitter"/>) appends the
/// in-scope `self` identifier to the emitted call. Always false for calls that don't match the
/// P7 trailing-context convention -- existing calls are byte-identical.
/// </param>
/// <param name="AppendViewArg">
/// P7 -- true when the target CLR method's parameter list ends with a recognized trailing
/// read-only <c>ISimulationView</c> context parameter (see <paramref name="AppendSelfArg"/> for
/// the OMIT/append split). Stage 7 appends the in-scope read-only view expression (never
/// <c>EntityRepository</c> write access) to the emitted call.
/// </param>
public sealed record IrOp_PureCall(
    string MethodFqn,
    IReadOnlyList<IrValue> Args,
    IrTypeRef ReturnType,
    bool AppendSelfArg = false,
    bool AppendViewArg = false) : IrOperation;

// Impure calls into Blueprint code
/// <param name="AppendSelfArg">P7 -- see <see cref="IrOp_PureCall.AppendSelfArg"/>.</param>
/// <param name="AppendViewArg">P7 -- see <see cref="IrOp_PureCall.AppendViewArg"/>.</param>
public sealed record IrOp_LibraryCall(
    int LibraryBlueprintId,
    string MethodName,
    IReadOnlyList<IrValue> Args,
    IrTypeRef ReturnType,
    bool AppendSelfArg = false,
    bool AppendViewArg = false) : IrOperation;

public sealed record IrOp_PeerCall(
    int PeerBlueprintId,
    string MethodName,
    IReadOnlyList<IrValue> Args,
    IrTypeRef ReturnType) : IrOperation;

public sealed record IrOp_AiPrimitiveCall(
    int AiPrimitiveBlueprintId,
    IReadOnlyList<IrValue> Args,
    IrTypeRef ReturnType) : IrOperation;

/// <summary>
/// Synchronous call to a local Function graph defined within the same blueprint instance.
/// Emitted by Stage 5 when FunctionCallNode.TargetGraphId is non-empty.
/// Stage 7 renders this as Func_{Sanitize(name)}(ref s, view, ecb, self, time, deltaTime, instanceVersion, args...).
/// LATENT NODES inside the target graph are FORBIDDEN (BP1650) — validated in Stage 2.
/// </summary>
public sealed record IrOp_GraphCall(
    System.Guid TargetGraphId,
    IReadOnlyList<IrValue> Args,
    IrTypeRef ReturnType) : IrOperation;

public sealed record IrOp_RaiseCustomEvent(
    int CustomEventIndex,
    IReadOnlyList<IrValue> Args) : IrOperation;

// Engine-event-driven (Instance only)
public sealed record IrOp_PollEngineEvent(
    string EventTypeFqn,
    string TargetFieldName,
    IReadOnlyList<IrField> PayloadFields,
    Guid HandlerGraphId) : IrOperation;

// ECS read (impure)
public sealed record IrOp_HasComponent(string ComponentTypeFqn, IrValue Entity) : IrOperation;
public sealed record IrOp_GetComponent(string ComponentTypeFqn, IrValue Entity, IrTypeRef Type) : IrOperation;
public sealed record IrOp_GetComponentRO(string ComponentTypeFqn, IrValue Entity, IrTypeRef Type) : IrOperation;

// ECS write via ECB (impure)
public sealed record IrOp_AddComponent(string ComponentTypeFqn, IrValue Entity, IrValue Value) : IrOperation;
public sealed record IrOp_RemoveComponent(string ComponentTypeFqn, IrValue Entity) : IrOperation;
public sealed record IrOp_DestroyEntity(IrValue Entity) : IrOperation;
public sealed record IrOp_PublishEvent(
    string EventTypeFqn,
    IReadOnlyList<(string FieldName, IrValue Value)> Fields) : IrOperation;

/// <summary>
/// P4 (GAP-3) -- publish an engine event on the world event bus from an AiPrimitive/Instance.
/// Emits `world.Bus.Publish(new global::{EventTypeFqn}{ Field = value, ... })` (or PublishManaged
/// when Managed==true). Distinct from IrOp_PublishEvent (ECB path) because the AiPrimitive TickCore
/// ABI deliberately has no IEntityCommandBuffer -- bus publish is a non-structural mutation and the
/// architect-sanctioned path (Q#5-A). Fields are the event struct's field assignments (target field
/// defaults to `self`).
/// </summary>
public sealed record IrOp_PublishBusEvent(
    string EventTypeFqn,
    IReadOnlyList<(string FieldName, IrValue Value)> Fields,
    bool Managed = false) : IrOperation;

/// <summary>
/// P1 (GAP-1) -- structured, bounded, latent-free inline foreach. Emits:
///   for (int __feN = 0; __feN &lt; global::{CountAccessorFqn}({RosterValue}); __feN++)
///   {
///       var __t{ItemVar.Index} = global::{ItemAccessorFqn}({RosterValue}, __feN);
///       {Body statements, emitted inline...}
///   }
/// <para><see cref="RosterValue"/> is the <c>ref readonly</c> local produced by an
/// <see cref="IrOp_GetComponentRO"/> on <c>self</c> (Stage5 emits that read just before this op).
/// <see cref="ItemVar"/> is the per-iteration item local, DECLARED by this op's emit inside the loop
/// (it has no defining statement of its own). <see cref="Body"/> is a NESTED statement list scheduled
/// inline by Stage5 -- NOT a BFS block -- so there is no per-iteration block / topological cycle.
/// P1a bodies are latent-free AND branch-free (Stage2 BP2050 enforces).</para>
/// </summary>
public sealed record IrOp_ForEach(
    string CountAccessorFqn,
    string ItemAccessorFqn,
    IrValue RosterValue,
    IrValue ItemVar,
    IReadOnlyList<IrStatement> Body) : IrOperation;

/// <summary>
/// P1b (GAP-1) -- structured, inline <c>if</c>/<c>else</c>. Emitted ONLY by Stage5 for a
/// <see cref="Assets.BranchNode"/> reached from a <see cref="IrOp_ForEach"/> "Body" exec-chain,
/// so the branch lowers to nested statements INSIDE the inline <c>for</c> (not a BFS block split,
/// which an inline loop body cannot span). Emits:
///   if (__t{Condition.Index})
///   {
///       {Then statements, emitted inline...}
///   }
///   else                       // omitted when Else is empty
///   {
///       {Else statements, emitted inline...}
///   }
/// <para><see cref="Condition"/> is the boolean produced by the Branch's "Condition" data-in
/// (resolved into the enclosing scope, before this op). <see cref="Then"/>/<see cref="Else"/> are
/// NESTED statement lists scheduled inline by Stage5 from the Branch's True/False exec-outs, each up
/// to the branch's inline join (immediate common successor) -- so the outer chain resumes ONCE after
/// the <c>if</c> at that join. Both arms are latent-free (Stage2 BP2050).</para>
/// </summary>
public sealed record IrOp_If(
    IrValue Condition,
    IReadOnlyList<IrStatement> Then,
    IReadOnlyList<IrStatement> Else) : IrOperation;

/// <summary>
/// AN8 — Inline-latent non-channel behavior-action invocation.
/// Emitted by Stage 5 for a <c>ChannelCommandNode</c> whose <c>ActionFqn</c> is non-null.
/// The action is called synchronously; on <c>NodeStatus.Running</c> the graph suspends inline
/// (resumes at the same node next tick) until Success/Failure is returned.
/// Stage 6 (<c>WaitLowering_AiPrimitive</c>) converts the <c>IrTerm_Suspend</c> that follows
/// into a phase-byte re-dispatch that re-invokes the action.
/// Stage 7 (<c>StatementEmitter</c>) emits the call inline.
/// </summary>
/// <param name="ActionFqn">
/// Fully-qualified name of the action in <c>"{DeclaringTypeFqn}.{MethodName}"</c> format.
/// For AiPrimitive (BlueprintCall): <c>"Hrot.AI.Behaviors.Generated.{ClassName}.Call"</c>.
/// Compiler splits at the last '.' to get the static class FQN and method name.
/// </param>
/// <param name="ParamsTypeFqn">
/// FQN of the action's parameter DTO, e.g.
/// <c>"Hrot.AI.Behaviors.Generated.MyAction_A1B2C3D4_Bp+Params"</c>.
/// Used to emit <c>new global::{ParamsTypeFqn} { Field = value }</c>.
/// </param>
/// <param name="ParamFields">Ordered list of (DTO field name, resolved IR value) pairs from data-IN pins.</param>
/// <param name="IsAiPrimitive">
/// True when the action is an AiPrimitive (BlueprintCall) — the call signature includes
/// <c>ref WorkingState ws</c> projected over <c>Blackboard1024</c>.
/// False for future stateless-action paths.
/// </param>
public sealed record IrOp_InlineActionCall(
    string ActionFqn,
    string ParamsTypeFqn,
    IReadOnlyList<(string FieldName, IrValue Value)> ParamFields,
    bool IsAiPrimitive) : IrOperation;

// Channel command (lowered from ChannelCommandNode in Stage 6)
public sealed record IrOp_ChannelCommand(
    string ChannelComponentTypeFqn,
    string ActionIdConstantName,
    string ParamsStructTypeFqn,
    IReadOnlyList<(string FieldName, IrValue Value)> ParamFields) : IrOperation;

// Wait primitives -- Stage 6 turns these into block structure
public sealed record IrOp_WaitForChannel(
    string ChannelComponentTypeFqn,
    IReadOnlyList<IrField> StatusFields) : IrOperation;

public sealed record IrOp_WaitForEvent(
    string EventTypeFqn,
    string? FilterFieldName,
    IrValue? FilterValue,
    IReadOnlyList<IrField> PayloadFields) : IrOperation;

public sealed record IrOp_LatentDelay(IrValue Seconds) : IrOperation;

// Cursor version check (Instance lowering, per Q-18.1)
public sealed record IrOp_CheckCursorVersion : IrOperation;

// AiPrimitive working-state phase field reads/writes (Stage 6 lowering)
public sealed record IrOp_WriteWorkingStatePhase(int PhaseValue) : IrOperation;
public sealed record IrOp_ReadWorkingStatePhase : IrOperation;
public sealed record IrOp_WriteWorkingStateWaitUntilTime(IrValue Value) : IrOperation;
public sealed record IrOp_ReadWorkingStateWaitUntilTime : IrOperation;

// Instance cursor reads/writes (Stage 6 lowering)
public sealed record IrOp_WriteCursorResumeAt(int ResumeAtValue) : IrOperation;
public sealed record IrOp_ReadCursorResumeAt : IrOperation;
public sealed record IrOp_WriteCursorInstanceVersion : IrOperation;
public sealed record IrOp_WriteCursorWaitUntilTime(IrValue Seconds) : IrOperation;
public sealed record IrOp_ReadCursorWaitUntilTime : IrOperation;

// Field read from a component ref (Stage 6 lowering)
public sealed record IrOp_FieldRead(IrValue Source, string FieldName, IrTypeRef ResultType) : IrOperation;

/// <summary>
/// GAP-12 -- native <c>CompareNode</c> lowering. Emits a single infix C# comparison expression:
/// <c>var __t{idx} = __t{Left.Index} {infix} __t{Right.Index};</c>, where <c>{infix}</c> comes from
/// a <see cref="Hrot.Blueprints.Core.Assets.ComparisonOperator"/> -> C#-infix switch in
/// <see cref="Emit.StatementEmitter"/> (mirrors the existing op_&lt;Op&gt;_&lt;Type&gt;
/// synthesized-operator infix map at StatementEmitter.cs ~936-949). Both operands must already
/// agree in type (the graph author wires same-typed A/B, e.g. an enum field read against an enum
/// Literal) -- no coercion is performed here.
/// </summary>
public sealed record IrOp_Compare(
    IrValue Left,
    IrValue Right,
    Hrot.Blueprints.Core.Assets.ComparisonOperator Op) : IrOperation;

/// <summary>
/// Native <c>BinaryOpNode</c> lowering (Compare's arithmetic sibling). Emits a single infix C#
/// arithmetic expression: <c>var __t{idx} = __t{Left.Index} {infix} __t{Right.Index};</c>, where
/// <c>{infix}</c> comes from a <see cref="Hrot.Blueprints.Core.Assets.ArithmeticOperator"/> ->
/// C#-infix switch in <see cref="Emit.StatementEmitter"/>. Both operands must already agree in
/// type (the graph author wires same-typed A/B) -- no coercion is performed here. Unlike
/// <see cref="IrOp_Compare"/>, the result IrValue is typed as the OPERAND type, not bool (see
/// Stage5_Schedule's BinaryOpNode case: result value = <c>AllocValue(aVal.Type)</c>).
/// </summary>
public sealed record IrOp_BinaryOp(
    IrValue Left,
    IrValue Right,
    Hrot.Blueprints.Core.Assets.ArithmeticOperator Op) : IrOperation;

/// <summary>
/// Native <c>BooleanOpNode</c> lowering (Compare's boolean sibling). Emits a single infix C#
/// boolean expression: <c>var __t{idx} = __t{Left.Index} {infix} __t{Right.Index};</c>, where
/// <c>{infix}</c> comes from a <see cref="Hrot.Blueprints.Core.Assets.BooleanOperator"/> ->
/// C#-infix switch in <see cref="Emit.StatementEmitter"/> (<c>&amp;&amp;</c>/<c>||</c>). Both
/// operands must already be <c>System.Boolean</c> -- no coercion is performed here. Like
/// <see cref="IrOp_Compare"/> (and unlike <see cref="IrOp_BinaryOp"/>), the result IrValue is
/// typed <c>BoolType</c> (see Stage5_Schedule's BooleanOpNode case). No short-circuit: both
/// operands are resolved as values (via ResolveDataPin) before this op combines them.
/// </summary>
public sealed record IrOp_BooleanOp(
    IrValue Left,
    IrValue Right,
    Hrot.Blueprints.Core.Assets.BooleanOperator Op) : IrOperation;

/// <summary>
/// Native <c>NotNode</c> lowering (unary boolean negation). Emits a single prefix C# expression:
/// <c>var __t{idx} = !__t{Operand.Index};</c>. The operand must already be <c>System.Boolean</c> --
/// no coercion is performed here. The result IrValue is typed <c>BoolType</c> (see
/// Stage5_Schedule's NotNode case).
/// </summary>
public sealed record IrOp_Not(IrValue Operand) : IrOperation;

// Debug probes (Debug/Trace modes)
public sealed record IrOp_DebugProbe_NodeEnter(Guid NodeId, string NodeKind) : IrOperation;
public sealed record IrOp_DebugProbe_PinValue(Guid PinId, IrValue Value, string PinName) : IrOperation;

// ── WhenNode lowering ops ──────────────────────────────────────────────────

/// <summary>
/// Emitted by Stage 5 for a WhenNode in ValueChanged mode.
/// Stage 7 emits the component-read + comparison inline.
/// Result value holds the "changed" bool used by IrTerm_Branch.
/// </summary>
public sealed record IrOp_WhenValueChangedCheck(
    /// <summary>Full FQN of the ECS component (e.g. "MyGame.Health").</summary>
    string ComponentFqn,
    /// <summary>Dot-separated property path into the component (e.g. "Current").</summary>
    string PropertyPath,
    /// <summary>Comparison epsilon (0 -> direct equality).</summary>
    float Epsilon,
    /// <summary>Name of the synthesized prev-state field in the State struct.</summary>
    string SynthFieldName,
    /// <summary>CSharp-level type name of the tracked field (e.g. "float", "bool").</summary>
    string FieldCSharpType,
    /// <summary>Block id of the OnFired block (used by Stage 6 to append StorePrev).</summary>
    IrBlockId OnFiredBlock,
    /// <summary>Source: 0=SelfComponent, 1=PeerBlueprintVariable, 2=WorkingStateField</summary>
    int SourceKind
) : IrOperation;

/// <summary>
/// Appended to the OnFired block by Stage 5 post-actions.
/// Re-reads the component field and stores to the synthesized prev-state field.
/// </summary>
public sealed record IrOp_WhenStorePrev(
    string ComponentFqn,
    string PropertyPath,
    string SynthFieldName
) : IrOperation;

/// <summary>
/// Emitted by Stage 5 for a WhenNode in EventFired mode.
/// Stage 7 emits the ReadEvents loop + optional filtering inline.
/// Result value holds the "matched" bool used by IrTerm_Branch.
/// </summary>
public sealed record IrOp_WhenEventFiredCheck(
    /// <summary>Full FQN of the event type (e.g. "MyGame.HitEvent").</summary>
    string EventFqn,
    /// <summary>Whether to filter by Target == self.</summary>
    bool FilterSelf,
    /// <summary>Name of the Entity field to check for self-filter (e.g. "Target").</summary>
    string TargetFieldName,
    /// <summary>Payload field path for the optional PayloadCondition (null = no check).</summary>
    string? PayloadFieldPath,
    /// <summary>Comparison operator as a C# string (e.g. "<=", ">", "=="). Null = no check.</summary>
    string? PayloadOperatorCSharp,
    /// <summary>Literal value for the payload comparison (e.g. "50f"). Null = no check.</summary>
    string? PayloadValueLiteral
) : IrOperation;

/// <summary>
/// Emitted by Stage 5 for a WhenNode in ConditionMet mode.
/// Stage 7 emits a self-contained predicate-check block with embedded goto branches.
/// No result value (ResultValue = null); branching is done inline with goto statements.
/// The block terminator must be IrTerm_Goto(outBlock) -- NOT IrTerm_Branch.
/// </summary>
public sealed record IrOp_WhenConditionMetCheck(
    /// <summary>JSON-serialized SearchPredicateDto, embedded as a const string in generated code.</summary>
    string PredicateDtoJson,
    /// <summary>Name of the synthesized bool prev-state field in the State struct (e.g. "_when_a3f7c218_prev").</summary>
    string SynthFieldName,
    /// <summary>Block to goto when condition fires (current && !prev). Null if no RisingEdge.</summary>
    IrBlockId? OnFiredBlock,
    /// <summary>Block to goto when condition ends (!current && prev). Null if no FallingEdge.</summary>
    IrBlockId? OnEndedBlock
) : IrOperation;

/// <summary>
/// Emitted by Stage 5 for a WhenNode in EqsResult mode.
/// Stage 7 emits the EQS child-entity read + trigger-specific comparison inline.
/// No result value (ResultValue = null); branching is done inline with goto statements,
/// same as IrOp_WhenConditionMetCheck.
/// The block terminator must be IrTerm_Goto(outBlock).
/// </summary>
public sealed record IrOp_WhenEqsResultCheck(
    /// <summary>Name of the EqsSensorHandle variable field in the State struct (e.g. "CoverQuery").</summary>
    string SensorVariableName,
    /// <summary>EQS trigger kind: "TopChanged", "FirstReady", "ScoreCrossed", "BecomesStale".</summary>
    string Trigger,
    /// <summary>Name of the synthesized prev-state field in the State struct (e.g. "_when_a3f7c218_prev").</summary>
    string SynthFieldName,
    /// <summary>Name of the prev-state struct type local to the generated class (e.g. "_WhenEqsTopChanged_a3f7c218_PrevState").</summary>
    string SynthStructTypeName,
    /// <summary>Size in bytes of the synthesized struct (used for StructureHash contributions).</summary>
    int SynthStructSizeBytes,
    /// <summary>Score threshold as C# float literal (e.g. "0.5f"). Null for non-ScoreCrossed triggers.</summary>
    string? ScoreThresholdLiteral,
    /// <summary>Max age in seconds as C# float literal (e.g. "3.0f"). Null for non-BecomesStale triggers.</summary>
    string? MaxAgeLiteral,
    /// <summary>Block to goto when condition fires (RisingEdge). Null if no RisingEdge.</summary>
    IrBlockId? OnFiredBlock,
    /// <summary>Block to goto when condition ends (FallingEdge). Null if no FallingEdge.</summary>
    IrBlockId? OnEndedBlock
) : IrOperation;

/// <summary>
/// Emitted by Stage 5 when a ReadEqsResultNode's output pin is first resolved.
/// Stage 7 emits an [AggressiveInlining] helper method + result struct per node.
/// The result value holds the EqsResultRead_<nodeId8> struct; downstream consumers
/// read individual fields via IrOp_FieldRead on this value.
/// </summary>
public sealed record IrOp_ReadEqsResult(
    /// <summary>Name of the EqsSensorHandle variable in State struct (e.g. "CoverQuery").</summary>
    string SensorVariableName,
    /// <summary>IrValue holding the result index expression (0 if unconnected).</summary>
    IrValue ResultIndexValue,
    /// <summary>8-char hex prefix of the node ID, used for naming the helper/struct.</summary>
    string NodeId8,
    /// <summary>Name of the local generated result struct type (e.g. "_EqsResultRead_a3f7c218").</summary>
    string ResultStructTypeName
) : IrOperation;

/// <summary>
/// Emitted by Stage 5 for a SpawnEqsSensorNode in the exec chain.
/// Stage 7 emits ECB.CreateEntity + 3x ECB.AddComponent calls + EqsSensorHandle construction.
/// ResultValue holds the spawned EqsSensorHandle (referenced by downstream Handle-pin consumers).
/// </summary>
public sealed record IrOp_SpawnEqsSensor(
    /// <summary>Template's BlueprintId as a hex uint literal (e.g. "0xA3F7C218u").</summary>
    string TemplateBlueprintIdLiteral,
    /// <summary>Baked InstanceId derived from node.Id.GetHashCode() at compile time.</summary>
    int BakedInstanceId,
    /// <summary>IrValue for SearchRadius input (or null -> literal 0f).</summary>
    IrValue? SearchRadiusValue,
    /// <summary>IrValue for FactionFilter input (or null -> literal 0u).</summary>
    IrValue? FactionFilterValue,
    /// <summary>IrValue for ThreatThreshold input (or null -> literal 0f).</summary>
    IrValue? ThreatThresholdValue,
    /// <summary>IrValue for PublishPolicy input (or null -> literal (byte)0).</summary>
    IrValue? PublishPolicyValue,
    /// <summary>IrValue for Priority input (or null -> literal (byte)0).</summary>
    IrValue? PriorityValue
) : IrOperation;

/// <summary>
/// Emitted by Stage 5 when a ScoreDecisionNode is encountered in the exec chain.
/// Stage 7 emits an [AggressiveInlining] helper that calls UtilityBlueprintBridge.ScoreDecision.
/// </summary>
public sealed record IrOp_ScoreDecision(
    /// <summary>Baked numeric decision ID literal (FNV-1a hash of the AssetId GUID).</summary>
    string DecisionIdLiteral,
    /// <summary>8-char hex prefix of the node ID.</summary>
    string NodeId8
) : IrOperation;

/// <summary>
/// Emitted by Stage 5 when a ReadRankedResultNode output pin is first resolved.
/// Stage 7 emits an [AggressiveInlining] helper + result struct per node.
/// </summary>
public sealed record IrOp_ReadRankedResult(
    /// <summary>Rank literal (0 = top).</summary>
    string RankLiteral,
    /// <summary>8-char hex prefix of the node ID.</summary>
    string NodeId8,
    /// <summary>Name of the generated result struct type.</summary>
    string ResultStructTypeName
) : IrOperation;

// ── GetShared / SetShared (Slice 2a-2) ────────────────────────────────────

/// <summary>
/// Emitted by Stage 5 for a <c>GetSharedNode</c>. Reads the ENTITY-scoped shared working-state
/// slot named <paramref name="VariableId"/> via
/// <c>Fdp.Toolkit.Blueprints.Partitioning.BlueprintSharedState.TryGetShared&lt;T&gt;</c> (Slice
/// 2a-1 accessor). The statement's own <c>ResultValue</c> holds the "Value" output;
/// <paramref name="FoundValue"/> is a second, already-allocated <see cref="IrValue"/> that this
/// op DECLARES (not references) -- Stage 7 emits both locals from this single statement, mirroring
/// how <c>IrOp_ReadEqsResult</c>/<c>IrOp_ReadRankedResult</c> feed a multi-field result to
/// downstream consumers, but without an intermediate helper struct (the two outputs are declared
/// inline).
/// </summary>
/// <param name="VariableId">Entity-scoped slot name (name-keyed, not a variable index).</param>
/// <param name="SharedTypeFqn">
/// FQN (unprefixed, dots for nested types) of the Category-1 shared struct -- the generic type
/// argument for <c>TryGetShared&lt;T&gt;</c>.
/// </param>
/// <param name="FoundValue">
/// The "Found" (<c>System.Boolean</c>) output slot, declared by this statement's emission.
/// </param>
/// <param name="TargetEntity">
/// Slice 2b -- cross-entity read. The resolved "Target" data-in pin, when the <c>GetSharedNode</c>
/// has it wired (any Entity-valued pin the graph author supplies -- mirrors how
/// <see cref="IrOp_GetComponent"/> carries its <c>Entity</c> argument as a resolved
/// <see cref="IrValue"/>). <c>null</c> when the pin is unwired -- Stage 7 then emits <c>self</c>
/// EXACTLY as Slice 2a-2 (byte-identical unwired path).
/// </param>
public sealed record IrOp_ReadShared(
    string VariableId,
    string SharedTypeFqn,
    IrValue FoundValue,
    IrValue? TargetEntity = null
) : IrOperation;

/// <summary>
/// Emitted by Stage 5 for a <c>SetSharedNode</c>. Writes <paramref name="Value"/> into the
/// ENTITY-scoped shared working-state slot named <paramref name="VariableId"/> via
/// <c>Fdp.Toolkit.Blueprints.Partitioning.BlueprintSharedState.TrySetShared&lt;T&gt;</c> (Slice
/// 2a-1 accessor). When the node's optional "Written" data-out pin is wired, the statement's
/// <c>ResultValue</c> captures the returned <c>bool</c>; otherwise the call's result is discarded.
/// </summary>
/// <param name="VariableId">Entity-scoped slot name (name-keyed, not a variable index).</param>
/// <param name="SharedTypeFqn">
/// FQN (unprefixed, dots for nested types) of the Category-1 shared struct -- the generic type
/// argument for <c>TrySetShared&lt;T&gt;</c>.
/// </param>
/// <param name="Value">The resolved "Value" data-in.</param>
public sealed record IrOp_WriteShared(
    string VariableId,
    string SharedTypeFqn,
    IrValue Value
) : IrOperation;
