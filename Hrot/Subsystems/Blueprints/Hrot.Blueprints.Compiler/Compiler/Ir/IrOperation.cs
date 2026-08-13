namespace Hrot.Blueprints.Core.Compiler.Ir;

public abstract record IrOperation;

// Constants and references
public sealed record IrOp_Const(string CSharpLiteral, IrTypeRef Type) : IrOperation;
public sealed record IrOp_ReadParam(int ParamIndex) : IrOperation;
// U-3 / BP-226: the target carries its KIND. ⛔ It used to be a bare `int` whose meaning Stage 5 and
// Stage 7 disagreed about — see VariableRef for what that cost.
public sealed record IrOp_ReadVariable(VariableRef Target) : IrOperation;
public sealed record IrOp_WriteVariable(VariableRef Target, IrValue Value) : IrOperation;

/// <summary>
/// BP-57 / Q27-A1 — reads a <b>function-local</b> variable: a plain C# local, not a
/// <c>State</c> field.
///
/// <para>
/// ⭐ <b>Why this is not <see cref="IrOp_ReadVariable"/> with a flag.</b> That op emits
/// <c>{stateVar}.{VarFieldName(index)}</c> — a field access on the state struct — and its index lives
/// in an asset-level union of Variables/WorkingState/Parameters. A local is neither a field nor a
/// member of that union, so representing one there would mean teaching every reader of that index
/// space about a fourth list whose entries are not fields at all. Q27-D ruled for a separate op, and
/// this is what forces it.
/// </para>
///
/// <para>⚠ <see cref="LocalIndex"/> indexes <c>IrGraph.Locals</c> — <b>per graph</b>, never the asset union.</para>
/// </summary>
public sealed record IrOp_ReadLocal(int LocalIndex) : IrOperation;

/// <summary>BP-57 — writes a function-local variable. See <see cref="IrOp_ReadLocal"/>.</summary>
public sealed record IrOp_WriteLocal(int LocalIndex, IrValue Value) : IrOperation;

/// <summary>
/// BP-57 / ⭐⭐ <b>Q27-A3</b> — resets every local of the current graph to its declared default.
///
/// <para>
/// ⭐ <b>Only ever appears in a suspending graph's ENTRY block</b>, injected by
/// <c>LocalStorage.PromoteSuspendingGraphLocals</c>. There the locals are blackboard slots that
/// outlive the method frame, so the "reset on entry" half of Q27-E has to be an explicit statement
/// rather than a C# initialiser — and it has to sit in the one block reached only when
/// <c>__phase == 0</c>, so it fires once per invocation and not once per frame.
/// </para>
///
/// <para>
/// ⚠ Carries no payload: the fields, their defaults and the slot prefix all come from
/// <c>EmissionContext.CurrentGraph</c>, so a local added or renamed cannot leave a stale copy here.
/// </para>
/// </summary>
public sealed record IrOp_ResetLocals : IrOperation;
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
/// <param name="IsManaged">
/// CA-05 (Slice 1b) -- when true, emits <c>{wv}.HasManagedComponent&lt;T&gt;()</c> instead of
/// <c>{wv}.HasComponent&lt;T&gt;()</c>. Both are PUBLIC, direct (non-reflective) members of the
/// concrete <c>Fdp.Core.EntityRepository</c> (no <c>InternalsVisibleTo</c> needed from generated
/// code) -- <c>HasManagedComponent&lt;T&gt; where T : class</c> is the idiomatic pairing used
/// throughout the engine's own production call sites (e.g. <c>SmartEgressUtil</c>) alongside
/// <c>GetManagedComponentRO&lt;T&gt;</c> (see <see cref="IrOp_GetManagedComponentRO"/>). Default
/// <c>false</c> -- existing (unmanaged) call sites are unaffected.
/// </param>
public sealed record IrOp_HasComponent(string ComponentTypeFqn, IrValue Entity, bool IsManaged = false) : IrOperation;
public sealed record IrOp_GetComponent(string ComponentTypeFqn, IrValue Entity, IrTypeRef Type) : IrOperation;
public sealed record IrOp_GetComponentRO(string ComponentTypeFqn, IrValue Entity, IrTypeRef Type) : IrOperation;

/// <summary>
/// CA-05 (Slice 1b, Q#15 managed read) -- reads a MANAGED (reference/<c>class</c>) ECS component
/// instance. Distinct from <see cref="IrOp_GetComponentRO"/> (unmanaged, <c>T : unmanaged</c>-shaped)
/// because the managed accessor is a DIFFERENT API surface: <c>ISimulationView.GetManagedComponentRO
/// &lt;T&gt;() where T : class</c> (an explicitly-implemented interface member on
/// <c>Fdp.Core.EntityRepository</c> -- PUBLIC only via the interface, not the concrete class, and
/// documented/observed to THROW if the entity lacks the component; every production call site in the
/// engine -- e.g. <c>SmartEgressUtil</c>, <c>RouteContextSystem</c> -- guards it with
/// <c>HasManagedComponent&lt;T&gt;</c> first). <see cref="Emit.StatementEmitter"/>'s case therefore
/// emits a SINGLE guarded expression (never an unconditional call) so a managed read stays
/// fail-safe/never-throw exactly like the unmanaged read, even when <c>Target</c> (or, in principle,
/// <c>self</c>) lacks the component: <c>HasManagedComponent&lt;T&gt;(e) ? GetManagedComponentRO&lt;T&gt;
/// (e) : default!</c>. Paired with <see cref="IrOp_FieldRead"/>'s <c>SourceIsManaged</c> flag, which
/// makes the per-field projection off this value null-safe too (a null managed instance's field reads
/// as the field's default, never an NRE).
/// </summary>
public sealed record IrOp_GetManagedComponentRO(string ComponentTypeFqn, IrValue Entity, IrTypeRef Type) : IrOperation;

/// <summary>
/// CA-03 (Slice W1, Q#16) -- unmanaged, self-only, write-if-present ECS write. A SINGLE guarded
/// block (not per-field ops, unlike multi-pin SetShared's <see cref="IrOp_WriteSharedField"/>):
/// the entity's <c>HasComponent&lt;T&gt;</c> result drives BOTH the emitting
/// <see cref="Assets.SetComponentNode"/>'s "Written" data-out (this op's ResultValue) AND the
/// write guard -- <c>GetComponentRW&lt;T&gt;</c> is fetched only INSIDE that guard (mirrors
/// <c>ChannelCommandLowering</c>'s pre-existing <c>HasComponent</c>-guarded RW emit shape). Only
/// the fields present in <see cref="Fields"/> are assigned; an unwired field is simply ABSENT from
/// the list (Stage5 only adds a WIRED field's resolved value here), so its value in the live
/// component is left untouched ("unwired preserved" -- same semantics as
/// <see cref="IrOp_WriteSharedField"/>, but as one statement/block instead of N, since this is a
/// typed member write, not a byte-offset write, so there is no independent-byte-range reason to
/// split it per field).
/// </summary>
/// <param name="ComponentTypeFqn">FQN of the ECS component struct to write. Baked string -- no reflection.</param>
/// <param name="Entity">
/// ALWAYS the resolved <c>self</c> Entity (an <see cref="IrOp_Self"/> value Stage5 emits just
/// before this op) -- <see cref="Assets.SetComponentNode"/> has no "Target" pin at all (self-only
/// by construction, Q#16), unlike <see cref="IrOp_GetComponent"/>/<see cref="IrOp_GetComponentRO"/>
/// which do carry a resolved cross-entity Entity argument.
/// </param>
/// <param name="Fields">WIRED (Name, Value) pairs only -- see the type doc comment.</param>
public sealed record IrOp_WriteComponentFields(
    string ComponentTypeFqn,
    IrValue Entity,
    IReadOnlyList<(string Name, IrValue Value)> Fields
) : IrOperation;

/// <summary>
/// CA-06 (Slice W2, Q#16-C) -- managed, self-only, write-if-present ECS WHOLE-COMPONENT replace via
/// the ECB. Distinct from <see cref="IrOp_WriteComponentFields"/> (unmanaged, per-field, direct
/// <c>GetComponentRW&lt;T&gt;</c> mutation): a managed component is never mutated field-by-field
/// (Q#16-C -- per-field managed write is FORBIDDEN, snapshot aliasing) -- the only legal managed write
/// is a full replacement value queued on the <see cref="Fdp.Interfaces.IEntityCommandBuffer"/>
/// (<c>SetManagedComponent&lt;T&gt;</c>, deferred playback). The guard's <c>HasManagedComponent&lt;T&gt;</c>
/// result drives BOTH the emitting <see cref="Assets.SetComponentNode"/>'s "Written" data-out (this
/// op's ResultValue) AND the write guard (write-if-present, no implicit add -- mirrors
/// <see cref="IrOp_WriteComponentFields"/>'s semantics exactly, just via the ECB instead of a direct
/// RW fetch).
/// </summary>
/// <param name="ComponentTypeFqn">FQN of the managed (<c>class</c>) ECS component to write. Baked string -- no reflection.</param>
/// <param name="Entity">
/// ALWAYS the resolved <c>self</c> Entity (an <see cref="IrOp_Self"/> value Stage5 emits just before
/// this op) -- <see cref="Assets.SetComponentNode"/> has no "Target" pin at all (self-only by
/// construction, Q#16), same as the unmanaged write.
/// </param>
/// <param name="Value">
/// The wired "Value" data-in pin's resolved value (a fresh/pass-through instance of the managed
/// component type), or <c>null</c> when the pin is left unwired -- in that case ONLY the guard is
/// emitted (Written still reflects <c>HasManagedComponent&lt;T&gt;</c>), never a
/// <c>SetManagedComponent</c> call with nothing to write.
/// </param>
public sealed record IrOp_SetManagedComponent(
    string ComponentTypeFqn,
    IrValue Entity,
    IrValue? Value
) : IrOperation;

/// <summary>
/// FC-1 (Q#20) -- component-collection element write through a curated
/// <c>[BlueprintCollectionWrite]</c> static accessor. Same guarded write-if-present shape as
/// <see cref="IrOp_WriteComponentFields"/> (the <c>HasComponent&lt;T&gt;</c> bool drives BOTH the
/// guard and this op's ResultValue), but the ResultValue is then REASSIGNED to the accessor's own
/// bool inside the guard, so it carries "component present AND op applied" -- the emitting
/// <see cref="Assets.CollectionWriteNode"/>'s "Ok" data-out (Stage5 ALWAYS allocates it):
/// <code>
/// var __tN = wv.HasComponent&lt;global::{ComponentTypeFqn}&gt;(self);
/// if (__tN)
/// {
///     ref var __wcN = ref wv.GetComponentRW&lt;global::{ComponentTypeFqn}&gt;(self);
///     __tN = global::{WriteAccessorFqn}(ref __wcN[, intArg][, value]);   // Clear: plain call, __tN stays true
/// }
/// </code>
/// Raw buffer mutation NEVER appears in generated code (Q#5-C / Q#20 G1) -- the accessor owns the
/// <c>Span&lt;T&gt;</c> pattern, Count maintenance, and the tail-always-default invariant. In
/// non-Release mode with <c>self</c> in scope, a refused op / absent component additionally emits
/// <c>DebugProbe.CollectionWriteFailed(self, nodeId, verb, reason)</c> (the never-silent
/// false-on-overflow contract; reasons: "op-rejected" / "component-absent").
/// </summary>
/// <param name="ComponentTypeFqn">FQN of the component carrying the collection. Baked string -- no reflection.</param>
/// <param name="Entity">ALWAYS the resolved <c>self</c> (an <see cref="IrOp_Self"/> value) -- the "Collection" wire is author-time binding only, never the write entity (Q#16/Q#20 self-only; Stage2 BP2069/BP2070 enforce).</param>
/// <param name="WriteAccessorFqn">Baked FQN of the curated write-accessor static for <paramref name="Verb"/>.</param>
/// <param name="Verb">The op name ("Add"/"SetAt"/"InsertAt"/"RemoveAt"/"Clear"/"Resize") -- probe argument only; arity is driven by <paramref name="IntArg"/>/<paramref name="Value"/>/<paramref name="ReturnsBool"/>.</param>
/// <param name="NodeId">The authoring node's id -- probe argument (mirrors <c>IrOp_DebugProbe_NodeEnter</c>'s "D" format).</param>
/// <param name="IntArg">The int operand (Index for SetAt/InsertAt/RemoveAt, Length for Resize), or null (Add/Clear). Passed AFTER the ref receiver, BEFORE <paramref name="Value"/>.</param>
/// <param name="Value">The element operand (Add/SetAt/InsertAt), or null.</param>
/// <param name="ReturnsBool">False only for Clear (a void accessor -- the ResultValue keeps the guard bool).</param>
public sealed record IrOp_CollectionWrite(
    string ComponentTypeFqn,
    IrValue Entity,
    string WriteAccessorFqn,
    string Verb,
    Guid NodeId,
    IrValue? IntArg,
    IrValue? Value,
    bool ReturnsBool
) : IrOperation;

/// <summary>
/// FC-2/LV-2 (Q#19-A/F1) -- binds a WRITABLE `ref` local to a State/WorkingState FIELD:
/// <c>ref var __tN = ref {s|ws}.{FieldName};</c>. The list-variable analog of the component path's
/// <c>IrOp_GetComponentRO</c> roster read: the collection consumers' RosterValue/Component argument
/// references this local and <c>RenderCollectionAccessors</c>' BlackboardFixedList branch renders
/// <c>__tN.Count</c>/<c>__tN.Items[i]</c> off it -- ref-bind (zero-copy, sees same-tick writes),
/// per the decided read-binding contract. A writable ref (not `ref readonly`) so element reads use
/// the inline array's direct element access, never the readonly defensive-copy path.
/// </summary>
public sealed record IrOp_StateFieldRef(string FieldName, IrTypeRef Type) : IrOperation;

/// <summary>
/// FC-2/LV-3 (Q#19-C/D) -- in-place fixed-list VARIABLE mutation, the blackboard sibling of
/// <see cref="IrOp_CollectionWrite"/> (they share the verb vocabulary, NOT machinery -- review R1).
/// Emits a scoped block that ref-binds the state field, applies the F2 clamp, mutates through the
/// <c>Span&lt;T&gt;</c> cast (never the naive inline-array indexer -- the amended Q#19-D emit), keeps
/// the G6 tail-always-default invariant (RemoveAt/Clear/Resize-shrink zero vacated slots; grow never
/// fills), and drives the "Ok" ResultValue per the false-on-overflow contract (+ a Debug-mode
/// <c>DebugProbe.CollectionWriteFailed</c> on refusal). Clear has no ResultValue semantics beyond
/// completing (its node has no "Ok" pin).
/// </summary>
/// <param name="FieldName">The state field (variable name) -- rendered off the s/ws local.</param>
/// <param name="ElementTypeFqn">Element type for the Span cast / default().</param>
/// <param name="Capacity">Declared capacity N (bounds + clamp).</param>
/// <param name="Verb">"Add"/"SetAt"/"InsertAt"/"RemoveAt"/"Clear"/"Resize" -- probe arg + emit dispatch.</param>
/// <param name="NodeId">Authoring node id -- probe arg.</param>
/// <param name="IntArg">Index (SetAt/InsertAt/RemoveAt) or Length (Resize); null for Add/Clear.</param>
/// <param name="Value">Element operand (Add/SetAt/InsertAt); null otherwise.</param>
public sealed record IrOp_ListWrite(
    string FieldName,
    string ElementTypeFqn,
    int Capacity,
    string Verb,
    Guid NodeId,
    IrValue? IntArg,
    IrValue? Value) : IrOperation;

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
/// <para>Optional loop-introspection outs (bound by Stage5 only when the corresponding FlowForEach
/// data-out pin is wired):
/// <list type="bullet">
/// <item><see cref="CountVar"/> -- when set, the element count is hoisted into an OUTER-scope local
///   (<c>var __t{CountVar.Index} = global::{CountAccessorFqn}({RosterValue});</c>) emitted just before
///   the <c>for</c>, and that local is reused as the loop bound (count evaluated once). The value is
///   loop-invariant and in scope both inside the body and in the "Completed" chain.</item>
/// <item><see cref="IndexVar"/> -- when set, the loop counter is copied into a BODY-scoped local
///   (<c>var __t{IndexVar.Index} = __fe{ItemVar.Index};</c>) at the top of the body, so body statements
///   reference the current 0-based index by the normal <c>__t</c> convention.</item>
/// </list></para>
/// </summary>
/// <param name="Kind">CA-07d-2: <c>CuratedStatic</c> (default) emits the baked
/// <paramref name="CountAccessorFqn"/>/<paramref name="ItemAccessorFqn"/> static calls (byte-identical
/// to FlowForEach / CA-07b); <c>ManagedMember</c> emits native <c>IReadOnlyList&lt;T&gt;</c> access off
/// <paramref name="ManagedFieldName"/> (the accessor FQNs are empty). See <see cref="Emit.StatementEmitter"/>.</param>
/// <param name="ManagedFieldName">CA-07d-2: for <c>ManagedMember</c>, the managed collection field name
/// on the component the <paramref name="RosterValue"/> read produced (element type = <c>RosterValue-less</c>
/// <see cref="ItemVar"/>'s type).</param>
public sealed record IrOp_ForEach(
    string CountAccessorFqn,
    string ItemAccessorFqn,
    IrValue RosterValue,
    IrValue ItemVar,
    IReadOnlyList<IrStatement> Body,
    IrValue? CountVar = null,
    IrValue? IndexVar = null,
    Hrot.Blueprints.Core.Assets.CollectionKind Kind = Hrot.Blueprints.Core.Assets.CollectionKind.CuratedStatic,
    string ManagedFieldName = "",
    int Capacity = 0) : IrOperation;

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
/// <param name="SourceIsManaged">
/// CA-05 (Slice 1b) -- when true, <see cref="Source"/> is the (possibly-<c>null</c>) result of an
/// <see cref="IrOp_GetManagedComponentRO"/> -- see that op's doc comment for why it can legitimately
/// be null (component absent). <see cref="Emit.StatementEmitter"/>'s case then emits a null-safe
/// projection (<c>{source}?.{FieldName} ?? default</c>) instead of a bare member access, so reading a
/// field off an absent managed component degrades to the field's default value instead of an NRE --
/// the same "fail-safe, never throw" contract the unmanaged read already has. Default <c>false</c> --
/// existing (unmanaged / non-nullable source) call sites emit the unchanged bare
/// <c>{source}.{FieldName}</c>.
/// </param>
public sealed record IrOp_FieldRead(IrValue Source, string FieldName, IrTypeRef ResultType, bool SourceIsManaged = false) : IrOperation;

/// <summary>
/// CA-07b -- calls a baked curated collection accessor (<c>[BlueprintCollection]</c>'s
/// <c>Count(in T)</c> or <c>[BlueprintCollectionItem]</c>'s <c>Item(in T, int)</c>) on a component
/// ref. Emits <c>global::{AccessorFqn}({component})</c> (Count shape, <see cref="Index"/> null) or
/// <c>global::{AccessorFqn}({component}, {index})</c> (Item shape). <see cref="Component"/> is the
/// <c>ref readonly</c> local produced by a preceding <see cref="IrOp_GetComponentRO"/> -- the
/// accessor's <c>in T</c> parameter binds to it implicitly, exactly like <see cref="IrOp_ForEach"/>'s
/// own Count/Item accessor calls (this op factors that same call shape out for the CA-07b consumer
/// nodes, which read a component OTHER than the one <c>IrOp_ForEach</c>'s roster read targets).
/// </summary>
/// <param name="Kind">CA-07d-2: <c>CuratedStatic</c> (default) emits <c>global::{AccessorFqn}(comp[,i])</c>;
/// <c>ManagedMember</c> emits native member access off <paramref name="ManagedFieldName"/> via an
/// <c>IReadOnlyList&lt;<paramref name="ElementTypeFqn"/>&gt;</c> local -- <c>(__ml?.Count ?? 0)</c> for the
/// Count shape (<paramref name="Index"/> null), a null+bounds-guarded <c>__ml[i]</c> for the Item shape.</param>
/// <param name="ManagedFieldName">CA-07d-2: for <c>ManagedMember</c>, the managed collection field name (accessor FQN empty).</param>
/// <param name="ElementTypeFqn">CA-07d-2: for <c>ManagedMember</c>, the collection's element type FQN, used to type the
/// <c>IReadOnlyList&lt;T&gt;</c> local so a <c>T[]</c> field still exposes <c>.Count</c>/indexer uniformly. Empty for curated.</param>
public sealed record IrOp_ComponentAccessorCall(
    string AccessorFqn, IrValue Component, IrValue? Index, IrTypeRef ResultType,
    Hrot.Blueprints.Core.Assets.CollectionKind Kind = Hrot.Blueprints.Core.Assets.CollectionKind.CuratedStatic,
    string ManagedFieldName = "", string ElementTypeFqn = "",
    int Capacity = 0) : IrOperation;

/// <summary>
/// CA-07d-1 -- bounded linear search over a component collection, sharing the SAME curated
/// <see cref="CountAccessorFqn"/>/<see cref="ItemAccessorFqn"/> accessors as <see cref="IrOp_ForEach"/>.
/// Emits (see <see cref="Emit.StatementEmitter"/>): declare the result(s), then
/// <c>for (int i = 0, n = Count(comp); i &lt; n; i++) if (EqualityComparer&lt;TElem&gt;.Default.Equals(Item(comp,i), query)) { …set results…; break; }</c>.
/// Backs BOTH consumer nodes via which result values are set:
///   <see cref="ComponentContainsNode"/> -> <see cref="ContainsResult"/> (bool);
///   <see cref="ComponentFindNode"/> -> <see cref="FindIndex"/> (int, -1 if absent) + <see cref="FindFound"/> (bool).
/// <see cref="ElementTypeFqn"/> types the <c>EqualityComparer&lt;T&gt;</c> so scalars, enums, and struct
/// value-copies all compare correctly with one reflection-free path (Q#18-A).
/// </summary>
/// <param name="Kind">CA-07d-2: <c>CuratedStatic</c> (default) walks the collection via the baked
/// <paramref name="CountAccessorFqn"/>/<paramref name="ItemAccessorFqn"/> static calls; <c>ManagedMember</c>
/// walks a native <c>IReadOnlyList&lt;<paramref name="ElementTypeFqn"/>&gt;</c> local off
/// <paramref name="ManagedFieldName"/> (accessor FQNs empty). The <c>EqualityComparer&lt;T&gt;</c> compare
/// + short-circuit are identical either way.</param>
/// <param name="ManagedFieldName">CA-07d-2: for <c>ManagedMember</c>, the managed collection field name (accessor FQNs empty).</param>
public sealed record IrOp_ComponentCollectionSearch(
    string CountAccessorFqn,
    string ItemAccessorFqn,
    string ElementTypeFqn,
    IrValue Component,
    IrValue Query,
    IrValue? ContainsResult = null,
    IrValue? FindIndex = null,
    IrValue? FindFound = null,
    Hrot.Blueprints.Core.Assets.CollectionKind Kind = Hrot.Blueprints.Core.Assets.CollectionKind.CuratedStatic,
    string ManagedFieldName = "",
    int Capacity = 0) : IrOperation;

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

/// <summary>
/// BP-108 — <c>Print String</c>. <paramref name="Format"/> is the ALREADY-REWRITTEN body of a C#
/// interpolated string (placeholders replaced with <c>{__tN}</c> temp references by Stage 5), so emit is
/// a pure string paste with no runtime formatting machinery.
///
/// <para>
/// ⭐ Emitted <b>inside a level probe</b>: <c>if (BlueprintLog.IsInfoEnabled) BlueprintLog.Info($"…")</c>.
/// The interpolation — and therefore every allocation — is skipped entirely when the level is off.
/// </para>
/// </summary>
public sealed record IrOp_PrintString(string InterpolatedBody, string Level) : IrOperation;

/// <summary>
/// BP-108 — <c>Format String</c>. Same rewritten interpolated body as <see cref="IrOp_PrintString"/>,
/// but the result is written into a <c>stackalloc</c> buffer and converted to a <c>FixedString</c>.
///
/// <para>
/// ⚖️ <b>Zero-allocation by user ruling</b> (<i>"favor zero alloc path, it is always better"</i>). Unlike
/// Print String this node is <b>pure</b>, so it has no level probe to hide behind — a naive
/// <c>string.Format</c> here would allocate every tick for every entity. Emitting
/// <c>MemoryExtensions.TryWrite</c> into a stack buffer and then the <c>ReadOnlySpan&lt;char&gt;</c>
/// FixedString constructor keeps it allocation-free.
/// </para>
/// </summary>
public sealed record IrOp_FormatString(
    string InterpolatedBody, string ResultTypeFqn, int BufferChars) : IrOperation;

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

/// <summary>
/// Q#14 multi-pin SetShared: writes ONE field (<paramref name="Value"/>) into the ENTITY-scoped shared
/// slot <paramref name="VariableId"/> at byte <paramref name="FieldOffset"/> within
/// <paramref name="SharedTypeFqn"/>, via <c>BlueprintSharedState.TrySetSharedField&lt;TStruct,TField&gt;</c>.
/// One statement per WIRED field pin (unwired fields never emit → preserved). Sources are resolved
/// top-to-bottom into temporaries before the writes (evaluate-then-write); the per-field writes touch
/// distinct byte ranges, so they are order-independent.
/// </summary>
/// <param name="VariableId">Entity-scoped slot name (name-keyed).</param>
/// <param name="SharedTypeFqn">FQN of the shared struct (TStruct — hash validation + bounds).</param>
/// <param name="FieldTypeFqn">FQN of the field type (TField — the write width).</param>
/// <param name="FieldOffset">Byte offset of the field within the struct (editor-baked).</param>
/// <param name="Value">The resolved field data-in.</param>
public sealed record IrOp_WriteSharedField(
    string VariableId,
    string SharedTypeFqn,
    string FieldTypeFqn,
    int FieldOffset,
    IrValue Value
) : IrOperation;

/// <summary>
/// Q#14 Option B (<c>MakeStructNode</c>): constructs a struct value from per-field values —
/// <c>var __t{result} = new global::{StructFqn} { A = __t{..}, B = __t{..} };</c>. The result is the
/// struct-typed value flowed to downstream consumers (mirrors <see cref="IrOp_PublishBusEvent"/>'s
/// object-initializer construction, but the value flows out instead of being published).
/// </summary>
public sealed record IrOp_MakeStruct(
    string StructFqn,
    IReadOnlyList<(string FieldName, IrValue Value)> Fields
) : IrOperation;

/// <summary>
/// Q#14 Option B (<c>SetMembersNode</c>): copy-with-changes — <c>var __t{result} = __t{Input};</c> then
/// <c>__t{result}.{Field} = __t{value};</c> per wired member. The result is a modified COPY (structs are
/// value types), so the source value is untouched and unwired members keep the source's value.
/// </summary>
public sealed record IrOp_SetMembers(
    string StructFqn,
    IrValue Input,
    IReadOnlyList<(string FieldName, IrValue Value)> Fields
) : IrOperation;

/// <summary>
/// BP-73: packs N function-graph outputs into the <b>carrier</b> a multi-output
/// <c>Func_X</c> returns — <c>var __t{result} = (__t{a}, __t{b});</c>.
/// <para>
/// The carrier is a <b>ValueTuple</b>, not a synthesized struct. Deciding constraint:
/// <c>CSharpEmitter.IsReferencableStateFieldType</c> treats a <c>'_'</c>-prefixed synthesized type as
/// NOT referencable outside the generated class and excludes it from <c>StateFields</c>, so a
/// <c>_FuncOut_X</c> return would be invisible to the debugger/watch; a ValueTuple is a BCL type.
/// </para>
/// <para>
/// ⚠ The carrier deliberately has <b>no <see cref="IrTypeRef"/> representation</b>. Temps are emitted
/// as <c>var __tN = …</c>, so C# infers it; only the three method-DECLARATION sites need a composed
/// type string, and each builds it from <c>graph.Outputs</c> via
/// <c>LibraryEmitter.CSharpReturnType</c>. That keeps N-output out of the type system entirely.
/// </para>
/// </summary>
public sealed record IrOp_MakeTuple(IReadOnlyList<IrValue> Values) : IrOperation;

/// <summary>
/// BP-73: reads one element back out of a multi-output carrier —
/// <c>var __t{result} = __t{Source}.Item{Index + 1};</c>.
/// <para>
/// Accessed positionally (<c>ItemN</c>) rather than by element name: <c>ItemN</c> is always present on
/// a ValueTuple regardless of whether the declaration names its elements, so the fan-out cannot break
/// on an output whose name is not a valid C# identifier.
/// </para>
/// </summary>
public sealed record IrOp_TupleField(IrValue Source, int Index) : IrOperation;
