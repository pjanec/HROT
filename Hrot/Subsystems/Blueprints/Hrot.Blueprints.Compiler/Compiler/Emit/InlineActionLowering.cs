using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

/// <summary>
/// AN8 — emits the inline-latent non-channel behavior-action invocation
/// for <see cref="IrOp_InlineActionCall"/>.
///
/// Call pattern for AiPrimitive (BlueprintCall) path:
/// <code>
/// unsafe
/// {
///     ref var bb1024 = ref world.GetComponentRW&lt;Blackboard1024&gt;(self);
///     fixed (byte* __mem_N = bb1024.Memory)
///     {
///         if (*(ulong*)__mem_N != global::{ClassFqn}.StructureHash)
///         {
///             Unsafe.InitBlock(__mem_N, 0, (uint)Unsafe.SizeOf&lt;Blackboard1024&gt;());
///             *(ulong*)__mem_N = global::{ClassFqn}.StructureHash;
///             // Note: InitDefaultWorkingState is private in the generated class;
///             // the working state is fully zeroed by InitBlock (Slice-1 acceptable).
///         }
///         ref var __ws_N = ref Unsafe.AsRef&lt;global::{ClassFqn}.WorkingState&gt;(__mem_N + 8);
///         var __p_N = new global::{ParamsTypeFqn} { Field1 = __t0, Field2 = __t1, ... };
///         var __t{idx} = global::{ClassFqn}.Call(ref __p_N, ref __ws_N, self, world, time);
///     }
/// }
/// </code>
///
/// The working state projection mirrors the pattern in
/// <see cref="AiPrimitiveEmitter.EmitBTreeActionThunk"/>.
/// Slice-1 constraint: only ONE stateful AiPrimitive may run per entity at a time.
/// </summary>
internal static class InlineActionLowering
{
    private const string Bb1024Fqn = "Fdp.Toolkit.Behavior.Components.Blackboard1024";
    private const string UnsafeFqn = "System.Runtime.CompilerServices.Unsafe";

    public static void Emit(CSharpEmitter e, IrOp_InlineActionCall op, int resultIdx)
    {
        // Derive the action's static class FQN from the ActionFqn.
        // Convention: ActionFqn = "{ClassFqn}.{MethodName}" — split at the last dot.
        var lastDot  = op.ActionFqn.LastIndexOf('.');
        var classFqn = lastDot > 0 ? op.ActionFqn.Substring(0, lastDot) : op.ActionFqn;
        // methodName is not needed for emit — we always call "Call".

        // Normalise ParamsTypeFqn: reflection uses '+' for nested types; C# syntax uses '.'.
        var paramsFqn = (op.ParamsTypeFqn ?? "").Replace('+', '.');

        var worldVar = e.Ctx.WorldVar;
        var n = e.Ctx.NextLocalCounter("ia"); // "inline action"

        if (op.IsAiPrimitive)
        {
            // -- AiPrimitive path: project WorkingState from Blackboard1024 inline --

            e.WriteLine("unsafe");
            e.WriteLine("{");
            e.Indent();

            e.WriteLine($"ref var __bb1024_{n} = ref {worldVar}.GetComponentRW<global::{Bb1024Fqn}>(self);");
            e.WriteLine($"fixed (byte* __mem_{n} = __bb1024_{n}.Memory)");
            e.WriteLine("{");
            e.Indent();

            // Hash check + init:
            // If the stored hash doesn't match, zero the entire Blackboard1024 block and
            // write the new hash.  The working state is then fully zeroed (all fields at
            // their zero/default values).  We deliberately do NOT call
            // InitDefaultWorkingState here because it is private to the generated AiPrimitive
            // class and is therefore inaccessible from an external host blueprint (Slice-1).
            e.WriteLine($"if (*(ulong*)__mem_{n} != global::{classFqn}.StructureHash)");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"global::{UnsafeFqn}.InitBlock(__mem_{n}, 0, (uint)global::{UnsafeFqn}.SizeOf<global::{Bb1024Fqn}>());");
            e.WriteLine($"*(ulong*)__mem_{n} = global::{classFqn}.StructureHash;");
            e.Outdent();
            e.WriteLine("}");

            // Project working state ref
            e.WriteLine($"ref var __ws_{n} = ref global::{UnsafeFqn}.AsRef<global::{classFqn}.WorkingState>(__mem_{n} + 8);");

            // Build params struct
            EmitParamsLocal(e, op, paramsFqn, classFqn, n);

            // Invoke the action
            if (resultIdx >= 0)
                e.WriteLine($"var __t{resultIdx} = global::{classFqn}.Call(ref __p_{n}, ref __ws_{n}, self, {worldVar}, time);");
            else
                e.WriteLine($"global::{classFqn}.Call(ref __p_{n}, ref __ws_{n}, self, {worldVar}, time);");

            e.Outdent();
            e.WriteLine("}"); // fixed

            e.Outdent();
            e.WriteLine("}"); // unsafe
        }
        else
        {
            // -- Stateless hardcoded action path (future extension) --
            // Not yet implemented in AN8 Slice-1 (SharedAiAction contract is ambiguous).
            // Emit a compile-error comment so it is obvious if this branch is hit.
            e.WriteLine($"#error AN8: stateless non-AiPrimitive action '{op.ActionFqn}' not implemented in Slice-1");
        }
    }

    private static void EmitParamsLocal(
        CSharpEmitter e,
        IrOp_InlineActionCall op,
        string paramsFqn,
        string classFqn,
        string n)
    {
        if (!string.IsNullOrEmpty(paramsFqn) && op.ParamFields.Count > 0)
        {
            e.WriteLine($"var __p_{n} = new global::{paramsFqn}");
            e.WriteLine("{");
            e.Indent();
            for (int i = 0; i < op.ParamFields.Count; i++)
            {
                var f   = op.ParamFields[i];
                var sep = i == op.ParamFields.Count - 1 ? "" : ",";
                e.WriteLine($"{f.FieldName} = __t{f.Value.Index}{sep}");
            }
            e.Outdent();
            e.WriteLine("};");
        }
        else if (!string.IsNullOrEmpty(paramsFqn))
        {
            // No pin values — emit default params.
            e.WriteLine($"var __p_{n} = default(global::{paramsFqn});");
        }
        else
        {
            // ParamsTypeFqn unknown — fall back to the nested Params type on the class.
            e.WriteLine($"var __p_{n} = default(global::{classFqn}.Params);");
        }
    }
}
