using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

/// <summary>
/// AN8/AN8b — emits the inline-latent non-channel behavior-action invocation
/// for <see cref="IrOp_InlineActionCall"/>.
///
/// Two call patterns are dispatched on <see cref="IrOp_InlineActionCall.IsAiPrimitive"/>:
///
/// <b>AiPrimitive (BlueprintCall) path</b> — <c>IsAiPrimitive == true</c>:
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
///         }
///         ref var __ws_N = ref Unsafe.AsRef&lt;global::{ClassFqn}.WorkingState&gt;(__mem_N + 8);
///         var __p_N = new global::{ParamsTypeFqn} { Field1 = __t0, Field2 = __t1, ... };
///         var __t{idx} = global::{ClassFqn}.Call(ref __p_N, ref __ws_N, self, world, time);
///     }
/// }
/// </code>
///
/// <b>[SharedAiAction] direct-invocation path</b> — <c>IsAiPrimitive == false</c> (AN8b):
/// <code>
/// var __p_N = new global::{ParamsTypeFqn} { Field1 = __t0, Field2 = __t1, ... };
/// var __t{idx} = global::{ActionFqn}(ref __p_N, self, world);
/// </code>
/// No WorkingState projection, no Blackboard1024, no <c>time</c> param.
/// Params are rebuilt from pins on every invocation (stateless).
///
/// Both paths use the same Stage 5 block-split + Stage 6 WaitLowering machinery for
/// inline-latent suspend/resume on Running.
/// The working state projection for the AiPrimitive path mirrors
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
        // For AiPrimitive: classFqn = "{Ns}.{SanitizedName}_{Id:X8}_Bp"; the method is always "Call".
        // For SharedAiAction: classFqn = declaring type (e.g. "Ns.DemoSharedActions"); op.ActionFqn is the full method FQN.

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
            // -- AN8b: [SharedAiAction] direct-invocation path --
            // Signature: static NodeStatus {Method}(ref {DtoType} dto, Entity self, EntityRepository world)
            // No working-state projection, no Blackboard1024, no 'time' param.
            // The dto is rebuilt from pins each invocation (stateless-params, correct for SharedAiAction).
            // The latent suspend/resume machinery (Stage 5 block-split + Stage 6 WaitLowering) is
            // identical to the AiPrimitive path — only the call emit differs here.

            // Build params DTO local from pins (same helper used by the AiPrimitive path).
            // classFqn here is the declaring type (e.g. "Fdp.Toolkit.Behavior.Demo.DemoSharedActions"),
            // used only as a fallback when paramsFqn is empty (defensive; paramsFqn should always be set).
            EmitParamsLocal(e, op, paramsFqn, classFqn, n);

            // Invoke the static method directly.
            if (resultIdx >= 0)
                e.WriteLine($"var __t{resultIdx} = global::{op.ActionFqn}(ref __p_{n}, self, {worldVar});");
            else
                e.WriteLine($"global::{op.ActionFqn}(ref __p_{n}, self, {worldVar});");
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
