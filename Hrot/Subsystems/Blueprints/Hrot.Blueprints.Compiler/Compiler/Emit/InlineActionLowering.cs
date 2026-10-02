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
    // ⛔ CE-311 / P4-①: `Bb1024Fqn` is GONE. The inline AiPrimitive host resolves its working state
    //    through the occurrence store now, so no emitter names Blackboard1024 any more.
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
            // -- AiPrimitive path: the inline host's WorkingState, in the OCCURRENCE STORE --
            //
            // 🔴🔴 CE-311 / P4-① (2026-09-22) — THIS EMITTED AGAINST A COMPONENT NOTHING ADDS.
            //   It used to emit `GetComponentRW<Blackboard1024>(self)` + the Slice-1 `Memory + 8`
            //   block behind an 8-byte StructureHash. SLICE2 moved AiPrimitive working state to the
            //   Blueprint tier ladder and NOTHING has added Blackboard1024 since ⇒ the emitted code
            //   would THROW on the first inline call. ⚠ It looked green only because
            //   BlueprintTestFixture:150 registers the component, so the fixture was the one world
            //   where this could run — the same blindness CE-310 had.
            //
            // ⭐⭐ THE ROUTE, and it is the standalone thunk's own seam, not a new one:
            //   AiPrimitiveEmitter.EmitStandaloneOccurrenceBody already resolves this asset's working
            //   state with OccurrenceSlots.StandaloneStateKeyFor(AssetId) — its comment says
            //   "NOT Blackboard1024" in as many words. The inline host uses the SAME key, so an inline
            //   call and a standalone tick of one asset address one slot, which is what the shared
            //   Memory+8 block already meant for them.
            //
            // ⭐ AND IT LIFTS SLICE-1's LIMIT rather than preserving it. The old block was ONE per
            //   ENTITY, so a second stateful AiPrimitive reset the first via the hash guard (that is
            //   exactly SLICE1-DESIGN.md:27's "exactly one stateful AiPrimitive per entity"). Keyed
            //   per ASSET, two different primitives now get two slots and stop colliding.
            //
            // ⚠ The hash guard is NOT dropped — ResolveOrAttach RESETS the slot on a StructureHash
            //   mismatch, which is the same defence in its one owner. ⇒ the manual InitBlock goes.
            e.WriteLine("unsafe");
            e.WriteLine("{");
            e.Indent();

            e.WriteLine($"int __iaKey_{n} = global::Fdp.Toolkit.Behavior.OccurrenceSlots.StandaloneStateKeyFor(global::{classFqn}.AssetId);");
            e.WriteLine($"ref var __ws_{n} = ref global::Fdp.Toolkit.Behavior.OccurrenceWorkingState.ResolveOrAttach<global::{classFqn}.WorkingState>(");
            e.WriteLine($"    {worldVar}, self, __iaKey_{n}, global::{classFqn}.StructureHash,");
            e.WriteLine($"    global::Fdp.Toolkit.Blueprints.Partitioning.OccurrenceKind.Blueprint, out bool __iaFresh_{n});");
            e.WriteLine($"_ = __iaFresh_{n};   // a fresh slot is already zeroed; InitDefaultWorkingState stays private to the asset (Slice-1)");

            // Build params struct
            EmitParamsLocal(e, op, paramsFqn, classFqn, n);

            // Invoke the action
            if (resultIdx >= 0)
                e.WriteLine($"var __t{resultIdx} = global::{classFqn}.Call(ref __p_{n}, ref __ws_{n}, self, {worldVar}, time);");
            else
                e.WriteLine($"global::{classFqn}.Call(ref __p_{n}, ref __ws_{n}, self, {worldVar}, time);");

            // ⭐ CE-311: no `fixed` block any more — the occurrence seam returns a managed ref, so the
            //   old pointer pin (and its closing brace) are gone with Blackboard1024.
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
