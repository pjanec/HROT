using System;
using Fhsm.Compiler;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fhsm.Tests.Kernel
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-506</c> — a global transition's guard, effect action and priority reach the kernel.</b>
    ///
    /// <para>🔴 The gap: <c>HsmBuilder.GlobalTransition(event, target, visualId)</c> took no guard, action or priority,
    /// although <c>GlobalTransitionDef</c> has the slots and the kernel honours them — so an authored global guard/action
    /// was saved by the editor and dropped at emit. These rails build a REAL machine through the builder (not a hand-made
    /// blob), so they prove the authoring surface, the flattener and the kernel agree.</para>
    /// </summary>
    [Collection("HsmActionDispatcher")]
    public unsafe class GlobalTransitionGuardActionTests
    {
        private const ushort GoEvent = 50;

        private static bool _guardPasses;
        private static int  _guardCalls;
        private static int  _actionCalls;

        private static bool Guard(void* instance, void* context, ushort eventId, HsmCommandWriter* writer)
        {
            _guardCalls++;
            return _guardPasses;
        }

        private static void Effect(void* i, void* c, HsmCommandWriter* w) => _actionCalls++;

        private static (HsmDefinitionBlob Blob, HsmBuilder Builder) Machine(Action<HsmBuilder> globals)
        {
            var b = new HsmBuilder("CE506");
            b.Event("Go", GoEvent);
            b.State("A").Initial();
            b.State("B");
            b.State("C");
            globals(b);
            return (b.GetGraph().Compile(), b);
        }

        private static ushort Leaf(HsmBuilder b, string name) => b.GetGraph().FindState(name)!.FlatIndex;

        private static HsmInstance64 Entered(HsmDefinitionBlob blob)
        {
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            HsmKernel.Trigger(ref instance);
            for (int i = 0; i < 3; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            return instance;
        }

        private static void Fire(HsmDefinitionBlob blob, ref HsmInstance64 instance)
        {
            fixed (HsmInstance64* p = &instance)
                HsmEventQueue.TryEnqueue(p, 64, new HsmEvent { EventId = GoEvent, Priority = EventPriority.Normal });
            // ⚠ The kernel advances ONE phase per Update (event → RTC → activity → idle), so pump a full cycle and then
            //   some — three updates left the second event parked in RTC.
            for (int i = 0; i < 8; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
        }

        private static void Arrange(HsmDefinitionBlob blob, bool guardPasses)
        {
            HsmActionDispatcher.ClearAll();
            ref readonly var gt = ref blob.GetGlobalTransition(0);
            HsmActionDispatcher.RegisterGuard(
                gt.GuardId, (IntPtr)(delegate* <void*, void*, ushort, HsmCommandWriter*, bool>)&Guard);
            HsmActionDispatcher.RegisterAction(
                gt.ActionId, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&Effect);
            _guardPasses = guardPasses;
            _guardCalls = _actionCalls = 0;
        }

        /// <summary>
        /// 🔴 CE506_R1 — the guard and the action are COMPILED into the global transition (they were dropped: both slots
        /// read "none"). ✅ Red-proof: stop the builder assigning <c>GuardFunction</c>/<c>ActionFunction</c> ⇒ this reddens.
        /// </summary>
        [Fact]
        public void CE506_R1_TheGlobalGuardAndAction_AreCompiledIntoTheBlob()
        {
            var (blob, _) = Machine(b => b.GlobalTransition("Go", "B", guard: "Ns.Nodes.CanGo", action: "Ns.Nodes.OnGo"));

            ref readonly var gt = ref blob.GetGlobalTransition(0);
            Assert.NotEqual((ushort)0xFFFF, gt.GuardId);
            Assert.NotEqual((ushort)0xFFFF, gt.ActionId);
            Assert.NotEqual(gt.GuardId, gt.ActionId);
        }

        /// <summary>
        /// ⭐ CE506_R2 — the guard GATES the global transition, and the effect action runs when it fires.
        /// </summary>
        [Fact]
        public void CE506_R2_TheGuardGates_AndTheActionRuns()
        {
            var (blob, b) = Machine(m => m.GlobalTransition("Go", "B", guard: "Ns.Nodes.CanGo", action: "Ns.Nodes.OnGo"));
            try
            {
                Arrange(blob, guardPasses: false);
                var instance = Entered(blob);
                Assert.Equal(Leaf(b, "A"), instance.ActiveLeafIds[0]);

                Fire(blob, ref instance);
                Assert.True(_guardCalls > 0, "the global guard must be consulted");
                Assert.Equal(Leaf(b, "A"), instance.ActiveLeafIds[0]);   // refused
                Assert.Equal(0, _actionCalls);

                _guardPasses = true;
                Fire(blob, ref instance);
                Assert.Equal(Leaf(b, "B"), instance.ActiveLeafIds[0]);   // taken
                Assert.Equal(1, _actionCalls);                            // the effect ran, once
            }
            finally { HsmActionDispatcher.ClearAll(); }
        }

        /// <summary>
        /// ⭐ CE506_R3 — PRIORITY: of two unguarded globals on one event, the higher priority wins even when declared second
        /// (the kernel takes the first match, so the flattener orders by priority). Ties keep declaration order.
        /// ✅ Red-proof: drop the flattener's priority ordering ⇒ the machine goes to C.
        /// </summary>
        [Fact]
        public void CE506_R3_TheHigherPriorityGlobal_WinsRegardlessOfDeclarationOrder()
        {
            var (blob, b) = Machine(m => m
                .GlobalTransition("Go", "C", priority: 10)
                .GlobalTransition("Go", "B", priority: 200));
            try
            {
                HsmActionDispatcher.ClearAll();
                Assert.Equal(200, blob.GetGlobalTransition(0).Priority);

                var instance = Entered(blob);
                Fire(blob, ref instance);
                Assert.Equal(Leaf(b, "B"), instance.ActiveLeafIds[0]);
            }
            finally { HsmActionDispatcher.ClearAll(); }
        }
    }
}
