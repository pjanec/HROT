using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

internal static class StatementEmitter
{
    public static void Emit(CSharpEmitter e, IrStatement stmt)
    {
        e.EmitNodeStart(stmt.Debug);
        EmitOp(e, stmt);
        e.EmitNodeEnd(stmt.Debug);
    }

    private static void EmitOp(CSharpEmitter e, IrStatement stmt)
    {
        var ctx = e.Ctx;
        int idx = stmt.ResultValue?.Index ?? -1;
        string sv = ctx.StateVar;   // "s" for Instance, "ws" for AiPrimitive
        string wv = ctx.WorldVar;   // "world" or cast of view

        switch (stmt.Operation)
        {
            // ------------------------------------------------------------------
            // Constants and simple reads
            // ------------------------------------------------------------------

            case IrOp_Const op:
                if (idx >= 0)
                {
                    string literal;
                    if (op.CSharpLiteral == "default")
                    {
                        // A bare `default` has no target type in `var x = default;` (CS8716). Emit a
                        // TYPED default from the op's result type -- used by CA-07b's unwired/unbaked
                        // component-collection consumer safe-default (ComponentItemGet/ItemCount).
                        // Unknown type ("?") -> object.
                        var tn = op.Type?.FullName;
                        literal = string.IsNullOrEmpty(tn) || tn == "?"
                            ? "default(object)"
                            : $"default(global::{tn})";
                    }
                    else
                    {
                        // Qualify NodeStatus.* literals synthesized by WaitLowering stages.
                        literal = op.CSharpLiteral.StartsWith("NodeStatus.", StringComparison.Ordinal)
                            ? $"global::Fbt.{op.CSharpLiteral}"
                            : op.CSharpLiteral;
                    }
                    e.WriteLine($"var __t{idx} = {literal};");
                }
                break;

            case IrOp_ReadParam op:
                if (idx >= 0) e.WriteLine($"var __t{idx} = p.{ctx.ParamFieldName(op.ParamIndex)};");
                break;

            case IrOp_ReadVariable op:
                if (idx >= 0) e.WriteLine($"var __t{idx} = {sv}.{ctx.VarFieldName(op.VariableIndex)};");
                break;

            case IrOp_WriteVariable op:
                e.WriteLine($"{sv}.{ctx.VarFieldName(op.VariableIndex)} = __t{op.Value.Index};");
                break;

            // ------------------------------------------------------------------
            // GetShared / SetShared (Slice 2a-2 + Slice 2b): entity-scoped shared working-state,
            // compiled to calls into the Slice 2a-1 accessor.
            // ------------------------------------------------------------------

            case IrOp_ReadShared op:
            {
                string sharedTypeFqn = op.SharedTypeFqn;
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = default(global::{sharedTypeFqn});");
                string valueRef = idx >= 0 ? $"__t{idx}" : "_";
                // Slice 2b: an explicit "Target" pin resolves to the accessor's entity arg (cross-
                // entity read); referenced directly by index exactly as IrOp_GetComponent references
                // its resolved Entity IrValue (no cast -- the producing statement's C# local is
                // already typed global::Fdp.Core.Entity). Unwired (TargetEntity == null) emits
                // `self` EXACTLY as Slice 2a-2 -- byte-identical unwired-path codegen.
                string entityArg = op.TargetEntity is { } targetEntity ? $"__t{targetEntity.Index}" : "self";
                e.WriteLine(
                    $"bool __t{op.FoundValue.Index} = global::Fdp.Toolkit.Blueprints.Partitioning." +
                    $"BlueprintSharedState.TryGetShared<global::{sharedTypeFqn}>({wv}, {entityArg}, \"{op.VariableId}\", out {valueRef});");
                break;
            }

            case IrOp_WriteShared op:
            {
                string sharedTypeFqn = op.SharedTypeFqn;
                string call =
                    $"global::Fdp.Toolkit.Blueprints.Partitioning.BlueprintSharedState." +
                    $"TrySetShared<global::{sharedTypeFqn}>({wv}, self, \"{op.VariableId}\", in __t{op.Value.Index})";
                if (idx >= 0)
                    e.WriteLine($"bool __t{idx} = {call};");
                else
                    e.WriteLine($"{call};");
                break;
            }

            case IrOp_MakeStruct op:
            {
                // Q#14 Option B: build a struct value into __t{idx}; the value flows to consumers.
                if (idx >= 0)
                {
                    e.WriteLine($"var __t{idx} = new global::{op.StructFqn}");
                    e.WriteLine("{");
                    e.Indent();
                    for (int i = 0; i < op.Fields.Count; i++)
                    {
                        var f = op.Fields[i];
                        var sep = i == op.Fields.Count - 1 ? "" : ",";
                        e.WriteLine($"{f.FieldName} = __t{f.Value.Index}{sep}");
                    }
                    e.Outdent();
                    e.WriteLine("};");
                }
                break;
            }

            case IrOp_SetMembers op:
            {
                // Q#14 Option B: copy the source struct, then overwrite the wired members.
                if (idx >= 0)
                {
                    e.WriteLine($"var __t{idx} = __t{op.Input.Index};");
                    foreach (var f in op.Fields)
                        e.WriteLine($"__t{idx}.{f.FieldName} = __t{f.Value.Index};");
                }
                break;
            }

            case IrOp_WriteSharedField op:
            {
                // Q#14 multi-pin: true per-field write — touches only this field's bytes, unwired
                // fields preserved. Result discarded (self-only, not-ready => no-op, mirrors WriteShared).
                e.WriteLine(
                    $"global::Fdp.Toolkit.Blueprints.Partitioning.BlueprintSharedState." +
                    $"TrySetSharedField<global::{op.SharedTypeFqn}, global::{op.FieldTypeFqn}>(" +
                    $"{wv}, self, \"{op.VariableId}\", {op.FieldOffset}, in __t{op.Value.Index});");
                break;
            }

            case IrOp_ReadInputArg op:
            {
                var argName = ctx.CurrentGraph?.Inputs is { } inputs && op.ArgIndex < inputs.Count
                    ? inputs[op.ArgIndex].Name
                    : $"arg{op.ArgIndex}";
                if (idx >= 0) e.WriteLine($"var __t{idx} = {argName};");
                break;
            }

            case IrOp_Self:
                if (idx >= 0) e.WriteLine($"var __t{idx} = self;");
                break;

            case IrOp_Time:
                if (idx >= 0) e.WriteLine($"var __t{idx} = time;");
                break;

            case IrOp_DeltaTime:
                if (idx >= 0) e.WriteLine($"var __t{idx} = deltaTime;");
                break;

            case IrOp_ReadInstanceVersion:
                if (idx >= 0) e.WriteLine($"uint __t{idx} = instanceVersion;");
                break;

            // ------------------------------------------------------------------
            // Function calls
            // ------------------------------------------------------------------

            case IrOp_PureCall op:
            {
                var argList = AppendContextArgs(
                    string.Join(", ", op.Args.Select(a => $"__t{a.Index}")),
                    ctx, op.AppendSelfArg, op.AppendViewArg);
                string call;
                // Intercept synthesized coercion casts produced by Stage3_Normalize.InsertImplicitCasts
                // (CastNode -> IrOp_PureCall "Cast.<TargetType>"). These must emit a native C# cast, NOT a
                // call to a nonexistent global::Cast.<Type> method (CS0400). Stage3 only inserts a cast
                // when ITypeRegistry.TryGetCoercion succeeds, so <TargetType> is always a scalar numeric/
                // enum FQN and the single arg is the value to convert (no context args).
                if (op.MethodFqn.StartsWith("Cast.", StringComparison.Ordinal) && op.Args.Count == 1)
                {
                    var targetType = op.MethodFqn.Substring("Cast.".Length);
                    call = $"(global::{targetType})__t{op.Args[0].Index}";
                }
                // Intercept synthesized comparison/arithmetic operators produced by WaitLowering_*.
                // These use the naming convention op_<Operation>_<Type> and must be emitted as
                // native C# infix expressions rather than global:: method calls (which would be invalid).
                else if (TryGetSynthesizedOpInfix(op.MethodFqn, op.Args, out var infixExpr))
                {
                    call = infixExpr!;
                }
                else
                {
                    call = $"global::{op.MethodFqn}({argList})";
                }
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = {call};");
                else
                    e.WriteLine($"{call};");
                break;
            }

            case IrOp_LibraryCall op:
            {
                var libClass = ctx.ResolveLibraryClass(op.LibraryBlueprintId);
                var argList = AppendContextArgs(
                    string.Join(", ", op.Args.Select(a => $"__t{a.Index}")),
                    ctx, op.AppendSelfArg, op.AppendViewArg);
                var call = $"{libClass}.{op.MethodName}({argList})";
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = {call};");
                else
                    e.WriteLine($"{call};");
                break;
            }

            case IrOp_PeerCall op:
            {
                var peerClass = $"__Peer_{op.PeerBlueprintId:X8}_Bp";
                var argList = string.Join(", ", op.Args.Select(a => $"__t{a.Index}"));
                var call = $"{peerClass}.{op.MethodName}({argList})";
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = {call};");
                else
                    e.WriteLine($"{call};");
                break;
            }

            case IrOp_AiPrimitiveCall op:
            {
                var primClass = $"__AiPrim_{op.AiPrimitiveBlueprintId:X8}_Bp";
                var argList = string.Join(", ", op.Args.Select(a => $"__t{a.Index}"));
                var call = $"{primClass}.Call({argList})";
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = {call};");
                else
                    e.WriteLine($"{call};");
                break;
            }

            case IrOp_GraphCall op:
            {
                // In-blueprint function-graph call (BATCH-03A).
                var fg = ctx.Asset.Graphs.FirstOrDefault(g => g.Id == op.TargetGraphId);
                if (fg is null)
                {
                    // Target graph not found -- emit a comment so the generated code still compiles.
                    e.WriteLine($"/* IrOp_GraphCall: target graph {op.TargetGraphId} not found */");
                    break;
                }
                var sanitized = Sanitizer.SanitizeName(fg.Name);
                var contextArgs = new[] { $"ref {sv}", "view", "ecb", "self", "time", "deltaTime", "instanceVersion" };
                var dataArgs = op.Args.Select(a => $"__t{a.Index}");
                var allArgs = string.Join(", ", contextArgs.Concat(dataArgs));
                var gcCall = $"Func_{sanitized}({allArgs})";
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = {gcCall};");
                else
                    e.WriteLine($"{gcCall};");
                break;
            }

            // ------------------------------------------------------------------
            // Custom events
            // ------------------------------------------------------------------

            case IrOp_RaiseCustomEvent op:
            {
                var evtName = ctx.CustomEventName(op.CustomEventIndex);
                var argList = string.Join(", ", op.Args.Select(a => $"__t{a.Index}"));
                var extraArgs = argList.Length > 0 ? $", {argList}" : "";
                e.WriteLine($"Event_{evtName}(ref {sv}, view, ecb, self, time{extraArgs});");
                break;
            }

            // ------------------------------------------------------------------
            // Engine event poll (Instance dispatch only)
            // ------------------------------------------------------------------

            case IrOp_PollEngineEvent op:
            {
                var n = ctx.NextLocalCounter("evt");
                var handlerGraph = ctx.Asset.Graphs.FirstOrDefault(g => g.Id == op.HandlerGraphId);
                var graphName = handlerGraph?.Name ?? op.HandlerGraphId.ToString("N");

                e.WriteLine($"var __evts_{n} = view.ReadEvents<global::{op.EventTypeFqn}>();");
                e.WriteLine($"for (int __i_{n} = 0; __i_{n} < __evts_{n}.Length; __i_{n}++)");
                e.WriteLine("{");
                e.Indent();
                e.WriteLine($"var __e_{n} = __evts_{n}[__i_{n}];");
                if (!string.IsNullOrEmpty(op.TargetFieldName))
                {
                    e.WriteLine($"if (__e_{n}.{op.TargetFieldName} != self) continue;");
                }
                var payloadArgs = op.PayloadFields.Count > 0
                    ? ", " + string.Join(", ", op.PayloadFields.Select(f => $"__e_{n}.{f.Name}"))
                    : "";
                e.WriteLine($"Event_{graphName}(ref s, view, ecb, self, time{payloadArgs});");
                e.Outdent();
                e.WriteLine("}");
                break;
            }

            // ------------------------------------------------------------------
            // ECS reads
            // ------------------------------------------------------------------

            case IrOp_HasComponent op:
                if (idx >= 0)
                {
                    // CA-05: managed components pair with HasManagedComponent<T> (public, direct,
                    // T : class) -- the idiomatic Has+Get pairing used throughout the engine's own
                    // production call sites (see IrOp_GetManagedComponentRO's doc comment).
                    string hasMethod = op.IsManaged ? "HasManagedComponent" : "HasComponent";
                    e.WriteLine($"var __t{idx} = {wv}.{hasMethod}<global::{op.ComponentTypeFqn}>(__t{op.Entity.Index});");
                }
                break;

            case IrOp_GetComponent op:
                if (idx >= 0)
                    e.WriteLine($"ref var __t{idx} = ref {wv}.GetComponentRW<global::{op.ComponentTypeFqn}>(__t{op.Entity.Index});");
                break;

            case IrOp_GetComponentRO op:
                if (idx >= 0)
                    e.WriteLine($"ref readonly var __t{idx} = ref {wv}.GetComponentRO<global::{op.ComponentTypeFqn}>(__t{op.Entity.Index});");
                break;

            case IrOp_GetManagedComponentRO op:
                if (idx >= 0)
                {
                    // CA-05 (Slice 1b, Q#15 managed read). ISimulationView.GetManagedComponentRO<T>
                    // (T : class) is an EXPLICITLY-implemented interface member on EntityRepository --
                    // only reachable via an ISimulationView-typed receiver (ctx.SimulationViewVar), and
                    // documented/observed to THROW if the entity lacks the component. Every real call
                    // site in the engine (SmartEgressUtil, RouteContextSystem, ...) guards it with
                    // HasManagedComponent<T> first -- mirrored here so a managed read stays
                    // fail-safe/never-throw exactly like the unmanaged read, even for an arbitrary
                    // Target entity that turns out not to carry the component. HasManagedComponent<T>
                    // itself is PUBLIC and DIRECT on the concrete EntityRepository (wv) -- no interface
                    // cast needed for the guard, only for the throwing Get.
                    string entity = $"__t{op.Entity.Index}";
                    string simView = ctx.SimulationViewVar;
                    e.WriteLine(
                        $"var __t{idx} = {wv}.HasManagedComponent<global::{op.ComponentTypeFqn}>({entity}) "
                        + $"? {simView}.GetManagedComponentRO<global::{op.ComponentTypeFqn}>({entity}) : default!;");
                }
                break;

            // ------------------------------------------------------------------
            // ECS write (direct, unmanaged, self-only, write-if-present) -- CA-03
            // ------------------------------------------------------------------

            case IrOp_WriteComponentFields op:
            {
                // CA-03 (Slice W1, Q#16). Single guarded block: HasComponent's bool drives BOTH
                // the "Written" out-pin (idx -- Stage5 ALWAYS allocates a ResultValue for this op,
                // so idx is always >= 0 here) and the write guard; GetComponentRW is fetched only
                // INSIDE the guard (mirrors ChannelCommandLowering's pre-existing HasComponent-
                // guarded RW emit shape). Only the WIRED fields carried in op.Fields are assigned --
                // an unwired field is simply absent from the list, so its value is preserved.
                string entity = $"__t{op.Entity.Index}";
                e.WriteLine($"var __t{idx} = {wv}.HasComponent<global::{op.ComponentTypeFqn}>({entity});");
                e.WriteLine($"if (__t{idx})");
                e.WriteLine("{");
                e.Indent();
                if (op.Fields.Count > 0)
                {
                    e.WriteLine($"ref var __wc{idx} = ref {wv}.GetComponentRW<global::{op.ComponentTypeFqn}>({entity});");
                    foreach (var f in op.Fields)
                        e.WriteLine($"__wc{idx}.{f.Name} = __t{f.Value.Index};");
                }
                e.Outdent();
                e.WriteLine("}");
                break;
            }

            // ------------------------------------------------------------------
            // ECS write (managed, self-only, write-if-present, whole-replace via ECB) -- CA-06
            // ------------------------------------------------------------------

            case IrOp_SetManagedComponent op:
            {
                // CA-06 (Slice W2, Q#16-C). Same guarded shape as IrOp_WriteComponentFields (the
                // HasManagedComponent bool drives BOTH "Written" and the write guard), but the write
                // itself is a single ECB-queued whole-value replace -- there is no direct RW fetch,
                // and never per-field assignment (per-field managed write is FORBIDDEN -- snapshot
                // aliasing). HasManagedComponent<T> is called on `wv` (the concrete EntityRepository),
                // NOT ctx.SimulationViewVar -- it is PUBLIC and DIRECT there (see IrOp_HasComponent's
                // and IrOp_GetManagedComponentRO's doc comments), exactly like the unmanaged guard
                // above; only GetManagedComponentRO needs the interface cast (an explicitly-implemented
                // member), which is irrelevant here since this op never reads.
                string entity = $"__t{op.Entity.Index}";
                e.WriteLine($"var __t{idx} = {wv}.HasManagedComponent<global::{op.ComponentTypeFqn}>({entity});");
                if (op.Value is { } val)
                {
                    // Brace-less single-statement if (mirrors TerminatorEmitter's goto shape) -- the
                    // guard's ONLY job when a value IS wired is to skip a single ECB call, not a block.
                    e.WriteLine($"if (__t{idx})");
                    e.Indent();
                    e.WriteLine($"{ctx.EcbVar}.SetManagedComponent<global::{op.ComponentTypeFqn}>({entity}, __t{val.Index});");
                    e.Outdent();
                }
                // op.Value is null (the "Value" pin was left unwired): the guard line above is the
                // ENTIRE emit -- "Written" still reflects HasManagedComponent, but there is nothing to
                // write, so no `if` at all (not even an empty one).
                break;
            }

            // ------------------------------------------------------------------
            // ECS writes via ECB
            // ------------------------------------------------------------------

            case IrOp_AddComponent op:
                e.WriteLine($"ecb.AddComponent<global::{op.ComponentTypeFqn}>(__t{op.Entity.Index}, __t{op.Value.Index});");
                break;

            case IrOp_RemoveComponent op:
                e.WriteLine($"ecb.RemoveComponent<global::{op.ComponentTypeFqn}>(__t{op.Entity.Index});");
                break;

            case IrOp_DestroyEntity op:
                e.WriteLine($"ecb.DestroyEntity(__t{op.Entity.Index});");
                break;

            case IrOp_PublishEvent op:
            {
                e.WriteLine($"ecb.PublishEvent(new global::{op.EventTypeFqn}");
                e.WriteLine("{");
                e.Indent();
                for (int i = 0; i < op.Fields.Count; i++)
                {
                    var f = op.Fields[i];
                    var sep = i == op.Fields.Count - 1 ? "" : ",";
                    e.WriteLine($"{f.FieldName} = __t{f.Value.Index}{sep}");
                }
                e.Outdent();
                e.WriteLine("});");
                break;
            }

            case IrOp_ForEach op:
            {
                // P1 (GAP-1) -- inline bounded foreach. RosterValue is a `ref readonly` local from a
                // preceding IrOp_GetComponentRO; the curated Count/Item accessors take `in T` (the
                // readonly local binds implicitly). ItemVar is declared HERE inside the loop (it has
                // no defining statement of its own). Body statements were scheduled inline by Stage5.
                string roster  = $"__t{op.RosterValue.Index}";
                string loopVar = $"__fe{op.ItemVar.Index}";
                // "Count" out-pin (op.CountVar): hoist the element count into an OUTER-scope local and
                // reuse it as the loop bound (evaluated once). Otherwise re-evaluate inline each pass
                // (the original P1a shape -- keeps existing goldens byte-identical).
                string bound;
                if (op.CountVar is not null)
                {
                    e.WriteLine($"var __t{op.CountVar.Value.Index} = global::{op.CountAccessorFqn}({roster});");
                    bound = $"__t{op.CountVar.Value.Index}";
                }
                else
                {
                    bound = $"global::{op.CountAccessorFqn}({roster})";
                }
                e.WriteLine($"for (int {loopVar} = 0; {loopVar} < {bound}; {loopVar}++)");
                e.WriteLine("{");
                e.Indent();
                e.WriteLine($"var __t{op.ItemVar.Index} = global::{op.ItemAccessorFqn}({roster}, {loopVar});");
                // "CurrentIndex" out-pin (op.IndexVar): copy the loop counter into a body-scoped local so
                // body statements reference the 0-based index by the normal __t convention.
                if (op.IndexVar is not null)
                    e.WriteLine($"var __t{op.IndexVar.Value.Index} = {loopVar};");
                foreach (var bodyStmt in op.Body)
                    Emit(e, bodyStmt);
                e.Outdent();
                e.WriteLine("}");
                break;
            }

            case IrOp_If op:
            {
                // P1b (GAP-1) -- inline structured if/else nested inside an IrOp_ForEach body. The
                // Then/Else statement lists were scheduled inline by Stage5 (each up to the branch
                // join); emit them nested. The `else` block is omitted when Else is empty (the common
                // "conditional side-effect" shape, e.g. slice-4's `if (!arrived) AllAtBaseline=false;`).
                e.WriteLine($"if (__t{op.Condition.Index})");
                e.WriteLine("{");
                e.Indent();
                foreach (var thenStmt in op.Then)
                    Emit(e, thenStmt);
                e.Outdent();
                e.WriteLine("}");
                if (op.Else.Count > 0)
                {
                    e.WriteLine("else");
                    e.WriteLine("{");
                    e.Indent();
                    foreach (var elseStmt in op.Else)
                        Emit(e, elseStmt);
                    e.Outdent();
                    e.WriteLine("}");
                }
                break;
            }

            case IrOp_PublishBusEvent op:
            {
                string publishMethod = op.Managed ? "PublishManaged" : "Publish";
                e.WriteLine($"{wv}.Bus.{publishMethod}(new global::{op.EventTypeFqn}");
                e.WriteLine("{");
                e.Indent();
                for (int i = 0; i < op.Fields.Count; i++)
                {
                    var f = op.Fields[i];
                    var sep = i == op.Fields.Count - 1 ? "" : ",";
                    e.WriteLine($"{f.FieldName} = __t{f.Value.Index}{sep}");
                }
                e.Outdent();
                e.WriteLine("});");
                break;
            }

            // ------------------------------------------------------------------
            // Channel command
            // ------------------------------------------------------------------

            case IrOp_ChannelCommand op:
                ChannelCommandLowering.Emit(e, op);
                break;

            // ------------------------------------------------------------------
            // AN8: inline-latent non-channel action call
            // ------------------------------------------------------------------

            case IrOp_InlineActionCall op:
                InlineActionLowering.Emit(e, op, idx);
                break;

            // ------------------------------------------------------------------
            // Wait primitives -- should not reach Stage 7
            // ------------------------------------------------------------------

            case IrOp_WaitForChannel:
            case IrOp_WaitForEvent:
            case IrOp_LatentDelay:
                throw new InvalidOperationException(
                    "latent op reached Stage 7; should have been lowered in Stage 6");

            // ------------------------------------------------------------------
            // Instance cursor staleness check (Q-18.1)
            // ------------------------------------------------------------------

            case IrOp_CheckCursorVersion:
                e.WriteLine("if (s.Cursor.InstanceVersion != instanceVersion)");
                e.WriteLine("{");
                e.Indent();
                e.WriteLine("s.Cursor.ResumeAt = 0;");
                e.WriteLine("return;");
                e.Outdent();
                e.WriteLine("}");
                break;

            // ------------------------------------------------------------------
            // AiPrimitive working-state phase reads/writes (Stage 6 lowering)
            // ------------------------------------------------------------------

            case IrOp_WriteWorkingStatePhase op:
                e.WriteLine($"ws.__phase = {op.PhaseValue};");
                break;

            case IrOp_ReadWorkingStatePhase:
                if (idx >= 0) e.WriteLine($"byte __t{idx} = ws.__phase;");
                break;

            case IrOp_WriteWorkingStateWaitUntilTime op:
                e.WriteLine($"ws.__waitUntilTime = __t{op.Value.Index};");
                break;

            case IrOp_ReadWorkingStateWaitUntilTime:
                if (idx >= 0) e.WriteLine($"float __t{idx} = ws.__waitUntilTime;");
                break;

            // ------------------------------------------------------------------
            // Instance cursor reads/writes (Stage 6 lowering)
            // ------------------------------------------------------------------

            case IrOp_WriteCursorResumeAt op:
                e.WriteLine($"s.Cursor.ResumeAt = {op.ResumeAtValue};");
                break;

            case IrOp_ReadCursorResumeAt:
                if (idx >= 0) e.WriteLine($"uint __t{idx} = s.Cursor.ResumeAt;");
                break;

            case IrOp_WriteCursorInstanceVersion:
                e.WriteLine("s.Cursor.InstanceVersion = instanceVersion;");
                break;

            case IrOp_WriteCursorWaitUntilTime op:
                e.WriteLine($"s.Cursor.WaitUntilTime = __t{op.Seconds.Index};");
                break;

            case IrOp_ReadCursorWaitUntilTime:
                if (idx >= 0) e.WriteLine($"float __t{idx} = s.Cursor.WaitUntilTime;");
                break;

            // ------------------------------------------------------------------
            // Field read from a component ref (Stage 6 lowering)
            // ------------------------------------------------------------------

            case IrOp_FieldRead op:
                if (idx >= 0)
                {
                    // CA-05: a managed source (IrOp_GetManagedComponentRO's result) may legitimately be
                    // null (component absent -- see that op's doc comment), so project the field with a
                    // null-conditional + "?? default" instead of a bare member access. This keeps the
                    // read fail-safe/never-throw all the way through (never an NRE downstream of a
                    // missing managed component), mirroring the unmanaged read's tolerance of a missing
                    // component. Unaffected (bare access, byte-identical) when SourceIsManaged is false.
                    string rhs = op.SourceIsManaged
                        ? $"__t{op.Source.Index}?.{op.FieldName} ?? default"
                        : $"__t{op.Source.Index}.{op.FieldName}";
                    e.WriteLine($"var __t{idx} = {rhs};");
                }
                break;

            // ------------------------------------------------------------------
            // Component collection accessor call (CA-07b)
            // ------------------------------------------------------------------

            case IrOp_ComponentAccessorCall op:
                if (idx >= 0)
                {
                    // Component binds to the accessor's `in T` parameter implicitly -- it is the
                    // `ref readonly` local a preceding IrOp_GetComponentRO produced, same as
                    // IrOp_ForEach's own accessor calls. Index is present only for the Item shape.
                    string comp = $"__t{op.Component.Index}";
                    string args = op.Index is not null ? $"{comp}, __t{op.Index.Value.Index}" : comp;
                    e.WriteLine($"var __t{idx} = global::{op.AccessorFqn}({args});");
                }
                break;

            case IrOp_ComponentCollectionSearch op:
            {
                // CA-07d-1 -- bounded linear search sharing IrOp_ForEach's curated Count/Item accessors.
                // Declare the result(s) first (so they outlive the loop scope), then walk the collection
                // and short-circuit on the first EqualityComparer match. Loop locals (__cs*) are scoped
                // to the for-statement, so multiple searches in one block never collide.
                string comp  = $"__t{op.Component.Index}";
                string query = $"__t{op.Query.Index}";
                string eq    = $"global::System.Collections.Generic.EqualityComparer<global::{op.ElementTypeFqn}>.Default";

                if (op.ContainsResult is not null) e.WriteLine($"var __t{op.ContainsResult.Value.Index} = false;");
                if (op.FindIndex is not null)      e.WriteLine($"var __t{op.FindIndex.Value.Index} = -1;");
                if (op.FindFound is not null)      e.WriteLine($"var __t{op.FindFound.Value.Index} = false;");

                e.WriteLine($"for (int __csI = 0, __csN = global::{op.CountAccessorFqn}({comp}); __csI < __csN; __csI++)");
                e.WriteLine("{");
                e.Indent();
                e.WriteLine($"if ({eq}.Equals(global::{op.ItemAccessorFqn}({comp}, __csI), {query}))");
                e.WriteLine("{");
                e.Indent();
                if (op.ContainsResult is not null) e.WriteLine($"__t{op.ContainsResult.Value.Index} = true;");
                if (op.FindIndex is not null)      e.WriteLine($"__t{op.FindIndex.Value.Index} = __csI;");
                if (op.FindFound is not null)      e.WriteLine($"__t{op.FindFound.Value.Index} = true;");
                e.WriteLine("break;");
                e.Outdent();
                e.WriteLine("}");
                e.Outdent();
                e.WriteLine("}");
                break;
            }

            // ------------------------------------------------------------------
            // Compare (GAP-12) -- native comparison node lowering
            // ------------------------------------------------------------------

            case IrOp_Compare op:
                if (idx >= 0)
                {
                    string infix = ComparisonOperatorInfix(op.Op);
                    e.WriteLine($"var __t{idx} = __t{op.Left.Index} {infix} __t{op.Right.Index};");
                }
                break;

            // ------------------------------------------------------------------
            // BinaryOp -- native arithmetic node lowering (Compare's arithmetic sibling)
            // ------------------------------------------------------------------

            case IrOp_BinaryOp op:
                if (idx >= 0)
                {
                    string infix = ArithmeticOperatorInfix(op.Op);
                    e.WriteLine($"var __t{idx} = __t{op.Left.Index} {infix} __t{op.Right.Index};");
                }
                break;

            // ------------------------------------------------------------------
            // BooleanOp / Not -- native boolean logic node lowering (Compare's boolean siblings)
            // ------------------------------------------------------------------

            case IrOp_BooleanOp op:
                if (idx >= 0)
                {
                    string infix = BooleanOperatorInfix(op.Op);
                    e.WriteLine($"var __t{idx} = __t{op.Left.Index} {infix} __t{op.Right.Index};");
                }
                break;

            case IrOp_Not op:
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = !__t{op.Operand.Index};");
                break;

            // ------------------------------------------------------------------
            // Debug probes (Debug/Trace modes only)
            // ------------------------------------------------------------------

            // Entity-scoped debug probes reference `self`, which only exists in
            // AiPrimitive/Instance methods — never in stateless Library functions
            // (emitting it there produces uncompilable C#, CS0103). Suppress in Library scope.
            case IrOp_DebugProbe_NodeEnter op:
                if (e.Ctx.Mode != Hrot.Blueprints.Core.Compiler.CompilerMode.Release && e.Ctx.HasSelfInScope)
                    e.WriteLine($"global::Hrot.Blueprints.Core.Debug.DebugProbe.NodeEnter(self, \"{op.NodeId:D}\");");
                break;

            case IrOp_DebugProbe_PinValue op:
                if (e.Ctx.Mode != Hrot.Blueprints.Core.Compiler.CompilerMode.Release && e.Ctx.HasSelfInScope)
                    e.WriteLine($"global::Hrot.Blueprints.Core.Debug.DebugProbe.PinValueChanged(self, \"{op.PinId:N}\", __t{op.Value.Index});");
                break;

            // ------------------------------------------------------------------
            // WhenNode lowering ops
            // ------------------------------------------------------------------

            case IrOp_WhenValueChangedCheck op:
            {
                // Read the component (SelfComponent source only for M2)
                e.WriteLine($"ref readonly var __t{idx}_comp = ref {wv}.GetComponentRO<global::{op.ComponentFqn}>(self);");
                e.WriteLine($"var __t{idx}_cur = __t{idx}_comp.{op.PropertyPath};");

                // Compare against previous state
                if (op.Epsilon == 0f)
                {
                    // Direct equality (bool, int, enum)
                    e.WriteLine($"bool __t{idx}_changed = __t{idx}_cur != {sv}.{op.SynthFieldName};");
                }
                else
                {
                    // Vector or float epsilon comparison
                    bool isVector2 = op.FieldCSharpType.Contains("Vector2");
                    bool isVector3 = op.FieldCSharpType.Contains("Vector3");
                    if (isVector2 || isVector3)
                    {
                        e.WriteLine($"bool __t{idx}_changed = " +
                            $"(__t{idx}_cur - {sv}.{op.SynthFieldName}).LengthSquared() > " +
                            $"({op.Epsilon}f * {op.Epsilon}f);");
                    }
                    else
                    {
                        e.WriteLine($"bool __t{idx}_changed = " +
                            $"global::System.MathF.Abs(__t{idx}_cur - {sv}.{op.SynthFieldName}) > " +
                            $"{op.Epsilon}f;");
                    }
                }

                if (idx >= 0) e.WriteLine($"bool __t{idx} = __t{idx}_changed;");
                break;
            }

            case IrOp_WhenStorePrev op:
            {
                // Re-read the component to get the current value (avoids cross-block variable scope).
                // This re-read always returns the same value within a single tick.
                e.WriteLine($"{{");
                e.Indent();
                e.WriteLine($"ref readonly var __storePrev_comp = ref {wv}.GetComponentRO<global::{op.ComponentFqn}>(self);");
                e.WriteLine($"{sv}.{op.SynthFieldName} = __storePrev_comp.{op.PropertyPath};");
                e.Outdent();
                e.WriteLine($"}}");
                break;
            }

            case IrOp_WhenEventFiredCheck op:
            {
                var evtShort = op.EventFqn.Split('.').Last();

                bool hasFilters = op.FilterSelf || op.PayloadFieldPath is not null;

                if (!hasFilters)
                {
                    // Fast path: check whether any events of this type arrived this frame
                    if (idx >= 0)
                        e.WriteLine($"bool __t{idx} = view.ReadEvents<global::{op.EventFqn}>().Length > 0;");
                }
                else
                {
                    // Full scan path: iterate events and apply filters
                    if (idx >= 0)
                    {
                        e.WriteLine($"bool __t{idx};");
                        e.WriteLine("{");
                        e.Indent();
                        e.WriteLine($"var __events_{evtShort} = view.ReadEvents<global::{op.EventFqn}>();");
                        e.WriteLine($"bool __matched_{evtShort} = false;");
                        e.WriteLine($"for (int __i = 0; __i < __events_{evtShort}.Length; __i++)");
                        e.WriteLine("{");
                        e.Indent();
                        e.WriteLine($"var __ev = __events_{evtShort}[__i];");

                        if (op.FilterSelf)
                            e.WriteLine($"if (__ev.{op.TargetFieldName} != self) continue;");

                        if (op.PayloadFieldPath is not null && op.PayloadOperatorCSharp is not null && op.PayloadValueLiteral is not null)
                            e.WriteLine($"if (!(__ev.{op.PayloadFieldPath} {op.PayloadOperatorCSharp} {op.PayloadValueLiteral})) continue;");

                        e.WriteLine($"__matched_{evtShort} = true;");
                        e.WriteLine("break;");
                        e.Outdent();
                        e.WriteLine("}");
                        e.WriteLine($"__t{idx} = __matched_{evtShort};");
                        e.Outdent();
                        e.WriteLine("}");
                    }
                }
                break;
            }

            case IrOp_WhenConditionMetCheck op:
            {
                // Extract the 8-char hex id from "_when_{id8}_prev"
                const string pfx = "_when_";
                const string sfx = "_prev";
                string id8 = op.SynthFieldName.StartsWith(pfx) && op.SynthFieldName.EndsWith(sfx)
                    ? op.SynthFieldName.Substring(pfx.Length,
                        op.SynthFieldName.Length - pfx.Length - sfx.Length)
                    : op.SynthFieldName;

                e.WriteLine($"// BEGIN WhenNode {id8}: Condition Met");
                e.WriteLine($"if (_whenCondPred_{id8} != null)");
                e.WriteLine("{");
                e.Indent();
                e.WriteLine($"bool __cur_{id8} = _whenCondPred_{id8}({wv}, self);");
                e.WriteLine($"bool __prev_{id8} = {sv}.{op.SynthFieldName};");
                e.WriteLine($"{sv}.{op.SynthFieldName} = __cur_{id8};");

                if (op.OnFiredBlock.HasValue)
                    e.WriteLine($"if (__cur_{id8} && !__prev_{id8}) goto __block_{ctx.LabelForBlock(op.OnFiredBlock.Value)};");

                if (op.OnEndedBlock.HasValue)
                    e.WriteLine($"if (!__cur_{id8} && __prev_{id8}) goto __block_{ctx.LabelForBlock(op.OnEndedBlock.Value)};");

                e.Outdent();
                e.WriteLine("}");
                e.WriteLine($"// END WhenNode {id8}: Condition Met (no branch taken -> fall to out)");
                // No result value emitted -- ResultValue is null; block terminator is IrTerm_Goto.
                break;
            }

            case IrOp_WhenEqsResultCheck op:
            {
                string id8 = ExtractId8FromFieldName(op.SynthFieldName);

                e.WriteLine($"// BEGIN WhenNode {id8}: EqsResult / {op.Trigger} / {(op.OnFiredBlock.HasValue ? "RisingEdge" : "")}{(op.OnEndedBlock.HasValue ? "FallingEdge" : "")}");
                e.WriteLine("{");
                e.Indent();

                e.WriteLine($"ref var prev = ref {sv}.{op.SynthFieldName};");
                e.WriteLine($"ref readonly var handle = ref {sv}.{op.SensorVariableName};");
                e.WriteLine();
                e.WriteLine($"if (!{wv}.IsAlive(handle.ChildId))");
                e.Indent();
                e.WriteLine($"goto whenNode_{id8}_end;");
                e.Outdent();
                e.WriteLine();

                switch (op.Trigger)
                {
                    case "TopChanged":
                        EmitEqsTopChanged(e, op, id8, wv, sv);
                        break;
                    case "FirstReady":
                        EmitEqsFirstReady(e, op, id8, wv, sv);
                        break;
                    case "ScoreCrossed":
                        EmitEqsScoreCrossed(e, op, id8, wv, sv);
                        break;
                    case "BecomesStale":
                        EmitEqsBecomesStale(e, op, id8, wv, sv);
                        break;
                }

                e.WriteLine();
                e.WriteLine($"whenNode_{id8}_end: ;");
                e.Outdent();
                e.WriteLine("}");
                e.WriteLine($"// END WhenNode {id8}");
                break;
            }

            case IrOp_ReadEqsResult op:
            {
                // Emit the helper method call; result is cached in a local struct variable.
                // Downstream IrOp_FieldRead ops access individual fields.
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = ReadEqsResult_{op.NodeId8}(ref {sv}, {wv}, __t{op.ResultIndexValue.Index});");
                break;
            }

            case IrOp_ScoreDecision op:
            {
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = ScoreDecision_{op.NodeId8}({wv}, self, time);");
                break;
            }

            case IrOp_ReadRankedResult op:
            {
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = ReadRankedResult_{op.NodeId8}({wv}, self);");
                break;
            }

            case IrOp_SpawnEqsSensor op:
            {
                // Emit ECB-based spawn pattern per DESIGN §7.8
                // Result value (idx) holds the spawned EqsSensorHandle.
                string localHandle = idx >= 0 ? $"__t{idx}" : "_spawnHandle";

                string searchRadius    = op.SearchRadiusValue    is not null ? $"__t{op.SearchRadiusValue.Value.Index}"    : "0f";
                string factionFilter   = op.FactionFilterValue   is not null ? $"__t{op.FactionFilterValue.Value.Index}"   : "0u";
                string threatThreshold = op.ThreatThresholdValue is not null ? $"__t{op.ThreatThresholdValue.Value.Index}" : "0f";
                string publishPolicy   = op.PublishPolicyValue   is not null ? $"(byte)__t{op.PublishPolicyValue.Value.Index}" : "(byte)0";
                string priority        = op.PriorityValue        is not null ? $"(byte)__t{op.PriorityValue.Value.Index}"  : "(byte)0";

                // Declare the result handle BEFORE the scope block so it is visible downstream.
                if (idx >= 0)
                    e.WriteLine($"global::FDP.Eqs.EqsSensorHandle __t{idx} = default;");
                e.WriteLine("// BEGIN SpawnEqsSensorNode");
                e.WriteLine("{");
                e.Indent();
                e.WriteLine($"var _spawnChild = ecb.CreateEntity();");
                e.WriteLine($"ecb.AddComponent(_spawnChild, new global::Fdp.Toolkit.Replication.Components.PartMetadata");
                e.WriteLine("{");
                e.Indent();
                e.WriteLine($"ParentEntity      = self,");
                e.WriteLine($"InstanceId        = {op.BakedInstanceId},");
                e.WriteLine($"DescriptorOrdinal = 0,");
                e.Outdent();
                e.WriteLine("});");
                e.WriteLine($"ecb.AddComponent(_spawnChild, new global::Fdp.Toolkit.Spatial.Eqs.EqsSensor");
                e.WriteLine("{");
                e.Indent();
                e.WriteLine($"BlueprintId     = {op.TemplateBlueprintIdLiteral},");
                e.WriteLine($"Epoch           = 1u,");
                e.WriteLine($"SearchRadius    = {searchRadius},");
                e.WriteLine($"FactionFilter   = {factionFilter},");
                e.WriteLine($"ThreatThreshold = {threatThreshold},");
                e.WriteLine($"PublishPolicy   = {publishPolicy},");
                e.WriteLine($"Priority        = {priority},");
                e.Outdent();
                e.WriteLine("});");
                e.WriteLine($"ecb.AddComponent(_spawnChild, new global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer());");
                if (idx >= 0)
                    e.WriteLine($"__t{idx} = new global::FDP.Eqs.EqsSensorHandle(_spawnChild);");
                e.Outdent();
                e.WriteLine("}");
                e.WriteLine("// END SpawnEqsSensorNode");
                break;
            }

            default:
                throw new NotSupportedException(
                    $"Unsupported IrOperation in StatementEmitter: {stmt.Operation.GetType().Name}");
        }
    }

    private static void EmitEqsTopChanged(CSharpEmitter e, IrOp_WhenEqsResultCheck op, string id8, string wv, string sv)
    {
        e.WriteLine($"ref readonly var sensor = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsSensor>(handle.ChildId);");
        e.WriteLine($"ref readonly var buffer = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
        e.WriteLine();
        e.WriteLine($"if (sensor.Epoch != prev.LastEvaluatedEpoch)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"if (buffer.IsReady)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"var results = buffer.GetSpanRO();");
        e.WriteLine($"if (results.Length > 0)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"var top = results[0];");
        // Positional candidates (EntityId == 0) need a stable identity for change detection.
        // System.HashCode.Combine is seeded per-process in .NET Core, so it produces different
        // values on different Brain/IG nodes and across hot-reloads — firing spurious TopChanged
        // events. Pack the two float bit-patterns into the long instead: fully deterministic and
        // collision-free for distinct (X, Y) pairs.
        e.WriteLine($"long currentTopId = top.EntityId != 0L");
        e.WriteLine($"    ? top.EntityId");
        e.WriteLine($"    : unchecked((long)(((ulong)(uint)global::System.BitConverter.SingleToInt32Bits(top.PositionX) << 32) | (uint)global::System.BitConverter.SingleToInt32Bits(top.PositionY)));");
        e.WriteLine();
        e.WriteLine($"if (currentTopId != prev.PrevTopId && prev.LastEvaluatedEpoch != 0)");
        e.WriteLine("{");
        e.Indent();
        if (op.OnFiredBlock.HasValue)
            e.WriteLine($"goto __block_{e.Ctx.LabelForBlock(op.OnFiredBlock.Value)};");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine();
        e.WriteLine($"prev.PrevTopId    = currentTopId;");
        e.WriteLine($"prev.PrevTopScore = top.Score;");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine("else");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"prev.PrevTopId    = 0L;");
        e.WriteLine($"prev.PrevTopScore = 0f;");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine($"prev.LastEvaluatedEpoch = sensor.Epoch;");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitEqsFirstReady(CSharpEmitter e, IrOp_WhenEqsResultCheck op, string id8, string wv, string sv)
    {
        // FirstReady fires the first time buffer.IsReady becomes true.
        // Uses LastEvaluatedEpoch as a "has fired" sentinel (0 = not fired, 1 = fired).
        // No sensor Epoch gating needed here.
        e.WriteLine($"ref readonly var buffer = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
        e.WriteLine();
        e.WriteLine($"if (buffer.IsReady && prev.LastEvaluatedEpoch == 0)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"prev.LastEvaluatedEpoch = 1u;");  // mark fired BEFORE goto to prevent re-fire
        if (op.OnFiredBlock.HasValue)
            e.WriteLine($"goto __block_{e.Ctx.LabelForBlock(op.OnFiredBlock.Value)};");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitEqsScoreCrossed(CSharpEmitter e, IrOp_WhenEqsResultCheck op, string id8, string wv, string sv)
    {
        e.WriteLine($"ref readonly var sensor = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsSensor>(handle.ChildId);");
        e.WriteLine($"ref readonly var buffer = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
        e.WriteLine();
        e.WriteLine($"if (sensor.Epoch != prev.LastEvaluatedEpoch)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"if (buffer.IsReady)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"var results = buffer.GetSpanRO();");
        e.WriteLine($"if (results.Length > 0)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"float currentScore = results[0].Score;");
        e.WriteLine($"bool wasAbove = prev.PrevTopScore >= _whenScoreThreshold_{id8};");
        e.WriteLine($"bool isAbove  = currentScore      >= _whenScoreThreshold_{id8};");
        e.WriteLine();
        e.WriteLine($"if (!wasAbove && isAbove && prev.LastEvaluatedEpoch != 0)");
        e.WriteLine("{");
        e.Indent();
        if (op.OnFiredBlock.HasValue)
            e.WriteLine($"goto __block_{e.Ctx.LabelForBlock(op.OnFiredBlock.Value)};");
        e.Outdent();
        e.WriteLine("}");
        if (op.OnEndedBlock.HasValue)
        {
            e.WriteLine($"else if (wasAbove && !isAbove && prev.LastEvaluatedEpoch != 0)");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"goto __block_{e.Ctx.LabelForBlock(op.OnEndedBlock.Value)};");
            e.Outdent();
            e.WriteLine("}");
        }
        e.WriteLine();
        e.WriteLine($"prev.PrevTopScore = currentScore;");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine($"prev.LastEvaluatedEpoch = sensor.Epoch;");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitEqsBecomesStale(CSharpEmitter e, IrOp_WhenEqsResultCheck op, string id8, string wv, string sv)
    {
        // BecomesStale: no sensor Epoch gate, no EqsSensor component read.
        // PrevStaleCheckTime stores the PREVIOUS age (time - buffer.LastUpdateTimeSeconds from last tick),
        // NOT the raw sim time. Initial value 0 means "first check; prevAge = 0 → wasStale = false".
        // This avoids spurious OnEnded fires on the first tick when the buffer starts old.
        e.WriteLine($"ref readonly var buffer = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
        e.WriteLine();
        e.WriteLine($"if (buffer.IsReady)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"float age     = time - buffer.LastUpdateTimeSeconds;");
        e.WriteLine($"float prevAge = prev.PrevStaleCheckTime;  // previous age, stored from last tick");
        e.WriteLine();
        e.WriteLine($"bool wasStale = prevAge > _whenMaxAge_{id8};");
        e.WriteLine($"bool isStale  = age     > _whenMaxAge_{id8};");
        e.WriteLine();
        e.WriteLine($"if (!wasStale && isStale)");
        e.WriteLine("{");
        e.Indent();
        if (op.OnFiredBlock.HasValue)
            e.WriteLine($"goto __block_{e.Ctx.LabelForBlock(op.OnFiredBlock.Value)};");
        e.Outdent();
        e.WriteLine("}");
        if (op.OnEndedBlock.HasValue)
        {
            e.WriteLine($"else if (wasStale && !isStale)");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"goto __block_{e.Ctx.LabelForBlock(op.OnEndedBlock.Value)};");
            e.Outdent();
            e.WriteLine("}");
        }
        e.WriteLine();
        e.WriteLine($"prev.PrevStaleCheckTime = age;  // store current age (NOT sim time)");
        e.Outdent();
        e.WriteLine("}");
    }

    private static string ExtractId8FromFieldName(string synthFieldName)
    {
        const string prefix = "_when_";
        const string suffix = "_prev";
        if (synthFieldName.StartsWith(prefix) && synthFieldName.EndsWith(suffix))
            return synthFieldName.Substring(prefix.Length,
                synthFieldName.Length - prefix.Length - suffix.Length);
        return synthFieldName;
    }

    /// <summary>
    /// GAP-12 -- <see cref="ComparisonOperator"/> -> C# infix operator text for <c>IrOp_Compare</c>.
    /// Shares the same six mappings as the existing op_&lt;Op&gt;_&lt;Type&gt; synthesized-operator
    /// infix map used by <see cref="TryGetSynthesizedOpInfix"/> above (Eq/NotEq/LessThan/
    /// LessThanOrEqual/GreaterThan/GreaterThanOrEqual -> ==/!=/&lt;/&lt;=/&gt;/&gt;=), extracted
    /// here as a direct enum switch since <c>CompareNode</c> carries a real
    /// <see cref="ComparisonOperator"/> value rather than a synthesized method-name string.
    /// </summary>
    private static string ComparisonOperatorInfix(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal              => "==",
        ComparisonOperator.NotEqual           => "!=",
        ComparisonOperator.LessThan           => "<",
        ComparisonOperator.LessThanOrEqual    => "<=",
        ComparisonOperator.GreaterThan        => ">",
        ComparisonOperator.GreaterThanOrEqual => ">=",
        _ => "==",
    };

    /// <summary>
    /// <see cref="ArithmeticOperator"/> -> C# infix operator text for <c>IrOp_BinaryOp</c>
    /// (Compare's arithmetic sibling). Mirrors <see cref="ComparisonOperatorInfix"/>'s shape as a
    /// direct enum switch since <c>BinaryOpNode</c> carries a real <see cref="ArithmeticOperator"/>
    /// value rather than a synthesized method-name string.
    /// </summary>
    private static string ArithmeticOperatorInfix(ArithmeticOperator op) => op switch
    {
        ArithmeticOperator.Add      => "+",
        ArithmeticOperator.Subtract => "-",
        ArithmeticOperator.Multiply => "*",
        ArithmeticOperator.Divide   => "/",
        ArithmeticOperator.Modulo   => "%",
        _ => "+",
    };

    /// <summary>
    /// <see cref="BooleanOperator"/> -> C# infix operator text for <c>IrOp_BooleanOp</c>
    /// (Compare's boolean sibling). Mirrors <see cref="ArithmeticOperatorInfix"/>'s shape as a
    /// direct enum switch since <c>BooleanOpNode</c> carries a real <see cref="BooleanOperator"/>
    /// value rather than a synthesized method-name string.
    /// </summary>
    private static string BooleanOperatorInfix(BooleanOperator op) => op switch
    {
        BooleanOperator.And => "&&",
        BooleanOperator.Or  => "||",
        _ => "&&",
    };

    /// <summary>
    /// P7 -- appends the in-scope `self`/read-only-view identifiers (in that order) to an
    /// already-built call argument list, per <see cref="IrOp_PureCall.AppendSelfArg"/> /
    /// <see cref="IrOp_LibraryCall.AppendSelfArg"/>. No-op (returns <paramref name="argList"/>
    /// unchanged) when neither flag is set -- existing FunctionCall emission stays byte-identical.
    /// `self` is always the bare identifier; the view expression is <see cref="EmissionContext.ViewVar"/>
    /// (read-only; never the write-capable <c>EntityRepository</c> cast used by
    /// <see cref="EmissionContext.WorldVar"/>). Safe to call unconditionally: Stage 5 never sets
    /// either flag for a Library-dispatch asset (no self/view in scope there), so this never emits
    /// an undefined identifier.
    /// </summary>
    private static string AppendContextArgs(string argList, EmissionContext ctx, bool appendSelf, bool appendView)
    {
        if (!appendSelf && !appendView)
            return argList;

        var extra = new List<string>(2);
        if (appendSelf) extra.Add("self");
        if (appendView) extra.Add(ctx.ViewVar);
        var extraStr = string.Join(", ", extra);

        return argList.Length == 0 ? extraStr : $"{argList}, {extraStr}";
    }

    /// <summary>
    /// Attempts to map a synthesized operator method name (e.g. "op_Eq_Byte") produced by
    /// WaitLowering stages to a native C# infix expression.  These are NOT real global methods
    /// and must never be emitted as <c>global::op_Eq_Byte(...)</c>.
    /// Returns true and sets <paramref name="infixExpr"/> when the method is a known synthesized op.
    /// </summary>
    private static bool TryGetSynthesizedOpInfix(
        string methodFqn,
        IReadOnlyList<IrValue> args,
        out string? infixExpr)
    {
        // Only intercept the well-known op_<Operation>_<Type> pattern.
        // Real FQN methods always contain at least one '.' separator.
        if (methodFqn.IndexOf('.') >= 0)
        {
            infixExpr = null;
            return false;
        }

        if (!methodFqn.StartsWith("op_", StringComparison.Ordinal))
        {
            infixExpr = null;
            return false;
        }

        // Extract the operation part (everything between "op_" and the final "_<Type>" suffix).
        // e.g. "op_Eq_Byte" -> operation = "Eq", "op_LessThan_Single" -> operation = "LessThan"
        // Strategy: split on '_', skip first token "op", last token is type, remainder is op name.
        var parts = methodFqn.Split('_');
        if (parts.Length < 3)
        {
            infixExpr = null;
            return false;
        }

        // parts[0] = "op", parts[1..^1] = operation words, parts[^1] = type suffix
        var operationWords = new System.ArraySegment<string>(parts, 1, parts.Length - 2);
        string operation = string.Join("", operationWords.Array!
            .Skip(operationWords.Offset).Take(operationWords.Count));

        string? infix = operation switch
        {
            "Eq"                  => "==",
            "NotEq"               => "!=",
            "LessThan"            => "<",
            "LessThanOrEqual"     => "<=",
            "GreaterThan"         => ">",
            "GreaterThanOrEqual"  => ">=",
            "Add"                 => "+",
            "Sub"                 => "-",
            "Mul"                 => "*",
            "Div"                 => "/",
            _                     => null,
        };

        if (infix == null)
        {
            infixExpr = null;
            return false;
        }

        if (args.Count == 2)
        {
            // The type suffix of the op name (last segment after the final '_').
            string typeSuffix = parts[parts.Length - 1];

            // NodeStatus comparison: both operands are now global::Fbt.NodeStatus (the emitted
            // WaitLowering constants also use global::Fbt.NodeStatus since the FQN prefix fix).
            // The (int) casts are therefore defensive/redundant but kept to avoid golden churn.
            if (typeSuffix == "NodeStatus" && (infix == "==" || infix == "!="))
            {
                infixExpr = $"((int)__t{args[0].Index} {infix} (int)__t{args[1].Index})";
                return true;
            }

            infixExpr = $"(__t{args[0].Index} {infix} __t{args[1].Index})";
            return true;
        }

        if (args.Count == 1)
        {
            // Unary (e.g. op_Not_Bool) — emit prefix operator.
            infixExpr = $"({infix}__t{args[0].Index})";
            return true;
        }

        infixExpr = null;
        return false;
    }

    internal static string TypeRefToCSharp(IrTypeRef t)
    {
        if (t.IsArray)
            return TypeRefToCSharp(t.ElementType!) + "[]";
        return t.FullName switch
        {
            "System.Boolean"  => "bool",
            "System.Byte"     => "byte",
            "System.Int16"    => "short",
            "System.Int32"    => "int",
            "System.Int64"    => "long",
            "System.UInt32"   => "uint",
            "System.Single"   => "float",
            "System.Double"   => "double",
            "System.Void"     => "void",
            "Fdp.Core.Entity" => "global::Fdp.Core.Entity",
            _ when t.FullName.StartsWith("_") => t.FullName, // local generated type (synthesized struct)
            _                                  => $"global::{t.FullName}",
        };
    }
}
