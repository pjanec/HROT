using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

internal static class AiPrimitiveEmitter
{
    public static void EmitClass(CSharpEmitter e, IrAsset asset)
    {
        var className = $"{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";

        e.WriteLine("namespace Hrot.AI.Behaviors.Generated;");
        e.WriteLine();
        e.WriteLine($"public static class {className}");
        e.WriteLine("{");
        e.Indent();

        e.WriteLine($"public const int BlueprintId = unchecked((int)0x{asset.BlueprintId:X8});");
        e.WriteLine($"public const ulong StructureHash = {asset.StructureHash}UL;");
        e.WriteLine();

        EmitParamsStruct(e, asset);
        e.WriteLine();

        EmitWorkingStateStruct(e, asset);
        e.WriteLine();

        EmitInitDefault(e, asset);
        e.WriteLine();

        EmitTickCore(e, asset);
        e.WriteLine();

        EmitThunks(e, asset, className);

        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitParamsStruct(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        e.WriteLine("public struct Params");
        e.WriteLine("{");
        e.Indent();
        
        foreach (var f in asset.Parameters)
        {
            EmitComment(e, f.Comment);
            EmitStructField(e, CSharpType(f.Type), f.Name);
        }
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitWorkingStateStruct(CSharpEmitter e, IrAsset asset)
    {
        // FC-2/LV-1b (Q#19-E): a WorkingState field may be a fixed-capacity list -- emit the same
        // per-class nested wrapper structs the Instance State path uses (additive: assets without
        // list fields emit byte-identical output).
        InstanceEmitter.EmitListWrappers(e, asset.WorkingState);
        e.WriteLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        e.WriteLine("public struct WorkingState");
        e.WriteLine("{");
        e.Indent();

        foreach (var f in asset.WorkingState)
        {
            EmitComment(e, f.Comment);
            EmitStructField(e, CSharpType(f.Type), f.Name);
        }
        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// Emits one Sequential-layout struct field. Bool fields get <c>[MarshalAs(UnmanagedType.I1)]</c>:
    /// <c>Marshal.SizeOf</c>/<c>OffsetOf</c> default a bool to a 4-byte WIN32 BOOL, whereas the runtime
    /// <c>Unsafe.As</c> projection (and the host bin-packer's size math) treat it as 1 byte. Without I1
    /// the two models disagree, silently drifting offsets and corrupting AAR replay / partition-slot
    /// layout. Applies to both Params (inline, bin-packed at a baked offset) and WorkingState (partition
    /// slot sized at runtime via <c>Marshal.SizeOf&lt;WorkingState&gt;()</c>).
    /// </summary>
    private static void EmitStructField(CSharpEmitter e, string csType, string name)
    {
        if (csType == "bool")
            e.WriteLine("[global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.I1)]");
        e.WriteLine($"public {csType} {name};");
    }

    private static void EmitComment(CSharpEmitter e, string? comment)
    {
        if (!string.IsNullOrWhiteSpace(comment))
        {
            e.WriteLine("/// <summary>");
            var lines = comment!.Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                e.WriteLine($"/// {line}");
            }
            e.WriteLine("/// </summary>");
        }
    }

    private static void EmitInitDefault(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("private static unsafe void InitDefaultWorkingState(WorkingState* dst)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("*dst = default;");
        foreach (var f in asset.WorkingState.Where(f =>
            !string.IsNullOrEmpty(f.DefaultValueCSharp) &&
            f.DefaultValueCSharp != "0" &&
            f.DefaultValueCSharp != "default"))
        {
            e.WriteLine($"dst->{f.Name} = {f.DefaultValueCSharp};");
        }
        // FC-2/LV-1b (Q#19-B): declared initial length seeds Count over the zeroed slots -- the
        // partial init the whole-field DefaultValueCSharp path cannot express (review F2). Runs on
        // every hash-mismatch (re)init inside the generated thunks; the BlueprintCall host's inline
        // zero path leaves Count=0 (safe empty list; documented LV-1b limitation).
        foreach (var f in asset.WorkingState.Where(f => f.Type.Capacity > 0 && f.Type.InitialLength > 0))
        {
            e.WriteLine($"dst->{f.Name}.Count = {f.Type.InitialLength};");
        }
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitTickCore(CSharpEmitter e, IrAsset asset)
    {
        // I4: mark TickCore so the editor's reflection-based ActionSchemaExporter discovers this
        // blueprint AiPrimitive as a placeable AI action (DtoType is read from the first ref param,
        // `ref Params`). Flags mirror the compiler's hosting set so the exporter maps them to the
        // correct host graphs and marks conditions. Distinct from [BTreeAction]/[SharedAiAction] so
        // the FastBTree/Shared-AI generators never re-process this already-registered thunk.
        string B(bool v) => v ? "true" : "false";
        e.WriteLine(
            "[global::Fbt.Kernel.GeneratedAiPrimitiveAction("
            + $"bTreeAction: {B(asset.Hostings.Contains(AiPrimitiveHosting.BTreeAction))}, "
            + $"bTreeCondition: {B(asset.Hostings.Contains(AiPrimitiveHosting.BTreeCondition))}, "
            + $"hsmAction: {B(asset.Hostings.Contains(AiPrimitiveHosting.HsmAction))}, "
            + $"hsmGuard: {B(asset.Hostings.Contains(AiPrimitiveHosting.HsmGuard))}, "
            + $"blueprintCall: {B(asset.Hostings.Contains(AiPrimitiveHosting.BlueprintCall))})]");
        e.WriteLine("public static global::Fbt.NodeStatus TickCore(");
        e.Indent();
        e.WriteLine("ref Params p,");
        e.WriteLine("ref WorkingState ws,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("global::Fdp.Core.EntityRepository world,");
        e.WriteLine("float time)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();

        var mainGraph = asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.AiPrimitiveMain)
            ?? asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function);

        if (mainGraph != null)
            LibraryEmitter.EmitGraphBody(e, asset, mainGraph);

        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitThunks(CSharpEmitter e, IrAsset asset, string className)
    {
        foreach (var hosting in asset.Hostings)
        {
            switch (hosting)
            {
                case AiPrimitiveHosting.BTreeAction:
                    EmitBTreeActionThunk(e);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.BTreeCondition:
                    EmitBTreeConditionThunk(e);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.HsmAction:
                    EmitHsmActivityThunk(e);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.HsmGuard:
                    EmitHsmGuardThunk(e);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.BlueprintCall:
                    EmitBlueprintCallThunk(e);
                    e.WriteLine();
                    break;
            }
        }
    }

    private static void EmitBTreeActionThunk(CSharpEmitter e)
    {
        e.WriteLine("public static unsafe global::Fbt.NodeStatus BTreeTick(");
        e.Indent();
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.Components.BrainBlackboard bb,");
        e.WriteLine("ref global::Fbt.BehaviorTreeState state,");
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.BTreeContext ctx,");
        e.WriteLine("int paramIndex)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ref var p = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, Params>(");
        e.WriteLine("    ref bb.BehaviorParameters[paramIndex * global::System.Runtime.CompilerServices.Unsafe.SizeOf<Params>()]);");
        e.WriteLine("ref var bb1024 = ref ctx.World.GetComponentRW<global::Fdp.Toolkit.Behavior.Components.Blackboard1024>(ctx.Self);");
        e.WriteLine("unsafe");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("fixed (byte* memory = bb1024.Memory)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ulong storedHash = *(ulong*)memory;");
        e.WriteLine("if (storedHash != StructureHash)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("global::System.Runtime.CompilerServices.Unsafe.InitBlock(memory, 0, (uint)global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::Fdp.Toolkit.Behavior.Components.Blackboard1024>());");
        e.WriteLine("*(ulong*)memory = StructureHash;");
        e.WriteLine("InitDefaultWorkingState((WorkingState*)(memory + 8));");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine("ref var ws = ref global::System.Runtime.CompilerServices.Unsafe.AsRef<WorkingState>(memory + 8);");
        e.WriteLine("return TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime);");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitBTreeConditionThunk(CSharpEmitter e)
    {
        e.WriteLine("public static unsafe bool BTreeEvaluate(");
        e.Indent();
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.Components.BrainBlackboard bb,");
        e.WriteLine("ref global::Fbt.BehaviorTreeState state,");
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.BTreeContext ctx,");
        e.WriteLine("int paramIndex)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ref var p = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, Params>(");
        e.WriteLine("    ref bb.BehaviorParameters[paramIndex * global::System.Runtime.CompilerServices.Unsafe.SizeOf<Params>()]);");
        e.WriteLine("ref var bb1024 = ref ctx.World.GetComponentRW<global::Fdp.Toolkit.Behavior.Components.Blackboard1024>(ctx.Self);");
        e.WriteLine("unsafe");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("fixed (byte* memory = bb1024.Memory)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ulong storedHash = *(ulong*)memory;");
        e.WriteLine("if (storedHash != StructureHash)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("global::System.Runtime.CompilerServices.Unsafe.InitBlock(memory, 0, (uint)global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::Fdp.Toolkit.Behavior.Components.Blackboard1024>());");
        e.WriteLine("*(ulong*)memory = StructureHash;");
        e.WriteLine("InitDefaultWorkingState((WorkingState*)(memory + 8));");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine("ref var ws = ref global::System.Runtime.CompilerServices.Unsafe.AsRef<WorkingState>(memory + 8);");
        e.WriteLine("return TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime) == global::Fbt.NodeStatus.Success;");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitHsmActivityThunk(CSharpEmitter e)
    {
        e.WriteLine("public static unsafe void HsmActivity(void* instance, void* context, global::Fhsm.Kernel.Data.HsmCommandWriter* writer)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("var bridge = (global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge*)context;");
        e.WriteLine("var world = (global::Fdp.Core.EntityRepository)global::System.Runtime.InteropServices.GCHandle.FromIntPtr(bridge->WorldHandle).Target!;");
        e.WriteLine("ref var p = ref *(Params*)instance;");
        e.WriteLine("ref var bb1024 = ref world.GetComponentRW<global::Fdp.Toolkit.Behavior.Components.Blackboard1024>(bridge->Self);");
        e.WriteLine("fixed (byte* memory = bb1024.Memory)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("if (*(ulong*)memory != StructureHash)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("global::System.Runtime.CompilerServices.Unsafe.InitBlock(memory, 0, (uint)global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::Fdp.Toolkit.Behavior.Components.Blackboard1024>());");
        e.WriteLine("*(ulong*)memory = StructureHash;");
        e.WriteLine("InitDefaultWorkingState((WorkingState*)(memory + 8));");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine("ref var ws = ref global::System.Runtime.CompilerServices.Unsafe.AsRef<WorkingState>(memory + 8);");
        e.WriteLine("TickCore(ref p, ref ws, bridge->Self, world, world.SimulationTime);");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitHsmGuardThunk(CSharpEmitter e)
    {
        e.WriteLine("public static unsafe bool HsmGuard(void* instance, void* context, ushort eventId)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("var bridge = (global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge*)context;");
        e.WriteLine("var world = (global::Fdp.Core.EntityRepository)global::System.Runtime.InteropServices.GCHandle.FromIntPtr(bridge->WorldHandle).Target!;");
        e.WriteLine("ref var p = ref *(Params*)instance;");
        e.WriteLine("ref var bb1024 = ref world.GetComponentRW<global::Fdp.Toolkit.Behavior.Components.Blackboard1024>(bridge->Self);");
        e.WriteLine("fixed (byte* memory = bb1024.Memory)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("if (*(ulong*)memory != StructureHash)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("global::System.Runtime.CompilerServices.Unsafe.InitBlock(memory, 0, (uint)global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::Fdp.Toolkit.Behavior.Components.Blackboard1024>());");
        e.WriteLine("*(ulong*)memory = StructureHash;");
        e.WriteLine("InitDefaultWorkingState((WorkingState*)(memory + 8));");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine("ref var ws = ref global::System.Runtime.CompilerServices.Unsafe.AsRef<WorkingState>(memory + 8);");
        e.WriteLine("return TickCore(ref p, ref ws, bridge->Self, world, world.SimulationTime) == global::Fbt.NodeStatus.Success;");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitBlueprintCallThunk(CSharpEmitter e)
    {
        e.WriteLine("public static global::Fbt.NodeStatus Call(");
        e.Indent();
        e.WriteLine("ref Params p,");
        e.WriteLine("ref WorkingState ws,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("global::Fdp.Core.EntityRepository world,");
        e.WriteLine("float time)");
        e.Outdent();
        e.WriteLine("    => TickCore(ref p, ref ws, self, world, time);");
    }

    private static string CSharpType(IrTypeRef t) => StatementEmitter.TypeRefToCSharp(t);
}
