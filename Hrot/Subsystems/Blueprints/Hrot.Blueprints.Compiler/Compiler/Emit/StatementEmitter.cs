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
                if (e.Ctx.Mode != Hrot.Blueprints.Core.Compiler.CompilerMode.Release)
                    e.WriteLine($"// [DebugProbe] NodeEnter {op.NodeId} ({op.NodeKind})");
                break;

            case IrOp_DebugProbe_PinValue op:
                if (e.Ctx.Mode != Hrot.Blueprints.Core.Compiler.CompilerMode.Release)
                    e.WriteLine($"// [DebugProbe] PinValue {op.PinId} = __t{op.Value.Index} ({op.PinName})");
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
                    // Float epsilon comparison
                    e.WriteLine($"bool __t{idx}_changed = global::System.MathF.Abs(__t{idx}_cur - {sv}.{op.SynthFieldName}) > {op.Epsilon}f;");
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
        e.WriteLine($"long currentTopId = top.EntityId != 0L");
        e.WriteLine($"    ? top.EntityId");
        e.WriteLine($"    : (long)global::System.HashCode.Combine(top.PositionX, top.PositionY);");
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
