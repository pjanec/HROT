using Fhsm.Compiler;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fhsm.Tests.Kernel
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-395</c> — an ordinary transition's PRIORITY reaches the kernel.</b>
    ///
    /// <para>🔴 The flattener packed priority into flag bits 8-11 (<c>&amp; 0x0F</c>, so the 128 default became 0) while the
    /// kernel read bits 12-15 ⇒ every priority read 0, and <c>SelectTransition</c> degenerated to first-match-wins. Priority
    /// is now a full-byte <c>TransitionDef.Priority</c> — the same representation <c>GlobalTransitionDef</c> carries — and
    /// the builder default is 0, the same as the editor model's. Built through the REAL builder + flattener.</para>
    /// </summary>
    public unsafe class TransitionPriorityTests
    {
        private const ushort GoEvent = 60;

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
            for (int i = 0; i < 8; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
        }

        private static ushort Leaf(HsmBuilder b, string name) => b.GetGraph().FindState(name)!.FlatIndex;

        /// <summary>
        /// 🔴 CE395_R1 — of two transitions on one event from one state, the HIGHER priority wins even when declared second.
        /// ✅ Red-proof: read the priority as 0 in the kernel ⇒ the first-declared (C) wins.
        /// </summary>
        [Fact]
        public void CE395_R1_TheHigherPriorityTransition_WinsRegardlessOfDeclarationOrder()
        {
            var b = new HsmBuilder("CE395a");
            b.Event("Go", GoEvent);
            var a = b.State("A").Initial();
            b.State("B");
            b.State("C");
            a.On("Go").Priority(10).GoTo("C");
            a.On("Go").Priority(200).GoTo("B");
            var blob = b.GetGraph().Compile();

            var instance = Entered(blob);
            Fire(blob, ref instance);
            Assert.Equal(Leaf(b, "B"), instance.ActiveLeafIds[0]);
        }

        /// <summary>
        /// ⭐ CE395_R2 — priority is compared ACROSS the hierarchy: a parent's higher-priority transition beats the leaf's
        /// own (the leaf is scanned first, so first-match-wins picked the leaf's).
        /// </summary>
        [Fact]
        public void CE395_R2_AParentsHigherPriorityTransition_BeatsTheLeafs()
        {
            var b = new HsmBuilder("CE395b");
            b.Event("Go", GoEvent);
            var parent = b.State("P").Initial();
            b.State("B");
            b.State("C");
            parent.Child("Leaf", leaf => leaf.Initial().On("Go").GoTo("C"));
            parent.On("Go").Priority(50).GoTo("B");
            var blob = b.GetGraph().Compile();

            var instance = Entered(blob);
            Assert.Equal(Leaf(b, "Leaf"), instance.ActiveLeafIds[0]);
            Fire(blob, ref instance);
            Assert.Equal(Leaf(b, "B"), instance.ActiveLeafIds[0]);
        }

        /// <summary>
        /// ⭐ CE395_R3 — equal priorities keep today's rule (the first match, leaf before parent, declaration order), and an
        /// unauthored priority is 0 — the editor's default, so an authored 0 the emitter omits means the same thing.
        /// </summary>
        [Fact]
        public void CE395_R3_EqualPriorities_KeepFirstMatch_AndTheDefaultIsZero()
        {
            var b = new HsmBuilder("CE395c");
            b.Event("Go", GoEvent);
            var a = b.State("A").Initial();
            b.State("B");
            b.State("C");
            a.On("Go").GoTo("C");
            a.On("Go").GoTo("B");
            var blob = b.GetGraph().Compile();

            Assert.Equal(0, blob.GetTransition(0).Priority);
            var instance = Entered(blob);
            Fire(blob, ref instance);
            Assert.Equal(Leaf(b, "C"), instance.ActiveLeafIds[0]);
        }
    }
}
