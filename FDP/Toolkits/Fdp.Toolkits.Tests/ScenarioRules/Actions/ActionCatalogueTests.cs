using System;
using System.Linq;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules.Actions;
using Xunit;
using static Fdp.Toolkit.ScenarioRules.Tests.Actions.ActionKit;

namespace Fdp.Toolkit.ScenarioRules.Tests.Actions
{
    /// <summary>⭐ rules S2-d (CE-4160, CE-4162) — the ONE runtime catalogue of stateful shared actions (the PoC's P1 rails, against the production type).</summary>
    public sealed class ActionCatalogueTests
    {
        [Fact]
        public void TheCatalogue_FindsTheSharedActions_ByShortNameAndFqn_WithTheirTypes()
        {
            Assert.True(ActionCatalogue.TryGet("TestWaitTicks", out var e));
            Assert.Equal(typeof(WaitParams), e.ParamsType);
            Assert.Equal(typeof(WaitState), e.StateType);
            Assert.True(ActionCatalogue.TryGet(e.Fqn, out var byFqn));
            Assert.Same(e, byFqn);
            Assert.True(ActionCatalogue.TryGet("RunActions", out var run));       // the loop is itself in the catalogue
            Assert.Equal(typeof(RunActionsParams), run.ParamsType);
            Assert.Equal(typeof(RunActionsState), run.StateType);
            Assert.False(ActionCatalogue.TryGet("NoSuchAction", out _));
        }

        [Fact]
        public void ADelegateIsBuiltOncePerActionType_AndASecondEntityHitsTheCache()
        {
            ActionCatalogue.ResetRunnerCache();
            using var w = NewWorld();
            var a = Unit(w); var b = Unit(w);

            var ra = ActionCatalogue.RunnerFor("TestWaitTicks");
            ra.Tick(w, a, StepRunner.SlotKey(0), "{\"ticks\":5}");
            var rb = ActionCatalogue.RunnerFor("TestWaitTicks");
            rb.Tick(w, b, StepRunner.SlotKey(0), "{\"ticks\":2}");

            Assert.Same(ra, rb);
            Assert.Equal(1, ActionCatalogue.RunnersBuilt);                       // ← the cache-hit rail (mutation: build a new runner per call → 2)
            ActionCatalogue.RunnerFor("TestRecord");
            Assert.Equal(2, ActionCatalogue.RunnersBuilt);                       // a different action TYPE builds its own
        }

        [Fact]
        public void CallingTheDelegate_MovesTheRightBytes_ForEachEntity()
        {
            using var w = NewWorld();
            var a = Unit(w); var b = Unit(w);
            var run = ActionCatalogue.RunnerFor("TestWaitTicks");
            int key = StepRunner.SlotKey(0);

            Assert.Equal(NodeStatus.Running, run.Tick(w, a, key, "{\"ticks\":3}"));
            Assert.Equal(NodeStatus.Running, run.Tick(w, a, key, "{\"ticks\":3}"));
            Assert.Equal(NodeStatus.Running, run.Tick(w, b, key, "{\"ticks\":9}"));

            var (pa, sa) = PeekWait(w, a); var (pb, sb) = PeekWait(w, b);
            Assert.Equal((3, 2), (pa.Ticks, sa.Elapsed));
            Assert.Equal((9, 1), (pb.Ticks, sb.Elapsed));                        // b untouched by a's two ticks
            Assert.Equal(NodeStatus.Success, run.Tick(w, a, key, "{\"ticks\":3}"));
        }

        [Fact]
        public void AnUnknownActionName_IsALoudRefusal_NotASilentSkip()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ActionCatalogue.RunnerFor("Nope"));
            Assert.Contains("unknown action", ex.Message);
        }

        [Fact]
        public void CE4162_AParamsStructWithAnEntityField_IsRefused_LoudlyAndNamingTheField()
        {
            // ⭐ FLIPPED from the PoC finding P2_FINDING_AnEntityField_IsSILENTLY_NotSeededFromJson: the catalogue LISTS the action (so the editor sees
            //   it) but refuses to run it as a step — an authored reference is an EntityLink.
            Assert.True(ActionCatalogue.TryGet("TestTakesEntity", out var e));
            Assert.NotNull(e.Refusal);
            var ex = Assert.Throws<InvalidOperationException>(() => ActionCatalogue.RunnerFor("TestTakesEntity"));
            Assert.Contains("Entity field", ex.Message);
            Assert.Contains("EntityParams.Target", ex.Message);
            Assert.Contains("EntityLink", ex.Message);
        }

        [Fact]
        public void CE4162_AnEntityFieldNestedInAStruct_IsRefusedToo()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ActionCatalogue.RunnerFor("TestTakesNestedEntity"));
            Assert.Contains("NestedEntityParams.Holder.Inner", ex.Message);
        }

        [Fact]
        public void AnActionWithAnEntityLinkField_IsNotRefused()
        {
            Assert.True(ActionCatalogue.TryGet("TestRich", out var e));
            Assert.Null(e.Refusal);                                              // RichParams holds an EntityLink, not an Entity
        }

        [Fact]
        public void AShortNameTwoActionsCarry_IsAmbiguous_ItsFullNamesStillWork()
        {
            Assert.True(ActionCatalogue.TryGet(typeof(AmbiguousA).FullName + ".SameName", out var a));
            Assert.True(ActionCatalogue.TryGet(typeof(AmbiguousB).FullName + ".SameName", out var b));
            Assert.NotSame(a, b);

            var ex = Assert.Throws<InvalidOperationException>(() => ActionCatalogue.TryGet("SameName", out _));
            Assert.Contains("ambiguous", ex.Message);
            Assert.Contains(typeof(AmbiguousA).FullName!, ex.Message);           // both are named
            Assert.Contains(typeof(AmbiguousB).FullName!, ex.Message);
            Assert.Contains("SameName", ActionCatalogue.Ambiguities.Keys);
        }

        [Fact]
        public void TheCatalogue_SeesTheProductionActions_OfEveryLoadedAssembly()
        {
            Assert.Contains(ActionCatalogue.All, e => e.ShortName == "RunActions" && e.Fqn == typeof(ScenarioActions).FullName + ".RunActions");
            Assert.True(ActionCatalogue.All.Count > 10);                         // the shipped behaviours' stateful shared actions are in it too
        }
    }

    public static class AmbiguousA
    {
        [SharedAiAction] public static NodeStatus SameName(ref FailParams p, ref NoState s, Entity self, EntityRepository world) => NodeStatus.Success;
    }
    public static class AmbiguousB
    {
        [SharedAiAction] public static NodeStatus SameName(ref FailParams p, ref NoState s, Entity self, EntityRepository world) => NodeStatus.Success;
    }
}
