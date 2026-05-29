using System;
using Hrot.Diagnostics.Tuning;
using Xunit;

namespace Hrot.Diagnostics.Tuning.Tests
{
    public class TuningRegistryTests
    {
        // Helper: builds a simple float Tunable backed by a captured variable.
        private static (TuningRegistry reg, TuningKey key, Func<float> readVal) MakeRegistry(
            float min, float max, float initial = 5f,
            Action<string>? warn = null)
        {
            float val = initial;
            var reg = new TuningRegistry(warn);
            var key = new TuningKey("test.val");
            reg.Register(key, new Tunable
            {
                Kind  = TuningKind.Float,
                Min   = min,
                Max   = max,
                Read  = () => val,
                Write = v => val = v,
            });
            return (reg, key, () => val);
        }

        [Fact]
        public void Apply_AboveMax_ClampsToMax_AndWarns()
        {
            string? warnMsg = null;
            var (reg, key, read) = MakeRegistry(0f, 1f, 0.5f, w => warnMsg = w);

            reg.Apply(key, 99f);
            reg.BeginFrame();

            Assert.Equal(1f, read(), 4);
            Assert.NotNull(warnMsg);
        }

        [Fact]
        public void Apply_BelowMin_ClampsToMin_AndWarns()
        {
            string? warnMsg = null;
            var (reg, key, read) = MakeRegistry(0f, 1f, 0.5f, w => warnMsg = w);

            reg.Apply(key, -99f);
            reg.BeginFrame();

            Assert.Equal(0f, read(), 4);
            Assert.NotNull(warnMsg);
        }

        [Fact]
        public void Apply_InRange_NoClamp_NoWarn()
        {
            string? warnMsg = null;
            var (reg, key, read) = MakeRegistry(0f, 1f, 0.5f, w => warnMsg = w);

            reg.Apply(key, 0.7f);
            reg.BeginFrame();

            Assert.Equal(0.7f, read(), 4);
            Assert.Null(warnMsg);
        }

        [Fact]
        public void Apply_IsQueuedNotImmediate()
        {
            var (reg, key, read) = MakeRegistry(0f, 10f, 5f);

            reg.Apply(key, 9f);

            // Value must not change until BeginFrame drains the queue.
            Assert.Equal(5f, read(), 4);

            reg.BeginFrame();
            Assert.Equal(9f, read(), 4);
        }

        [Fact]
        public void Apply_UnknownKey_ReturnsFalse()
        {
            var reg = new TuningRegistry();
            bool result = reg.Apply(new TuningKey("unknown.key"), 1f);
            Assert.False(result);
        }

        [Fact]
        public void BeginFrame_MultipleQueued_AppliesAll()
        {
            float a = 0f, b = 0f;
            var reg = new TuningRegistry();
            var keyA = new TuningKey("a.val");
            var keyB = new TuningKey("b.val");
            reg.Register(keyA, new Tunable { Min = 0f, Max = 10f, Read = () => a, Write = v => a = v });
            reg.Register(keyB, new Tunable { Min = 0f, Max = 10f, Read = () => b, Write = v => b = v });

            reg.Apply(keyA, 3f);
            reg.Apply(keyB, 7f);
            reg.BeginFrame();

            Assert.Equal(3f, a, 4);
            Assert.Equal(7f, b, 4);
        }

        [Fact]
        public void TuningKey_SameName_EqualId()
        {
            var k1 = new TuningKey("utility.foo.0.0.weight");
            var k2 = new TuningKey("utility.foo.0.0.weight");
            Assert.Equal(k1.Id, k2.Id);
            Assert.Equal(k1, k2);
        }

        [Fact]
        public void TuningKey_DifferentName_DifferentId()
        {
            var k1 = new TuningKey("utility.foo.0.0.weight");
            var k2 = new TuningKey("utility.foo.0.0.slope");
            Assert.NotEqual(k1.Id, k2.Id);
        }
    }
}
