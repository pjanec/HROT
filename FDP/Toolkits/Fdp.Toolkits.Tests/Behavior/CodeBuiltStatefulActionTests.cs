using System;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// ⭐ <c>CE-430</c> (<c>Q76</c> §12.23): proves the code-builder stateful helper
/// (⭐ CE-504 slice 4: <see cref="SharedNodeBinder.RegisterStatefulAction{TBB,TParams,TWorkingState}"/> + the authoring-side
/// <c>StatefulAction</c> extension) binds a four-parameter stateful method through FastBTree's generic
/// <c>Action</c> seam with BOTH its params and its working state as FIELDS of the builder's blackboard — the
/// behaviour's one block.
///
/// <para>⭐ The suite's two claims survive the move from slots to fields, re-expressed: nodes that project ONE
/// state field share it; nodes that project TWO fields keep independent state.
/// ⛔ HISTORY — under S3-G the same claims were "one Behavior-scoped slot" vs "two Node-scoped slots", proved by
/// provisioning a partition tier from a slot manifest. That manifest and the slot thunk are deleted.</para>
/// </summary>
public sealed class CodeBuiltStatefulActionTests
{
    /// <summary>The code builder's block: params and two independent working-state fields.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorBlackboard
    {
        public DemoCounterNodes.DemoCursorParams Cfg;
        public DemoCounterNodes.DemoCursorState  Shared;
        public DemoCounterNodes.DemoCursorState  Other;
    }

    private static CursorBlackboard Tick(BTreeBuilder<CursorBlackboard, BTreeContext> builder, string name)
    {
        var blob   = builder.Compile(name);
        var interp = new Interpreter<CursorBlackboard, BTreeContext>(blob, builder.GetRegistry());
        var bb     = new CursorBlackboard { Cfg = { Limit = 1 } };
        var ctx    = new BTreeContext();
        var state  = new BehaviorTreeState();
        interp.Tick(ref bb, ref state, ref ctx);
        return bb;
    }

    /// <summary>
    /// Two nodes project the SAME state field. With Limit=1 the Sequence runs A (0→1, Success) then B (1→2,
    /// Success) over the one field ⇒ Cursor=2. Independent state would leave 1.
    /// </summary>
    [Fact]
    public void CodeBuilt_TwoNodes_OverOneStateField_ShareIt()
    {
        var builder = new BTreeBuilder<CursorBlackboard, BTreeContext>()
            .Sequence(seq => seq
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, bb => bb.Shared, DemoCounterNodes.Action_AdvanceCursor)
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, bb => bb.Shared, DemoCounterNodes.Action_AdvanceCursor));

        var bb = Tick(builder, "CodeBuiltShared");

        Assert.Equal(2, bb.Shared.Cursor);
        Assert.Equal(0, bb.Other.Cursor);
    }

    /// <summary>
    /// Two nodes project DIFFERENT state fields ⇒ each advances its own cursor to 1 — and they register under
    /// distinct keys (the state offset is part of the key), so neither thunk overwrites the other.
    /// </summary>
    [Fact]
    public void CodeBuilt_TwoNodes_OverTwoStateFields_KeepIndependentState()
    {
        var builder = new BTreeBuilder<CursorBlackboard, BTreeContext>()
            .Sequence(seq => seq
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, bb => bb.Shared, DemoCounterNodes.Action_AdvanceCursor)
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, bb => bb.Other, DemoCounterNodes.Action_AdvanceCursor));

        var bb = Tick(builder, "CodeBuiltIndependent");

        Assert.Equal(1, bb.Shared.Cursor);
        Assert.Equal(1, bb.Other.Cursor);
    }

    /// <summary>
    /// ⭐ The shipped author: the hand-written PlatoonHillAttack tree binds its six stateful nodes over
    /// <see cref="PlatoonHillAttackBlackboard.State"/> — one key per method, all at the same state offset, and
    /// no key carries a slot hash any more.
    /// </summary>
    [Fact]
    public void PlatoonHillAttack_CodeTree_BindsItsStateFromTheBlock()
    {
        var builder = HillAttackCommanderNodes.BuildPlatoonHillAttackTree();
        builder.Compile("PlatoonHillAttack");

        int stateOffset = (int)Marshal.OffsetOf<PlatoonHillAttackBlackboard>(nameof(PlatoonHillAttackBlackboard.State));
        string fqn = typeof(HillAttackCommanderNodes).FullName!;
        foreach (var m in new[] { "Action_CalculateSegments", "Action_DispatchAllToBaseline", "Action_RequestAreaQuery",
                                  "Condition_IsAreaQueryResolved", "Action_DispatchWaveWithTargets", "Condition_IsWaveCompleted" })
            Assert.True(builder.GetRegistry().TryGetAction($"{fqn}.{m}@0@{stateOffset}", out _), $"{m} bound over the block");
    }

    // ── CE-504 C-4: the shared C# node signature, bound by a curated tree ─────────────────────────────

    private static int _noParamsCalls;

    private static class SharedForms
    {
        public static bool Always(Entity self, EntityRepository world) => true;

        public static NodeStatus Bump(ref DemoCounterNodes.DemoCursorParams p, Entity self, EntityRepository world)
        { p.Limit += 10; return NodeStatus.Success; }

        public static NodeStatus Step(ref DemoCounterNodes.DemoCursorParams p, ref DemoCounterNodes.DemoCursorState ws,
                                      Entity self, EntityRepository world)
        { ws.Cursor++; return NodeStatus.Success; }

        public static NodeStatus Count(Entity self, EntityRepository world) { _noParamsCalls++; return NodeStatus.Success; }

        // ⭐ CE-504 slice 4 — one paired deactivator per shared form, declared beside its action, targeting it by FQN.
        [BTreeDeactivator("Fdp.Toolkit.Behavior.Tests.CodeBuiltStatefulActionTests.SharedForms.Bump")]
        public static void UndoBump(ref DemoCounterNodes.DemoCursorParams p, Entity self, EntityRepository world) => p.Limit = -7;

        [BTreeDeactivator("Fdp.Toolkit.Behavior.Tests.CodeBuiltStatefulActionTests.SharedForms.Step")]
        public static void UndoStep(ref DemoCounterNodes.DemoCursorParams p, ref DemoCounterNodes.DemoCursorState ws,
                                    Entity self, EntityRepository world) => ws.Cursor = -9;

        [BTreeDeactivator("Fdp.Toolkit.Behavior.Tests.CodeBuiltStatefulActionTests.SharedForms.Count")]
        public static void UndoCount(Entity self, EntityRepository world) => _noParamsCalls = -1;
    }

    /// <summary>A deactivator in a form its action cannot feed (a stateful one on a plain action).</summary>
    private static class MismatchedForms
    {
        public static NodeStatus Plain(ref DemoCounterNodes.DemoCursorParams p, Entity self, EntityRepository world)
            => NodeStatus.Success;

        [BTreeDeactivator("Fdp.Toolkit.Behavior.Tests.CodeBuiltStatefulActionTests.MismatchedForms.Plain")]
        public static void UndoPlain(ref DemoCounterNodes.DemoCursorParams p, ref DemoCounterNodes.DemoCursorState ws,
                                     Entity self, EntityRepository world) { }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-504</c> C-4 — a curated tree binds every shared form with the builder's own verbs: a param-less
    /// condition, a plain action (writes its params field in place), a stateful action (advances its own state field) and
    /// a param-less action. 📄 <c>DESIGN_BTree_Node_Call_Shapes.md</c> §4 C-4.
    /// <para>✅ Red-proof: project the plain form 4 bytes off its field (onto the state) ⇒ the
    /// Cfg assertion reddens.</para>
    /// </summary>
    [Fact]
    public void CodeBuilt_SharedForms_BindThroughTheBuildersOwnVerbs()
    {
        _noParamsCalls = 0;
        var builder = new BTreeBuilder<CursorBlackboard, BTreeContext>()
            .Sequence(seq => seq
                .Condition(SharedForms.Always)
                .Action(bb => bb.Cfg, SharedForms.Bump)
                .StatefulAction(bb => bb.Cfg, bb => bb.Other, SharedForms.Step)
                .Action(SharedForms.Count));

        var bb = Tick(builder, "CodeBuiltSharedForms");

        Assert.Equal(11, bb.Cfg.Limit);       // Tick seeds Limit = 1; the plain form wrote its own field in place
        Assert.Equal(1, bb.Other.Cursor);     // the stateful form advanced ITS state field …
        Assert.Equal(0, bb.Shared.Cursor);    // … and no other
        Assert.Equal(1, _noParamsCalls);

        // ⭐ the keys a JSON asset would use for the same bindings
        string fqn = typeof(SharedForms).FullName!;
        int other = (int)Marshal.OffsetOf<CursorBlackboard>(nameof(CursorBlackboard.Other));
        Assert.True(builder.GetRegistry().TryGetAction($"{fqn}.Bump@0", out _));
        Assert.True(builder.GetRegistry().TryGetAction($"{fqn}.Step@0@{other}", out _));
        Assert.True(builder.GetRegistry().TryGetAction($"{fqn}.Count", out _));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-504</c> slice 4 — a curated tree pairs each shared form's <c>[BTreeDeactivator]</c> by method, registers it
    /// under the SAME key as its action (the interpreter looks it up by the node's key) and feeds it the action's own
    /// projection — the params at the selected field, the state at ITS field. And the runtime copy carries it too, so the
    /// curated registrar's resource-owning predicate sees it before the blob compiles.
    /// <para>✅ Red-proof: drop the <c>PairDeactivator</c> call from <c>RegisterAction</c> ⇒ the plain-form assertions redden.</para>
    /// </summary>
    [Fact]
    public void CodeBuilt_SharedForms_PairTheirDeactivators_UnderTheActionsKey()
    {
        var builder = new BTreeBuilder<CursorBlackboard, BTreeContext>()
            .Sequence(seq => seq
                .Action(bb => bb.Cfg, SharedForms.Bump)
                .StatefulAction(bb => bb.Cfg, bb => bb.Other, SharedForms.Step)
                .Action(SharedForms.Count));
        var reg = builder.GetRegistry();
        string fqn = typeof(SharedForms).FullName!;   // the registry key keeps the CLR spelling (nested `+`)
        int other = (int)Marshal.OffsetOf<CursorBlackboard>(nameof(CursorBlackboard.Other));

        var bb = new CursorBlackboard { Cfg = { Limit = 3 } };
        var st = new BehaviorTreeState();
        var ctx = new BTreeContext();

        Assert.True(reg.TryGetDeactivator($"{fqn}.Bump@0", out var undoBump));
        undoBump(ref bb, ref st, ref ctx, 0);
        Assert.Equal(-7, bb.Cfg.Limit);                 // the plain deactivator wrote the params at the action's field

        Assert.True(reg.TryGetDeactivator($"{fqn}.Step@0@{other}", out var undoStep));
        undoStep(ref bb, ref st, ref ctx, 0);
        Assert.Equal(-9, bb.Other.Cursor);              // … the stateful one wrote ITS state field …
        Assert.Equal(0, bb.Shared.Cursor);              // … and no other

        Assert.True(reg.TryGetDeactivator($"{fqn}.Count", out var undoCount));
        undoCount(ref bb, ref st, ref ctx, 0);
        Assert.Equal(-1, _noParamsCalls);

        var runtime = new ActionRegistry<byte, BTreeContext>();
        Assert.Equal(6, SharedNodeBinder.CopyRuntimeThunks(reg, runtime));   // three thunks + three deactivators
        Assert.True(runtime.TryGetDeactivator($"{fqn}.Bump@0", out _));
        Assert.True(runtime.TryGetDeactivator($"{fqn}.Step@0@{other}", out _));
        Assert.True(runtime.TryGetDeactivator($"{fqn}.Count", out _));
    }

    // ── CE-1411: a [WritesChannel] node releases its channel on Failure, as the JSON path does ───────────────────────

    private static class ChannelWriters
    {
        public static NodeStatus Outcome = NodeStatus.Failure;

        [Fbt.Kernel.WritesChannel(Fbt.Kernel.ChannelKind.Locomotion)]
        public static NodeStatus Move(ref DemoCounterNodes.DemoCursorParams p, Entity self, EntityRepository world) => Outcome;

        [Fbt.Kernel.WritesChannel(Fbt.Kernel.ChannelKind.Weapon)]
        public static bool Aim(Entity self, EntityRepository world) => false;
    }

    private static (EntityRepository World, Entity Self) ChannelWorld()
    {
        var world = TestWorldFactory.Create();
        var e = world.CreateEntity();
        world.AddComponent(e, new Fdp.Toolkit.Behavior.Components.LocomotionChannel { ActiveAction = 5, ActionInstanceId = 7, DispatchedInstanceId = 7 });
        world.AddComponent(e, new Fdp.Toolkit.Behavior.Components.WeaponChannel { ActiveAction = 9, ActionInstanceId = 3, DispatchedInstanceId = 3 });
        return (world, e);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1411</c> — a C#-built tree's <c>[WritesChannel]</c> node that returns <c>Failure</c> releases its channel exactly as the
    /// JSON path's thunk does (<c>ChannelClearEmit</c> → <see cref="ChannelRelease.OnFailure"/>; its text rail is
    /// <c>SharedAiBindingCompilesTests.ABTreeAssetBindingSharedAiMethods_CompilesToOnePerBindingCall_AtTheHostOffset</c>): <c>ActiveAction</c>
    /// cleared, <c>ActionInstanceId</c> stepped (the dispatcher then runs the abandoned action's OnExit), <c>DispatchedInstanceId</c> untouched —
    /// through the typed thunk the builder ticks AND the byte thunk the runtime registry runs. A Success leaves the channel claimed.
    /// <para>✅ Red-proof: <c>SharedNodeBinder.Release</c> returns <c>status</c> without calling <c>OnFailure</c> ⇒ "Expected: 0, Actual: 5".</para>
    /// </summary>
    [Fact]
    public void CE1411_ACodeBuiltChannelWriter_ReleasesItsChannelOnFailure()
    {
        var (world, self) = ChannelWorld();
        try
        {
            var builder = new BTreeBuilder<CursorBlackboard, BTreeContext>()
                .Selector(sel => sel
                    .Action(bb => bb.Cfg, ChannelWriters.Move)
                    .Condition(ChannelWriters.Aim));
            var blob = builder.Compile("CE1411ChannelWriters");
            var interp = new Interpreter<CursorBlackboard, BTreeContext>(blob, builder.GetRegistry());
            var bb = new CursorBlackboard();
            var st = new BehaviorTreeState();
            var ctx = new BTreeContext { World = world, Self = self };

            ChannelWriters.Outcome = NodeStatus.Failure;
            interp.Tick(ref bb, ref st, ref ctx);

            var loco = world.GetComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(self);
            Assert.Equal(0, loco.ActiveAction);              // released …
            Assert.Equal(8u, loco.ActionInstanceId);         // … as a new instance (7 → 8) …
            Assert.Equal(7u, loco.DispatchedInstanceId);     // … so the dispatcher owes the old one its OnExit
            var weapon = world.GetComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>(self);
            Assert.Equal(0, weapon.ActiveAction);            // the param-less condition's channel too
            Assert.Equal(4u, weapon.ActionInstanceId);

            // ⭐ the RUNTIME (byte) thunk a registered curated tree actually runs carries the same release
            var runtime = new ActionRegistry<byte, BTreeContext>();
            SharedNodeBinder.CopyRuntimeThunks(builder.GetRegistry(), runtime);
            Assert.True(runtime.TryGetAction(typeof(ChannelWriters).FullName + ".Aim", out var aim));
            ref var w = ref world.GetComponentRW<Fdp.Toolkit.Behavior.Components.WeaponChannel>(self);
            w.ActiveAction = 9;
            byte none = 0;
            Assert.Equal(NodeStatus.Failure, aim(ref none, ref st, ref ctx, 0));
            Assert.Equal(0, world.GetComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>(self).ActiveAction);

            // ⛔ a Success keeps the channel claimed
            ref var l = ref world.GetComponentRW<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(self);
            l.ActiveAction = 5;
            ChannelWriters.Outcome = NodeStatus.Success;
            st = new BehaviorTreeState();
            interp.Tick(ref bb, ref st, ref ctx);
            Assert.Equal(5, world.GetComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(self).ActiveAction);
            Assert.Equal(8u, world.GetComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(self).ActionInstanceId);
        }
        finally
        {
            ChannelWriters.Outcome = NodeStatus.Failure;
            world.Dispose();
        }
    }

    /// <summary>⛔ CE-504 slice 4 — a deactivator its action cannot feed is refused at BUILD time, never silently unpaired.</summary>
    [Fact]
    public void CodeBuilt_ADeactivatorTheActionCannotFeed_IsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new BTreeBuilder<CursorBlackboard, BTreeContext>().Action(bb => bb.Cfg, MismatchedForms.Plain));
        Assert.Contains("UndoPlain", ex.Message);
    }

    /// <summary>⛔ A lambda has no stable FQN to key by — refused, rather than registered under a compiler-generated name.</summary>
    [Fact]
    public void CodeBuilt_SharedForm_RefusesALambda()
        => Assert.Throws<ArgumentException>(() =>
               new BTreeBuilder<CursorBlackboard, BTreeContext>()
                   .Action((Entity self, EntityRepository world) => NodeStatus.Success));
}

