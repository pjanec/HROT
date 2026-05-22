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
            e.WriteLine($"public {CSharpType(f.Type)} {f.Name};");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitWorkingStateStruct(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        e.WriteLine("public struct WorkingState");
        e.WriteLine("{");
        e.Indent();
        foreach (var f in asset.WorkingState)
            e.WriteLine($"public {CSharpType(f.Type)} {f.Name};");
        e.Outdent();
        e.WriteLine("}");
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
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitTickCore(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("public static global::Hrot.Blueprints.Core.Assets.NodeStatus TickCore(");
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

        // Fallback: ensures the method always compiles when the graph body does not
        // terminate every control-flow path (e.g. stub/placeholder graphs in Phase 3).
        // Unreachable if the graph already returns on all paths.
        e.WriteLine("return global::Hrot.Blueprints.Core.Assets.NodeStatus.Failure;");

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
        e.WriteLine("return (global::Fbt.NodeStatus)(int)TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime);");
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
        e.WriteLine("return TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime) == global::Hrot.Blueprints.Core.Assets.NodeStatus.Success;");
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
        e.WriteLine("return TickCore(ref p, ref ws, bridge->Self, world, world.SimulationTime) == global::Hrot.Blueprints.Core.Assets.NodeStatus.Success;");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitBlueprintCallThunk(CSharpEmitter e)
    {
        e.WriteLine("public static global::Hrot.Blueprints.Core.Assets.NodeStatus Call(");
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
