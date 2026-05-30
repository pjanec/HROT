using System;
using Fdp.Toolkit.Utility;
using Hrot.Diagnostics.Tuning;
using Xunit;

namespace Hrot.Diagnostics.Tuning.Tests
{
    // Tests for P6-04: snapshot/restore (SC-P6-4).
    // Verifies that TuningRegistry captures authored defaults at registration
    // and that RevertGroup/RevertAll re-enqueue those defaults through the apply queue.
    public sealed class SnapshotRestoreTests
    {
        // Helper: registers one float Tunable backed by a captured field, returns
        // (registry, key, field-reader). The registry is freshly created each call.
        private static (TuningRegistry registry, TuningKey key, Func<float> readField)
            RegisterWithField(float initialValue, string keyName = "utility.Alpha.0.0.weight",
                              float min = 0f, float max = 10f)
        {
            float field  = initialValue;
            var registry = new TuningRegistry();
            var key      = new TuningKey(keyName);
            registry.Register(key, new Tunable
            {
                Kind  = TuningKind.Float,
                Min   = min,
                Max   = max,
                Scope = TuningScope.Global,
                Owner = TuningOwner.Brain,
                Read  = () => field,
                Write = v => field = v,
            });
            return (registry, key, () => field);
        }

        [Fact]
        public void DefaultCapturedAtRegistration()
        {
            var (registry, key, _) = RegisterWithField(3.0f);

            registry.TryGet(key, out var tunable);

            Assert.NotNull(tunable);
            Assert.Equal(3.0f, tunable!.Default, 4);
        }

        [Fact]
        public void RevertGroup_RestoresDefaultValue()
        {
            var (registry, key, readField) = RegisterWithField(1.0f);

            // Modify to 5.0f and apply.
            registry.Apply(key, 5.0f);
            registry.BeginFrame();
            Assert.Equal(5.0f, readField(), 4);

            // Revert the group and drain.
            registry.RevertGroup("utility.Alpha");
            registry.BeginFrame();

            Assert.Equal(1.0f, readField(), 4);
        }

        [Fact]
        public void RevertGroup_DoesNotAffectOtherGroup()
        {
            float alpha = 1.0f, beta = 2.0f;
            var registry = new TuningRegistry();

            var keyAlpha = new TuningKey("utility.Alpha.0.0.weight");
            registry.Register(keyAlpha, new Tunable
            {
                Kind  = TuningKind.Float,
                Min   = 0f,
                Max   = 10f,
                Scope = TuningScope.Global,
                Owner = TuningOwner.Brain,
                Read  = () => alpha,
                Write = v => alpha = v,
            });

            var keyBeta = new TuningKey("utility.Beta.0.0.weight");
            registry.Register(keyBeta, new Tunable
            {
                Kind  = TuningKind.Float,
                Min   = 0f,
                Max   = 10f,
                Scope = TuningScope.Global,
                Owner = TuningOwner.Brain,
                Read  = () => beta,
                Write = v => beta = v,
            });

            // Apply different values to both.
            registry.Apply(keyAlpha, 7.0f);
            registry.Apply(keyBeta,  9.0f);
            registry.BeginFrame();
            Assert.Equal(7.0f, alpha, 4);
            Assert.Equal(9.0f, beta,  4);

            // Revert only utility.Alpha.
            registry.RevertGroup("utility.Alpha");
            registry.BeginFrame();

            Assert.Equal(1.0f, alpha, 4); // restored to authored default
            Assert.Equal(9.0f, beta,  4); // unchanged
        }

        [Fact]
        public void RevertAll_RestoresAllTunables()
        {
            float alpha = 1.0f, beta = 2.0f;
            var registry = new TuningRegistry();

            var keyAlpha = new TuningKey("utility.Alpha.0.0.weight");
            registry.Register(keyAlpha, new Tunable
            {
                Kind  = TuningKind.Float,
                Min   = 0f,
                Max   = 10f,
                Scope = TuningScope.Global,
                Owner = TuningOwner.Brain,
                Read  = () => alpha,
                Write = v => alpha = v,
            });

            var keyBeta = new TuningKey("utility.Beta.0.0.weight");
            registry.Register(keyBeta, new Tunable
            {
                Kind  = TuningKind.Float,
                Min   = 0f,
                Max   = 10f,
                Scope = TuningScope.Global,
                Owner = TuningOwner.Brain,
                Read  = () => beta,
                Write = v => beta = v,
            });

            // Apply different values to both.
            registry.Apply(keyAlpha, 6.0f);
            registry.Apply(keyBeta,  8.0f);
            registry.BeginFrame();

            // Revert all.
            registry.RevertAll();
            registry.BeginFrame();

            Assert.Equal(1.0f, alpha, 4);
            Assert.Equal(2.0f, beta,  4);
        }

        [Fact]
        public void DefaultCaptured_CurveTunable()
        {
            var initialCurve = new UtilityCurve { Kind = CurveKind.Linear, M = 1.5f };
            UtilityCurve field = initialCurve;

            var registry = new TuningRegistry();
            var key      = new TuningKey("utility.Alpha.0.0.curve");
            registry.RegisterCurve(key, new CurveTunable
            {
                Scope = TuningScope.Global,
                Owner = TuningOwner.Brain,
                Read  = () => field,
                Write = v => field = v,
            });

            registry.TryGetCurve(key, out var tunable);

            Assert.NotNull(tunable);
            Assert.Equal(initialCurve.Kind, tunable!.DefaultCurve.Kind);
        }
    }
}
