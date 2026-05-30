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
public sealed record IrOp_PureCall(
    string MethodFqn,
    IReadOnlyList<IrValue> Args,
    IrTypeRef ReturnType) : IrOperation;

// Impure calls into Blueprint code
public sealed record IrOp_LibraryCall(
    int LibraryBlueprintId,
    string MethodName,
    IReadOnlyList<IrValue> Args,
    IrTypeRef ReturnType) : IrOperation;

public sealed record IrOp_PeerCall(
    int PeerBlueprintId,
    string MethodName,
    IReadOnlyList<IrValue> Args,
    IrTypeRef ReturnType) : IrOperation;

public sealed record IrOp_AiPrimitiveCall(
    int AiPrimitiveBlueprintId,
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
