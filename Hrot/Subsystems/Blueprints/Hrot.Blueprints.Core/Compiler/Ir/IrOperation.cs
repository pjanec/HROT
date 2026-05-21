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

// Field read from a component ref (Stage 6 lowering)
public sealed record IrOp_FieldRead(IrValue Source, string FieldName, IrTypeRef ResultType) : IrOperation;

// Debug probes (Debug/Trace modes)
public sealed record IrOp_DebugProbe_NodeEnter(Guid NodeId, string NodeKind) : IrOperation;
public sealed record IrOp_DebugProbe_PinValue(Guid PinId, IrValue Value, string PinName) : IrOperation;
