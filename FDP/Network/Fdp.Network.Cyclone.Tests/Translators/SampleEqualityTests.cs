using System;
using Fdp.Network.Cyclone.Translators;
using Xunit;

namespace Fdp.Network.Cyclone.Tests.Translators
{
    /// <summary>
    /// ⭐ CE-2406 (R-2084) — the egress change-cache test: equal samples are the same, any field change is a change, a struct with a float
    /// compares without boxing (it went through <c>ValueType.Equals(object)</c> — a box per owned entity, per translator, per frame), and a
    /// struct with references still uses its own equality.
    /// 🔴 Red-proof: <c>Same</c> returning <c>a.Equals((object)b)</c> ⇒ the zero-allocation assert fails.
    /// </summary>
    public sealed class SampleEqualityTests
    {
        private struct Posture { public int EntityId; public byte Kind; public int Target; public float X, Y, Z; public byte Mount; }
        private struct WithText { public int Id; public string Label; }

        [Fact]
        public void EqualSamplesAreTheSame_AChangedFieldIsNot_AndTheCompareAllocatesNothing()
        {
            var a = new Posture { EntityId = 7, Kind = 2, Target = 9, X = 1.5f, Y = 2f, Z = -3f, Mount = 1 };
            var b = a;
            Assert.True(SampleEquality.Same(a, b));
            var c = a; c.Z = -3.25f; Assert.False(SampleEquality.Same(a, c));
            var d = a; d.Mount = 2; Assert.False(SampleEquality.Same(a, d));

            SampleEquality.Same(a, b);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool all = true;
            for (int i = 0; i < 1000; i++) all &= SampleEquality.Same(a, b);
            Assert.True(all);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void AStructWithReferences_UsesItsOwnEquality()
        {
            var a = new WithText { Id = 1, Label = "alpha" };
            var b = new WithText { Id = 1, Label = new string("alpha".ToCharArray()) };   // equal text, a different string object
            Assert.True(SampleEquality.Same(a, b));
            Assert.False(SampleEquality.Same(a, new WithText { Id = 1, Label = "beta" }));
        }

        [Fact]
        public void BytesEqualOnlyEverImpliesFieldEqual_SoAMismatchCostsAtMostAnExtraSend()
        {
            var plus = new Posture { X = 0f }; var minus = new Posture { X = -0f };
            Assert.False(SampleEquality.Same(plus, minus));     // -0 vs +0: reported as a change (an extra send), never the reverse
            Assert.True(plus.Equals(minus));                     // ...where the field test called them equal
        }
    }
}
