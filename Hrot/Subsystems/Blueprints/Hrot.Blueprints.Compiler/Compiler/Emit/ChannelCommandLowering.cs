using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

internal static class ChannelCommandLowering
{
    public static void Emit(CSharpEmitter e, IrOp_ChannelCommand op)
    {
        var n = e.Ctx.NextLocalCounter("ch");
        var worldVar = e.Ctx.WorldVar;

        // Guard: only issue the channel command when the entity actually owns the channel
        // component.  This makes the generated code safe to call on entities that were not
        // fully set up (e.g. test scenarios that create minimal entities).  In production
        // all AI entities carry the required channel components.
        e.WriteLine($"if ({worldVar}.HasComponent<global::{op.ChannelComponentTypeFqn}>(self))");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"ref var __ch_{n} = ref {worldVar}.GetComponentRW<global::{op.ChannelComponentTypeFqn}>(self);");
        e.WriteLine($"__ch_{n}.ActiveAction = {op.ActionIdConstantName};");
        if (op.ParamFields.Count > 0)
        {
            e.WriteLine("unsafe");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"fixed (byte* __paramSlot_{n} = __ch_{n}.Params)");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"*(global::{op.ParamsStructTypeFqn}*)__paramSlot_{n} = new global::{op.ParamsStructTypeFqn}");
            e.WriteLine("{");
            e.Indent();
            for (int i = 0; i < op.ParamFields.Count; i++)
            {
                var f = op.ParamFields[i];
                var sep = i == op.ParamFields.Count - 1 ? "" : ",";
                e.WriteLine($"{f.FieldName} = __t{f.Value.Index}{sep}");
            }
            e.Outdent();
            e.WriteLine("};");
            e.Outdent();
            e.WriteLine("}");
            e.Outdent();
            e.WriteLine("}");
        }
        // ⭐⭐⭐ CE-402 — CLAIM THE CHANNEL FOR THIS BEHAVIOUR, or arbitration wipes it next tick.
        //
        // 🔴 The defect this closes. ChannelArbitrationSystem.cs:44 clears any channel where
        //    `ActiveAction != 0 && channel.BehaviorInstanceId != behavior.InstanceId`. Until this
        //    line existed, the blueprint route wrote ActiveAction and the params and never claimed
        //    the channel ⇒ the command was zeroed on the very next tick and NOTHING EVER MOVED.
        //
        // 📐 Measured 2026-09-28: every OTHER production channel writer stamps this explicitly —
        //    CgfNodes.cs:297,345,381,458,570 · HillAttackTankNodes.cs:271,408,470 ·
        //    EqsCombatNodes.cs:91 · HsmChannelRegionNodes.cs:50,63. The blueprint lowering was the
        //    ONLY channel writer that did not, which is why it read as a blueprint-specific bug and
        //    is in fact a missing half of the channel lifecycle (Q74 §0 ③).
        //
        // ⚠ Guarded like the channel itself: a minimal test entity may carry the channel and no
        //   BehaviorState. Leaving BehaviorInstanceId at its previous value there is correct — with
        //   no BehaviorState there is no arbitration query to match against either.
        e.WriteLine($"if ({worldVar}.HasComponent<global::Fdp.Toolkit.Behavior.Components.BehaviorState>(self))");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"__ch_{n}.BehaviorInstanceId = {worldVar}.GetComponent<global::Fdp.Toolkit.Behavior.Components.BehaviorState>(self).InstanceId;");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine($"__ch_{n}.ActionInstanceId++;");
        e.Outdent();
        e.WriteLine("}");
    }
}
