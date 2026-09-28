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

        // ⭐⭐⭐ CE-405 — DECIDE WHETHER THIS IS A *NEW* COMMAND, before anything is overwritten.
        //
        // 🔴 The defect this closes. The lowering used to bump ActionInstanceId unconditionally,
        //    every time the op ran. LocomotionDispatcherSystem.cs:62 reads ANY change in that id
        //    as "a new action was dispatched" and calls OnExit(previous) + OnEnter(current) — for
        //    MoveTo, OnEnter re-plans the path. ⇒ a blueprint issuing a channel command on a
        //    per-tick path re-planned EVERY FRAME. It was latent only because the one shipped
        //    asset that issues a channel command gates it behind its own ws.__phase wait.
        //
        // 📐 Measured 2026-09-28: every production C# channel writer already guards this —
        //    CgfNodes.cs:305,313 · 348-351 · 384-387 · 580-582 all compute
        //      needsActivation = channel.ActiveAction != <id> || channel.Status == Failure
        //    The blueprint lowering was the only writer that did not. This mirrors that idiom,
        //    INCLUDING the Failure clause, so a failed action is retried rather than stuck.
        //
        // ⭐ Params are part of the identity of a command, not just the action id: CgfNodes.cs:461-463
        //   (Wander) re-enters DELIBERATELY on a fresh destination. So "same action id" alone is the
        //   wrong test — the params bytes are compared too, and only a real change re-enters.
        //
        // ⭐⭐⭐ AND "STILL RUNNING" IS THE THIRD PART OF A COMMAND'S IDENTITY — this is what lets an
        //   author issue THE SAME COMMAND TWICE and mean it twice.
        //
        // 📐 Measured 2026-09-28 across all 12 channel executors: every one is a STANDING ORDER that
        //    terminates by setting Status to Success or Failure (AimAndFireExecutor.cs:49,56 ·
        //    OpenDoorExecutor.cs:33 · EjectPassengersExecutor.cs:70 · MoveToExecutor.cs:108,122 · …).
        //    ⇒ a TERMINAL status is the runtime's own statement that the previous instance of this
        //    command IS OVER. Re-issuing after that is by definition a NEW instance, never a
        //    duplicate — so it must re-enter, or the second "open that door" is silently swallowed.
        //
        // ⚠ Hence `!= Running` and not `== Failure`. Deduplication applies ONLY while the command is
        //   still in flight, which is exactly the re-plan storm CE-405 exists to stop. ⛔ A narrower
        //   `== Failure` test (which is what the C# nodes use) is WRONG here: CgfNodes has a second
        //   route out that a blueprint statement does not have — it FORWARDS the terminal status up
        //   the tree (CgfNodes.cs:307-310) instead of re-issuing, so it never needs this case.
        //   ⚠ NodeStatus.Failure is 0, so a default-initialised channel is correctly "not Running".
        e.WriteLine($"bool __chNew_{n} = __ch_{n}.ActiveAction != {op.ActionIdConstantName}");
        e.Indent();
        e.WriteLine($"|| __ch_{n}.Status != global::Fbt.NodeStatus.Running;");
        e.Outdent();
        e.WriteLine($"__ch_{n}.ActiveAction = {op.ActionIdConstantName};");
        if (op.ParamFields.Count > 0)
        {
            e.WriteLine("unsafe");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"var __chNext_{n} = new global::{op.ParamsStructTypeFqn}");
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
            e.WriteLine($"fixed (byte* __paramSlot_{n} = __ch_{n}.Params)");
            e.WriteLine("{");
            e.Indent();
            // ⭐ A raw byte compare of the params PREFIX of the slot — the struct is smaller than
            //   the fixed buffer, so only sizeof(T) bytes carry meaning. ⛔ Not ValueType.Equals:
            //   that is reflection-based on a struct with no override.
            e.WriteLine($"if (!__chNew_{n})");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"__chNew_{n} = !global::System.MemoryExtensions.SequenceEqual(");
            e.Indent();
            e.WriteLine($"new global::System.ReadOnlySpan<byte>(__paramSlot_{n}, sizeof(global::{op.ParamsStructTypeFqn})),");
            e.WriteLine($"new global::System.ReadOnlySpan<byte>(&__chNext_{n}, sizeof(global::{op.ParamsStructTypeFqn})));");
            e.Outdent();
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine($"*(global::{op.ParamsStructTypeFqn}*)__paramSlot_{n} = __chNext_{n};");
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
        // ⭐ CE-405 — and ONLY a genuinely new command advances the instance id, which is what
        //   makes the dispatcher call OnExit/OnEnter. Re-issuing the same command is a no-op.
        e.WriteLine($"if (__chNew_{n})");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"unchecked {{ __ch_{n}.ActionInstanceId++; }}");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
    }
}
