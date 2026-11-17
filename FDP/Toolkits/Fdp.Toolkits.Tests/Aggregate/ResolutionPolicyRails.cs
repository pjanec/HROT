using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8300</c> (agg-behaviors ST-5, §16b R-1…R-7, SV-9, SV-15; R-8326) — the resolution POLICY component: unmanaged, ≤ 1024 B, read by the manager
    /// through <see cref="ResolutionPolicies.BandsOf"/> (the authored values, the SV-9 defaults where nothing is authored), saved with the scenario, recorded.
    /// </summary>
    public sealed class ResolutionPolicyRails
    {
        private static EntityRepository Repo(bool register = true)
        {
            var r = new EntityRepository();
            if (register) r.RegisterComponent<ResolutionPolicy>();
            return r;
        }

        /// <summary>✅ Red-proof: BandsOf returning a fixed band ignoring the component ⇒ the authored 650 is not seen ⇒ red.</summary>
        [Fact]
        public void BandsOf_ReturnsTheAuthoredValues_AndTheDefaultsWhereNothingIsAuthored()
        {
            using var none = Repo(register: false);
            Assert.Equal(ResolutionBands.Default, ResolutionPolicies.BandsOf(none));      // the component is not even registered
            using var repo = Repo();
            Assert.Equal(ResolutionBands.Default, ResolutionPolicies.BandsOf(repo));      // registered, nobody holds one

            var env = repo.CreateEntity();
            repo.AddComponent(env, new ResolutionPolicy { PhysicalBand = 650f, QuietSeconds = 20f });   // a PARTIAL authoring: the zeros are the defaults
            var b = ResolutionPolicies.BandsOf(repo);
            var d = ResolutionBands.Default;
            Assert.Equal((650f, 20f), (b.PhysicalBand, b.QuietSeconds));
            Assert.Equal((d.LeadMetres, d.MergeMargin, d.ForcedRange, d.DeadlineSeconds), (b.LeadMetres, b.MergeMargin, b.ForcedRange, b.DeadlineSeconds));

            repo.SetComponent(env, new ResolutionPolicy { PhysicalBand = 800f, LeadMetres = 300f, MergeMargin = 50f, QuietSeconds = 5f, ForcedRange = 150f, DeadlineSeconds = 3f });
            Assert.Equal(new ResolutionBands(800f, 300f, 50f, 5f, 150f, 3f), ResolutionPolicies.BandsOf(repo));
        }

        /// <summary>✅ Red-proof: a field added that holds a reference / a size over 1024 ⇒ red.</summary>
        [Fact]
        public void ThePolicy_IsUnmanaged_AndFitsOneCommandBufferRecord()
        {
            Assert.True(Unsafe.SizeOf<ResolutionPolicy>() <= 1024, $"{Unsafe.SizeOf<ResolutionPolicy>()} B");
            Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ResolutionPolicy>());
            Assert.Equal(119, GlobalComponentIds.ResolutionPolicy);   // agg-behaviors' block (§23.1), never 324–326
        }

        /// <summary>SV-15 — the detail areas: up to 8, each once, none for "no reference", cleared whole. ✅ Red-proof: AddArea without the duplicate check ⇒ 2 ⇒ red.</summary>
        [Fact]
        public void DetailAreas_AreAddedOnce_UpToEight_AndCleared()
        {
            var p = default(ResolutionPolicy);
            Assert.False(p.AddArea(EntityRef.None));
            Assert.True(p.AddArea(new EntityRef(7)));
            Assert.True(p.AddArea(new EntityRef(7)));
            Assert.Equal(1, p.AreaCount);
            for (int i = 1; i < ResolutionPolicy.MaxAreas; i++) Assert.True(p.AddArea(new EntityRef(100 + i)));
            Assert.False(p.AddArea(new EntityRef(999)));          // the ninth is refused
            Assert.Equal(new EntityRef(107), p.AreaAt(7));
            p.ClearAreas();
            Assert.Equal(0, p.AreaCount);
            Assert.True(p.AreaAt(0).IsNone);
        }

        /// <summary>⭐ SAVED with the scenario (the default data policy): the authored numbers survive a save and a load. ✅ Red-proof: the component marked
        /// <c>NoScenario</c> ⇒ not serialised ⇒ red.</summary>
        [Fact]
        public void ThePolicy_SurvivesASaveAndLoad()
        {
            using var src = Repo();
            var env = src.CreateEntity();
            src.AddComponent(env, new ResolutionPolicy { PhysicalBand = 650f, LeadMetres = 250f, MergeMargin = 75f, QuietSeconds = 30f, ForcedRange = 220f, DeadlineSeconds = 6f, FrameBudgetMs = 12f });
            var serializer = new Fdp.Toolkit.Scenario.ScenarioSerializerBuilder("Fdp.Toolkits.Tests").Build();
            var dom = serializer.Serialize(src, new Fdp.Toolkit.Scenario.ScenarioHeader("Fdp.Toolkits.Tests"));

            using var fresh = Repo();
            serializer.Deserialize(fresh, dom);
            Assert.Equal(new ResolutionBands(650f, 250f, 75f, 30f, 220f, 6f), ResolutionPolicies.BandsOf(fresh));
            foreach (var e in fresh.Query().With<ResolutionPolicy>().Build()) Assert.Equal(12f, fresh.GetComponentRO<ResolutionPolicy>(e).FrameBudgetMs);
        }

        /// <summary>⭐ RECORDED: a mid-run change of the policy (the verb) comes back at the scrubbed frame. ✅ Red-proof: the component not added on the
        /// recording side ⇒ the sandbox holds none ⇒ red.</summary>
        [Fact]
        public void ARecording_GivesBackThePolicyAsItWasAtThatFrame()
        {
            using var harness = new Fdp.Toolkit.ReplayBrowser.Support.FdpRecordingHarness();
            var repo = harness.Repository;
            repo.RegisterComponent<ResolutionPolicy>();
            var env = repo.CreateEntity();
            repo.AddComponent(env, new ResolutionPolicy { PhysicalBand = 500f });
            harness.Tick().RecordKeyframe(100_000L);
            repo.GetComponentRW<ResolutionPolicy>(env).PhysicalBand = 900f;
            repo.GetComponentRW<ResolutionPolicy>(env).AddArea(new EntityRef(42));
            harness.Tick().RecordDelta(200_000L);
            harness.BuildToTempFile(out var path);

            using var sandbox = new EntityRepository();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessPosition>();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessVelocity>();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessTransform>();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessEntityInfo>();
            sandbox.RegisterComponent<ResolutionPolicy>();
            using var playback = new Fdp.Core.FlightRecorder.PlaybackController(path);
            Assert.True(playback.StepForward(sandbox));
            Assert.Equal(500f, sandbox.GetComponentRO<ResolutionPolicy>(env).PhysicalBand);
            Assert.True(playback.StepForward(sandbox));
            var p = sandbox.GetComponentRO<ResolutionPolicy>(env);
            Assert.Equal((900f, 1, new EntityRef(42)), (p.PhysicalBand, p.AreaCount, p.AreaAt(0)));
        }
    }
}
