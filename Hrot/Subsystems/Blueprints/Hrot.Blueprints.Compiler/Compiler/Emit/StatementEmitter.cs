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
                if (idx >= 0) e.WriteLine($"var __t{idx} = {op.CSharpLiteral};");
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
                var argList = string.Join(", ", op.Args.Select(a => $"__t{a.Index}"));
                var call = $"global::{op.MethodFqn}({argList})";
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = {call};");
                else
                    e.WriteLine($"{call};");
                break;
            }

            case IrOp_LibraryCall op:
            {
                var libClass = ctx.ResolveLibraryClass(op.LibraryBlueprintId);
                var argList = string.Join(", ", op.Args.Select(a => $"__t{a.Index}"));
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

            // ------------------------------------------------------------------
            // Custom events
            // ------------------------------------------------------------------

            case IrOp_RaiseCustomEvent op:
            {
                var evtName = ctx.CustomEventName(op.CustomEventIndex);
                var argList = string.Join(", ", op.Args.Select(a => $"__t{a.Index}"));
                e.WriteLine($"// RaiseCustomEvent: {evtName}({argList})");
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
                e.WriteLine($"Event_{graphName}(ref s, view, ecb, self, time, deltaTime);");
                e.Outdent();
                e.WriteLine("}");
                break;
            }

            // ------------------------------------------------------------------
            // ECS reads
            // ------------------------------------------------------------------

            case IrOp_HasComponent op:
                if (idx >= 0)
                    e.WriteLine($"var __t{idx} = {wv}.HasComponent<global::{op.ComponentTypeFqn}>(__t{op.Entity.Index});");
                break;

            case IrOp_GetComponent op:
                if (idx >= 0)
                    e.WriteLine($"ref var __t{idx} = ref {wv}.GetComponentRW<global::{op.ComponentTypeFqn}>(__t{op.Entity.Index});");
                break;

            case IrOp_GetComponentRO op:
                if (idx >= 0)
                    e.WriteLine($"ref readonly var __t{idx} = ref {wv}.GetComponentRO<global::{op.ComponentTypeFqn}>(__t{op.Entity.Index});");
                break;

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

            // ------------------------------------------------------------------
            // Channel command
            // ------------------------------------------------------------------

            case IrOp_ChannelCommand op:
                ChannelCommandLowering.Emit(e, op);
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

            // ------------------------------------------------------------------
            // Field read from a component ref (Stage 6 lowering)
            // ------------------------------------------------------------------

            case IrOp_FieldRead op:
                if (idx >= 0) e.WriteLine($"var __t{idx} = __t{op.Source.Index}.{op.FieldName};");
                break;

            // ------------------------------------------------------------------
            // Debug probes (Debug/Trace modes only)
            // ------------------------------------------------------------------

            case IrOp_DebugProbe_NodeEnter op:
                if (e.Ctx.Mode != global::Fdp.Toolkit.Blueprints.CompilerMode.Release)
                    e.WriteLine($"// [DebugProbe] NodeEnter {op.NodeId} ({op.NodeKind})");
                break;

            case IrOp_DebugProbe_PinValue op:
                if (e.Ctx.Mode != global::Fdp.Toolkit.Blueprints.CompilerMode.Release)
                    e.WriteLine($"// [DebugProbe] PinValue {op.PinId} = __t{op.Value.Index} ({op.PinName})");
                break;

            default:
                throw new NotSupportedException(
                    $"Unsupported IrOperation in StatementEmitter: {stmt.Operation.GetType().Name}");
        }
    }

    internal static string TypeRefToCSharp(IrTypeRef t)
    {
        if (t.IsArray)
            return TypeRefToCSharp(t.ElementType!) + "[]";
        return t.FullName switch
        {
            "System.Boolean" => "bool",
            "System.Byte"    => "byte",
            "System.Int16"   => "short",
            "System.Int32"   => "int",
            "System.Int64"   => "long",
            "System.UInt32"  => "uint",
            "System.Single"  => "float",
            "System.Double"  => "double",
            "System.Void"    => "void",
            "Fdp.Core.Entity" => "global::Fdp.Core.Entity",
            _                => $"global::{t.FullName}",
        };
    }
}
